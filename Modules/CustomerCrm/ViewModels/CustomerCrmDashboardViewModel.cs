namespace RentalSphere.Modules.CustomerCrm.ViewModels;

public class CustomerCrmDashboardRow
{
    public int CustomerID { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int NoteCount { get; set; }
    public int OverdueFollowUps { get; set; }

    /// <summary>Pending follow-ups assigned to the current user.</summary>
    public int PendingForMe { get; set; }

    /// <summary>Notes on this customer that the current user has not yet read.</summary>
    public int UnreadNotesForMe { get; set; }
}

public class CustomerCrmDashboardViewModel
{
    public string? Search { get; set; }
    public List<CustomerCrmDashboardRow> Rows { get; set; } = new();

    /// <summary>Total pending follow-ups assigned to the current user across all customers.</summary>
    public int TotalPendingForMe { get; set; }

    /// <summary>Total unread notes across all customers for the current user.</summary>
    public int TotalUnreadNotesForMe { get; set; }

    /// <summary>Combined CRM activity (unread notes + pending follow-ups) for the global bell.</summary>
    public int TotalUnreadCrmItems { get; set; }

    // Paging / sorting — mirrors MaintenanceIndexViewModel so the dashboard
    // shares one visual language with the other module landing pages.
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public string? Sort { get; set; }

    /// <summary>URL for a sortable column header link.</summary>
    public string SortUrl(string column)
    {
        // Toggle: ascending <-> descending. Default to 'asc' (no suffix) on first click.
        string next;
        if (string.Equals(Sort, column, StringComparison.OrdinalIgnoreCase))
            next = column + "_desc";
        else if (string.Equals(Sort, column + "_desc", StringComparison.OrdinalIgnoreCase))
            next = column; // back to asc — but keep the column token
        else
            next = column;

        return $"?search={Search ?? ""}&sort={next}&page=1&pageSize={PageSize}";
    }

    /// <summary>URL for a pagination pill.</summary>
    public string PageUrl(int page) =>
        $"?search={Search ?? ""}&sort={Sort ?? ""}&page={page}&pageSize={PageSize}";
}
