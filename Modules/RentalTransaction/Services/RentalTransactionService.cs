using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Identity.Services;
using RentalSphere.Modules.Billing.Services;
using RentalSphere.Modules.CustomerManagement.Repositories;
using RentalSphere.Modules.DamagePenalty.Services;
using RentalSphere.Modules.Equipment.Models;
using RentalSphere.Modules.Equipment.Repositories;
using RentalSphere.Modules.Equipment.Services;
using RentalSphere.Modules.Maintenance.Services;
using RentalSphere.Modules.Maintenance.Repositories;
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
    Task<int> CountOverdueAsync(int? customerId = null);
    Task<int> CountOverdueForCurrentUserAsync();
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
    private readonly IDamagePenaltyService _damagePenalties;
    private readonly IMaintenanceService _maintenance;
    private readonly IMaintenanceRepository _maintenanceRepo;
    private readonly ICurrentUser _current;
    private readonly IAuditLogger _audit;

    public RentalTransactionService(
        IRentalTransactionRepository repo,
        IReservationRepository reservations,
        IEquipmentItemRepository items,
        IEquipmentItemService itemService,
        ICustomerRepository customers,
        IBillingService billing,
        IDamagePenaltyService damagePenalties,
        IMaintenanceService maintenance,
        IMaintenanceRepository maintenanceRepo,
        ICurrentUser current,
        IAuditLogger audit)
    {
        _repo = repo;
        _reservations = reservations;
        _items = items;
        _itemService = itemService;
        _customers = customers;
        _billing = billing;
        _damagePenalties = damagePenalties;
        _maintenance = maintenance;
        _maintenanceRepo = maintenanceRepo;
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

    public async Task<int> CountOverdueAsync(int? customerId = null)
    {
        // Staff/Admin get a global overdue count; passing a customerId scopes
        // the count to that customer's overdue rentals for the customer-portal
        // sidebar badge.
        if (customerId.HasValue)
        {
            return await _repo.CountOverdueForCustomerAsync(customerId.Value);
        }
        if (!_current.IsAdmin && !_current.IsStaff) return 0;
        return await _repo.CountOverdueAsync();
    }

    /// <summary>
    /// Role-aware overdue count used by the sidebar ViewComponent. Resolves
    /// the current user to a Customer when in customer scope and forwards
    /// the id; returns the global overdue count for staff. No-op for
    /// anonymous or other roles.
    /// </summary>
    public async Task<int> CountOverdueForCurrentUserAsync()
    {
        if (_current.IsCustomerScoped)
        {
            var customer = await _customers.GetByUserIdAsync(_current.UserId!);
            if (customer is null) return 0;
            return await CountOverdueAsync(customer.CustomerID);
        }
        if (!_current.IsAdmin && !_current.IsStaff) return 0;
        return await CountOverdueAsync();
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

        // Pin one concrete EquipmentItem per unit. Pull items physically bookable
        // AND not currently pinned to a Confirmed/CheckedOut reservation overlapping
        // the checkout window half-open. Per-item status is unreliable for "is this
        // unit free"; the reservation table is the source of truth.
        var itemLines = new List<RentalTransactionItem>();
        var pinnedIds = new List<int>();
        foreach (var line in r.Items)
        {
            var free = await _items.ListFreeForRangeAsync(
                line.EquipmentID, r.RentalStartDate, r.RentalEndDate,
                excludeReservationId: r.ReservationID);
            var picked = free
                .Where(i => !pinnedIds.Contains(i.ItemID))
                .Take(line.Quantity)
                .ToList();

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

        // ponytail: maintenance-flagged returns go straight to InMaintenance so
        // the unit is quarantined and removed from the customer booking pool
        // until staff explicitly close the maintenance record. Late-fee-only
        // returns (equipment is fine, just past due) go back to Available —
        // we never quarantine a unit that's in good condition.
        var postReturnStatus = dto.FlagForMaintenance
            ? AvailabilityStatus.InMaintenance
            : AvailabilityStatus.Available;

        // Backfill any unpinned line items with a unit chosen by the operator
        // when the return is flagged for maintenance. This is what makes "9
        // Avail / 1 Mx" reflect correctly on the Equipment Units page after a
        // damaged walk-in return or any line that was created without a serial.
        var explicitAssignments = (dto.DamageAssignments ?? new())
            .GroupBy(a => a.RentalTransactionItemID)
            .ToDictionary(g => g.Key, g => g.Last().EquipmentItemID);

        if (dto.FlagForMaintenance)
        {
            foreach (var line in t.Items.Where(i => !i.EquipmentItemID.HasValue))
            {
                if (!explicitAssignments.TryGetValue(line.RentalTransactionItemID, out var chosenId) || chosenId <= 0)
                    throw new InvalidOperationException(
                        $"Line {line.RentalTransactionItemID} ({line.Equipment?.Name ?? "equipment"}) has no pinned unit. " +
                        "Pick a unit to quarantine before flagging this return for damage.");

                // Verify the chosen unit is actually a unit of this equipment
                // row and not already InMaintenance/Retired — prevent the
                // operator from accidentally quarantining a unit that was
                // already off-rotation for someone else's repair.
                var chosen = await _items.GetAsync(chosenId)
                    ?? throw new NotFoundException($"Equipment unit #{chosenId} not found.");
                if (chosen.EquipmentID != line.EquipmentID)
                    throw new InvalidOperationException(
                        $"Unit {chosen.SerialNumber} is not a unit of {line.Equipment?.Name}.");
                if (chosen.AvailabilityStatus == AvailabilityStatus.Retired)
                    throw new InvalidOperationException(
                        $"Unit {chosen.SerialNumber} is retired and cannot be quarantined.");
                if (chosen.AvailabilityStatus == AvailabilityStatus.InMaintenance)
                    throw new InvalidOperationException(
                        $"Unit {chosen.SerialNumber} is already InMaintenance. Close the existing maintenance record first.");

                line.EquipmentItemID = chosenId;
                // The line is already tracked by EF (Include in GetWithItemsAsync);
                // setting EquipmentItemID is enough — SaveChanges below will persist it.
            }
        }

        var pinned = t.Items
            .Where(i => i.EquipmentItemID.HasValue)
            .Select(i => i.EquipmentItemID!.Value)
            .Distinct()
            .ToList();

        // ponytail: flip the pinned units' AvailabilityStatus AND seed the
        // pending DamagePenalty rows as part of the SAME DbContext commit as
        // the rental-transaction closeout. Three concerns (rental closeout +
        // unit status flip + damage seed) all share one SaveChanges at the
        // bottom of this method, so they're atomic.
        //
        // Req 1a: explicitly fetch the matching EquipmentItem entity per
        // pinned id (GetAsync gives a fresh read; do not rely on the
        // Include-tracked navigation on t.Items because that's the stale
        // snapshot EF loaded with the rental).
        // Req 1b: item.AvailabilityStatus = AvailabilityStatus.InMaintenance
        // (when FlaggedForDamage) — explicit field assignment on the tracked
        // entity, no per-unit SaveChanges.
        // Req 1c: _damagePenalties.SeedPendingRecords attaches a Pending
        // (Amount=0) DamagePenalty row per unit to the SAME DbContext.
        var unitStatusChanges = new List<(int ItemID, string Old, string New)>();
        if (pinned.Count > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var itemId in pinned)
            {
                // 1a. Explicit fetch.
                var item = await _items.GetAsync(itemId)
                    ?? throw new NotFoundException($"Equipment unit #{itemId} not found.");

                // 1b. Explicit status flip. Set the field on the tracked
                // entity; the SaveChanges at the bottom of this method
                // commits the change.
                var oldStatus = item.AvailabilityStatus;
                if (oldStatus != postReturnStatus)
                {
                    item.AvailabilityStatus = postReturnStatus;
                    item.LastStatusChange = now;
                    unitStatusChanges.Add((item.ItemID, oldStatus.ToString(), postReturnStatus.ToString()));
                }
            }
        }

        // 1c. Seed the pending damage records. Only when the operator
        // explicitly flagged the return for maintenance. The service attaches
        // to the current DbContext; SaveChanges at the bottom commits them in
        // the same transaction as the unit status flip and the rental closeout.
        if (dto.FlagForMaintenance && pinned.Count > 0)
        {
            _damagePenalties.SeedPendingRecords(
                t.RentalTransactionID, pinned, actorUserId, notes: dto.ConditionNotes);
        }

        // 1c-late. Seed a single transaction-level LateFee DamagePenalty when
        // the operator flagged the return for late penalty. Equipment units
        // are NOT quarantined in this path — the unit status is already set
        // to Available above. The penalty row carries no EquipmentItemID so
        // it's a transaction-level fee, not a per-unit charge. We pre-fill
        // Amount = overdueDays * totalDailyRate so the operator has a
        // sensible default; the Update Amount form on the Details view still
        // lets them adjust before the invoice is finalized.
        if (dto.FlagForLatePenalty)
        {
            var overdueDays = Math.Max(1, (int)Math.Ceiling((DateTime.UtcNow - t.ExpectedReturnDate).TotalDays));
            var totalDailyRate = t.Items.Sum(i => i.Quantity * (i.Equipment?.DailyRate ?? 0m));
            var suggestedAmount = overdueDays * totalDailyRate;
            _damagePenalties.SeedLateFeeRecord(
                t.RentalTransactionID,
                suggestedAmount,
                overdueDays,
                t.ExpectedReturnDate,
                actorUserId,
                notes: dto.ConditionNotes);
        }

        // 1d. Auto-open a MaintenanceRecord per quarantined unit so the unit
        // has an active repair ticket on the Maintenance dashboard the
        // moment the return is recorded. Only when the operator flagged
        // the return for maintenance. The service attaches to the caller's
        // tracked DbContext via AddSync (no SaveChanges) so the maintenance
        // record commits atomically with the unit status flip, the damage
        // seed, and the rental closeout in the single SaveChanges at the
        // bottom.
        //
        // Req 2: MaintenanceRecord.EquipmentItemID = unit ID,
        //        Status = InProgress, Reason seeded from the rental context,
        //        StartedAt = now (UTC).
        if (dto.FlagForMaintenance && pinned.Count > 0)
        {
            // Pre-filter units that already have an open record so we
            // don't double-open. ItemsWithOpenRecordAsync does one DB hit
            // and returns the set; we skip those when seeding.
            var alreadyOpen = await _maintenanceRepo.ItemsWithOpenRecordAsync(pinned);
            var now = DateTime.UtcNow;
            var seedReason = $"Damage flagged on Return for Tx #{t.RentalTransactionID}";
            foreach (var itemId in pinned)
            {
                if (alreadyOpen.Contains(itemId)) continue;
                _maintenance.SeedRecordForReturn(
                    equipmentItemID: itemId,
                    rentalTransactionId: t.RentalTransactionID,
                    reason: seedReason,
                    actorUserId: actorUserId,
                    now: now);
            }
        }

        var oldTxStatus = t.Status;
        t.ReturnDate = DateTime.UtcNow;
        t.ConditionNotes = dto.ConditionNotes;
        // The persisted FlaggedForDamage flag drives BillingService:
        // it adds a damage-assessment invoice line per pending DamagePenalty
        // for this transaction. Either the maintenance flag or the late
        // penalty flag produces pending penalty rows, so the persisted flag
        // is the OR of the two.
        t.FlaggedForDamage = dto.FlagForMaintenance || dto.FlagForLatePenalty;
        t.Status = RentalTransactionStatus.Returned;
        t.ReturnedToUserId = _current.UserId;
        _repo.Update(t);

        // Single SaveChanges commits: rental closeout + every unit status
        // flip + every pending DamagePenalty row. If any of them fails, the
        // whole batch rolls back.
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
            oldValues: new { Status = oldTxStatus.ToString() },
            newValues: new
            {
                Status = RentalTransactionStatus.Returned.ToString(),
                t.ReturnDate,
                t.ConditionNotes,
                ReturnedUnits = pinned.Count,
                FlaggedForDamage = t.FlaggedForDamage,
                UnitStatusChanges = unitStatusChanges,
            });

        // Auto-generate the invoice now that the rental is fully closed out.
        // The narrow InvalidOperationException catch is for the specific
        // "invoice already exists" path in BillingService; anything else
        // (DbUpdateException, NullReferenceException, etc.) propagates so the
        // operator sees a 500 with the real cause instead of a silent
        // "looks fine, but invoice is missing" outcome. We still write an
        // audit entry for the swallowed case so it leaves a trail.
        try
        {
            await _billing.GenerateForReturnAsync(t.RentalTransactionID, actorUserId);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already", StringComparison.OrdinalIgnoreCase))
        {
            await _audit.LogAsync(
                actorUserId, "RentalTransaction.Return.InvoiceSkipped", "RentalTransaction", t.RentalTransactionID.ToString(),
                newValues: new { Reason = ex.Message });
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
