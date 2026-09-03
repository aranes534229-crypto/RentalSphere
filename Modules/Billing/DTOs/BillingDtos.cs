using System.ComponentModel.DataAnnotations;
using RentalSphere.Modules.Billing.Models;

namespace RentalSphere.Modules.Billing.DTOs;

public class InvoiceLineDto
{
    public int InvoiceLineID { get; set; }
    public InvoiceLineType LineType { get; set; }
    public string LineTypeName => LineType.ToString();
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitAmount { get; set; }
    public decimal LineTotal { get; set; }
    public decimal WaivedAmount { get; set; }
    public int? ReferenceId { get; set; }
}

public class InvoiceListItemDto
{
    public int InvoiceID { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public int RentalTransactionID { get; set; }
    public int CustomerID { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Balance => TotalAmount - AmountPaid;
    public string Status { get; set; } = "Unpaid";
}

public class InvoiceDetailsDto
{
    public int InvoiceID { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public int RentalTransactionID { get; set; }
    public int CustomerID { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal SubTotal { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Balance => TotalAmount - AmountPaid;
    public string Status { get; set; } = "Unpaid";
    public string? Notes { get; set; }
    public DateTime? VoidedAt { get; set; }
    public string? VoidedByUserName { get; set; }
    public string? VoidedReason { get; set; }
    public string? CreatedByUserName { get; set; }
    public List<InvoiceLineDto> Lines { get; set; } = new();
    public List<PaymentDto> Payments { get; set; } = new();
}

public class PaymentDto
{
    public int PaymentID { get; set; }
    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = "Cash";
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public string? RecordedByUserName { get; set; }
}

public class PaymentCreateDto
{
    [Required]
    public int InvoiceID { get; set; }

    [Required]
    [Display(Name = "Payment date")]
    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

    [Required, Range(0.01, 9_999_999.99)]
    [Display(Name = "Amount")]
    public decimal Amount { get; set; }

    [Required]
    [Display(Name = "Method")]
    public string Method { get; set; } = "Cash";

    [StringLength(100)]
    [Display(Name = "Reference #")]
    public string? ReferenceNumber { get; set; }

    [StringLength(500)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }
}
