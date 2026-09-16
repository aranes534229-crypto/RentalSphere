using Microsoft.EntityFrameworkCore;
using RentalSphere.Data;
using RentalSphere.Modules.Billing.Models;
using RentalSphere.Modules.DamagePenalty.Models;
using DPEntity = RentalSphere.Modules.DamagePenalty.Models.DamagePenalty;

namespace RentalSphere.Modules.DamagePenalty.Repositories;

public interface IDamagePenaltyRepository
{
    Task<List<DPEntity>> ListAsync(string? statusFilter = null, int? rentalTransactionId = null, string? sort = null);
    Task<int> CountByStatusAsync(DamagePenaltyStatus status);
    Task<DPEntity?> GetAsync(int id);
    Task<List<DPEntity>> ListByTransactionAsync(int rentalTransactionId);
    Task<List<DPEntity>> ListByInvoiceAsync(int invoiceId);
    Task AddAsync(DPEntity entity);
    void AddSync(DPEntity entity);
    void Update(DPEntity entity);
    Task<int> SaveChangesAsync();
}

public class DamagePenaltyRepository : IDamagePenaltyRepository
{
    private readonly ApplicationDbContext _db;
    public DamagePenaltyRepository(ApplicationDbContext db) { _db = db; }

    public async Task<List<DPEntity>> ListAsync(string? statusFilter = null, int? rentalTransactionId = null, string? sort = null)
    {
        var q = _db.DamagePenalties
            .Include(p => p.Equipment)
            .Include(p => p.EquipmentItem)
            .Include(p => p.ReportedByUser)
            .Include(p => p.ResolvedByUser)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(statusFilter)
            && Enum.TryParse<DamagePenaltyStatus>(statusFilter, out var st))
        {
            q = q.Where(p => p.Status == st);
        }
        if (rentalTransactionId.HasValue)
            q = q.Where(p => p.RentalTransactionID == rentalTransactionId.Value);

        q = sort?.ToLowerInvariant() switch
        {
            "id"            => q.OrderBy(p => p.DamagePenaltyID),
            "id_desc"       => q.OrderByDescending(p => p.DamagePenaltyID),
            "tx"            => q.OrderBy(p => p.RentalTransactionID),
            "tx_desc"       => q.OrderByDescending(p => p.RentalTransactionID),
            "amount"        => q.OrderBy(p => p.Amount),
            "amount_desc"   => q.OrderByDescending(p => p.Amount),
            "date"          => q.OrderBy(p => p.ReportedAt),
            "date_desc"     => q.OrderByDescending(p => p.ReportedAt),
            _               => q.OrderByDescending(p => p.ReportedAt),
        };

        return await q.ToListAsync();
    }

    public Task<int> CountByStatusAsync(DamagePenaltyStatus status) =>
        _db.DamagePenalties.CountAsync(p => p.Status == status);

    public Task<DPEntity?> GetAsync(int id) =>
        _db.DamagePenalties
            .Include(p => p.Equipment)
            .Include(p => p.EquipmentItem)
            .Include(p => p.ReportedByUser)
            .Include(p => p.ResolvedByUser)
            .FirstOrDefaultAsync(p => p.DamagePenaltyID == id);

    public Task<List<DPEntity>> ListByTransactionAsync(int rentalTransactionId) =>
        _db.DamagePenalties
            .Where(p => p.RentalTransactionID == rentalTransactionId)
            .OrderByDescending(p => p.ReportedAt)
            .ToListAsync();

    /// <summary>Look up via InvoiceLine.ReferenceId; loose reference, not enforced FK.</summary>
    public async Task<List<DPEntity>> ListByInvoiceAsync(int invoiceId)
    {
        var lineRefs = await _db.InvoiceLines
            .Where(l => l.InvoiceID == invoiceId && l.LineType == InvoiceLineType.DamagePenalty && l.ReferenceId.HasValue)
            .Select(l => l.ReferenceId!.Value)
            .ToListAsync();
        if (lineRefs.Count == 0) return new List<DPEntity>();
        return await _db.DamagePenalties
            .Where(p => lineRefs.Contains(p.DamagePenaltyID))
            .ToListAsync();
    }

    public async Task AddAsync(DPEntity entity) => await _db.DamagePenalties.AddAsync(entity);
    public void AddSync(DPEntity entity) => _db.DamagePenalties.Add(entity);
    public void Update(DPEntity entity) => _db.DamagePenalties.Update(entity);
    public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
}
