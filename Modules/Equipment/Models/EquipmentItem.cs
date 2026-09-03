namespace RentalSphere.Modules.Equipment.Models;

/// <summary>
/// Lifecycle status of one rentable unit. Source of truth for availability across date
/// ranges. Equipment.StockQuantity is denormalized from the count of items in "Available".
/// </summary>
public enum AvailabilityStatus
{
    Available = 0,
    Reserved = 1,
    CheckedOut = 2,
    InMaintenance = 3,
    Retired = 4,
}

public class EquipmentItem
{
    public int ItemID { get; set; }
    public int EquipmentID { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public AvailabilityStatus AvailabilityStatus { get; set; } = AvailabilityStatus.Available;
    public string? Location { get; set; }
    public DateTime? LastStatusChange { get; set; }

    // Navigation
    public EquipmentCatalog Equipment { get; set; } = null!;
}
