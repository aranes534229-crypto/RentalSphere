using System.ComponentModel.DataAnnotations;
using RentalSphere.Modules.RentalTransaction.Models;

namespace RentalSphere.Modules.RentalTransaction.DTOs;

public class RentalTransactionItemDto
{
    public int RentalTransactionItemID { get; set; }
    public int EquipmentID { get; set; }
    public string EquipmentName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal DailyRate { get; set; }
    public decimal LineTotal { get; set; }
    public int? EquipmentItemID { get; set; }
    public string? SerialNumber { get; set; }
}

public class RentalTransactionListItemDto
{
    public int RentalTransactionID { get; set; }
    public int ReservationID { get; set; }
    public int CustomerID { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime CheckoutDate { get; set; }
    public DateTime ExpectedReturnDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Active";
}

public class RentalTransactionDetailsDto
{
    public int RentalTransactionID { get; set; }
    public int ReservationID { get; set; }
    public int CustomerID { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public DateTime CheckoutDate { get; set; }
    public DateTime ExpectedReturnDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public string? ConditionNotes { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Active";
    public string? ProcessedByUserName { get; set; }
    public string? ReturnedToUserName { get; set; }
    public List<RentalTransactionItemDto> Items { get; set; } = new();
}

public class RentalTransactionReturnDto
{
    [Required]
    public int RentalTransactionID { get; set; }

    [StringLength(2000)]
    [Display(Name = "Condition notes")]
    public string? ConditionNotes { get; set; }

    [Display(Name = "Mark as damaged for follow-up")]
    public bool FlaggedForDamage { get; set; }
}
