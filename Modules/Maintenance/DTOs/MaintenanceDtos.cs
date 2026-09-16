using System.ComponentModel.DataAnnotations;
using RentalSphere.Modules.Maintenance.Models;

namespace RentalSphere.Modules.Maintenance.DTOs;

public class MaintenanceOpenDto
{
    [Required, MinLength(1, ErrorMessage = "Select at least one unit.")]
    [Display(Name = "Equipment units")]
    public List<int> EquipmentItemIDs { get; set; } = new();

    [Required, StringLength(500)]
    [Display(Name = "Reason")]
    public string Reason { get; set; } = string.Empty;

    [Display(Name = "Expected return")]
    public DateTime? ExpectedEnd { get; set; }

    [Range(0, 9_999_999.99)]
    [Display(Name = "Estimated cost per unit")]
    public decimal Cost { get; set; }

    [StringLength(2000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }
}

public class MaintenanceCloseDto
{
    [Required]
    public int MaintenanceRecordID { get; set; }

    [Range(0, 9_999_999.99)]
    [Display(Name = "Repair cost")]
    public decimal Cost { get; set; }

    [Display(Name = "Expected completion")]
    public DateTime? ExpectedEnd { get; set; }

    [StringLength(2000)]
    [Display(Name = "Closure notes")]
    public string? Notes { get; set; }
}

public class MaintenanceListItemDto
{
    public int MaintenanceRecordID { get; set; }
    public int EquipmentItemID { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public string EquipmentName { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? ExpectedEnd { get; set; }
    public DateTime? EndedAt { get; set; }
    public string Reason { get; set; } = string.Empty;
    public decimal Cost { get; set; }
    public string Status { get; set; } = "InProgress";
    public string? OpenedByUserName { get; set; }
    public string? ClosedByUserName { get; set; }
}

public class MaintenanceDetailsDto
{
    public int MaintenanceRecordID { get; set; }
    public int EquipmentItemID { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public string EquipmentName { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? ExpectedEnd { get; set; }
    public DateTime? EndedAt { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public decimal Cost { get; set; }
    public string Status { get; set; } = "InProgress";
    public string? OpenedByUserName { get; set; }
    public string? ClosedByUserName { get; set; }
}
