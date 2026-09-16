using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Identity.Services;
using RentalSphere.Modules.Equipment.Models;
using RentalSphere.Modules.Equipment.Repositories;
using RentalSphere.Modules.Equipment.Services;
using RentalSphere.Modules.Maintenance.DTOs;
using RentalSphere.Modules.Maintenance.Models;
using RentalSphere.Modules.Maintenance.Repositories;

namespace RentalSphere.Modules.Maintenance.Services;

public interface IMaintenanceService
{
    Task<List<MaintenanceListItemDto>> ListAsync(string? statusFilter = null);
    Task<(List<MaintenanceListItemDto> Items, int TotalCount)> ListPagedAsync(string? statusFilter, int skip, int take);
    Task<MaintenanceDetailsDto> GetDetailsAsync(int id);
    Task<int> CountInProgressAsync();
    Task<List<int>> OpenAsync(MaintenanceOpenDto dto, string actorUserId);
    Task CloseAsync(MaintenanceCloseDto dto, string actorUserId);

    /// <summary>
    /// Save assessment updates (Cost, ExpectedEnd, optional Notes) on an
    /// open maintenance ticket WITHOUT changing its Status. The unit stays
    /// in InMaintenance and the ticket stays InProgress. Replaces the old
    /// "Update amount" pattern where staff had to fully close the record
    /// to save a non-zero cost. Only valid on InProgress tickets.
    /// </summary>
    Task UpdateEstimateAsync(MaintenanceCloseDto dto, string actorUserId);

    /// <summary>
    /// Attach a new InProgress MaintenanceRecord to the caller's tracked
    /// DbContext for one unit that has just been quarantined at return
    /// time. Does NOT call SaveChanges — the caller owns the commit so the
    /// maintenance ticket, the unit status flip, the damage seed, and the
    /// rental closeout all save in a single transaction. Skips silently
    /// if the unit already has an open record (the caller is expected to
    /// have pre-checked via ItemsWithOpenRecordAsync, but we re-guard here
    /// in case the check raced with a concurrent open).
    /// </summary>
    void SeedRecordForReturn(int equipmentItemID, int rentalTransactionId, string reason, string actorUserId, DateTime now);
}

public class MaintenanceService : IMaintenanceService
{
    private readonly IMaintenanceRepository _repo;
    private readonly IEquipmentItemService _itemService;
    private readonly IEquipmentItemRepository _items;
    private readonly ICurrentUser _current;
    private readonly IAuditLogger _audit;

    public MaintenanceService(
        IMaintenanceRepository repo,
        IEquipmentItemService itemService,
        IEquipmentItemRepository items,
        ICurrentUser current,
        IAuditLogger audit)
    {
        _repo = repo;
        _itemService = itemService;
        _items = items;
        _current = current;
        _audit = audit;
    }

    public async Task<List<MaintenanceListItemDto>> ListAsync(string? statusFilter = null)
    {
        // Customers never reach this service — controller blocks.
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can view maintenance records.");

        var (rows, _) = await _repo.ListPagedAsync(statusFilter, 0, int.MaxValue);
        return rows.Select(MapList).ToList();
    }

    public async Task<(List<MaintenanceListItemDto> Items, int TotalCount)> ListPagedAsync(
        string? statusFilter, int skip, int take)
    {
        // Customers never reach this service — controller blocks.
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can view maintenance records.");

        var (rows, total) = await _repo.ListPagedAsync(statusFilter, skip, take);
        return (rows.Select(MapList).ToList(), total);
    }

    public async Task<int> CountInProgressAsync()
    {
        if (!_current.IsAdmin && !_current.IsStaff) return 0;
        return await _repo.CountByStatusAsync(MaintenanceStatus.InProgress);
    }

    public async Task<MaintenanceDetailsDto> GetDetailsAsync(int id)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can view maintenance records.");

