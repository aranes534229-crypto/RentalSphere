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
}
