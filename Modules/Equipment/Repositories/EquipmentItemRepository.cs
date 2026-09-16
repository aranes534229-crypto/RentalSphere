using Microsoft.EntityFrameworkCore;
using RentalSphere.Data;
using RentalSphere.Modules.Equipment.Models;

namespace RentalSphere.Modules.Equipment.Repositories;

public interface IEquipmentItemRepository
{
    Task<List<EquipmentItem>> ListAsync(int? equipmentId = null, string? status = null, string? search = null, int? categoryId = null);
    Task<int> CountAsync(int? equipmentId, string? status, string? search, int? categoryId = null);
    Task<int> CountParentsAsync(int? categoryId, string? search);
    Task<List<EquipmentCatalog>> ListParentsWithCountsAsync(int? categoryId, string? search, int skip, int take);
    Task<Dictionary<(int EquipmentID, string Status), int>> GetStatusCountsForParentsAsync(IEnumerable<int> parentIds);
    Task<EquipmentItem?> GetAsync(int id);
    Task<int> CountAsync(int equipmentId, AvailabilityStatus status);
    Task<int> CountAsyncByEquipment(int equipmentId, AvailabilityStatus status);
    Task<List<EquipmentItem>> ListAvailableByEquipmentAsync(int equipmentId);
    Task<List<EquipmentItem>> ListReservedByEquipmentAsync(int equipmentId);
    Task<List<EquipmentItem>> ListAllAvailableAsync();
    Task<List<EquipmentItem>> ListAllAvailableWithCategoryAsync();
    Task<List<EquipmentItem>> ListFreeForRangeAsync(int equipmentId, DateTime start, DateTime end, int? excludeReservationId = null);
    Task<List<EquipmentItem>> ListByIdsAsync(IEnumerable<int> ids);
    Task<List<string>> ListSerialNumbersAsync(int equipmentId);
    Task<bool> SerialNumberExistsAsync(string serialNumber, int? excludeItemId = null);
    Task AddAsync(EquipmentItem item);
    void Update(EquipmentItem item);
    Task RemoveAsync(EquipmentItem item);
    Task<int> SaveChangesAsync();
}

public class EquipmentItemRepository : IEquipmentItemRepository
{
    private readonly ApplicationDbContext _db;
    public EquipmentItemRepository(ApplicationDbContext db) { _db = db; }

