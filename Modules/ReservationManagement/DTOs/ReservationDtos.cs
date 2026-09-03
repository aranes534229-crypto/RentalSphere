using System.ComponentModel.DataAnnotations;
using RentalSphere.Modules.ReservationManagement.Models;

namespace RentalSphere.Modules.ReservationManagement.DTOs;

public class ReservationItemInputDto
{
    [Required]
    [Display(Name = "Equipment")]
    public int EquipmentID { get; set; }

    [Required, Range(1, 10000)]
    [Display(Name = "Quantity")]
    public int Quantity { get; set; } = 1;

    public string? Notes { get; set; }
}

public class ReservationCreateDto
{
    [Required]
    [Display(Name = "Customer")]
    public int CustomerID { get; set; }

    [Required]
    [Display(Name = "Rental start")]
    public DateTime RentalStartDate { get; set; }

    [Required]
    [Display(Name = "Rental end")]
    public DateTime RentalEndDate { get; set; }

    [Required, MinLength(1, ErrorMessage = "Add at least one equipment item.")]
    public List<ReservationItemInputDto> Items { get; set; } = new();

    [StringLength(1000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }
}

public class ReservationItemDto
{
    public int ReservationItemID { get; set; }
    public int EquipmentID { get; set; }
    public string EquipmentName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal DailyRate { get; set; }
    public decimal LineTotal { get; set; }
    public int? AssignedItemID { get; set; }
    public string? AssignedSerialNumber { get; set; }
    public string? Notes { get; set; }
}

public class ReservationListItemDto
{
    public int ReservationID { get; set; }
    public int CustomerID { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime ReservationDate { get; set; }
    public DateTime RentalStartDate { get; set; }
    public DateTime RentalEndDate { get; set; }
    public int ItemCount { get; set; }
    public decimal TotalEstimatedCost { get; set; }
    public string Status { get; set; } = "Pending";
}

public class ReservationDetailsDto
{
    public int ReservationID { get; set; }
    public int CustomerID { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public DateTime ReservationDate { get; set; }
    public DateTime RentalStartDate { get; set; }
    public DateTime RentalEndDate { get; set; }
    public int Days { get; set; }
    public List<ReservationItemDto> Items { get; set; } = new();
    public decimal TotalEstimatedCost { get; set; }
    public string Status { get; set; } = "Pending";
    public string? Notes { get; set; }
    public string? CreatedByUserName { get; set; }
    public string? CancellationReason { get; set; }
    public string? CancellationNotes { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledByName { get; set; }
    public string? CancelledByRole { get; set; }
}

public class AvailabilityLineResultDto
{
    public int EquipmentID { get; set; }
    public string EquipmentName { get; set; } = string.Empty;
    public int Requested { get; set; }
    public int Available { get; set; }
    public bool IsAvailable { get; set; }
    public string? Reason { get; set; }
}

public class AvailabilityResultDto
{
    public bool IsAvailable { get; set; }
    public List<AvailabilityLineResultDto> Lines { get; set; } = new();
}