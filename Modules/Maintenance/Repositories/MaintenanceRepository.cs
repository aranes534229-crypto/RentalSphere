using Microsoft.EntityFrameworkCore;
using RentalSphere.Data;
using RentalSphere.Modules.Maintenance.Models;

namespace RentalSphere.Modules.Maintenance.Repositories;

public interface IMaintenanceRepository
{
    Task<List<MaintenanceRecord>> ListAsync(string? statusFilter = null);
    Task<(List<MaintenanceRecord> Items, int TotalCount)> ListPagedAsync(string? statusFilter, int skip, int take);
    Task<int> CountAsync(string? statusFilter = null);
    Task<int> CountByStatusAsync(MaintenanceStatus status);
    Task<MaintenanceRecord?> GetAsync(int id);
    Task<bool> HasOpenForItemAsync(int equipmentItemId);
    Task<HashSet<int>> ItemsWithOpenRecordAsync(IEnumerable<int> equipmentItemIds);
    Task AddAsync(MaintenanceRecord record);
    void AddSync(MaintenanceRecord record);
    void Update(MaintenanceRecord record);
    Task<int> SaveChangesAsync();
}

public class MaintenanceRepository : IMaintenanceRepository
{
    private readonly ApplicationDbContext _db;
    public MaintenanceRepository(ApplicationDbContext db) { _db = db; }

    public async Task<List<MaintenanceRecord>> ListAsync(string? statusFilter = null)
    {
        var q = _db.MaintenanceRecords
            .Include(m => m.EquipmentItem).ThenInclude(i => i!.Equipment)
            .Include(m => m.OpenedByUser)
            .Include(m => m.ClosedByUser)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(statusFilter)
            && Enum.TryParse<MaintenanceStatus>(statusFilter, out var st))
        {
            q = q.Where(m => m.Status == st);
        }

        return await q.OrderByDescending(m => m.StartedAt).ToListAsync();
    }

    public async Task<(List<MaintenanceRecord> Items, int TotalCount)> ListPagedAsync(
        string? statusFilter, int skip, int take)
    {
        var q = _db.MaintenanceRecords
            .Include(m => m.EquipmentItem).ThenInclude(i => i!.Equipment)
            .Include(m => m.OpenedByUser)
            .Include(m => m.ClosedByUser)
            .AsQueryable();

        q = ApplyStatusFilter(q, statusFilter);

        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(m => m.StartedAt)
            .Skip(skip).Take(take)
            .ToListAsync();
        return (rows, total);
    }

    public async Task<int> CountAsync(string? statusFilter = null)
    {
        var q = _db.MaintenanceRecords.AsQueryable();
        q = ApplyStatusFilter(q, statusFilter);
        return await q.CountAsync();
    }

    // Centralized so List, ListPaged, and Count agree on the filter semantics.
    private static IQueryable<MaintenanceRecord> ApplyStatusFilter(
        IQueryable<MaintenanceRecord> q, string? statusFilter)
    {
        if (!string.IsNullOrWhiteSpace(statusFilter)
            && Enum.TryParse<MaintenanceStatus>(statusFilter, out var st))
        {
            q = q.Where(m => m.Status == st);
        }
        return q;
    }

    public Task<int> CountByStatusAsync(MaintenanceStatus status) =>
        _db.MaintenanceRecords.CountAsync(m => m.Status == status);

    public Task<MaintenanceRecord?> GetAsync(int id) =>
        _db.MaintenanceRecords
            .Include(m => m.EquipmentItem).ThenInclude(i => i!.Equipment)
            .Include(m => m.OpenedByUser)
            .Include(m => m.ClosedByUser)
            .FirstOrDefaultAsync(m => m.MaintenanceRecordID == id);

    public Task<bool> HasOpenForItemAsync(int equipmentItemId) =>
        _db.MaintenanceRecords.AnyAsync(m =>
            m.EquipmentItemID == equipmentItemId && m.Status == MaintenanceStatus.InProgress);

    public async Task<HashSet<int>> ItemsWithOpenRecordAsync(IEnumerable<int> equipmentItemIds)
    {
        var ids = equipmentItemIds as IList<int> ?? equipmentItemIds.ToList();
        if (ids.Count == 0) return new HashSet<int>();
        var hits = await _db.MaintenanceRecords
            .Where(m => m.Status == MaintenanceStatus.InProgress && ids.Contains(m.EquipmentItemID))
            .Select(m => m.EquipmentItemID)
            .ToListAsync();
        return new HashSet<int>(hits);
    }

    public async Task AddAsync(MaintenanceRecord record) => await _db.MaintenanceRecords.AddAsync(record);
    public void AddSync(MaintenanceRecord record) => _db.MaintenanceRecords.Add(record);
    public void Update(MaintenanceRecord record) => _db.MaintenanceRecords.Update(record);
    public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
}
