using Microsoft.EntityFrameworkCore;
using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Data;
using RentalSphere.Identity.Services;
using RentalSphere.Modules.CustomerManagement.Repositories;
using RentalSphere.Modules.CustomerManagement.Services;
using RentalSphere.Modules.Equipment.Models;
using RentalSphere.Modules.Equipment.Repositories;
using RentalSphere.Modules.Equipment.Services;
using RentalSphere.Modules.ReservationManagement.DTOs;
using RentalSphere.Modules.ReservationManagement.Models;
using RentalSphere.Modules.ReservationManagement.Repositories;

namespace RentalSphere.Modules.ReservationManagement.Services;

public interface IReservationService
{
    Task<List<ReservationListItemDto>> ListAsync(string? search = null, string? statusFilter = null);
    Task<(List<ReservationListItemDto> Items, int TotalCount)> ListPagedAsync(
        string? search, string? statusFilter, string? sort, int skip, int take);
    Task<List<ReservationListItemDto>> ListMineAsync(string? statusFilter = null);
    Task<(List<ReservationListItemDto> Items, int TotalCount)> ListMinePagedAsync(
        string? statusFilter, int skip, int take);
    Task<int> CountMineByStatusAsync(string? statusFilter);
    Task<ReservationDetailsDto> GetDetailsAsync(int id);
    Task<int> CountPendingAsync(int? customerId = null);
    Task<int> CountPendingForCurrentUserAsync();
    Task<AvailabilityResultDto> CheckAvailabilityAsync(
        DateTime start, DateTime end, List<ReservationItemInputDto> items, int? excludeReservationId = null);
    Task<int> CreateAsync(ReservationCreateDto dto, string actorUserId);
    Task ConfirmAsync(int id, string actorUserId);
    Task CancelAsync(int id, string actorUserId, ReservationCancellationReason reason, string? notes = null);
}

public class ReservationService : IReservationService
{
    private readonly IReservationRepository _repo;
    private readonly ICustomerRepository _customers;
    private readonly IEquipmentRepository _equipment;
    private readonly IEquipmentItemRepository _items;
    private readonly IEquipmentItemService _itemService;
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IAuditLogger _audit;

    public ReservationService(
        IReservationRepository repo,
        ICustomerRepository customers,
        IEquipmentRepository equipment,
        IEquipmentItemRepository items,
        IEquipmentItemService itemService,
        ApplicationDbContext db,
        ICurrentUser current,
        IAuditLogger audit)
    {
        _repo = repo;
        _customers = customers;
        _equipment = equipment;
        _items = items;
        _itemService = itemService;
        _db = db;
        _current = current;
        _audit = audit;
    }

    public async Task<List<ReservationListItemDto>> ListAsync(string? search = null, string? statusFilter = null)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can list all reservations.");