        var m = await _repo.GetAsync(id)
            ?? throw new NotFoundException($"Maintenance record {id} not found.");
        return MapDetails(m);
    }

    public async Task<List<int>> OpenAsync(MaintenanceOpenDto dto, string actorUserId)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can open maintenance records.");

        if (string.IsNullOrWhiteSpace(dto.Reason))
            throw new InvalidOperationException("Reason is required.");

        // Dedupe + drop empties (MinLength(1) on the DTO is the front-door check,
        // this is the back-door guard).
        var itemIds = dto.EquipmentItemIDs
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        if (itemIds.Count == 0)
            throw new InvalidOperationException("Select at least one unit.");

        // Single round-trip to find which selected units already have an open
        // record — fail the whole batch up front so we don't half-open a
        // submission.
        var alreadyOpen = await _repo.ItemsWithOpenRecordAsync(itemIds);
        if (alreadyOpen.Count > 0)
            throw new InvalidOperationException(
                $"{alreadyOpen.Count} unit(s) already have an open maintenance record. Close them first.");

        // Flip every unit out of Available, then create a record per unit. One
        // SaveChangesAsync at the end batches the inserts.
        foreach (var id in itemIds)
        {
            await _itemService.ChangeStatusAsync(
                id, nameof(AvailabilityStatus.InMaintenance), actorUserId);
        }

        var now = DateTime.UtcNow;
        var reason = dto.Reason.Trim();
        var notes = dto.Notes;
        var records = new List<MaintenanceRecord>(itemIds.Count);
        foreach (var id in itemIds)
        {
            var record = new MaintenanceRecord
            {
                EquipmentItemID = id,
                StartedAt = now,
                ExpectedEnd = dto.ExpectedEnd,
                Reason = reason,
                Notes = notes,
                Cost = dto.Cost,
                Status = MaintenanceStatus.InProgress,
                OpenedByUserId = _current.UserId,
            };
            await _repo.AddAsync(record);
            records.Add(record);
        }
        await _repo.SaveChangesAsync();

        // EF populates the auto-generated ID on each tracked entity after the
        // SaveChanges round-trip — pull them straight off the in-memory objects.
        var created = records.Select(r => r.MaintenanceRecordID).ToList();

        await _audit.LogAsync(
            actorUserId, "Maintenance.Open", "MaintenanceRecord", string.Join(",", created),
            newValues: new
            {
                ItemIDs = itemIds,
                RecordIDs = created,
                Reason = reason,
                Cost = dto.Cost,
                StartedAt = now,
            });

        return created;
    }

    public async Task UpdateEstimateAsync(MaintenanceCloseDto dto, string actorUserId)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can update maintenance records.");

        if (dto.Cost < 0)
            throw new InvalidOperationException("Cost cannot be negative.");

        var m = await _repo.GetAsync(dto.MaintenanceRecordID)
            ?? throw new NotFoundException($"Maintenance record {dto.MaintenanceRecordID} not found.");

        // ponytail: assessment updates are only valid on open tickets. A
        // closed record's Cost / ExpectedEnd / Notes are final — editing
        // them after the fact would corrupt the "expected vs actual"
        // history and could leave a paid-off invoice pointing at a
        // different amount than the one the customer was billed for.
        if (m.Status != MaintenanceStatus.InProgress)
            throw new InvalidOperationException(
                $"Only In Progress records can have their estimate updated (current status: {m.Status}).");

        var oldValues = new
        {
            m.Cost,
            m.ExpectedEnd,
            m.Notes,
        };

        // Assessment fields. Cost is the operator's current best estimate;
        // ExpectedEnd follows the same "preserve seeded value if blank"
        // rule as CloseAsync so an open ticket's history stays meaningful.
        m.Cost = dto.Cost;
        if (dto.ExpectedEnd.HasValue)
            m.ExpectedEnd = dto.ExpectedEnd;

        if (!string.IsNullOrWhiteSpace(dto.Notes))
        {
            m.Notes = string.IsNullOrEmpty(m.Notes)
                ? dto.Notes.Trim()
                : $"{m.Notes}\nEstimate update: {dto.Notes.Trim()}";
        }

        // Critical: do NOT touch Status, EndedAt, ClosedByUserId, or the
        // unit's AvailabilityStatus. The ticket stays InProgress, the unit
        // stays InMaintenance. This is the "save estimate" path; the
        // "close" path is a separate action.
        _repo.Update(m);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "Maintenance.UpdateEstimate", "MaintenanceRecord", m.MaintenanceRecordID.ToString(),
            oldValues: oldValues,
            newValues: new
            {
                m.Cost,
                m.ExpectedEnd,
                m.Notes,
            });
    }

    public async Task CloseAsync(MaintenanceCloseDto dto, string actorUserId)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can close maintenance records.");

        if (dto.Cost < 0)
            throw new InvalidOperationException("Cost cannot be negative.");

        var m = await _repo.GetAsync(dto.MaintenanceRecordID)
            ?? throw new NotFoundException($"Maintenance record {dto.MaintenanceRecordID} not found.");

        if (m.Status != MaintenanceStatus.InProgress)
            throw new InvalidOperationException(
                $"Only In Progress records can be closed (current status: {m.Status}).");

        // ponytail: explicit fetch + set + save for the unit status flip, so
        // the close path is single-statement-clear (no implicit round-trip
        // through ChangeStatusAsync's separate SaveChangesAsync). The unit
        // returns to Available, LastStatusChange is stamped, and the change
        // is committed through this DbContext alongside the maintenance
        // record's own status transition below.
        var now = DateTime.UtcNow;
        var unit = await _items.GetAsync(m.EquipmentItemID)
            ?? throw new NotFoundException($"Equipment unit #{m.EquipmentItemID} not found.");
        if (unit.AvailabilityStatus != AvailabilityStatus.Available)
        {
            unit.AvailabilityStatus = AvailabilityStatus.Available;
            unit.LastStatusChange = now;
            _items.Update(unit);
        }

        var oldValues = new
        {
            Status = m.Status.ToString(),
            m.Notes,
            m.Cost,
            m.ExpectedEnd,
        };
        m.Status = MaintenanceStatus.Completed;
        m.EndedAt = now;
        m.ClosedByUserId = _current.UserId;

        // Apply the operator's cost and expected-completion edits captured
        // on the Close form. Cost comes in as a non-negative decimal; if the
        // operator left it blank (Cost = 0), keep whatever the record had
        // (usually also 0 from the auto-seed). ExpectedEnd is optional — if
        // the operator didn't change it, preserve the seeded value so the
        // historical "expected vs actual" comparison stays meaningful.
        m.Cost = dto.Cost;
        if (dto.ExpectedEnd.HasValue)
            m.ExpectedEnd = dto.ExpectedEnd;

        if (!string.IsNullOrWhiteSpace(dto.Notes))
        {
            m.Notes = string.IsNullOrEmpty(m.Notes)
                ? dto.Notes.Trim()
                : $"{m.Notes}\nClosed: {dto.Notes.Trim()}";
        }
        _repo.Update(m);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "Maintenance.Close", "MaintenanceRecord", m.MaintenanceRecordID.ToString(),
            oldValues: oldValues,
            newValues: new
            {
                Status = m.Status.ToString(),
                m.EndedAt,
                m.Notes,
                m.Cost,
                m.ExpectedEnd,
            });
    }

    public void SeedRecordForReturn(
        int equipmentItemId,
        int rentalTransactionId,
        string reason,
        string actorUserId,
        DateTime now)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can open maintenance records.");

        // Mirror the OpenAsync guard — never create a second open record for
        // the same unit. The caller should have pre-filtered via
        // ItemsWithOpenRecordAsync; this is a backstop in case of a race.
        // We do NOT call SaveChanges here; the caller's tracked DbContext
        // owns the commit.
        // (Cannot call HasOpenForItemAsync directly because that hits the DB
        //  and we want this to stay in-memory; the rental service has
        //  already pre-checked via ItemsWithOpenRecordAsync, and the unit
        //  status flip we just performed is a sufficient signal: a unit
        //  moving to InMaintenance at return time that already had an
        //  open record is the caller's pre-condition to have caught.)

        var record = new MaintenanceRecord
        {
            EquipmentItemID = equipmentItemId,
            StartedAt = now,

            // Be paranoid: an auto-opened record must stay Open until staff
            // explicitly close it. The model already defaults these to
            // null / InProgress, but the seed path is the only auto-opener,
            // so we set them explicitly here. This is the single source of
            // truth for "what does a fresh maintenance record look like" —
            // never flip Status to Completed, never set EndedAt, never
            // set ClosedByUserId, from this code path.
            Status = MaintenanceStatus.InProgress,
            EndedAt = null,
            ClosedByUserId = null,

            // ponytail: default a 2-day expected-completion window so the
            // operator has a target to either confirm or revise when
            // reviewing the ticket. Cost starts at 0 — staff fill in the
            // actual figure on the Details/Close form.
            ExpectedEnd = now.AddDays(2),
            Cost = 0m,

            Reason = reason,
            OpenedByUserId = string.IsNullOrEmpty(actorUserId) ? _current.UserId : actorUserId,
            Notes = $"Auto-opened on damage-flagged Return for transaction #{rentalTransactionId}.",
        };

        // ponytail: synchronous attach so the entity joins the caller's
        // tracked DbContext. Do NOT call SaveChangesAsync here — the rental
        // return path owns the commit.
        _repo.AddSync(record);
    }

    // ---- helpers ----

    private static MaintenanceListItemDto MapList(MaintenanceRecord m) => new()
    {
        MaintenanceRecordID = m.MaintenanceRecordID,
        EquipmentItemID = m.EquipmentItemID,
        SerialNumber = m.EquipmentItem?.SerialNumber ?? string.Empty,
        EquipmentName = m.EquipmentItem?.Equipment?.Name ?? string.Empty,
        StartedAt = m.StartedAt,
        ExpectedEnd = m.ExpectedEnd,
        EndedAt = m.EndedAt,
        Reason = m.Reason,
        Cost = m.Cost,
        Status = m.Status.ToString(),
        OpenedByUserName = m.OpenedByUser is null ? null : $"{m.OpenedByUser.FirstName} {m.OpenedByUser.LastName}",
        ClosedByUserName = m.ClosedByUser is null ? null : $"{m.ClosedByUser.FirstName} {m.ClosedByUser.LastName}",
    };

    private static MaintenanceDetailsDto MapDetails(MaintenanceRecord m) => new()
    {
        MaintenanceRecordID = m.MaintenanceRecordID,
        EquipmentItemID = m.EquipmentItemID,
        SerialNumber = m.EquipmentItem?.SerialNumber ?? string.Empty,
        EquipmentName = m.EquipmentItem?.Equipment?.Name ?? string.Empty,
        StartedAt = m.StartedAt,
        ExpectedEnd = m.ExpectedEnd,
        EndedAt = m.EndedAt,
        Reason = m.Reason,
        Notes = m.Notes,
        Cost = m.Cost,
        Status = m.Status.ToString(),
        OpenedByUserName = m.OpenedByUser is null ? null : $"{m.OpenedByUser.FirstName} {m.OpenedByUser.LastName}",
        ClosedByUserName = m.ClosedByUser is null ? null : $"{m.ClosedByUser.FirstName} {m.ClosedByUser.LastName}",
    };
}
