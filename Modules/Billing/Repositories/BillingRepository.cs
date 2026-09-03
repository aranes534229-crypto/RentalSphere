using Microsoft.EntityFrameworkCore;
using RentalSphere.Data;
using RentalSphere.Modules.Billing.Models;

namespace RentalSphere.Modules.Billing.Repositories;

public interface IBillingRepository
{
    Task<List<Invoice>> ListAsync(string? statusFilter = null, string? search = null);
    Task<Invoice?> GetAsync(int id);
    Task<Invoice?> GetWithLinesAsync(int id);
    Task<Invoice?> GetByTransactionAsync(int rentalTransactionId);
    Task<InvoiceLine?> GetLineAsync(int invoiceLineId);
    Task<int> CountByMonthAsync(int year, int month);
    Task AddAsync(Invoice invoice);
    void Update(Invoice invoice);
    Task AddPaymentAsync(Payment payment);
    Task<int> SaveChangesAsync();
}

public class BillingRepository : IBillingRepository
{
    private readonly ApplicationDbContext _db;
    public BillingRepository(ApplicationDbContext db) { _db = db; }

    public async Task<List<Invoice>> ListAsync(string? statusFilter = null, string? search = null)
    {
        var q = _db.Invoices
            .Include(i => i.Customer)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(statusFilter)
            && Enum.TryParse<InvoiceStatus>(statusFilter, out var st))
        {
            q = q.Where(i => i.Status == st);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(i =>
                i.Customer.FirstName.ToLower().Contains(s) ||
                i.Customer.LastName.ToLower().Contains(s) ||
                i.Customer.Email.ToLower().Contains(s) ||
                i.InvoiceNumber.ToLower().Contains(s));
        }

        return await q.OrderByDescending(i => i.InvoiceDate).ToListAsync();
    }

    public Task<Invoice?> GetAsync(int id) =>
        _db.Invoices
            .Include(i => i.Customer)
            .Include(i => i.RentalTransaction)
            .Include(i => i.VoidedByUser)
            .Include(i => i.CreatedByUser)
            .FirstOrDefaultAsync(i => i.InvoiceID == id);

    public Task<Invoice?> GetWithLinesAsync(int id) =>
        _db.Invoices
            .Include(i => i.Customer)
            .Include(i => i.RentalTransaction)
            .Include(i => i.VoidedByUser)
            .Include(i => i.CreatedByUser)
            .Include(i => i.Lines)
            .Include(i => i.Payments).ThenInclude(p => p.RecordedByUser)
            .FirstOrDefaultAsync(i => i.InvoiceID == id);

    public Task<Invoice?> GetByTransactionAsync(int rentalTransactionId) =>
        _db.Invoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.RentalTransactionID == rentalTransactionId);

    public Task<InvoiceLine?> GetLineAsync(int invoiceLineId) =>
        _db.InvoiceLines.FirstOrDefaultAsync(l => l.InvoiceLineID == invoiceLineId);

    public async Task<int> CountByMonthAsync(int year, int month)
    {
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);
        return await _db.Invoices
            .Where(i => i.InvoiceDate >= start && i.InvoiceDate < end)
            .CountAsync();
    }

    public async Task AddAsync(Invoice invoice) => await _db.Invoices.AddAsync(invoice);
    public void Update(Invoice invoice) => _db.Invoices.Update(invoice);
    public async Task AddPaymentAsync(Payment payment) => await _db.Payments.AddAsync(payment);
    public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
}
