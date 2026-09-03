using Microsoft.EntityFrameworkCore;
using RentalSphere.Data;
using RentalSphere.Modules.CustomerManagement.Models;

namespace RentalSphere.Modules.CustomerManagement.Repositories;

public interface ICustomerRepository
{
    Task<List<Customer>> ListAsync(string? search = null, string? loyaltyTier = null);
    Task<int> CountAsync(string? search = null, string? loyaltyTier = null);
    Task<List<Customer>> ListPagedAsync(string? search, string? loyaltyTier, string? sort, int skip, int take);
    Task<Customer?> GetAsync(int id);
    Task<Customer?> GetByUserIdAsync(string userId);
    Task AddAsync(Customer entity);
    void Update(Customer entity);
    Task RemoveAsync(Customer entity);
    Task<int> SaveChangesAsync();
}

public class CustomerRepository : ICustomerRepository
{
    private readonly ApplicationDbContext _db;
    public CustomerRepository(ApplicationDbContext db) { _db = db; }

    private IQueryable<Customer> ApplyFilters(string? search, string? loyaltyTier)
    {
        var q = _db.Customers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(c =>
                c.FirstName.ToLower().Contains(s) ||
                c.LastName.ToLower().Contains(s) ||
                c.Email.ToLower().Contains(s));
        }
        if (!string.IsNullOrWhiteSpace(loyaltyTier) &&
            Enum.TryParse<LoyaltyTier>(loyaltyTier, out var tier))
        {
            q = q.Where(c => c.LoyaltyTier == tier);
        }
        return q;
    }

    public Task<List<Customer>> ListAsync(string? search = null, string? loyaltyTier = null) =>
        ApplyFilters(search, loyaltyTier)
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .ToListAsync();

    public Task<int> CountAsync(string? search = null, string? loyaltyTier = null) =>
        ApplyFilters(search, loyaltyTier).CountAsync();

    public Task<List<Customer>> ListPagedAsync(string? search, string? loyaltyTier, string? sort, int skip, int take)
    {
        var q = ApplyFilters(search, loyaltyTier);
        q = sort?.ToLowerInvariant() switch
        {
            "name" or "name_desc" => sort == "name_desc"
                ? q.OrderByDescending(c => c.LastName).ThenByDescending(c => c.FirstName)
                : q.OrderBy(c => c.LastName).ThenBy(c => c.FirstName),
            "email" => q.OrderBy(c => c.Email),
            "email_desc" => q.OrderByDescending(c => c.Email),
            "tier" => q.OrderBy(c => c.LoyaltyTier).ThenBy(c => c.LastName),
            "tier_desc" => q.OrderByDescending(c => c.LoyaltyTier).ThenBy(c => c.LastName),
            "registered" => q.OrderBy(c => c.DateRegistered),
            "registered_desc" => q.OrderByDescending(c => c.DateRegistered),
            _ => q.OrderBy(c => c.LastName).ThenBy(c => c.FirstName),
        };
        return q.Skip(skip).Take(take).ToListAsync();
    }

    public Task<Customer?> GetAsync(int id) =>
        _db.Customers.Include(c => c.User).FirstOrDefaultAsync(c => c.CustomerID == id);

    public Task<Customer?> GetByUserIdAsync(string userId) =>
        _db.Customers.FirstOrDefaultAsync(c => c.UserId == userId);

    public async Task AddAsync(Customer entity) => await _db.Customers.AddAsync(entity);

    public void Update(Customer entity) => _db.Customers.Update(entity);

    public async Task RemoveAsync(Customer entity) => _db.Customers.Remove(entity);

    public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
}