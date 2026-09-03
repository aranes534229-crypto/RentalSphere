using Microsoft.EntityFrameworkCore;
using RentalSphere.Data;
using RentalSphere.Modules.Maintenance.Models;

namespace RentalSphere.Modules.Maintenance.Repositories;

public interface IMaintenanceRepository
{
    Task<List<MaintenanceRecord>> ListAsync(string? statusFilter = null);
    Task<MaintenanceRecord?> GetAsync(int id);
    Task<bool> HasOpenForItemAsync(int equipmentItemId);
    Task AddAsync(MaintenanceRecord record);
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

    public Task<MaintenanceRecord?> GetAsync(int id) =>
        _db.MaintenanceRecords
            .Include(m => m.EquipmentItem).ThenInclude(i => i!.Equipment)
            .Include(m => m.OpenedByUser)
            .Include(m => m.ClosedByUser)
            .FirstOrDefaultAsync(m => m.MaintenanceRecordID == id);

    public Task<bool> HasOpenForItemAsync(int equipmentItemId) =>
        _db.MaintenanceRecords.AnyAsync(m =>
            m.EquipmentItemID == equipmentItemId && m.Status == MaintenanceStatus.InProgress);

    public async Task AddAsync(MaintenanceRecord record) => await _db.MaintenanceRecords.AddAsync(record);
    public void Update(MaintenanceRecord record) => _db.MaintenanceRecords.Update(record);
    public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
}
