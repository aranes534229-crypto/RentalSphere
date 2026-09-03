using System.ComponentModel.DataAnnotations;
using RentalSphere.Modules.Equipment.Models;

namespace RentalSphere.Modules.RentalTransaction.Models;

/// <summary>
/// Line item on a RentalTransaction. Pinned to a concrete EquipmentItem at Checkout so we
/// know exactly which unit is on the truck; nullable only to support legacy data imports.
/// </summary>
public class RentalTransactionItem
{
    public int RentalTransactionItemID { get; set; }

    public int RentalTransactionID { get; set; }
    public RentalTransaction RentalTransaction { get; set; } = null!;

    public int EquipmentID { get; set; }
    public EquipmentCatalog Equipment { get; set; } = null!;

    [Range(1, 10000)]
    public int Quantity { get; set; } = 1;

    public int? EquipmentItemID { get; set; }
    public EquipmentItem? EquipmentItem { get; set; }
}
