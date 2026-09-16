namespace RentalSphere.Modules.Dashboard.Models;

/// <summary>
/// Single KPI tile for the home dashboard: title, value, status badge, and a link.
/// </summary>
public class HomeDashboardKpiVm
{
    public string Title { get; set; } = string.Empty;   // e.g. "Today's Revenue"
    public string Icon { get; set; } = string.Empty;     // Bootstrap icon class, e.g. "bi bi-cash-coin"
    public string Value { get; set; } = string.Empty;    // pre-formatted for display

    /// <summary>Short status line shown as a badge under the value. Null → "No data yet".</summary>
    public string? BadgeLabel { get; set; }
    /// <summary>One of: success, warning, neutral, muted. Drives badge color.</summary>
    public string? BadgeTone { get; set; }

    public string? LinkController { get; set; }
    public string? LinkAction { get; set; }
    public string? LinkQuery { get; set; }   // e.g. "statusFilter=Overdue"
    public string? LinkLabel { get; set; }
}

/// <summary>
/// One day's bar in the "Rentals this week" SVG chart.
/// </summary>
public class DashboardBarDayVm
{
    public string Day { get; set; } = string.Empty;     // "Mon".."Sun"
    public int Count { get; set; }
    public decimal BarHeightPct { get; set; }           // 0–100 of the max-day height
}

/// <summary>
/// A single recent-activity line item.
/// </summary>
public class DashboardActivityVm
{
    public string Icon { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string RelativeTime { get; set; } = string.Empty;  // e.g. "2h ago"
    public string? LinkUrl { get; set; }

    /// <summary>
    /// Badge tone for the colored circle: "primary" (reservation),
    /// "success" (payment), "secondary" (return), "warning" (overdue).
    /// </summary>
    public string BadgeTone { get; set; } = "secondary";
}

/// <summary>
/// One quick-action button in the dashboard action strip.
/// </summary>
public class DashboardQuickActionVm
{
    public string Icon { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Controller { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? QueryString { get; set; }   // e.g. "statusFilter=Overdue"
}

/// <summary>
/// Top-level view model for the Staff/Admin home dashboard.
/// Strongly typed — replaces the old ViewBag.Kpi pattern.
/// </summary>
public class HomeDashboardViewModel
{
    public List<HomeDashboardKpiVm> Kpis { get; set; } = new();
    public List<DashboardBarDayVm> RentalsThisWeek { get; set; } = new();
    public List<DashboardActivityVm> RecentActivities { get; set; } = new();
    public bool HasActivity => RecentActivities.Any();
    public List<DashboardQuickActionVm> QuickActions { get; set; } = new();
}
