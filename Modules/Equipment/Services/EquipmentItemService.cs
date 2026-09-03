using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Identity.Services;
using RentalSphere.Modules.Equipment.Controllers;
using RentalSphere.Modules.Equipment.DTOs;
using RentalSphere.Modules.Equipment.Models;
using RentalSphere.Modules.Equipment.Repositories;

namespace RentalSphere.Modules.Equipment.Services;

public interface IEquipmentItemService
{
    Task<List<EquipmentItemListItemDto>> ListAsync(int? equipmentId = null, string? status = null, string? search = null, int? categoryId = null);
    Task<(List<EquipmentItemListItemDto> Items, int TotalCount)> ListPagedAsync(
        int? equipmentId, string? status, string? search, int skip, int take, int? categoryId = null);
    Task<(List<EquipmentParentSummaryDto> Parents, int TotalCount)> ListParentsWithCountsPagedAsync(
        int? categoryId, string? search, int skip, int take);
    Task<EquipmentItemDetailsDto> GetDetailsAsync(int id);
    Task<(int ItemID, string SerialNumber)> CreateAsync(EquipmentItemCreateDto dto, string actorUserId);
    Task UpdateAsync(EquipmentItemEditDto dto, string actorUserId);
    Task DeleteAsync(int id, string actorUserId);
    Task ChangeStatusAsync(int id, string newStatus, string actorUserId);
}

public class EquipmentItemService : IEquipmentItemService
{
    private readonly IEquipmentItemRepository _items;
    private readonly IEquipmentRepository _equipment;
    private readonly ICurrentUser _current;
    private readonly IAuditLogger _audit;

    public EquipmentItemService(
        IEquipmentItemRepository items,
        IEquipmentRepository equipment,
        ICurrentUser current,
        IAuditLogger audit)
    {
        _items = items;
        _equipment = equipment;
        _current = current;
        _audit = audit;
    }

    public async Task<List<EquipmentItemListItemDto>> ListAsync(int? equipmentId = null, string? status = null, string? search = null, int? categoryId = null)
    {
        // Non-paged callers (e.g. ChangeStatusAsync) get everything.
        var (items, _) = await ListPagedAsync(equipmentId, status, search, 0, int.MaxValue, categoryId);
        return items;
    }

    public async Task<(List<EquipmentItemListItemDto> Items, int TotalCount)> ListPagedAsync(
        int? equipmentId, string? status, string? search, int skip, int take, int? categoryId = null)
    {
        var total = await _items.CountAsync(equipmentId, status, search, categoryId);
        var rows = await _items.ListAsync(equipmentId, status, search, categoryId);

        // Customers only see Available items (and only across Available catalog rows).
        // Applied before paging so the page slice reflects what the customer is allowed to see.
        if (_current.IsCustomer)
        {
            rows = rows.Where(r => r.Equipment.Status == EquipmentStatus.Available
                                && r.AvailabilityStatus == AvailabilityStatus.Available).ToList();
        }

        var mapped = rows.Select(MapList).ToList();
        var page = mapped.Skip(skip).Take(take).ToList();
        return (page, total);
    }

    public async Task<(List<EquipmentParentSummaryDto> Parents, int TotalCount)> ListParentsWithCountsPagedAsync(
        int? categoryId, string? search, int skip, int take)
    {
        var total = await _items.CountParentsAsync(categoryId, search);
        var parents = await _items.ListParentsWithCountsAsync(categoryId, search, skip, take);
        if (parents.Count == 0) return (new List<EquipmentParentSummaryDto>(), 0);

        var statusCounts = await _items.GetStatusCountsForParentsAsync(parents.Select(p => p.EquipmentID));

        var result = parents.Select(p => new EquipmentParentSummaryDto
        {
            EquipmentID = p.EquipmentID,
            Name = p.Name,
            CategoryID = p.CategoryID,
            CategoryName = p.Category?.Name ?? "—",
            DailyRate = p.DailyRate,
            Status = p.Status.ToString(),
            TotalCount = statusCounts.Where(kv => kv.Key.EquipmentID == p.EquipmentID).Sum(kv => kv.Value),
            AvailableCount = statusCounts.GetValueOrDefault((p.EquipmentID, nameof(AvailabilityStatus.Available))),
            ReservedCount = statusCounts.GetValueOrDefault((p.EquipmentID, nameof(AvailabilityStatus.Reserved))),
            CheckedOutCount = statusCounts.GetValueOrDefault((p.EquipmentID, nameof(AvailabilityStatus.CheckedOut))),
            MaintCount = statusCounts.GetValueOrDefault((p.EquipmentID, nameof(AvailabilityStatus.InMaintenance))),
            RetiredCount = statusCounts.GetValueOrDefault((p.EquipmentID, nameof(AvailabilityStatus.Retired))),
        }).ToList();

        return (result, total);
    }

