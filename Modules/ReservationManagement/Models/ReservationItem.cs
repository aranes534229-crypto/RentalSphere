using System.ComponentModel.DataAnnotations;
using RentalSphere.Modules.Equipment.Models;

namespace RentalSphere.Modules.ReservationManagement.Models;

/// <summary>
/// Line-item join table linking a Reservation to Equipment (and optionally a concrete EquipmentItem).
/// </summary>
public class ReservationItem
{
    public int ReservationItemID { get; set; }

    public int ReservationID { get; set; }
    public Reservation Reservation { get; set; } = null!;

    public int EquipmentID { get; set; }
    public EquipmentCatalog Equipment { get; set; } = null!;

    [Range(1, 10000)]
    public int Quantity { get; set; } = 1;

    /// <summary>Set by Confirm: which concrete unit(s) were pinned. Nullable to allow model-only rows on Create.</summary>
    public int? AssignedItemID { get; set; }
    public EquipmentItem? AssignedItem { get; set; }

    public string? Notes { get; set; }
}