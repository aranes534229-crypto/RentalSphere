using System.ComponentModel.DataAnnotations;
using RentalSphere.Identity;
using RentalSphere.Modules.Equipment.Models;
using RentalSphere.Modules.RentalTransaction.Models;
using RTEntity = RentalSphere.Modules.RentalTransaction.Models.RentalTransaction;

namespace RentalSphere.Modules.DamagePenalty.Models;

/// <summary>
/// Tracks a damage or penalty event against a rental. Becomes a line on the invoice
/// when Status flips to AppliedToInvoice (handled by BillingService).
/// </summary>
public class DamagePenalty
{
    public int DamagePenaltyID { get; set; }

    public int RentalTransactionID { get; set; }
    public RTEntity RentalTransaction { get; set; } = null!;

    /// <summary>Nullable: some penalties are transaction-level (e.g., lost key).</summary>
    public int? EquipmentID { get; set; }
    public EquipmentCatalog? Equipment { get; set; }

    public int? EquipmentItemID { get; set; }
    public EquipmentItem? EquipmentItem { get; set; }

    public DamagePenaltyType PenaltyType { get; set; } = DamagePenaltyType.Damage;

    [Required, StringLength(500)]
    public string Description { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public decimal WaivedAmount { get; set; }

    public DamagePenaltyStatus Status { get; set; } = DamagePenaltyStatus.Pending;

    public DateTime ReportedAt { get; set; } = DateTime.UtcNow;

    public string? ReportedByUserId { get; set; }
    public ApplicationUser? ReportedByUser { get; set; }

    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedByUserId { get; set; }
    public ApplicationUser? ResolvedByUser { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }
}

public enum DamagePenaltyType
{
    Damage,
    LateFee,
    Loss,
    Cleaning,
    Other,
}

public enum DamagePenaltyStatus
{
    Pending,
    AppliedToInvoice,
    Waived,
}
