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

    public async Task<List<EquipmentItem>> ListAllAvailableAsync() =>
        await _db.EquipmentItems
            .Include(i => i.Equipment)
            .Where(i => i.AvailabilityStatus == AvailabilityStatus.Available)
            .OrderBy(i => i.Equipment.Name).ThenBy(i => i.SerialNumber)
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
