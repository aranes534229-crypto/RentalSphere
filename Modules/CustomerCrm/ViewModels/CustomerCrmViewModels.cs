using Microsoft.AspNetCore.Mvc.Rendering;
using RentalSphere.Modules.CustomerCrm.DTOs;

namespace RentalSphere.Modules.CustomerCrm.ViewModels;

/// <summary>
/// Combined view for a customer's CRM page: notes + follow-ups, plus author/assignee dropdowns.
/// </summary>
public class CustomerCrmViewModel
{
    public int CustomerID { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;

    public List<CustomerNoteDto> Notes { get; set; } = new();
    public List<CustomerFollowUpDto> FollowUps { get; set; } = new();

    public CustomerNoteCreateDto NewNote { get; set; } = new();
    public CustomerFollowUpCreateDto NewFollowUp { get; set; } = new();

    /// <summary>Optional status filter for follow-ups (All / Pending / Done / Cancelled).</summary>
    public string? FollowUpStatusFilter { get; set; }

    /// <summary>Staff dropdown for "Assigned to".</summary>
    public List<SelectListItem> StaffOptions { get; set; } = new();
}
