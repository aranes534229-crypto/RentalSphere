using System.ComponentModel.DataAnnotations;

namespace RentalSphere.Modules.Equipment.Models;

/// <summary>
/// Lifecycle status of an Equipment catalog entry. Distinct from per-item AvailabilityStatus
/// (which lives on EquipmentItem) — this is about whether the catalog itself is bookable.
/// </summary>
public enum EquipmentStatus
{
    Available = 0,
    Unavailable = 1,
    Retired = 2,
}

/// <summary>
/// A single equipment SKU in the rental catalog. Class is named EquipmentCatalog to
/// avoid a name collision with the parent namespace RentalSphere.Modules.Equipment.
/// </summary>
public class EquipmentCatalog
{
    public int EquipmentID { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CategoryID { get; set; }
    public string? Description { get; set; }
    public decimal DailyRate { get; set; }
    public int StockQuantity { get; set; }
    public EquipmentStatus Status { get; set; } = EquipmentStatus.Available;
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
    public string? ImageURL { get; set; }

    /// <summary>
    /// Optional prefix used to auto-generate EquipmentItem serial numbers, e.g. "TBL-" or "CHR-".
    /// New items get a serial like {prefix}{counter:0000}. Falls back to "EQ-" when blank.
    /// </summary>
    [MaxLength(8)]
    public string? SerialPrefix { get; set; }

    // Navigation
    public Category Category { get; set; } = null!;
    public ICollection<EquipmentItem> Items { get; set; } = new List<EquipmentItem>();
}
