using System.ComponentModel.DataAnnotations;
using RentalSphere.Identity;
using RentalSphere.Modules.Equipment.Models;

namespace RentalSphere.Modules.Maintenance.Models;

/// <summary>
/// Maintenance window for a single EquipmentItem. Opening pulls the unit out of the
/// Available pool (EquipmentItem.AvailabilityStatus = InMaintenance); closing restores it.
/// </summary>
public class MaintenanceRecord
{
    public int MaintenanceRecordID { get; set; }

    public int EquipmentItemID { get; set; }
    public EquipmentItem EquipmentItem { get; set; } = null!;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpectedEnd { get; set; }
    public DateTime? EndedAt { get; set; }

    [Required, StringLength(500)]
    public string Reason { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Notes { get; set; }

    public decimal Cost { get; set; }

    public MaintenanceStatus Status { get; set; } = MaintenanceStatus.InProgress;

    public string? OpenedByUserId { get; set; }
    public ApplicationUser? OpenedByUser { get; set; }

    public string? ClosedByUserId { get; set; }
    public ApplicationUser? ClosedByUser { get; set; }
}

public enum MaintenanceStatus
{
    InProgress,
    Completed,
}
