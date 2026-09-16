using Microsoft.EntityFrameworkCore;
using RentalSphere.Modules.Billing.Models;
using RentalSphere.Modules.CustomerCrm.Models;
using RentalSphere.Modules.RentalTransaction.Models;
using RentalSphere.Data;
using RentalSphere.Modules.Dashboard.Models;

namespace RentalSphere.Modules.Dashboard;

/// <summary>
/// One-shot KPI snapshot + dashboard data for the home page.
/// All queries are sequential (DbContext is not thread-safe).
/// </summary>
public class DashboardKpiService
{
    private readonly ApplicationDbContext _db;

    public DashboardKpiService(ApplicationDbContext db) { _db = db; }

    /// <summary>
    /// Legacy KPIs — still used by the CRM dashboard badge components.
    /// </summary>
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

    /// <summary>
    /// Full dashboard view model — KPIs with badges, bar-chart data,
    /// recent activity, and quick-action buttons.
    /// </summary>
    public async Task<HomeDashboardViewModel> GetHomeDashboardVmAsync()
    {
        var kpis = await GetKpisWithBadgesAsync();
        var rentalsThisWeek = await GetRentalsThisWeekAsync();
        var activities = await GetRecentActivitiesAsync(5);
        var quickActions = GetQuickActions();

        return new HomeDashboardViewModel
        {
            Kpis = kpis,
            RentalsThisWeek = rentalsThisWeek,
            RecentActivities = activities,
            QuickActions = quickActions,
        };
    }

    private async Task<List<HomeDashboardKpiVm>> GetKpisWithBadgesAsync()
    {
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        var yesterday = today.AddDays(-1);

        // --- Today's Revenue + vs yesterday badge ---
        var todaysRevenue = await _db.Payments
            .Where(p => p.PaymentDate >= today && p.PaymentDate < tomorrow)
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;

        var yesterdayRevenue = await _db.Payments
            .Where(p => p.PaymentDate >= yesterday && p.PaymentDate < today)
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;

        string? revenueBadge = null;
        string? revenueTone = null;
        if (yesterdayRevenue > 0)
        {
            var pct = ((todaysRevenue - yesterdayRevenue) / yesterdayRevenue) * 100;
            var absPct = Math.Abs(pct);
            revenueBadge = pct >= 0
                ? $"+{absPct:F0}% vs yesterday"
                : $"-{absPct:F0}% vs yesterday";
            revenueTone = pct >= 0 ? "success" : "warning";
        }

        // --- Active Rentals ---
        var activeRentals = await _db.RentalTransactions
            .CountAsync(t => t.Status == RentalTransactionStatus.Active);

        // Due-back this week badge: Active rentals expected back Mon–Sun of current week.
        var (weekStart, weekEnd) = CurrentWeekBoundsUtc();
        var dueBackThisWeek = await _db.RentalTransactions
            .CountAsync(t => t.Status == RentalTransactionStatus.Active
                          && t.ExpectedReturnDate >= weekStart
                          && t.ExpectedReturnDate <= weekEnd);

        // --- Open Invoices ---
        var openInvoices = await _db.Invoices
            .CountAsync(i => i.Status == InvoiceStatus.Unpaid
                          || i.Status == InvoiceStatus.PartiallyPaid);

        var partiallyPaid = await _db.Invoices
            .CountAsync(i => i.Status == InvoiceStatus.PartiallyPaid);

        string? invoiceBadge = null;
        string? invoiceTone = null;
        if (partiallyPaid > 0)
        {
            invoiceBadge = $"{partiallyPaid} partially paid";
            invoiceTone = "neutral";
        }

        // --- Overdue Items (renamed from Overdue follow-ups) ---
        var overdueItems = await _db.CustomerFollowUps
            .CountAsync(f => f.Status == FollowUpStatus.Pending
                          && f.FollowUpDate < today);

        string? overdueBadge = null;
        string? overdueTone = null;
        if (overdueItems == 0)
        {
            overdueBadge = "All caught up";
            overdueTone = "success";
        }
        else
        {
            overdueBadge = "Follow-up queue open";
            overdueTone = "warning";
        }

        return new List<HomeDashboardKpiVm>
        {
            new()
            {
                Title = "Today's Revenue",
                Icon = "bi bi-cash-coin",
                Value = "&#8369;&nbsp;" + todaysRevenue.ToString("N2"),
                BadgeLabel = revenueBadge,
                BadgeTone = string.IsNullOrEmpty(revenueTone) ? "muted" : revenueTone,
                LinkController = "Invoices",
                LinkAction = "Index",
                LinkLabel = "View invoices",
            },
            new()
            {
                Title = "Active Rentals",
                Icon = "bi bi-box-seam",
                Value = activeRentals.ToString(),
                BadgeLabel = dueBackThisWeek > 0
                    ? $"{dueBackThisWeek} due back this week"
                    : "Due back this week",
                BadgeTone = dueBackThisWeek > 0 ? "warning" : "muted",
                LinkController = "RentalTransactions",
                LinkAction = "Index",
                LinkLabel = "View transactions",
            },
            new()
            {
                Title = "Open Invoices",
                Icon = "bi bi-receipt",
                Value = openInvoices.ToString(),
                BadgeLabel = invoiceBadge,
                BadgeTone = string.IsNullOrEmpty(invoiceTone) ? "muted" : invoiceTone,
                LinkController = "Invoices",
                LinkAction = "Index",
                LinkLabel = "Manage billing",
            },
            new()
            {
                Title = "Overdue Items",
                Icon = "bi bi-bell",
                Value = overdueItems.ToString(),
                BadgeLabel = overdueBadge,
                BadgeTone = string.IsNullOrEmpty(overdueTone) ? "muted" : overdueTone,
                LinkController = "CustomerCrm",
                LinkAction = "Dashboard",
                LinkLabel = "Open queue",
            },
        };
    }

