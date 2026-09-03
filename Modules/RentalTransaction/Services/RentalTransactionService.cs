using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Identity.Services;
using RentalSphere.Modules.Billing.Services;
using RentalSphere.Modules.CustomerManagement.Repositories;
using RentalSphere.Modules.Equipment.Models;
using RentalSphere.Modules.Equipment.Repositories;
using RentalSphere.Modules.Equipment.Services;
using RentalSphere.Modules.RentalTransaction.DTOs;
using RentalSphere.Modules.RentalTransaction.Models;
using RentalSphere.Modules.RentalTransaction.Repositories;
using RentalSphere.Modules.ReservationManagement.Models;
using RentalSphere.Modules.ReservationManagement.Repositories;
using RTEntity = RentalSphere.Modules.RentalTransaction.Models.RentalTransaction;

namespace RentalSphere.Modules.RentalTransaction.Services;

public interface IRentalTransactionService
{
    Task<List<RentalTransactionListItemDto>> ListAsync(string? statusFilter = null, bool mineOnly = false);
    Task<(List<RentalTransactionListItemDto> Items, int TotalCount)> ListPagedAsync(
        string? statusFilter, bool mineOnly, int skip, int take);
    Task<int> CountAsync(string? statusFilter, bool mineOnly);
    Task<RentalTransactionDetailsDto> GetDetailsAsync(int id);
    Task<int> CheckoutAsync(int reservationId, string actorUserId);
    Task ReturnAsync(RentalTransactionReturnDto dto, string actorUserId);
    Task CancelAsync(int id, string actorUserId, string? reason = null);
}

public class RentalTransactionService : IRentalTransactionService
{
    private readonly IRentalTransactionRepository _repo;
    private readonly IReservationRepository _reservations;
    private readonly IEquipmentItemRepository _items;
    private readonly IEquipmentItemService _itemService;
    private readonly ICustomerRepository _customers;
    private readonly IBillingService _billing;
    private readonly ICurrentUser _current;
    private readonly IAuditLogger _audit;

    public RentalTransactionService(
        IRentalTransactionRepository repo,
        IReservationRepository reservations,
        IEquipmentItemRepository items,
        IEquipmentItemService itemService,
        ICustomerRepository customers,
        IBillingService billing,
        ICurrentUser current,
        IAuditLogger audit)
    {
        _repo = repo;
        _reservations = reservations;
        _items = items;
        _itemService = itemService;
        _customers = customers;
        _billing = billing;
        _current = current;
        _audit = audit;
    }

    public async Task<List<RentalTransactionListItemDto>> ListAsync(string? statusFilter = null, bool mineOnly = false)
    {
        // Non-paged callers get everything.
        var (items, _) = await ListPagedAsync(statusFilter, mineOnly, 0, int.MaxValue);
        return items;
    }

    public async Task<(List<RentalTransactionListItemDto> Items, int TotalCount)> ListPagedAsync(
        string? statusFilter, bool mineOnly, int skip, int take)
    {
        var (customerId, _) = await ResolveCustomerScopeAsync(mineOnly);
        if (_repo is null) return (new List<RentalTransactionListItemDto>(), 0);

        var total = await _repo.CountAsync(statusFilter, customerId);
        var rows = await _repo.ListPagedAsync(skip, take, statusFilter, customerId);
        return (rows.Select(MapList).ToList(), total);
    }

    public async Task<int> CountAsync(string? statusFilter, bool mineOnly)
    {
        var (customerId, _) = await ResolveCustomerScopeAsync(mineOnly);
        return await _repo.CountAsync(statusFilter, customerId);
    }

    /// <summary>Returns (customerId, ok). ok=false when the scope is wrong.</summary>
    private async Task<(int? customerId, bool ok)> ResolveCustomerScopeAsync(bool mineOnly)
    {
        if (mineOnly || _current.IsCustomerScoped)
        {
            var customer = await _customers.GetByUserIdAsync(_current.UserId!);
            if (customer is null) return (null, true);
            return (customer.CustomerID, true);
        }
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can list all transactions.");
        return (null, true);
    }

    public async Task<RentalTransactionDetailsDto> GetDetailsAsync(int id)
    {
        var t = await _repo.GetWithItemsAsync(id)
            ?? throw new NotFoundException($"Rental transaction {id} not found.");

        EnsureMayRead(t);
        return MapDetails(t);
    }

    public async Task<int> CheckoutAsync(int reservationId, string actorUserId)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can check out rentals.");

        var r = await _reservations.GetWithItemsAsync(reservationId)
            ?? throw new NotFoundException($"Reservation {reservationId} not found.");

        if (r.Status != ReservationStatus.Confirmed)
            throw new InvalidOperationException(
                $"Only Confirmed reservations can be checked out (current status: {r.Status}).");

        if (await _repo.GetByReservationAsync(reservationId) is not null)
            throw new InvalidOperationException(
                $"Reservation {reservationId} has already been checked out.");

