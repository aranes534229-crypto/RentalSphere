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
    Task<int> CountOverdueAsync();
    Task<int> CountOverdueForCustomerAsync(int customerId);
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
        q = ApplyStatusFilter(q, statusFilter);
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
        q = ApplyStatusFilter(q, statusFilter);
        return await q
            .OrderByDescending(t => t.CheckoutDate)
            .Skip(skip).Take(take)
            .ToListAsync();
    }

    // "Overdue" is a derived filter, not a persisted status: Active rows whose
    // ExpectedReturnDate has already passed. Centralized so Count and ListPaged
    // agree on the definition.
    private static IQueryable<RT> ApplyStatusFilter(IQueryable<RT> q, string? statusFilter)
    {
        if (string.IsNullOrWhiteSpace(statusFilter)) return q;
        if (statusFilter == "Overdue")
        {
            return q.Where(t => t.Status == RentalTransactionStatus.Active
                             && t.ExpectedReturnDate < DateTime.UtcNow);
        }
        if (Enum.TryParse<RentalTransactionStatus>(statusFilter, out var st))
        {
            q = q.Where(t => t.Status == st);
        }
        return q;
    }

    public Task<int> CountOverdueAsync() =>
        _db.RentalTransactions.CountAsync(t =>
            t.Status == RentalTransactionStatus.Active
            && t.ExpectedReturnDate < DateTime.UtcNow);

    public Task<int> CountOverdueForCustomerAsync(int customerId) =>
        _db.RentalTransactions.CountAsync(t =>
            t.CustomerID == customerId
            && t.Status == RentalTransactionStatus.Active
            && t.ExpectedReturnDate < DateTime.UtcNow);

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