    /// <summary>
    /// Group RentalTransactions by checkout day-of-week for the current ISO week (Mon–Sun, UTC).
    /// Returns 7 entries. BarHeightPct = count / max * 100 (so the tallest bar is 100%).
    /// </summary>
    private async Task<List<DashboardBarDayVm>> GetRentalsThisWeekAsync()
    {
        var (weekStart, weekEnd) = CurrentWeekBoundsUtc();

        // Mon=0 .. Sun=6 in .NET DayOfWeek (Sunday=0). We want Mon-first ordering.
        var countsByDow = new Dictionary<int, int>();
        for (int i = 0; i < 7; i++) countsByDow[i] = 0;  // index 0=Mon, 1=Tue, ..., 6=Sun

        var rentals = await _db.RentalTransactions
            .Where(t => t.CheckoutDate >= weekStart && t.CheckoutDate <= weekEnd)
            .ToListAsync();

        foreach (var r in rentals)
        {
            // Convert DayOfWeek (Sun=0) to Mon-first index (Mon=0..Sun=6)
            int dowIndex = ((int)r.CheckoutDate.DayOfWeek + 6) % 7;
            countsByDow[dowIndex]++;
        }

        var max = Math.Max(countsByDow.Values.Max(), 1);
        var dayLabels = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

        return dayLabels.Select((label, i) => new DashboardBarDayVm
        {
            Day = label,
            Count = countsByDow[i],
            BarHeightPct = (countsByDow[i] / (decimal)max) * 100m,
        }).ToList();
    }

