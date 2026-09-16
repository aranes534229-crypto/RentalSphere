using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Identity.Services;
using RentalSphere.Modules.Billing.Services;
using RentalSphere.Modules.DamagePenalty.DTOs;
using RentalSphere.Modules.DamagePenalty.Models;
using RentalSphere.Modules.DamagePenalty.Repositories;
using DPEntity = RentalSphere.Modules.DamagePenalty.Models.DamagePenalty;

namespace RentalSphere.Modules.DamagePenalty.Services;

public interface IDamagePenaltyService
{
    Task<List<DamagePenaltyListItemDto>> ListAsync(string? statusFilter = null, int? rentalTransactionId = null, string? sort = null);
    Task<(List<DamagePenaltyListItemDto> Items, int TotalCount)> ListPagedAsync(
        string? statusFilter, int? rentalTransactionId, string? sort, int skip, int take);
    Task<DamagePenaltyDetailsDto> GetDetailsAsync(int id);
    Task<int> CreateAsync(DamagePenaltyCreateDto dto, string actorUserId);
    Task WaiveAsync(DamagePenaltyWaiveDto dto, string actorUserId);
    Task UpdateAmountAsync(int id, decimal amount, string actorUserId);

    /// <summary>
    /// Transition every Pending DamagePenalty with Amount &gt; 0 to
    /// AppliedToInvoice. Idempotent — running it twice in a row is a no-op.
    /// Used by the startup hook to clean up legacy records created before
    /// UpdateAmountAsync learned to flip the status automatically. Returns
    /// the number of records transitioned on this call.
    /// </summary>
    Task<int> BackfillPendingToAppliedAsync();
    Task<int> CountPendingAsync();

    /// <summary>
    /// Attach a Pending (Amount=0) DamagePenalty row per unit to the current
    /// DbContext change tracker. Does NOT call SaveChanges — the caller is
    /// expected to commit as part of a larger unit of work (e.g. the rental
    /// return closeout, so the penalty seed and the unit status flip are
    /// atomic). Returns the list of pending record ids in the order they
    /// were attached (the DB-assigned ids are populated after SaveChanges
    /// on the in-memory entity instances).
    /// </summary>
    void SeedPendingRecords(int rentalTransactionId, IReadOnlyList<int> equipmentItemIds, string actorUserId, string? notes = null);

    /// <summary>
    /// Attach a single transaction-level Pending DamagePenalty row of type
    /// <c>LateFee</c> to the current DbContext with a pre-calculated Amount
    /// and a description that includes the delay. Mirrors
    /// <see cref="SeedPendingRecords"/> but for the late-return penalty path
    /// — no per-unit attachment, no unit status flip. Does NOT call
    /// SaveChanges; the caller commits as part of the rental return closeout
    /// so the penalty seed is atomic with the rental row update.
    /// </summary>
    /// <param name="suggestedAmount">
    /// Pre-calculated fee (overdueDays * totalDailyRate). Staff can still
    /// adjust via the Update Amount form on the Details view.
    /// </param>
    /// <param name="daysLate">Whole-day ceiling of how late the return is.</param>
    /// <param name="expectedReturnDate">The rental's original expected return date (for the description).</param>
    void SeedLateFeeRecord(
        int rentalTransactionId,
        decimal suggestedAmount,
        int daysLate,
        DateTime expectedReturnDate,
        string actorUserId,
        string? notes = null);
}

public class DamagePenaltyService : IDamagePenaltyService
{
    private readonly IDamagePenaltyRepository _repo;
    private readonly IBillingService _billing;
    private readonly ICurrentUser _current;
    private readonly IAuditLogger _audit;

    public DamagePenaltyService(
        IDamagePenaltyRepository repo,
        IBillingService billing,
        ICurrentUser current,
        IAuditLogger audit)
    {
        _repo = repo;
        _billing = billing;
        _current = current;
        _audit = audit;
    }

    public async Task<List<DamagePenaltyListItemDto>> ListAsync(string? statusFilter = null, int? rentalTransactionId = null, string? sort = null)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can view damage records.");

