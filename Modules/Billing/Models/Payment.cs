using System.ComponentModel.DataAnnotations;
using RentalSphere.Identity;

namespace RentalSphere.Modules.Billing.Models;

public class Payment
{
    public int PaymentID { get; set; }

    public int InvoiceID { get; set; }
    public Invoice Invoice { get; set; } = null!;

    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

    public decimal Amount { get; set; }

    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;

    [StringLength(100)]
    public string? ReferenceNumber { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    public string? RecordedByUserId { get; set; }
    public ApplicationUser? RecordedByUser { get; set; }
}

public enum PaymentMethod
{
    Cash,
    BankTransfer,
    Check,
    Other,
}