    /// <summary>
    /// Union query across Reservations, Payments, RentalTransactions (returned),
    /// and CustomerFollowUps (overdue/pending). Ordered by most recent first.
    /// Returns a maximum of `limit` combined items. If nothing exists → empty list.
    /// </summary>
    private async Task<List<DashboardActivityVm>> GetRecentActivitiesAsync(int limit)
    {
        var now = DateTime.UtcNow;
        var items = new List<(DateTime Timestamp, DashboardActivityVm Vm)>();

        // Recent reservations
        var reservations = await _db.Reservations
            .OrderByDescending(r => r.ReservationDate)
            .Take(limit)
            .Select(r => new { r.ReservationDate, r.Status, r.Customer.FirstName, r.Customer.LastName })
            .ToListAsync();

        foreach (var r in reservations)
        {
            items.Add((r.ReservationDate, new DashboardActivityVm
            {
                Icon = "bi bi-calendar-plus",
                Label = $"New reservation for {r.FirstName} {r.LastName}",
                RelativeTime = RelativeTime(r.ReservationDate, now),
                LinkUrl = null,
                BadgeTone = "primary",
            }));
        }

        // Recent payments — Invoice is required, Customer is denormalized on Invoice (not null)
        var payments = await _db.Payments
            .OrderByDescending(p => p.PaymentDate)
            .Take(limit)
            .Select(p => new { p.PaymentDate, p.Amount, p.Invoice.Customer.FirstName, p.Invoice.Customer.LastName })
            .ToListAsync();

        foreach (var p in payments)
        {
            items.Add((p.PaymentDate, new DashboardActivityVm
            {
                Icon = "bi bi-cash-coin",
                Label = $"Payment of &#8369;{p.Amount:N2} received from {p.FirstName} {p.LastName}",
                RelativeTime = RelativeTime(p.PaymentDate, now),
                LinkUrl = null,
                BadgeTone = "success",
            }));
        }

        // Recent returns — Customer is required on RentalTransaction (not null)
        var returns = await _db.RentalTransactions
            .Where(t => t.ReturnDate != null)
            .OrderByDescending(t => t.ReturnDate)
            .Take(limit)
            .Select(t => new { ReturnDate = (DateTime)t.ReturnDate!, t.Customer.FirstName, t.Customer.LastName })
            .ToListAsync();

        foreach (var r in returns)
        {
            items.Add((r.ReturnDate, new DashboardActivityVm
            {
                Icon = "bi bi-box-seam",
                Label = $"Equipment returned — {r.FirstName} {r.LastName}",
                RelativeTime = RelativeTime(r.ReturnDate, now),
                LinkUrl = null,
                BadgeTone = "secondary",
            }));
        }

        // Overdue pending follow-ups — Customer is nullable on the nav, so use the FK ID
        var today = now.Date;
        var overdueFollowUps = await _db.CustomerFollowUps
            .Where(f => f.Status == FollowUpStatus.Pending && f.FollowUpDate < today)
            .OrderByDescending(f => f.FollowUpDate)
            .Take(limit)
            .Select(f => new { f.FollowUpDate, Reason = f.Reason, CustomerId = f.CustomerID })
            .ToListAsync();

        foreach (var f in overdueFollowUps)
        {
            items.Add((f.FollowUpDate, new DashboardActivityVm
            {
                Icon = "bi bi-exclamation-triangle",
                Label = $"Overdue follow-up: {f.Reason}",
                RelativeTime = RelativeTime(f.FollowUpDate, now),
                LinkUrl = null,
                BadgeTone = "warning",
            }));
        }

        return items
            .OrderByDescending(x => x.Timestamp)
            .Take(limit)
            .Select(x => x.Vm)
            .ToList();
    }

    /// <summary>
    /// The four quick-action buttons: New Reservation, Record Payment,
    /// Add Equipment, View Overdue.
    /// </summary>
    private static List<DashboardQuickActionVm> GetQuickActions() => new()
    {
        new() { Icon = "bi bi-calendar-plus", Label = "New Reservation", Controller = "Reservations", Action = "Create" },
        new() { Icon = "bi bi-cash-coin", Label = "Record Payment", Controller = "Invoices", Action = "Index" },
        new() { Icon = "bi bi-box-seam", Label = "Add Equipment", Controller = "Equipment", Action = "Create" },
        new() { Icon = "bi bi-exclamation-triangle", Label = "View Overdue", Controller = "RentalTransactions", Action = "Index", QueryString = "statusFilter=Overdue" },
    };

    #region Helpers

    /// <summary>
    /// Current ISO week bounds in UTC: Monday 00:00 → Sunday 23:59:59.
    /// </summary>
    private static (DateTime Start, DateTime End) CurrentWeekBoundsUtc()
    {
        var today = DateTime.UtcNow.Date;
        // Monday = DayOfWeek.Monday = 1; subtract to get to Monday of current week.
        int daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var monday = today.AddDays(-daysSinceMonday);
        var sunday = monday.AddDays(6).AddDays(1).AddTicks(-1); // end of Sunday
        return (monday, sunday);
    }

    /// <summary>
    /// Format a timestamp as a short relative string: "2h ago", "Yesterday", "3d ago", "Just now".
    /// </summary>
    private static string RelativeTime(DateTime timestamp, DateTime now)
    {
        var span = now - timestamp;

        if (span.TotalSeconds < 60) return "Just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 2) return "Yesterday";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
        return timestamp.ToString("MMM d");
    }

    #endregion
}

/// <summary>
/// Legacy KPI model — kept for the CRM dashboard badge components.
/// </summary>
public class DashboardKpiViewModel
{
    public decimal TodaysRevenue { get; set; }
    public int ActiveRentals { get; set; }
    public int OpenInvoices { get; set; }
    public int OverdueFollowUps { get; set; }
}
