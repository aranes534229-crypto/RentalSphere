using System.ComponentModel.DataAnnotations;

namespace RentalSphere.Modules.Equipment.DTOs;

public class EquipmentItemListItemDto
{
    public int ItemID { get; set; }
    public int EquipmentID { get; set; }
    public string EquipmentName { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string AvailabilityStatus { get; set; } = "Available";
    public string? Location { get; set; }
    public DateTime? LastStatusChange { get; set; }
}

public class EquipmentItemDetailsDto
{
    public int ItemID { get; set; }
    public int EquipmentID { get; set; }
    public string EquipmentName { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string AvailabilityStatus { get; set; } = "Available";
    public string? Location { get; set; }
    public DateTime? LastStatusChange { get; set; }
}

public class EquipmentItemCreateDto
{
    [Required]
    [Display(Name = "Equipment")]
    public int EquipmentID { get; set; }

    [Required]
    [Display(Name = "Availability status")]
    public string AvailabilityStatus { get; set; } = "Available";

    [StringLength(200)]
    [Display(Name = "Location")]
    public string? Location { get; set; }
}

public class EquipmentItemEditDto
{
    public int ItemID { get; set; }

    [Required, StringLength(100)]
    [Display(Name = "Serial number")]
    public string SerialNumber { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Availability status")]
    public string AvailabilityStatus { get; set; } = "Available";

    [StringLength(200)]
    [Display(Name = "Location")]
    public string? Location { get; set; }
}
