using RentalSphere.Identity;
using CustomerEntity = RentalSphere.Modules.CustomerManagement.Models.Customer;

namespace RentalSphere.Modules.CustomerCrm.Models;

/// <summary>
/// Free-form interaction log entry on a customer profile.
/// Staff/Admin add notes; customers can read their own.
/// </summary>
public class CustomerNote
{
    public int CustomerNoteID { get; set; }

    public int CustomerID { get; set; }
    public CustomerEntity? Customer { get; set; }

    /// <summary>Note body. Required, max 2000 chars.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Staff/Admin flag for "needs attention". Surface in follow-up queue and customer summary.
    /// </summary>
    public bool IsFlagged { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Restrict FK to AspNetUsers — author identity preserved.</summary>
    public string? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }
}
