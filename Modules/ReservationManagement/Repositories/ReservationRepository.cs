using Microsoft.EntityFrameworkCore;
using RentalSphere.Data;
using RentalSphere.Modules.ReservationManagement.Models;

namespace RentalSphere.Modules.ReservationManagement.Repositories;

public interface IReservationRepository
{
    Task<List<Reservation>> ListAsync(string? search = null, string? statusFilter = null);
    Task<Reservation?> GetAsync(int id);
    Task<Reservation?> GetWithItemsAsync(int id);
    Task<List<Reservation>> ListByCustomerAsync(int customerId);
    Task<int> CountByCustomerAsync(int customerId, string? statusFilter = null);
    Task<List<Reservation>> ListByCustomerPagedAsync(int customerId, int skip, int take, string? statusFilter = null);
    Task<int> CountByStatusAsync(ReservationStatus status);
    Task<int> CountPendingForCustomerAsync(int customerId);
    Task AddAsync(Reservation entity);
    void Update(Reservation entity);
    Task RemoveAsync(Reservation entity);
    Task<int> SaveChangesAsync();
}

public class ReservationRepository : IReservationRepository
{
    private readonly ApplicationDbContext _db;
    public ReservationRepository(ApplicationDbContext db) { _db = db; }

    public async Task<List<Reservation>> ListAsync(string? search = null, string? statusFilter = null)
    {
        var q = _db.Reservations
            .Include(r => r.Customer)
            .Include(r => r.Items)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(r =>
                r.Customer.FirstName.ToLower().Contains(s) ||
                r.Customer.LastName.ToLower().Contains(s) ||
                r.Customer.Email.ToLower().Contains(s));
        }
        if (!string.IsNullOrWhiteSpace(statusFilter) &&
            Enum.TryParse<ReservationStatus>(statusFilter, out var st))
        {
            q = q.Where(r => r.Status == st);
        }
        return await q.OrderByDescending(r => r.ReservationDate).ToListAsync();
    }

    public Task<Reservation?> GetAsync(int id) =>
        _db.Reservations
            .Include(r => r.Customer)
            .FirstOrDefaultAsync(r => r.ReservationID == id);

    public Task<Reservation?> GetWithItemsAsync(int id) =>
        _db.Reservations
            .Include(r => r.Customer)
            .Include(r => r.Items).ThenInclude(i => i.Equipment)
            .Include(r => r.Items).ThenInclude(i => i.AssignedItem)
            .Include(r => r.CreatedByUser)
            .FirstOrDefaultAsync(r => r.ReservationID == id);

    public async Task<List<Reservation>> ListByCustomerAsync(int customerId) =>
        await _db.Reservations
            .Include(r => r.Items)
            .Where(r => r.CustomerID == customerId)
            .OrderByDescending(r => r.ReservationDate)
            .ToListAsync();

    public async Task<int> CountByCustomerAsync(int customerId, string? statusFilter = null)
    {
        var q = _db.Reservations.AsQueryable().Where(r => r.CustomerID == customerId);
        q = ApplyStatusFilter(q, statusFilter);
        return await q.CountAsync();
    }

    public Task<int> CountByStatusAsync(ReservationStatus status) =>
        _db.Reservations.CountAsync(r => r.Status == status);

    // Pending only — Confirmed/Cancelled/Completed/CheckedOut/Expired all
    // clear the sidebar badge. Used by the customer-portal sidebar ViewComponent.
    public Task<int> CountPendingForCustomerAsync(int customerId) =>
        _db.Reservations.CountAsync(r =>
            r.CustomerID == customerId
            && r.Status == ReservationStatus.Pending);

    public async Task<List<Reservation>> ListByCustomerPagedAsync(
        int customerId, int skip, int take, string? statusFilter = null)
    {
        var q = _db.Reservations
            .Include(r => r.Items)
            .Where(r => r.CustomerID == customerId);
        q = ApplyStatusFilter(q, statusFilter);
        return await q.OrderByDescending(r => r.ReservationDate)
            .Skip(skip).Take(take)
            .ToListAsync();
    }

    // Status filtering shared by the paged and count paths. An empty/unknown
    // filter returns everything; "Other" captures every closed/cancelled state
    // in one pill. Mirrors how RentalTransactions treats derived filters.
    private static IQueryable<Reservation> ApplyStatusFilter(IQueryable<Reservation> q, string? statusFilter)
    {
        if (string.IsNullOrWhiteSpace(statusFilter)) return q;
        if (statusFilter == "Other")
        {
            return q.Where(r => r.Status != ReservationStatus.Pending
                             && r.Status != ReservationStatus.Confirmed);
        }
        if (Enum.TryParse<ReservationStatus>(statusFilter, out var st))
        {
            q = q.Where(r => r.Status == st);
        }
        return q;
    }

    public async Task AddAsync(Reservation entity) => await _db.Reservations.AddAsync(entity);

    public void Update(Reservation entity) => _db.Reservations.Update(entity);

    public async Task RemoveAsync(Reservation entity) => _db.Reservations.Remove(entity);

    public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
}