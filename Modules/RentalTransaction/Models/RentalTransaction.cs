using System.ComponentModel.DataAnnotations;
using RentalSphere.Identity;
using RentalSphere.Modules.CustomerManagement.Models;
using RentalSphere.Modules.ReservationManagement.Models;

namespace RentalSphere.Modules.RentalTransaction.Models;

/// <summary>
/// One row per rental. Lifecycle: Active (CheckoutDate set, ReturnDate empty) → Returned
/// (ReturnDate + ConditionNotes set on the same row) → optional Cancelled. Per CLAUDE.md
/// the lifecycle mutates a single record — Return does NOT create a new transaction.
/// </summary>
public class RentalTransaction
{
    public int RentalTransactionID { get; set; }

    /// <summary>Source Confirmed reservation. Nullable to allow future walk-ins (Batch 4).</summary>
    public int? ReservationID { get; set; }
    public Reservation? Reservation { get; set; }

    /// <summary>Snapshot at Checkout — never mutated by Return.</summary>
    public int CustomerID { get; set; }
    public Customer Customer { get; set; } = null!;

    public DateTime CheckoutDate { get; set; } = DateTime.UtcNow;
    public DateTime ExpectedReturnDate { get; set; }

    /// <summary>Empty while Active; set on Return.</summary>
    public DateTime? ReturnDate { get; set; }

    [StringLength(2000)]
    public string? ConditionNotes { get; set; }

    public RentalTransactionStatus Status { get; set; } = RentalTransactionStatus.Active;

    /// <summary>Snapshot of Reservation.TotalEstimatedCost at Checkout.</summary>
    public decimal TotalAmount { get; set; }

    public string? ProcessedByUserId { get; set; }
    public ApplicationUser? ProcessedByUser { get; set; }

    public string? ReturnedToUserId { get; set; }
    public ApplicationUser? ReturnedToUser { get; set; }

    /// <summary>Set true at Return when physical condition requires follow-up damage assessment.</summary>
    public bool FlaggedForDamage { get; set; }

    public ICollection<RentalTransactionItem> Items { get; set; } = new List<RentalTransactionItem>();
}

public enum RentalTransactionStatus
{
    Active,
    Returned,
    Cancelled,
}