    public async Task<List<EquipmentItem>> ListAsync(int? equipmentId = null, string? status = null, string? search = null, int? categoryId = null)
    {
        var q = _db.EquipmentItems
            .Include(i => i.Equipment).ThenInclude(e => e.Category)
            .AsQueryable();

        if (equipmentId.HasValue)
            q = q.Where(i => i.EquipmentID == equipmentId.Value);

        if (categoryId.HasValue)
            q = q.Where(i => i.Equipment.CategoryID == categoryId.Value);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<AvailabilityStatus>(status, out var st))
            q = q.Where(i => i.AvailabilityStatus == st);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(i => i.SerialNumber.ToLower().Contains(s));
        }

        return await q.OrderBy(i => i.Equipment.Name).ThenBy(i => i.SerialNumber).ToListAsync();
    }

    public Task<EquipmentItem?> GetAsync(int id) =>
        _db.EquipmentItems.Include(i => i.Equipment).FirstOrDefaultAsync(i => i.ItemID == id);

    public async Task<int> CountParentsAsync(int? categoryId, string? search)
    {
        // Distinct equipment IDs that have at least one matching item.
        var q = _db.EquipmentItems.AsQueryable();
        if (categoryId.HasValue)
            q = q.Where(i => i.Equipment.CategoryID == categoryId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(i => i.Equipment.Name.ToLower().Contains(s)
                          || i.SerialNumber.ToLower().Contains(s));
        }
        return await q.Select(i => i.EquipmentID).Distinct().CountAsync();
    }

    public async Task<List<EquipmentCatalog>> ListParentsWithCountsAsync(
        int? categoryId, string? search, int skip, int take)
    {
        // 1) Page the distinct parent IDs.
        var q = _db.EquipmentItems.AsQueryable();
        if (categoryId.HasValue)
            q = q.Where(i => i.Equipment.CategoryID == categoryId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(i => i.Equipment.Name.ToLower().Contains(s)
                          || i.SerialNumber.ToLower().Contains(s));
        }

        var ids = await q.Select(i => i.EquipmentID)
            .Distinct()
            .OrderBy(id => id)
            .Skip(skip).Take(take)
            .ToListAsync();

        if (ids.Count == 0) return new List<EquipmentCatalog>();

        // 2) Hydrate the parent rows in one query.
        var parents = await _db.Equipment
            .Include(e => e.Category)
            .Where(e => ids.Contains(e.EquipmentID))
            .ToListAsync();

        // 3) Order to match the id-sorted slice above.
        var byId = parents.ToDictionary(p => p.EquipmentID);
        return ids.Select(id => byId[id]).ToList();
    }

    public async Task<Dictionary<(int EquipmentID, string Status), int>> GetStatusCountsForParentsAsync(IEnumerable<int> parentIds)
    {
        var ids = parentIds as IList<int> ?? parentIds.ToList();
        if (ids.Count == 0) return new Dictionary<(int, string), int>();

        var grouped = await _db.EquipmentItems
            .Where(i => ids.Contains(i.EquipmentID))
            .GroupBy(i => new { i.EquipmentID, i.AvailabilityStatus })
            .Select(g => new { g.Key.EquipmentID, Status = g.Key.AvailabilityStatus.ToString(), Count = g.Count() })
            .ToListAsync();

        var result = new Dictionary<(int, string), int>();
        foreach (var row in grouped)
            result[(row.EquipmentID, row.Status)] = row.Count;
        return result;
    }

    public Task<int> CountAsync(int equipmentId, AvailabilityStatus status) =>
        _db.EquipmentItems.CountAsync(i => i.EquipmentID == equipmentId && i.AvailabilityStatus == status);

    public async Task<int> CountAsync(int? equipmentId, string? status, string? search, int? categoryId = null)
    {
        var q = _db.EquipmentItems.AsQueryable();
        if (equipmentId.HasValue)
            q = q.Where(i => i.EquipmentID == equipmentId.Value);
        if (categoryId.HasValue)
            q = q.Where(i => i.Equipment.CategoryID == categoryId.Value);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<AvailabilityStatus>(status, out var st))
            q = q.Where(i => i.AvailabilityStatus == st);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(i => i.SerialNumber.ToLower().Contains(s));
        }
        return await q.CountAsync();
    }

    public Task<int> CountAsyncByEquipment(int equipmentId, AvailabilityStatus status) =>
        _db.EquipmentItems.CountAsync(i => i.EquipmentID == equipmentId && i.AvailabilityStatus == status);

    public async Task<List<EquipmentItem>> ListAvailableByEquipmentAsync(int equipmentId) =>
        await _db.EquipmentItems
            .Where(i => i.EquipmentID == equipmentId && i.AvailabilityStatus == AvailabilityStatus.Available)
            .OrderBy(i => i.ItemID)
            .ToListAsync();

    public async Task<List<EquipmentItem>> ListFreeForRangeAsync(
        int equipmentId, DateTime start, DateTime end, int? excludeReservationId = null)
    {
        // Items of this equipment that are physically bookable AND not currently
        // pinned to a Confirmed/CheckedOut reservation overlapping [start, end).
        // Half-open on the end: a reservation ending on `start` is a drop-off day
        // and does NOT pin a unit for the next renter.
        //
        // Per-item status is filtered for physical states only (InMaintenance,
        // Retired). The Reserved/CheckedOut/Available triplet is driven by the
        // reservation table, not the per-item status — the status field can lag
        // the reservation table by an arbitrary amount of time, so we can't
        // trust it for "is this unit free right now".
        var heldItemIds = await _db.ReservationItems
            .Where(ri => ri.EquipmentID == equipmentId
                      && ri.AssignedItemID != null
                      && (ri.Reservation.Status == ReservationManagement.Models.ReservationStatus.Confirmed
                       || ri.Reservation.Status == ReservationManagement.Models.ReservationStatus.CheckedOut)
                      && (excludeReservationId == null || ri.ReservationID != excludeReservationId.Value)
                      && ri.Reservation.RentalStartDate.Date < end.Date
                      && ri.Reservation.RentalEndDate.Date > start.Date)
            .Select(ri => ri.AssignedItemID!.Value)
            .Distinct()
            .ToListAsync();

        return await _db.EquipmentItems
            .Where(i => i.EquipmentID == equipmentId
                     && i.AvailabilityStatus != AvailabilityStatus.InMaintenance
                     && i.AvailabilityStatus != AvailabilityStatus.Retired
                     && !heldItemIds.Contains(i.ItemID))
            .OrderBy(i => i.ItemID)
            .ToListAsync();
    }

    public async Task<List<EquipmentItem>> ListAllAvailableAsync() =>
        await _db.EquipmentItems
            .Include(i => i.Equipment)
            .Where(i => i.AvailabilityStatus == AvailabilityStatus.Available)
            .OrderBy(i => i.Equipment.Name).ThenBy(i => i.SerialNumber)
            .ToListAsync();

    public async Task<List<EquipmentItem>> ListAllAvailableWithCategoryAsync() =>
        await _db.EquipmentItems
            .Include(i => i.Equipment).ThenInclude(e => e.Category)
            .Where(i => i.AvailabilityStatus == AvailabilityStatus.Available)
            .OrderBy(i => i.Equipment.Category!.Name)
            .ThenBy(i => i.Equipment.Name)
            .ThenBy(i => i.SerialNumber)
            .ToListAsync();

    public async Task<List<string>> ListSerialNumbersAsync(int equipmentId) =>
        await _db.EquipmentItems
            .Where(i => i.EquipmentID == equipmentId)
            .Select(i => i.SerialNumber)
            .ToListAsync();

    public async Task<List<EquipmentItem>> ListReservedByEquipmentAsync(int equipmentId) =>
        await _db.EquipmentItems
            .Where(i => i.EquipmentID == equipmentId && i.AvailabilityStatus == AvailabilityStatus.Reserved)
            .OrderBy(i => i.ItemID)
            .ToListAsync();

    /// <summary>
    /// Loads EquipmentItems by primary key with default tracking, so the
    /// caller can mutate properties and have EF persist them on the next
    /// SaveChanges. Used by callers that need to flip AvailabilityStatus as
    /// part of a larger transaction (e.g. RentalTransactionService.ReturnAsync).
    /// </summary>
    public async Task<List<EquipmentItem>> ListByIdsAsync(IEnumerable<int> ids)
    {
        var idList = ids as IList<int> ?? ids.ToList();
        if (idList.Count == 0) return new List<EquipmentItem>();
        return await _db.EquipmentItems.Where(i => idList.Contains(i.ItemID)).ToListAsync();
    }

    public Task<bool> SerialNumberExistsAsync(string serialNumber, int? excludeItemId = null)
    {
        var s = serialNumber.Trim();
        return _db.EquipmentItems.AnyAsync(i =>
            i.SerialNumber == s && (!excludeItemId.HasValue || i.ItemID != excludeItemId.Value));
    }

    public async Task AddAsync(EquipmentItem item) => await _db.EquipmentItems.AddAsync(item);
    public void Update(EquipmentItem item) => _db.EquipmentItems.Update(item);
    public async Task RemoveAsync(EquipmentItem item) => _db.EquipmentItems.Remove(item);
    public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
}
