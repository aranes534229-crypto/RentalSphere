namespace RentalSphere.Modules.Equipment.Models;

public class Category
{
    public int CategoryID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<EquipmentCatalog> Equipment { get; set; } = new List<EquipmentCatalog>();
}