    public async Task<EquipmentItemDetailsDto> GetDetailsAsync(int id)
    {
        var i = await _items.GetAsync(id) ?? throw new NotFoundException($"Item {id} not found.");
        return new EquipmentItemDetailsDto
        {
            ItemID = i.ItemID,
            EquipmentID = i.EquipmentID,
            EquipmentName = i.Equipment.Name,
            SerialNumber = i.SerialNumber,
            AvailabilityStatus = i.AvailabilityStatus.ToString(),
            Location = i.Location,
            LastStatusChange = i.LastStatusChange,
        };
    }

    public async Task<(int ItemID, string SerialNumber)> CreateAsync(EquipmentItemCreateDto dto, string actorUserId)
    {
        // Service-layer guard: only Admin/Staff can create items.
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can create equipment items.");

        var eq = await _equipment.GetAsync(dto.EquipmentID)
            ?? throw new NotFoundException($"Equipment {dto.EquipmentID} not found.");

        var prefix = !string.IsNullOrWhiteSpace(eq.SerialPrefix) ? eq.SerialPrefix : "EQ-";

        // ponytail: read-then-insert is racy by design. The unique index on
        // SerialNumber catches the race at the DB level; we re-check + retry
        // once before bubbling up a friendly error.
        var existingSerials = await _items.ListSerialNumbersAsync(dto.EquipmentID);
        string? newSerial = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var counter = SerialNumberGenerator.NextCounter(existingSerials, prefix);
            var candidate = SerialNumberGenerator.Build(prefix, counter);
            if (!await _items.SerialNumberExistsAsync(candidate))
            {
                newSerial = candidate;
                break;
            }
            existingSerials.Add(candidate);
        }
        if (newSerial is null)
            throw new InvalidOperationException("Could not allocate a unique serial after 3 attempts. Please retry.");

        var newStatus = ParseStatus(dto.AvailabilityStatus);

        var item = new EquipmentItem
        {
            EquipmentID = dto.EquipmentID,
            SerialNumber = newSerial,
            AvailabilityStatus = newStatus,
            Location = dto.Location,
            LastStatusChange = DateTime.UtcNow,
        };
        await _items.AddAsync(item);
        await _items.SaveChangesAsync();

        // Sync parent StockQuantity if we created an Available item.
        if (newStatus == AvailabilityStatus.Available)
            AdjustStock(eq, +1);