        // Pin one concrete EquipmentItem per unit. The reservation's Confirm step already
        // moved items into Reserved status; here we pick concrete units from that pool,
        // preferring the AssignedItemID recorded by Confirm.
        var itemLines = new List<RentalTransactionItem>();
        var pinnedIds = new List<int>();
        foreach (var line in r.Items)
        {
            var reserved = await _items.ListReservedByEquipmentAsync(line.EquipmentID);
            var picked = reserved
                .Where(i => !pinnedIds.Contains(i.ItemID))
                .Take(line.Quantity)
                .ToList();

            // If we still need units, fall back to the Available pool (covers reservations
            // that pre-dated the per-line AssignedItemID logic, plus any manual edge cases).
            if (picked.Count < line.Quantity)
            {
                var stillNeeded = line.Quantity - picked.Count;
                var fallback = await _items.ListAvailableByEquipmentAsync(line.EquipmentID);
                picked.AddRange(fallback
                    .Where(i => !pinnedIds.Contains(i.ItemID))
                    .Take(stillNeeded));
            }

            if (picked.Count < line.Quantity)
                throw new InvalidOperationException(
                    $"Not enough units for equipment {line.EquipmentID} at checkout (need {line.Quantity}, have {picked.Count}).");

            foreach (var item in picked)
            {
                pinnedIds.Add(item.ItemID);
                itemLines.Add(new RentalTransactionItem
                {
                    EquipmentID = line.EquipmentID,
                    Quantity = 1,
                    EquipmentItemID = item.ItemID,
                });
            }
        }

        // Flip pinned items Reserved → CheckedOut.
        foreach (var itemId in pinnedIds.Distinct())
        {
            await _itemService.ChangeStatusAsync(itemId, nameof(AvailabilityStatus.CheckedOut), actorUserId);
        }

        var tx = new RTEntity
        {
            ReservationID = r.ReservationID,
            CustomerID = r.CustomerID,
            CheckoutDate = DateTime.UtcNow,
            ExpectedReturnDate = r.RentalEndDate,
            Status = RentalTransactionStatus.Active,
            TotalAmount = r.TotalEstimatedCost,
            ProcessedByUserId = _current.UserId,
            Items = itemLines,
        };

        await _repo.AddAsync(tx);
        await _repo.SaveChangesAsync();

