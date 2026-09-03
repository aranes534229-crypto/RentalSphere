using System.ComponentModel.DataAnnotations;
using RentalSphere.Identity;
using RentalSphere.Modules.CustomerManagement.Models;
using RentalSphere.Modules.RentalTransaction.Models;
using RTEntity = RentalSphere.Modules.RentalTransaction.Models.RentalTransaction;

namespace RentalSphere.Modules.Billing.Models;

public class Invoice
{
    public int InvoiceID { get; set; }

    public int RentalTransactionID { get; set; }
    public RTEntity RentalTransaction { get; set; } = null!;

    /// <summary>Denormalized snapshot for fast list filtering.</summary>
    public int CustomerID { get; set; }
    public Customer Customer { get; set; } = null!;

    [Required, StringLength(20)]
    public string InvoiceNumber { get; set; } = string.Empty;

    public DateTime InvoiceDate { get; set; } = DateTime.UtcNow;
    public DateTime? DueDate { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid;

    /// <summary>Sum of (LineTotal) across all lines, before waivers.</summary>
    public decimal SubTotal { get; set; }

    /// <summary>SubTotal minus sum of line WaivedAmount. Recomputed on every line change.</summary>
    public decimal TotalAmount { get; set; }

    public decimal AmountPaid { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public DateTime? VoidedAt { get; set; }
    public string? VoidedByUserId { get; set; }
    public ApplicationUser? VoidedByUser { get; set; }
    [StringLength(500)]
    public string? VoidedReason { get; set; }

    public string? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }

    public ICollection<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public enum InvoiceStatus
{
    Unpaid,
    PartiallyPaid,
    Paid,
    Voided,
}