        await _items.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "EquipmentItem.Create", "EquipmentItem", item.ItemID.ToString(),
            newValues: new { item.SerialNumber, item.EquipmentID, Status = item.AvailabilityStatus.ToString() });
        return (item.ItemID, item.SerialNumber);
    }

    public async Task UpdateAsync(EquipmentItemEditDto dto, string actorUserId)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can edit equipment items.");

        var item = await _items.GetAsync(dto.ItemID)
            ?? throw new NotFoundException($"Item {dto.ItemID} not found.");

        var newStatus = ParseStatus(dto.AvailabilityStatus);
        var newSerial = dto.SerialNumber.Trim();

        var statusChanged = item.AvailabilityStatus != newStatus;
        var serialChanged = !string.Equals(item.SerialNumber, newSerial, StringComparison.Ordinal);
        var involvesRetired = statusChanged && (item.AvailabilityStatus == AvailabilityStatus.Retired
                                          || newStatus == AvailabilityStatus.Retired);
        var editingRetiredUnit = item.AvailabilityStatus == AvailabilityStatus.Retired
                              && (statusChanged || serialChanged);

        // Admin-only operations per CLAUDE.md: Staff do day-to-day status
        // flips among the four operational statuses, but cannot retire,
        // restore from Retired, edit the serial, or touch a unit that is
        // already Retired. The disabled input / filtered dropdown in the
        // Edit view are UX hints; this guard is what actually enforces it.
        if (!_current.IsAdmin && (involvesRetired || serialChanged || editingRetiredUnit))
            throw new ForbiddenException(
                "Only Admin can retire a unit, restore a Retired unit, change a serial, " +
                "or edit a unit that is already Retired. Please escalate to an Admin.");

        if (serialChanged && await _items.SerialNumberExistsAsync(newSerial, dto.ItemID))
            throw new InvalidOperationException($"Serial number '{newSerial}' is already in use.");

        var oldStatus = item.AvailabilityStatus;

        var oldValues = new
        {
            item.SerialNumber,
            Status = oldStatus.ToString(),
            item.Location,
        };

        item.SerialNumber = newSerial;
        item.AvailabilityStatus = newStatus;
        item.Location = dto.Location;

        if (statusChanged)
            item.LastStatusChange = DateTime.UtcNow;

        _items.Update(item);

        // If status crossed the Available boundary, sync parent.
        if (statusChanged)
        {
            var eq = await _equipment.GetAsync(item.EquipmentID)
                ?? throw new NotFoundException($"Parent equipment {item.EquipmentID} not found.");
            var delta = StatusAvailableDelta(oldStatus, newStatus);
            if (delta != 0) AdjustStock(eq, delta);
        }

        await _items.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "EquipmentItem.Update", "EquipmentItem", item.ItemID.ToString(),
            oldValues: oldValues,
            newValues: new
            {
                item.SerialNumber,
                Status = newStatus.ToString(),
                item.Location,
            });
    }

    public async Task DeleteAsync(int id, string actorUserId)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can delete equipment items.");

        var item = await _items.GetAsync(id)
            ?? throw new NotFoundException($"Item {id} not found.");

        // If we delete an Available item, decrement StockQuantity.
        if (item.AvailabilityStatus == AvailabilityStatus.Available)
        {
            var eq = await _equipment.GetAsync(item.EquipmentID)
                ?? throw new NotFoundException($"Parent equipment {item.EquipmentID} not found.");
            AdjustStock(eq, -1);
        }

        var snapshot = new { item.SerialNumber, item.EquipmentID };
        await _items.RemoveAsync(item);
        await _items.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "EquipmentItem.Delete", "EquipmentItem", id.ToString(),
            oldValues: snapshot);
    }

    public async Task ChangeStatusAsync(int id, string newStatus, string actorUserId)
    {
        // Convenience for future modules (Maintenance, etc.) — single-status transition
        // with the same StockQuantity sync as Update.
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can change item status.");

        var item = await _items.GetAsync(id) ?? throw new NotFoundException($"Item {id} not found.");
        var target = ParseStatus(newStatus);
        if (item.AvailabilityStatus == target) return;

        var oldStatus = item.AvailabilityStatus;
        item.AvailabilityStatus = target;
        item.LastStatusChange = DateTime.UtcNow;
        _items.Update(item);

        var eq = await _equipment.GetAsync(item.EquipmentID)
            ?? throw new NotFoundException($"Parent equipment {item.EquipmentID} not found.");
        var delta = StatusAvailableDelta(oldStatus, target);
        if (delta != 0) AdjustStock(eq, delta);

        await _items.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "EquipmentItem.ChangeStatus", "EquipmentItem", id.ToString(),
            oldValues: new { Status = oldStatus.ToString() },
            newValues: new { Status = target.ToString() });
    }

    // ---- helpers ----

    private static EquipmentItemListItemDto MapList(EquipmentItem i) => new()
    {
        ItemID = i.ItemID,
        EquipmentID = i.EquipmentID,
        EquipmentName = i.Equipment?.Name ?? string.Empty,
        SerialNumber = i.SerialNumber,
        AvailabilityStatus = i.AvailabilityStatus.ToString(),
        Location = i.Location,
        LastStatusChange = i.LastStatusChange,
    };

    private static AvailabilityStatus ParseStatus(string s) => s switch
    {
        "Available"     => AvailabilityStatus.Available,
        "Reserved"      => AvailabilityStatus.Reserved,
        "CheckedOut"    => AvailabilityStatus.CheckedOut,
        "InMaintenance" => AvailabilityStatus.InMaintenance,
        "Retired"       => AvailabilityStatus.Retired,
        _               => AvailabilityStatus.Available,
    };

    /// <summary>+1 if moving INTO Available, -1 if moving OUT of Available, 0 otherwise.</summary>
    private static int StatusAvailableDelta(AvailabilityStatus from, AvailabilityStatus to) =>
        (from == AvailabilityStatus.Available, to == AvailabilityStatus.Available) switch
        {
            (false, true)  => +1,
            (true,  false) => -1,
            _              => 0,
        };

    private void AdjustStock(EquipmentCatalog eq, int delta)
    {
        eq.StockQuantity = Math.Max(0, eq.StockQuantity + delta);
        _equipment.Update(eq);
    }
}
