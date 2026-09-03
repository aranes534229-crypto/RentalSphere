using System.ComponentModel.DataAnnotations;

namespace RentalSphere.Modules.Billing.Models;

/// <summary>
/// One row on an invoice. LineTotal is persisted (computed at write time) so invoice
/// snapshots survive rate changes on the underlying EquipmentCatalog row.
/// </summary>
public class InvoiceLine
{
    public int InvoiceLineID { get; set; }

    public int InvoiceID { get; set; }
    public Invoice Invoice { get; set; } = null!;

    public InvoiceLineType LineType { get; set; }

    [Required, StringLength(500)]
    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; } = 1m;

    public decimal UnitAmount { get; set; }

    /// <summary>Quantity * UnitAmount, captured at write time.</summary>
    public decimal LineTotal { get; set; }

    /// <summary>Admin can waive part of a line (never negative, never exceeds LineTotal).</summary>
    public decimal WaivedAmount { get; set; }

    /// <summary>Loose pointer to source row (RentalTransactionItemID, DamagePenaltyID, etc.).</summary>
    public int? ReferenceId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum InvoiceLineType
{
    Rental,
    LateFee,
    DamagePenalty,
    ManualAdjustment,
}
