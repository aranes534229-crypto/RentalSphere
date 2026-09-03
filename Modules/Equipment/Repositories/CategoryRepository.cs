using Microsoft.EntityFrameworkCore;
using RentalSphere.Data;
using RentalSphere.Modules.Equipment.Models;

namespace RentalSphere.Modules.Equipment.Repositories;

public interface ICategoryRepository
{
    Task<List<Category>> ListAsync(string? search = null);
    Task<int> CountAsync(string? search);
    Task<List<Category>> ListPagedAsync(string? search, int skip, int take);
    Task<Category?> GetAsync(int id);
    Task<bool> NameExistsAsync(string name, int? excludeId = null);
    Task<bool> HasEquipmentAsync(int id);
    Task AddAsync(Category entity);
    void Update(Category entity);
    Task RemoveAsync(Category entity);
    Task<int> SaveChangesAsync();
}

public class CategoryRepository : ICategoryRepository
{
    private readonly ApplicationDbContext _db;
    public CategoryRepository(ApplicationDbContext db) { _db = db; }

    public async Task<List<Category>> ListAsync(string? search = null)
    {
        var q = _db.Categories
            .Include(c => c.Equipment)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(c => c.Name.ToLower().Contains(s));
        }
        return await q.OrderBy(c => c.Name).ToListAsync();
    }

    public Task<int> CountAsync(string? search)
    {
        var q = _db.Categories.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(c => c.Name.ToLower().Contains(s));
        }
        return q.CountAsync();
    }

    public async Task<List<Category>> ListPagedAsync(string? search, int skip, int take)
    {
        var q = _db.Categories
            .Include(c => c.Equipment)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(c => c.Name.ToLower().Contains(s));
        }
        return await q.OrderBy(c => c.Name).Skip(skip).Take(take).ToListAsync();
    }

    public Task<Category?> GetAsync(int id) =>
        _db.Categories.FirstOrDefaultAsync(c => c.CategoryID == id);

    public Task<bool> NameExistsAsync(string name, int? excludeId = null)
    {
        var n = name.Trim().ToLower();
        return _db.Categories.AnyAsync(c =>
            c.Name.ToLower() == n && (!excludeId.HasValue || c.CategoryID != excludeId.Value));
    }

    public Task<bool> HasEquipmentAsync(int id) =>
        _db.Equipment.AnyAsync(e => e.CategoryID == id);

    public async Task AddAsync(Category entity) => await _db.Categories.AddAsync(entity);
    public void Update(Category entity) => _db.Categories.Update(entity);
    public async Task RemoveAsync(Category entity) => _db.Categories.Remove(entity);
    public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
}
