using System.ComponentModel.DataAnnotations;
using RentalSphere.Modules.DamagePenalty.Models;

namespace RentalSphere.Modules.DamagePenalty.DTOs;

public class DamagePenaltyListItemDto
{
    public int DamagePenaltyID { get; set; }
    public int RentalTransactionID { get; set; }
    public string? EquipmentName { get; set; }
    public string? SerialNumber { get; set; }
    public DamagePenaltyType PenaltyType { get; set; }
    public string PenaltyTypeName => PenaltyType.ToString();
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal WaivedAmount { get; set; }
    public DamagePenaltyStatus Status { get; set; }
    public string StatusName => Status.ToString();
    public DateTime ReportedAt { get; set; }
    public string? ReportedByUserName { get; set; }
}

public class DamagePenaltyDetailsDto
{
    public int DamagePenaltyID { get; set; }
    public int RentalTransactionID { get; set; }
    public int? InvoiceID { get; set; }
    public string? EquipmentName { get; set; }
    public string? SerialNumber { get; set; }
    public DamagePenaltyType PenaltyType { get; set; }
    public string PenaltyTypeName => PenaltyType.ToString();
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal WaivedAmount { get; set; }
    public DamagePenaltyStatus Status { get; set; }
    public string StatusName => Status.ToString();
    public DateTime ReportedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ReportedByUserName { get; set; }
    public string? ResolvedByUserName { get; set; }
    public string? Notes { get; set; }
}

public class DamagePenaltyCreateDto
{
    [Required]
    [Display(Name = "Rental transaction")]
    public int RentalTransactionID { get; set; }

    [Required]
    [Display(Name = "Penalty type")]
    public DamagePenaltyType PenaltyType { get; set; } = DamagePenaltyType.Damage;

    [Display(Name = "Equipment (optional)")]
    public int? EquipmentID { get; set; }

    [Display(Name = "Equipment unit (optional)")]
    public int? EquipmentItemID { get; set; }

    [Required, StringLength(500)]
    [Display(Name = "Description")]
    public string Description { get; set; } = string.Empty;

    [Range(0, 9_999_999.99)]
    [Display(Name = "Amount")]
    public decimal Amount { get; set; }

    [StringLength(2000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }
}

public class DamagePenaltyAmountUpdateDto
{
    [Required]
    public int DamagePenaltyID { get; set; }

    [Required, Range(0, 9_999_999.99)]
    public decimal Amount { get; set; }
}

public class DamagePenaltyWaiveDto
{
    [Required]
    public int DamagePenaltyID { get; set; }

    [Required, Range(0, 9_999_999.99)]
    public decimal Amount { get; set; }

    [StringLength(500)]
    public string? Reason { get; set; }
}
