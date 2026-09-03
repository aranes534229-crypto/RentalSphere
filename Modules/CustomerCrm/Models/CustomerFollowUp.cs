using RentalSphere.Identity;
using CustomerEntity = RentalSphere.Modules.CustomerManagement.Models.Customer;

namespace RentalSphere.Modules.CustomerCrm.Models;

public enum FollowUpStatus
{
    Pending,
    Done,
    Cancelled,
}

/// <summary>
/// Scheduled follow-up tied to a customer. Drives CRM reminders and the daily queue.
/// </summary>
public class CustomerFollowUp
{
    public int CustomerFollowUpID { get; set; }

    public int CustomerID { get; set; }
    public CustomerEntity? Customer { get; set; }

    /// <summary>Why the follow-up exists — required, free text.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Date the follow-up is due (UTC date).</summary>
    public DateTime FollowUpDate { get; set; }

    public FollowUpStatus Status { get; set; } = FollowUpStatus.Pending;

    /// <summary>Staff member responsible. Null = unassigned (visible to everyone).</summary>
    public string? AssignedToUserId { get; set; }
    public ApplicationUser? AssignedToUser { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>Optional outcome note once the follow-up is resolved.</summary>
    public string? ResolutionNotes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }

    /// <summary>
    /// Set when the assignee first views this follow-up (via the CRM tab or the
    /// Queue). Stays null when unassigned — those follow-ups are team-visible
    /// and have no single "unacked for me" state.
    /// </summary>
    public DateTime? AcknowledgedAt { get; set; }

    /// <summary>UserId who acked. Must match AssignedToUserId; only the assignee acks.</summary>
    public string? AcknowledgedByUserId { get; set; }
}
