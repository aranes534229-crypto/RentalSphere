using Microsoft.EntityFrameworkCore;
using RentalSphere.Data;
using RentalSphere.Modules.CustomerCrm.Models;

namespace RentalSphere.Modules.CustomerCrm.Repositories;

public interface ICustomerCrmRepository
{
    Task<List<CustomerNote>> ListNotesAsync(int customerId);
    Task<List<CustomerFollowUp>> ListFollowUpsAsync(int customerId, FollowUpStatus? status = null);
    Task<List<CustomerFollowUp>> ListOverdueFollowUpsAsync(DateTime asOf);
    Task AddNoteAsync(CustomerNote entity);
    Task AddFollowUpAsync(CustomerFollowUp entity);
    void UpdateFollowUp(CustomerFollowUp entity);
    Task<CustomerNote?> GetNoteAsync(int id);
    Task<CustomerFollowUp?> GetFollowUpAsync(int id);
    Task<int> SaveChangesAsync();
}

public class CustomerCrmRepository : ICustomerCrmRepository
{
    private readonly ApplicationDbContext _db;
    public CustomerCrmRepository(ApplicationDbContext db) { _db = db; }

    public Task<List<CustomerNote>> ListNotesAsync(int customerId) =>
        _db.CustomerNotes
            .Include(n => n.CreatedByUser)
            .Where(n => n.CustomerID == customerId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();

    public Task<List<CustomerFollowUp>> ListFollowUpsAsync(int customerId, FollowUpStatus? status = null)
    {
        var q = _db.CustomerFollowUps
            .Include(f => f.AssignedToUser)
            .Include(f => f.CreatedByUser)
            .Where(f => f.CustomerID == customerId);
        if (status.HasValue)
            q = q.Where(f => f.Status == status.Value);
        return q.OrderBy(f => f.FollowUpDate).ThenBy(f => f.CustomerFollowUpID).ToListAsync();
    }

    public Task<List<CustomerFollowUp>> ListOverdueFollowUpsAsync(DateTime asOf) =>
        _db.CustomerFollowUps
            .Include(f => f.Customer)
            .Where(f => f.Status == FollowUpStatus.Pending && f.FollowUpDate < asOf)
            .OrderBy(f => f.FollowUpDate)
            .ToListAsync();

    public async Task AddNoteAsync(CustomerNote entity) => await _db.CustomerNotes.AddAsync(entity);

    public async Task AddFollowUpAsync(CustomerFollowUp entity) => await _db.CustomerFollowUps.AddAsync(entity);

    public void UpdateFollowUp(CustomerFollowUp entity) => _db.CustomerFollowUps.Update(entity);

    public Task<CustomerNote?> GetNoteAsync(int id) =>
        _db.CustomerNotes.FirstOrDefaultAsync(n => n.CustomerNoteID == id);

    public Task<CustomerFollowUp?> GetFollowUpAsync(int id) =>
        _db.CustomerFollowUps
            .Include(f => f.Customer)
            .FirstOrDefaultAsync(f => f.CustomerFollowUpID == id);

    public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
}
