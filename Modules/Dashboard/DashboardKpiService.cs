using Microsoft.EntityFrameworkCore;
using RentalSphere.Modules.Billing.Models;
using RentalSphere.Modules.CustomerCrm.Models;
using RentalSphere.Modules.RentalTransaction.Models;
using RentalSphere.Data;

namespace RentalSphere.Modules.Dashboard;

/// <summary>
/// One-shot KPI snapshot for the home page.
/// All four queries are sequential (DbContext is not thread-safe).
/// </summary>
public class DashboardKpiService
{
    private readonly ApplicationDbContext _db;

    public DashboardKpiService(ApplicationDbContext db) { _db = db; }

    public async Task<DashboardKpiViewModel> GetAsync()
    {
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        var todaysRevenue = await _db.Payments
            .Where(p => p.PaymentDate >= today && p.PaymentDate < tomorrow)
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;

        var activeRentals = await _db.RentalTransactions
            .CountAsync(t => t.Status == RentalTransactionStatus.Active);

        var openInvoices = await _db.Invoices
            .CountAsync(i => i.Status == InvoiceStatus.Unpaid
                          || i.Status == InvoiceStatus.PartiallyPaid);

        var overdueFollowUps = await _db.CustomerFollowUps
            .CountAsync(f => f.Status == FollowUpStatus.Pending
                          && f.FollowUpDate < today);

        return new DashboardKpiViewModel
        {
            TodaysRevenue = todaysRevenue,
            ActiveRentals = activeRentals,
            OpenInvoices = openInvoices,
            OverdueFollowUps = overdueFollowUps,
        };
    }
}

public class DashboardKpiViewModel
{
    public decimal TodaysRevenue { get; set; }
    public int ActiveRentals { get; set; }
    public int OpenInvoices { get; set; }
    public int OverdueFollowUps { get; set; }
}