        var rows = await _repo.ListAsync(statusFilter, rentalTransactionId, sort);
        return rows.Select(MapList).ToList();
    }

    public async Task<(List<DamagePenaltyListItemDto> Items, int TotalCount)> ListPagedAsync(
        string? statusFilter, int? rentalTransactionId, string? sort, int skip, int take)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can view damage records.");

        // Sort is now applied at the repository (DB) level — the repo returns
        // rows in the requested order, so we just skip/take here.
        var all = await _repo.ListAsync(statusFilter, rentalTransactionId, sort);
        var total = all.Count;
        var page = all.Skip(skip).Take(take).Select(MapList).ToList();
        return (page, total);
    }

    public async Task<DamagePenaltyDetailsDto> GetDetailsAsync(int id)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can view damage records.");

        var p = await _repo.GetAsync(id)
            ?? throw new NotFoundException($"Damage record {id} not found.");
        return MapDetails(p);
    }

    public async Task<int> CreateAsync(DamagePenaltyCreateDto dto, string actorUserId)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can record damage.");
        if (dto.Amount < 0)
            throw new InvalidOperationException("Amount cannot be negative.");
        if (string.IsNullOrWhiteSpace(dto.Description))
            throw new InvalidOperationException("Description is required.");

        var entity = new DPEntity
        {
            RentalTransactionID = dto.RentalTransactionID,
            PenaltyType = dto.PenaltyType,
            EquipmentID = dto.EquipmentID,
            EquipmentItemID = dto.EquipmentItemID,
            Description = dto.Description.Trim(),
            Amount = dto.Amount,
            Status = DamagePenaltyStatus.Pending,
            ReportedByUserId = _current.UserId,
            Notes = dto.Notes,
        };
        await _repo.AddAsync(entity);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "DamagePenalty.Create", "DamagePenalty", entity.DamagePenaltyID.ToString(),
            newValues: new
            {
                entity.RentalTransactionID,
                entity.PenaltyType,
                entity.Amount,
                entity.Description,
            });

        return entity.DamagePenaltyID;
    }

    public async Task WaiveAsync(DamagePenaltyWaiveDto dto, string actorUserId)
    {
        if (!_current.IsAdmin)
            throw new ForbiddenException("Only Admin can waive damage records.");

        var p = await _repo.GetAsync(dto.DamagePenaltyID)
            ?? throw new NotFoundException($"Damage record {dto.DamagePenaltyID} not found.");

        if (dto.Amount < 0)
            throw new InvalidOperationException("Waive amount cannot be negative.");

        var newWaived = p.WaivedAmount + dto.Amount;
        if (newWaived > p.Amount)
            throw new InvalidOperationException(
                $"Cannot waive more than the record amount (₱{p.Amount:N2}, already waived ₱{p.WaivedAmount:N2}).");

        var oldWaived = p.WaivedAmount;
        p.WaivedAmount = newWaived;
        if (p.WaivedAmount >= p.Amount)
        {
            p.Status = DamagePenaltyStatus.Waived;
            p.ResolvedAt = DateTime.UtcNow;
            p.ResolvedByUserId = _current.UserId;
        }
        _repo.Update(p);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "DamagePenalty.Waive", "DamagePenalty", p.DamagePenaltyID.ToString(),
            oldValues: new { WaivedAmount = oldWaived, p.Status },
            newValues: new { WaivedAmount = p.WaivedAmount, Status = p.Status.ToString() });
    }

    public async Task UpdateAmountAsync(int id, decimal amount, string actorUserId)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can update damage records.");

        var p = await _repo.GetAsync(id)
            ?? throw new NotFoundException($"Damage record {id} not found.");
        if (amount < 0)
            throw new InvalidOperationException("Amount cannot be negative.");

        var oldAmount = p.Amount;
        var oldStatus = p.Status;
        p.Amount = amount;

        // Once a non-zero amount is set, the penalty has been "assessed" —
        // flip the lifecycle status from Pending to AppliedToInvoice so the
        // dashboard, KPI cards, and reports treat it as finalized. The
        // matching InvoiceLine already references this penalty via
        // ReferenceId (seeded by BillingService.GenerateForReturnAsync), so
        // "Applied" here means "ready to be billed / already linked to an
        // invoice line". Stamping ResolvedAt + ResolvedByUserId on the same
        // record keeps the audit trail tight: who finalized the assessment
        // and when.
        //
        // Don't re-flip records that are already AppliedToInvoice or Waived
        // — those are terminal, and re-stamping ResolvedAt would clobber
        // the original Waive-time timestamp. (WaiveAsync manages its own
        // ResolvedAt for full-waiver cases.)
        if (amount > 0 && p.Status == DamagePenaltyStatus.Pending)
        {
            p.Status = DamagePenaltyStatus.AppliedToInvoice;
            p.ResolvedAt = DateTime.UtcNow;
            // Only stamp ResolvedByUserId with a real, FK-valid id. If the
            // caller didn't pass one AND the scoped user is empty, leave
            // the column null rather than fabricate a string and trip the
            // FK constraint to AspNetUsers (SQLite Error 19).
            var resolvedActor = string.IsNullOrEmpty(actorUserId) ? _current.UserId : actorUserId;
            p.ResolvedByUserId = string.IsNullOrEmpty(resolvedActor) ? null : resolvedActor;
        }

        _repo.Update(p);
        await _repo.SaveChangesAsync();

        // Push the new amount into any linked invoice line and recompute totals.
        // Ignore not-found: penalty might not have a linked invoice yet (admin edits first).
        try
        {
            await _billing.SyncDamageLineAsync(id, actorUserId);
        }
        catch (NotFoundException) { }

        await _audit.LogAsync(
            actorUserId, "DamagePenalty.UpdateAmount", "DamagePenalty", id.ToString(),
            oldValues: new
            {
                Amount = oldAmount,
                Status = oldStatus.ToString(),
                ResolvedAt = (DateTime?)null,
            },
            newValues: new
            {
                Amount = amount,
                Status = p.Status.ToString(),
                ResolvedAt = p.ResolvedAt,
            });
    }

    public async Task<int> CountPendingAsync()
    {
        if (!_current.IsAdmin && !_current.IsStaff) return 0;
        return await _repo.CountByStatusAsync(DamagePenaltyStatus.Pending);
    }

    public async Task<int> BackfillPendingToAppliedAsync()
    {
        // ponytail: the Admin/Staff guard in every other method assumes an
        // active HttpContext (a user is signed in). This method is the
        // data-migration hook for legacy Pending records that already have
        // a non-zero Amount — it does not need a role check, AND it must
        // not stamp a fake user id into ResolvedByUserId, because the FK to
        // AspNetUsers is OnDelete(DeleteBehavior.Restrict) and SQLite will
        // reject an insert/update that points at a non-existent user. The
        // safe migration: flip Status, stamp ResolvedAt, leave
        // ResolvedByUserId = null (the column is nullable). The audit log
        // records the migration separately so admins can see when and how
        // the lifecycle moved.
        //
        // If a real signed-in user is in scope (rare — this method is
        // primarily for unattended data fixes), we use their id; otherwise
        // we leave the column null rather than fabricate one.

        var rows = await _repo.ListAsync("Pending", rentalTransactionId: null);
        var candidates = rows.Where(p => p.Amount > 0).ToList();
        if (candidates.Count == 0) return 0;

        var now = DateTime.UtcNow;
        var realActor = string.IsNullOrEmpty(_current.UserId) ? null : _current.UserId;
        var transitions = new List<(int Id, decimal Amount, DateTime? ResolvedAt, string? Actor)>();
        foreach (var p in candidates)
        {
            p.Status = DamagePenaltyStatus.AppliedToInvoice;
            p.ResolvedAt = now;
            // ONLY set ResolvedByUserId when we have a verified user id.
            // Setting it to a fabricated string would violate the FK
            // constraint to AspNetUsers (SQLite Error 19).
            p.ResolvedByUserId = realActor;
            _repo.Update(p);
            transitions.Add((p.DamagePenaltyID, p.Amount, p.ResolvedAt, realActor));
        }
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            realActor ?? "system", "DamagePenalty.BackfillPendingToApplied", "DamagePenalty", "batch",
            newValues: new
            {
                Count = transitions.Count,
                ResolvedAt = now,
                Records = transitions,
            });

        return transitions.Count;
    }

    public void SeedPendingRecords(
        int rentalTransactionId,
        IReadOnlyList<int> equipmentItemIds,
        string actorUserId,
        string? notes = null)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can record damage.");

        if (equipmentItemIds.Count == 0) return;

        // ponytail: use the synchronous Add on DbSet so the entity is attached
        // to the caller's tracked DbContext — NOT a new DbContext from a
        // separate scope. We deliberately do NOT call _repo.SaveChangesAsync
        // here; the rental-transaction service owns the commit so the seed
        // is atomic with the unit status flip and the rental closeout.
        foreach (var itemId in equipmentItemIds)
        {
            if (itemId <= 0) continue;
            var penalty = new DPEntity
            {
                RentalTransactionID = rentalTransactionId,
                EquipmentItemID = itemId,
                PenaltyType = DamagePenaltyType.Damage,
                Description = "Damage flagged at return — assess amount to finalize.",
                Amount = 0m,
                Status = DamagePenaltyStatus.Pending,
                ReportedByUserId = string.IsNullOrEmpty(actorUserId) ? _current.UserId : actorUserId,
                Notes = notes,
            };
            // Resolve synchronously to the underlying DbContext so it joins
            // the caller's unit-of-work. We can't use the async AddAsync path
            // here because that would require an await and this method is
            // synchronous on purpose.
            _repo.AddSync(penalty);
        }
    }

    public void SeedLateFeeRecord(
        int rentalTransactionId,
        decimal suggestedAmount,
        int daysLate,
        DateTime expectedReturnDate,
        string actorUserId,
        string? notes = null)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can record a late return penalty.");

        // Transaction-level penalty: no EquipmentItemID, no EquipmentID. The
        // late fee is a single charge against the transaction as a whole,
        // independent of how many units were on the rental. We seed a
        // pre-calculated Amount (overdueDays * totalDailyRate) so staff have a
        // sensible default to review; the Update Amount form on the Details
        // view still lets them adjust before the invoice is finalized. The
        // description spells out the delay so the operator can verify the
        // math at a glance.
        var penalty = new DPEntity
        {
            RentalTransactionID = rentalTransactionId,
            PenaltyType = DamagePenaltyType.LateFee,
            Description = $"Late return by {daysLate} day(s) based on expected date {expectedReturnDate:yyyy-MM-dd}",
            Amount = suggestedAmount < 0m ? 0m : suggestedAmount,
            Status = DamagePenaltyStatus.Pending,
            ReportedByUserId = string.IsNullOrEmpty(actorUserId) ? _current.UserId : actorUserId,
            Notes = notes,
        };
        _repo.AddSync(penalty);
    }

    private static DamagePenaltyListItemDto MapList(DPEntity p) => new()
    {
        DamagePenaltyID = p.DamagePenaltyID,
        RentalTransactionID = p.RentalTransactionID,
        EquipmentName = p.Equipment?.Name,
        SerialNumber = p.EquipmentItem?.SerialNumber,
        PenaltyType = p.PenaltyType,
        Description = p.Description,
        Amount = p.Amount,
        WaivedAmount = p.WaivedAmount,
        Status = p.Status,
        ReportedAt = p.ReportedAt,
        ReportedByUserName = p.ReportedByUser is null ? null : $"{p.ReportedByUser.FirstName} {p.ReportedByUser.LastName}",
    };

    private static DamagePenaltyDetailsDto MapDetails(DPEntity p) => new()
    {
        DamagePenaltyID = p.DamagePenaltyID,
        RentalTransactionID = p.RentalTransactionID,
        EquipmentName = p.Equipment?.Name,
        SerialNumber = p.EquipmentItem?.SerialNumber,
        PenaltyType = p.PenaltyType,
        Description = p.Description,
        Amount = p.Amount,
        WaivedAmount = p.WaivedAmount,
        Status = p.Status,
        ReportedAt = p.ReportedAt,
        ResolvedAt = p.ResolvedAt,
        ReportedByUserName = p.ReportedByUser is null ? null : $"{p.ReportedByUser.FirstName} {p.ReportedByUser.LastName}",
        ResolvedByUserName = p.ResolvedByUser is null ? null : $"{p.ResolvedByUser.FirstName} {p.ResolvedByUser.LastName}",
        Notes = p.Notes,
    };
}