        // ponytail: promote the source reservation to CheckedOut so the Details
        // view hides the Check out button. Cancelled/Expired/Completed stay as-is.
        r.Status = ReservationStatus.CheckedOut;
        _reservations.Update(r);
        await _reservations.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "RentalTransaction.Checkout", "RentalTransaction", tx.RentalTransactionID.ToString(),
            newValues: new
            {
                tx.ReservationID,
                tx.CustomerID,
                tx.CheckoutDate,
                tx.ExpectedReturnDate,
                tx.TotalAmount,
                ItemCount = itemLines.Count,
            });

        return tx.RentalTransactionID;
    }

    public async Task ReturnAsync(RentalTransactionReturnDto dto, string actorUserId)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can record returns.");

        var t = await _repo.GetWithItemsAsync(dto.RentalTransactionID)
            ?? throw new NotFoundException($"Rental transaction {dto.RentalTransactionID} not found.");

        if (t.Status != RentalTransactionStatus.Active)
            throw new InvalidOperationException(
                $"Only Active transactions can be returned (current status: {t.Status}).");

        var pinned = t.Items
            .Where(i => i.EquipmentItemID.HasValue)
            .Select(i => i.EquipmentItemID!.Value)
            .Distinct()
            .ToList();

        // ponytail: damage-flagged returns go straight to InMaintenance so the
        // unit is quarantined and removed from the customer booking pool until
        // staff explicitly close the maintenance record. Non-damaged returns
        // go back to Available as before.
        var postReturnStatus = dto.FlaggedForDamage
            ? AvailabilityStatus.InMaintenance
            : AvailabilityStatus.Available;

        foreach (var itemId in pinned)
        {
            await _itemService.ChangeStatusAsync(itemId, postReturnStatus.ToString(), actorUserId);
        }

        var oldStatus = t.Status;
        t.ReturnDate = DateTime.UtcNow;
        t.ConditionNotes = dto.ConditionNotes;
        t.FlaggedForDamage = dto.FlaggedForDamage;
        t.Status = RentalTransactionStatus.Returned;
        t.ReturnedToUserId = _current.UserId;
        _repo.Update(t);
        await _repo.SaveChangesAsync();

        // ponytail: when the source reservation is CheckedOut (set by CheckoutAsync),
        // promote it to Completed so the Details view shows the success state.
        // Cancelled/Expired/Completed stay as-is.
        if (t.ReservationID.HasValue)
        {
            var reservation = await _reservations.GetAsync(t.ReservationID.Value);
            if (reservation is not null && reservation.Status == ReservationStatus.CheckedOut)
            {
                reservation.Status = ReservationStatus.Completed;
                _reservations.Update(reservation);
                await _reservations.SaveChangesAsync();
            }
        }

        await _audit.LogAsync(
            actorUserId, "RentalTransaction.Return", "RentalTransaction", t.RentalTransactionID.ToString(),
            oldValues: new { Status = oldStatus.ToString() },
            newValues: new
            {
                Status = RentalTransactionStatus.Returned.ToString(),
                t.ReturnDate,
                t.ConditionNotes,
                ReturnedUnits = pinned.Count,
                FlaggedForDamage = t.FlaggedForDamage,
            });

        // Auto-generate the invoice now that the rental is fully closed out.
        // If generation fails (e.g. one already exists from a manual call), the rental
        // remains Returned — invoice can be regenerated from the Invoices UI later.
        try
        {
            await _billing.GenerateForReturnAsync(t.RentalTransactionID, actorUserId);
        }
        catch (InvalidOperationException)
        {
            // Invoice already exists — safe to ignore.
        }
    }

    public async Task CancelAsync(int id, string actorUserId, string? reason = null)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can cancel transactions.");

        var t = await _repo.GetWithItemsAsync(id)
            ?? throw new NotFoundException($"Rental transaction {id} not found.");

        if (t.Status != RentalTransactionStatus.Active)
            throw new InvalidOperationException(
                $"Only Active transactions can be cancelled (current status: {t.Status}).");

        var pinned = t.Items
            .Where(i => i.EquipmentItemID.HasValue)
            .Select(i => i.EquipmentItemID!.Value)
            .Distinct()
            .ToList();

        foreach (var itemId in pinned)
        {
            await _itemService.ChangeStatusAsync(itemId, nameof(AvailabilityStatus.Available), actorUserId);
        }

        var oldStatus = t.Status;
        t.Status = RentalTransactionStatus.Cancelled;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            t.ConditionNotes = string.IsNullOrEmpty(t.ConditionNotes)
                ? $"Cancelled: {reason}"
                : $"{t.ConditionNotes}\nCancelled: {reason}";
        }
        _repo.Update(t);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "RentalTransaction.Cancel", "RentalTransaction", t.RentalTransactionID.ToString(),
            oldValues: new { Status = oldStatus.ToString() },
            newValues: new { Status = RentalTransactionStatus.Cancelled.ToString() });
    }

    // ---- helpers ----

    private void EnsureMayRead(RTEntity t)
    {
        if (_current.IsAdmin || _current.IsStaff) return;
        if (_current.IsCustomerScoped && t.Customer.UserId == _current.UserId) return;
        throw new NotFoundException($"Rental transaction {t.RentalTransactionID} not found.");
    }

    private static RentalTransactionListItemDto MapList(RTEntity t) => new()
    {
        RentalTransactionID = t.RentalTransactionID,
        ReservationID = t.ReservationID ?? 0,
        CustomerID = t.CustomerID,
        CustomerName = t.Customer is null ? string.Empty : $"{t.Customer.FirstName} {t.Customer.LastName}",
        CheckoutDate = t.CheckoutDate,
        ExpectedReturnDate = t.ExpectedReturnDate,
        ReturnDate = t.ReturnDate,
        TotalAmount = t.TotalAmount,
        Status = t.Status.ToString(),
    };

    private static RentalTransactionDetailsDto MapDetails(RTEntity t) => new()
    {
        RentalTransactionID = t.RentalTransactionID,
        ReservationID = t.ReservationID ?? 0,
        CustomerID = t.CustomerID,
        CustomerName = t.Customer is null ? string.Empty : $"{t.Customer.FirstName} {t.Customer.LastName}",
        CustomerEmail = t.Customer?.Email ?? string.Empty,
        CheckoutDate = t.CheckoutDate,
        ExpectedReturnDate = t.ExpectedReturnDate,
        ReturnDate = t.ReturnDate,
        ConditionNotes = t.ConditionNotes,
        TotalAmount = t.TotalAmount,
        Status = t.Status.ToString(),
        ProcessedByUserName = t.ProcessedByUser is null
            ? null : $"{t.ProcessedByUser.FirstName} {t.ProcessedByUser.LastName}",
        ReturnedToUserName = t.ReturnedToUser is null
            ? null : $"{t.ReturnedToUser.FirstName} {t.ReturnedToUser.LastName}",
        Items = (t.Items ?? new List<RentalTransactionItem>()).Select(i => new RentalTransactionItemDto
        {
            RentalTransactionItemID = i.RentalTransactionItemID,
            EquipmentID = i.EquipmentID,
            EquipmentName = i.Equipment?.Name ?? string.Empty,
            Quantity = i.Quantity,
            DailyRate = i.Equipment?.DailyRate ?? 0m,
            LineTotal = (i.Equipment?.DailyRate ?? 0m) * i.Quantity,
            EquipmentItemID = i.EquipmentItemID,
            SerialNumber = i.EquipmentItem?.SerialNumber,
        }).ToList(),
    };
}