        var rows = await _repo.ListAsync(search, statusFilter);
        return rows.Select(MapList).ToList();
    }

    public async Task<(List<ReservationListItemDto> Items, int TotalCount)> ListPagedAsync(
        string? search, string? statusFilter, string? sort, int skip, int take)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can list all reservations.");

        var all = await _repo.ListAsync(search, statusFilter);
        var total = all.Count;

        var sorted = sort?.ToLowerInvariant() switch
        {
            "id" or "id_desc" => sort == "id_desc"
                ? all.OrderByDescending(r => r.ReservationID).ToList()
                : all.OrderBy(r => r.ReservationID).ToList(),
            "customer" => all.OrderBy(r => r.Customer?.LastName ?? "").ThenBy(r => r.Customer?.FirstName ?? "").ToList(),
            "customer_desc" => all.OrderByDescending(r => r.Customer?.LastName ?? "").ToList(),
            "dates" => all.OrderBy(r => r.RentalStartDate).ToList(),
            "dates_desc" => all.OrderByDescending(r => r.RentalStartDate).ToList(),
            "total" => all.OrderBy(r => r.TotalEstimatedCost).ToList(),
            "total_desc" => all.OrderByDescending(r => r.TotalEstimatedCost).ToList(),
            _ => all.OrderByDescending(r => r.ReservationDate).ToList(),
        };

        var page = sorted.Skip(skip).Take(take).Select(MapList).ToList();
        return (page, total);
    }

    public async Task<List<ReservationListItemDto>> ListMineAsync(string? statusFilter = null)
    {
        // Non-paged callers get everything.
        var (items, _) = await ListMinePagedAsync(statusFilter, 0, int.MaxValue);
        return items;
    }

    public async Task<int> CountMineByStatusAsync(string? statusFilter)
    {
        if (!_current.IsCustomerScoped)
            throw new ForbiddenException("My reservations is a customer-only view.");

        var customer = await _customers.GetByUserIdAsync(_current.UserId!);
        if (customer is null) return 0;

        return await _repo.CountByCustomerAsync(customer.CustomerID, statusFilter);
    }

    public async Task<(List<ReservationListItemDto> Items, int TotalCount)> ListMinePagedAsync(
        string? statusFilter, int skip, int take)
    {
        if (!_current.IsCustomerScoped)
            throw new ForbiddenException("My reservations is a customer-only view.");

        // Find the linked Customer row, then pull its reservations.
        var customer = await _customers.GetByUserIdAsync(_current.UserId!);
        if (customer is null) return (new List<ReservationListItemDto>(), 0);

        var total = await _repo.CountByCustomerAsync(customer.CustomerID, statusFilter);
        var rows = await _repo.ListByCustomerPagedAsync(customer.CustomerID, skip, take, statusFilter);
        return (rows.Select(MapList).ToList(), total);
    }

    public async Task<int> CountPendingAsync(int? customerId = null)
    {
        // Staff/Admin get a global Pending count; passing a customerId scopes
        // the count to that customer's Pending reservations only. The badge
        // intentionally clears as soon as a reservation transitions out of
        // Pending (Confirmed, Cancelled, CheckedOut, Completed, or Expired) —
        // confirmed reservations are no longer "awaiting action" and don't
        // need to pulse on the sidebar.
        if (customerId.HasValue)
        {
            return await _repo.CountPendingForCustomerAsync(customerId.Value);
        }
        if (!_current.IsAdmin && !_current.IsStaff) return 0;
        return await _repo.CountByStatusAsync(ReservationStatus.Pending);
    }

    /// <summary>
    /// Role-aware count used by the sidebar ViewComponent. Resolves the
    /// current user to a Customer when in customer scope and forwards the
    /// id; returns the global Pending count for staff. No-op for anonymous
    /// or other roles.
    /// </summary>
    public async Task<int> CountPendingForCurrentUserAsync()
    {
        if (_current.IsCustomerScoped)
        {
            var customer = await _customers.GetByUserIdAsync(_current.UserId!);
            if (customer is null) return 0;
            return await CountPendingAsync(customer.CustomerID);
        }
        if (!_current.IsAdmin && !_current.IsStaff) return 0;
        return await CountPendingAsync();
    }

    public async Task<ReservationDetailsDto> GetDetailsAsync(int id)
    {
        var r = await _repo.GetWithItemsAsync(id)
            ?? throw new NotFoundException($"Reservation {id} not found.");

        EnsureMayRead(r);

        var dto = MapDetails(r);

        // If the reservation is cancelled, look up the most recent Reservation.Cancel
        // audit row to identify who did it and what their role was. The audit log is
        // the source of truth; we don't store the role on the reservation itself.
        if (r.Status == ReservationStatus.Cancelled)
        {
            var cancelRow = await _db.AuditLogs
                .Where(a => a.Action == "Reservation.Cancel" && a.EntityId == id.ToString())
                .OrderByDescending(a => a.Timestamp)
                .FirstOrDefaultAsync();

            if (cancelRow is not null && !string.IsNullOrEmpty(cancelRow.ActorUserId))
            {
                var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == cancelRow.ActorUserId);
                if (user is not null)
                {
                    dto.CancelledByName = string.IsNullOrWhiteSpace(user.Email)
                        ? user.UserName
                        : user.Email;

                    var roleNames = await _db.UserRoles
                        .Where(ur => ur.UserId == user.Id)
                        .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                        .ToListAsync();
                    if (roleNames.Any())
                    {
                        dto.CancelledByRole = string.Join(", ", roleNames);
                    }
                }
            }
        }

        return dto;
    }

    public async Task<AvailabilityResultDto> CheckAvailabilityAsync(
        DateTime start, DateTime end, List<ReservationItemInputDto> items, int? excludeReservationId = null)
    {
        var result = new AvailabilityResultDto { IsAvailable = true };

        foreach (var line in items)
        {
            var eq = await _equipment.GetAsync(line.EquipmentID)
                ?? throw new NotFoundException($"Equipment {line.EquipmentID} not found.");

            // ponytail: total stock pulls from the catalog StockQuantity (same number
            // the customer sees on the equipment page) rather than the EquipmentItems
            // row count. align_stock.sql keeps these aligned; this keeps the booking
            // gate consistent with the display.
            var totalStock = eq.StockQuantity > 0 ? eq.StockQuantity : await _items
                .CountAsyncByEquipment(line.EquipmentID, AvailabilityStatus.Available);

            // Half-open overlap: an existing reservation [existing.Start, existing.End)
            // blocks a new reservation [start, end) iff their day-ranges share a day.
            // Day-granular: a drop-off on `start` is a free day for the next renter,
            // even if the stored RentalEndDate has a non-zero time component.
            var conflictedRows = await _db.ReservationItems
                .Where(ri => ri.EquipmentID == line.EquipmentID
                             && (ri.Reservation.Status == ReservationStatus.Pending
                                 || ri.Reservation.Status == ReservationStatus.Confirmed
                                 || ri.Reservation.Status == ReservationStatus.CheckedOut)
                             && (excludeReservationId == null || ri.ReservationID != excludeReservationId.Value)
                             && ri.Reservation.RentalStartDate.Date < end.Date
                             && ri.Reservation.RentalEndDate.Date > start.Date)
                .SumAsync(ri => (int?)ri.Quantity) ?? 0;

            // Only count overlapping Pending/Confirmed reservations. Cancelled and
            // Returned reservations are excluded by the Status filter above.
            var available = totalStock - conflictedRows;
            if (available < 0) available = 0;

            var lineResult = new AvailabilityLineResultDto
            {
                EquipmentID = line.EquipmentID,
                EquipmentName = eq.Name,
                Requested = line.Quantity,
                Available = available,
                IsAvailable = available >= line.Quantity,
            };
            if (!lineResult.IsAvailable)
            {
                result.IsAvailable = false;
                lineResult.Reason = $"Only {available} unit(s) available between {start:yyyy-MM-dd} and {end:yyyy-MM-dd}; requested {line.Quantity}.";
            }
            result.Lines.Add(lineResult);
        }
        return result;
    }

    public async Task<int> CreateAsync(ReservationCreateDto dto, string actorUserId)
    {
        if (dto.RentalEndDate <= dto.RentalStartDate)
            throw new InvalidOperationException("Rental end date must be after the start date.");
        if (dto.Items is null || dto.Items.Count == 0)
            throw new InvalidOperationException("At least one equipment item is required.");

        var customer = await _customers.GetAsync(dto.CustomerID)
            ?? throw new NotFoundException($"Customer {dto.CustomerID} not found.");

        // Customer role may only create on their own behalf. Walk-in customers
        // (UserId == null) cannot create on their own — only admins/staff can.
        if (_current.IsCustomerScoped)
        {
            if (string.IsNullOrEmpty(customer.UserId) || customer.UserId != _current.UserId)
                throw new ForbiddenException("You can only create reservations for yourself.");
        }

        // Advisory availability check on create; block create if any line is short.
        var availability = await CheckAvailabilityAsync(
            dto.RentalStartDate, dto.RentalEndDate, dto.Items);
        if (!availability.IsAvailable)
        {
            var firstShort = availability.Lines.First(l => !l.IsAvailable);
            throw new InvalidOperationException(firstShort.Reason ?? "Equipment unavailable for the requested dates.");
        }

        var days = Math.Max(1, (int)Math.Ceiling((dto.RentalEndDate.Date - dto.RentalStartDate.Date).TotalDays));
        decimal total = 0m;
        var lines = new List<ReservationItem>();
        foreach (var input in dto.Items)
        {
            var eq = await _equipment.GetAsync(input.EquipmentID)
                ?? throw new NotFoundException($"Equipment {input.EquipmentID} not found.");
            var lineTotal = eq.DailyRate * input.Quantity * days;
            total += lineTotal;
            lines.Add(new ReservationItem
            {
                EquipmentID = eq.EquipmentID,
                Quantity = input.Quantity,
                Notes = input.Notes,
            });
        }

        var reservation = new Reservation
        {
            CustomerID = customer.CustomerID,
            ReservationDate = DateTime.UtcNow,
            RentalStartDate = dto.RentalStartDate,
            RentalEndDate = dto.RentalEndDate,
            Status = ReservationStatus.Pending,
            TotalEstimatedCost = total,
            Notes = dto.Notes,
            CreatedByUserId = _current.UserId,
            Items = lines,
        };

        await _repo.AddAsync(reservation);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "Reservation.Create", "Reservation", reservation.ReservationID.ToString(),
            newValues: new
            {
                reservation.CustomerID,
                reservation.RentalStartDate,
                reservation.RentalEndDate,
                Items = reservation.Items.Count,
                reservation.TotalEstimatedCost,
            });

        return reservation.ReservationID;
    }

    public async Task ConfirmAsync(int id, string actorUserId)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can confirm reservations.");

        var r = await _repo.GetWithItemsAsync(id)
            ?? throw new NotFoundException($"Reservation {id} not found.");

        if (r.Status != ReservationStatus.Pending)
            throw new InvalidOperationException($"Only Pending reservations can be confirmed (current status: {r.Status}).");

        // Authoritative availability re-check at confirm time.
        var inputs = r.Items.Select(i => new ReservationItemInputDto
        {
            EquipmentID = i.EquipmentID,
            Quantity = i.Quantity,
        }).ToList();
        var availability = await CheckAvailabilityAsync(
            r.RentalStartDate, r.RentalEndDate, inputs, excludeReservationId: id);
        if (!availability.IsAvailable)
        {
            var firstShort = availability.Lines.First(l => !l.IsAvailable);
            throw new InvalidOperationException(firstShort.Reason ?? "Equipment unavailable; cannot confirm.");
        }

        // Pin concrete items to each line. We pull items that are physically
        // bookable AND not currently pinned to a Confirmed/CheckedOut reservation
        // overlapping [start, end) half-open — the reservation table is the
        // source of truth for "is this unit free right now", not the per-item
        // status flag. If a single equipment requires more units than exist,
        // we surface the failure from the availability check above.
        var assignedMap = new Dictionary<int, List<int>>(); // equipmentId → list of item ids
        foreach (var line in r.Items)
        {
            var freeItems = await _items
                .ListFreeForRangeAsync(line.EquipmentID, r.RentalStartDate, r.RentalEndDate,
                    excludeReservationId: id);
            var picked = freeItems.Take(line.Quantity).Select(i => i.ItemID).ToList();
            if (picked.Count < line.Quantity)
                throw new InvalidOperationException(
                    $"Not enough free units for equipment {line.EquipmentID}.");
            assignedMap[line.EquipmentID] = picked;
        }

        // Apply assignments to the line items.
        foreach (var line in r.Items)
        {
            var picked = assignedMap[line.EquipmentID];
            // For multi-unit lines we record the first assigned id on the line and rely on
            // the audit log for the rest. (Single-unit-per-line is the common case.)
            line.AssignedItemID = picked.First();
        }

        // Flip each picked item to Reserved for the EquipmentItems list view. The
        // per-item status no longer mutates StockQuantity — that's a permanent
        // inventory total and reservations hold units via the reservation table,
        // not by mutating the parent count.
        var allAssigned = assignedMap.Values.SelectMany(ids => ids).Distinct().ToList();
        foreach (var itemId in allAssigned)
        {
            await _itemService.ChangeStatusAsync(itemId, nameof(AvailabilityStatus.Reserved), actorUserId);
        }

        r.Status = ReservationStatus.Confirmed;
        _repo.Update(r);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "Reservation.Confirm", "Reservation", id.ToString(),
            oldValues: new { Status = ReservationStatus.Pending.ToString() },
            newValues: new
            {
                Status = ReservationStatus.Confirmed.ToString(),
                AssignedItems = allAssigned,
            });
    }

    public async Task CancelAsync(int id, string actorUserId, ReservationCancellationReason reason, string? notes = null)
    {
        var r = await _repo.GetWithItemsAsync(id)
            ?? throw new NotFoundException($"Reservation {id} not found.");

        // Customer may cancel only their own Pending reservation. Once checked
        // out, completed, or confirmed, the rental is in motion and the customer
        // must contact staff — we don't let them self-cancel from a later state.
        if (_current.IsCustomerScoped)
        {
            if (r.Customer.UserId != _current.UserId)
                throw new ForbiddenException("You can only cancel your own reservations.");
            if (r.Status != ReservationStatus.Pending)
            {
                var msg = r.Status switch
                {
                    ReservationStatus.CheckedOut => "This reservation is already checked out. Please contact staff to arrange a return or refund.",
                    ReservationStatus.Completed  => "This reservation has already been completed and cannot be cancelled.",
                    ReservationStatus.Confirmed  => "This reservation has been confirmed by staff. Please contact them to cancel.",
                    ReservationStatus.Cancelled  => "This reservation is already cancelled.",
                    ReservationStatus.Expired    => "This reservation has expired.",
                    _ => "Only pending reservations can be cancelled by customers."
                };
                throw new InvalidOperationException(msg);
            }
        }
        else if (!_current.IsAdmin && !_current.IsStaff)
        {
            throw new ForbiddenException("Only Admin or Staff can cancel reservations.");
        }

        var oldStatus = r.Status;
        if (oldStatus == ReservationStatus.Cancelled || oldStatus == ReservationStatus.Expired)
            return;

        // If reservation was Confirmed, release the assigned items back to Available.
        if (oldStatus == ReservationStatus.Confirmed)
        {
            // Collect ALL equipment item ids currently assigned to this reservation. The
            // confirm-time AssignedItemID on each line only records the first picked unit,
            // so for multi-unit lines we discover the rest by finding items that are
            // Reserved and belong to one of the line's equipment, but that haven't been
            // claimed by another Confirmed reservation. Simpler & safer: collect every
            // line's AssignedItemID plus enough extras to satisfy the Quantity, scoped to
            // this reservation's equipment set.
            var equipmentIds = r.Items.Select(i => i.EquipmentID).Distinct().ToList();
            var candidates = await _db.EquipmentItems
                .Where(ei => equipmentIds.Contains(ei.EquipmentID)
                          && ei.AvailabilityStatus == AvailabilityStatus.Reserved)
                .Select(ei => ei.ItemID)
                .ToListAsync();

            var explicitAssigned = r.Items
                .Where(i => i.AssignedItemID.HasValue)
                .Select(i => i.AssignedItemID!.Value)
                .Distinct()
                .ToList();

            // Take all explicit assignments first, then fill the rest from the reserved pool
            // up to the Quantity per line.
            var toRelease = new HashSet<int>(explicitAssigned);
            foreach (var line in r.Items)
            {
                var picked = candidates
                    .Where(id => !toRelease.Contains(id))
                    .Take(line.Quantity)
                    .ToList();
                foreach (var p in picked) toRelease.Add(p);
            }

            foreach (var itemId in toRelease)
            {
                await _itemService.ChangeStatusAsync(itemId, nameof(AvailabilityStatus.Available), actorUserId);
            }
        }

        r.Status = ReservationStatus.Cancelled;
        r.CancellationReason = reason;
        r.CancellationNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        r.CancelledAt = DateTime.UtcNow;

        _repo.Update(r);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "Reservation.Cancel", "Reservation", id.ToString(),
            oldValues: new { Status = oldStatus.ToString() },
            newValues: new
            {
                Status = ReservationStatus.Cancelled.ToString(),
                CancellationReason = reason.ToString(),
                HasNotes = !string.IsNullOrWhiteSpace(notes),
            });
    }

    // ---- helpers ----

    private void EnsureMayRead(Reservation r)
    {
        if (_current.IsAdmin || _current.IsStaff) return;
        if (_current.IsCustomerScoped && r.Customer.UserId == _current.UserId) return;
        throw new NotFoundException($"Reservation {r.ReservationID} not found.");
    }

    private static ReservationListItemDto MapList(Reservation r) => new()
    {
        ReservationID = r.ReservationID,
        CustomerID = r.CustomerID,
        CustomerName = r.Customer is null ? string.Empty : $"{r.Customer.FirstName} {r.Customer.LastName}",
        ReservationDate = r.ReservationDate,
        RentalStartDate = r.RentalStartDate,
        RentalEndDate = r.RentalEndDate,
        ItemCount = r.Items?.Count ?? 0,
        TotalEstimatedCost = r.TotalEstimatedCost,
        Status = r.Status.ToString(),
    };

    private static ReservationDetailsDto MapDetails(Reservation r) => new()
    {
        ReservationID = r.ReservationID,
        CustomerID = r.CustomerID,
        CustomerName = r.Customer is null ? string.Empty : $"{r.Customer.FirstName} {r.Customer.LastName}",
        CustomerEmail = r.Customer?.Email ?? string.Empty,
        ReservationDate = r.ReservationDate,
        RentalStartDate = r.RentalStartDate,
        RentalEndDate = r.RentalEndDate,
        Days = Math.Max(1, (int)Math.Ceiling((r.RentalEndDate.Date - r.RentalStartDate.Date).TotalDays)),
        TotalEstimatedCost = r.TotalEstimatedCost,
        Status = r.Status.ToString(),
        Notes = r.Notes,
        CreatedByUserName = r.CreatedByUser is null ? null : $"{r.CreatedByUser.FirstName} {r.CreatedByUser.LastName}",
        CancellationReason = r.CancellationReason?.ToString(),
        CancellationNotes = r.CancellationNotes,
        CancelledAt = r.CancelledAt,
        Items = (r.Items ?? new List<ReservationItem>()).Select(i => new ReservationItemDto
        {
            ReservationItemID = i.ReservationItemID,
            EquipmentID = i.EquipmentID,
            EquipmentName = i.Equipment?.Name ?? string.Empty,
            Quantity = i.Quantity,
            DailyRate = i.Equipment?.DailyRate ?? 0m,
            LineTotal = (i.Equipment?.DailyRate ?? 0m) * i.Quantity *
                Math.Max(1, (int)Math.Ceiling((r.RentalEndDate.Date - r.RentalStartDate.Date).TotalDays)),
            AssignedItemID = i.AssignedItemID,
            AssignedSerialNumber = i.AssignedItem?.SerialNumber,
            Notes = i.Notes,
        }).ToList(),
    };
}