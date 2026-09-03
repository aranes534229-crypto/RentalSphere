using Microsoft.EntityFrameworkCore;
using RentalSphere.Data;
using RentalSphere.Modules.Equipment.Models;

namespace RentalSphere.Modules.Equipment.Repositories;

public interface IEquipmentRepository
{
    Task<List<EquipmentCatalog>> ListAsync(string? search = null, int? categoryId = null);
    Task<EquipmentCatalog?> GetAsync(int id);
    Task<EquipmentCatalog?> GetWithItemsAsync(int id);
    Task AddAsync(EquipmentCatalog entity);
    void Update(EquipmentCatalog entity);
    Task RemoveAsync(EquipmentCatalog entity);
    Task<int> SaveChangesAsync();
}

public class EquipmentRepository : IEquipmentRepository
{
    private readonly ApplicationDbContext _db;
    public EquipmentRepository(ApplicationDbContext db) { _db = db; }

    public async Task<List<EquipmentCatalog>> ListAsync(string? search = null, int? categoryId = null)
    {
        var q = _db.Equipment.Include(e => e.Category).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(e => e.Name.ToLower().Contains(s));
        }
        if (categoryId.HasValue)
            q = q.Where(e => e.CategoryID == categoryId.Value);

        return await q.OrderBy(e => e.Name).ToListAsync();
    }

    public Task<EquipmentCatalog?> GetAsync(int id) =>
        _db.Equipment.Include(e => e.Category).FirstOrDefaultAsync(e => e.EquipmentID == id);

    public Task<EquipmentCatalog?> GetWithItemsAsync(int id) =>
        _db.Equipment
            .Include(e => e.Category)
            .Include(e => e.Items)
            .FirstOrDefaultAsync(e => e.EquipmentID == id);

    public async Task AddAsync(EquipmentCatalog entity)
    {
        await _db.Equipment.AddAsync(entity);
    }

    public void Update(EquipmentCatalog entity)
    {
        _db.Equipment.Update(entity);
    }

    public async Task RemoveAsync(EquipmentCatalog entity)
    {
        _db.Equipment.Remove(entity);
    }

    public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
}
