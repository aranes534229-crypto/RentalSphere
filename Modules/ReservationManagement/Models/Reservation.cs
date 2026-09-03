using System.ComponentModel.DataAnnotations;
using RentalSphere.Identity;
using RentalSphere.Modules.CustomerManagement.Models;
using RentalSphere.Modules.Equipment.Models;

namespace RentalSphere.Modules.ReservationManagement.Models;

/// <summary>
/// A multi-item reservation request. Transitions: Pending → (Confirmed | Cancelled | Expired).
/// Completion (CheckedOut / Returned / Billed) lives on RentalTransaction (Batch 3+).
/// </summary>
public class Reservation
{
    public int ReservationID { get; set; }

    public int CustomerID { get; set; }
    public Customer Customer { get; set; } = null!;

    public DateTime ReservationDate { get; set; } = DateTime.UtcNow;

    public DateTime RentalStartDate { get; set; }
    public DateTime RentalEndDate { get; set; }

    public ReservationStatus Status { get; set; } = ReservationStatus.Pending;

    /// <summary>Sum of (Equipment.DailyRate * days * Quantity) across all line items.</summary>
    public decimal TotalEstimatedCost { get; set; }

    public string? Notes { get; set; }

    /// <summary>The staff/admin who created the reservation on behalf of the customer (nullable).</summary>
    public string? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }

    /// <summary>Set when the reservation is cancelled. Stored as the enum name (string).</summary>
    public ReservationCancellationReason? CancellationReason { get; set; }

    /// <summary>Optional staff note accompanying a cancellation (free text).</summary>
    [StringLength(2000)]
    public string? CancellationNotes { get; set; }

    /// <summary>When the cancellation was applied. Null until cancelled.</summary>
    public DateTime? CancelledAt { get; set; }

    public ICollection<ReservationItem> Items { get; set; } = new List<ReservationItem>();
}

public enum ReservationStatus
{
    Pending,
    Confirmed,
    CheckedOut,
    Cancelled,
    Expired,
    Completed,
}

public enum ReservationCancellationReason
{
    CustomerRequested = 0,
    ItemUnavailableOrDamaged = 1,
    PaymentOrPolicyViolation = 2,
    Other = 3,
}