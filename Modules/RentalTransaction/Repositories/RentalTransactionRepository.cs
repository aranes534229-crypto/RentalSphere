using Microsoft.EntityFrameworkCore;
using RentalSphere.Data;
using RentalSphere.Modules.RentalTransaction.Models;
using RT = RentalSphere.Modules.RentalTransaction.Models.RentalTransaction;

namespace RentalSphere.Modules.RentalTransaction.Repositories;

public interface IRentalTransactionRepository
{
    Task<List<RT>> ListAsync(string? statusFilter = null);
    Task<List<RT>> ListByCustomerAsync(int customerId, string? statusFilter = null);
    Task<int> CountAsync(string? statusFilter = null, int? customerId = null);
    Task<List<RT>> ListPagedAsync(int skip, int take, string? statusFilter = null, int? customerId = null);
    Task<RT?> GetAsync(int id);
    Task<RT?> GetWithItemsAsync(int id);
    Task<RT?> GetByReservationAsync(int reservationId);
    Task AddAsync(RT tx);
    void Update(RT tx);
    Task<int> SaveChangesAsync();
}

public class RentalTransactionRepository : IRentalTransactionRepository
{
    private readonly ApplicationDbContext _db;
    public RentalTransactionRepository(ApplicationDbContext db) { _db = db; }

    public async Task<List<RT>> ListAsync(string? statusFilter = null)
    {
        var q = _db.RentalTransactions
            .Include(t => t.Customer)
            .Include(t => t.Items).ThenInclude(i => i.Equipment)
            .Include(t => t.Items).ThenInclude(i => i.EquipmentItem)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(statusFilter)
            && Enum.TryParse<RentalTransactionStatus>(statusFilter, out var st))
        {
            q = q.Where(t => t.Status == st);
        }

        return await q
            .OrderByDescending(t => t.CheckoutDate)
            .ToListAsync();
    }

    public async Task<List<RT>> ListByCustomerAsync(int customerId, string? statusFilter = null)
    {
        var q = _db.RentalTransactions
            .Include(t => t.Customer)
            .Include(t => t.Items).ThenInclude(i => i.Equipment)
            .Include(t => t.Items).ThenInclude(i => i.EquipmentItem)
            .Where(t => t.CustomerID == customerId);

        if (!string.IsNullOrWhiteSpace(statusFilter)
            && Enum.TryParse<RentalTransactionStatus>(statusFilter, out var st))
        {
            q = q.Where(t => t.Status == st);
        }

        return await q
            .OrderByDescending(t => t.CheckoutDate)
            .ToListAsync();
    }

    public async Task<int> CountAsync(string? statusFilter = null, int? customerId = null)
    {
        var q = _db.RentalTransactions.AsQueryable();
        if (customerId.HasValue) q = q.Where(t => t.CustomerID == customerId.Value);
        if (!string.IsNullOrWhiteSpace(statusFilter)
            && Enum.TryParse<RentalTransactionStatus>(statusFilter, out var st))
            q = q.Where(t => t.Status == st);
        return await q.CountAsync();
    }

    public async Task<List<RT>> ListPagedAsync(
        int skip, int take, string? statusFilter = null, int? customerId = null)
    {
        var q = _db.RentalTransactions
            .Include(t => t.Customer)
            .Include(t => t.Items).ThenInclude(i => i.Equipment)
            .Include(t => t.Items).ThenInclude(i => i.EquipmentItem)
            .AsQueryable();
        if (customerId.HasValue) q = q.Where(t => t.CustomerID == customerId.Value);
        if (!string.IsNullOrWhiteSpace(statusFilter)
            && Enum.TryParse<RentalTransactionStatus>(statusFilter, out var st))
            q = q.Where(t => t.Status == st);
        return await q
            .OrderByDescending(t => t.CheckoutDate)
            .Skip(skip).Take(take)
            .ToListAsync();
    }

    public Task<RT?> GetAsync(int id) =>
        _db.RentalTransactions
            .Include(t => t.Customer)
            .Include(t => t.Reservation)
            .Include(t => t.ProcessedByUser)
            .Include(t => t.ReturnedToUser)
            .FirstOrDefaultAsync(t => t.RentalTransactionID == id);

    public Task<RT?> GetWithItemsAsync(int id) =>
        _db.RentalTransactions
            .Include(t => t.Customer)
            .Include(t => t.Reservation)
            .Include(t => t.ProcessedByUser)
            .Include(t => t.ReturnedToUser)
            .Include(t => t.Items).ThenInclude(i => i.Equipment)
            .Include(t => t.Items).ThenInclude(i => i.EquipmentItem)
            .FirstOrDefaultAsync(t => t.RentalTransactionID == id);

    public Task<RT?> GetByReservationAsync(int reservationId) =>
        _db.RentalTransactions.FirstOrDefaultAsync(t => t.ReservationID == reservationId);

    public async Task AddAsync(RT tx) => await _db.RentalTransactions.AddAsync(tx);
    public void Update(RT tx) => _db.RentalTransactions.Update(tx);
    public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
}
