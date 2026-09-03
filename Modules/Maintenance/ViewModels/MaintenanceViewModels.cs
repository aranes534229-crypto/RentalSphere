using Microsoft.AspNetCore.Mvc.Rendering;
using RentalSphere.Modules.Equipment.Services;
using RentalSphere.Modules.Maintenance.DTOs;

namespace RentalSphere.Modules.Maintenance.ViewModels;

public class MaintenanceIndexViewModel
{
    public List<MaintenanceListItemDto> Items { get; set; } = new();
    public string? StatusFilter { get; set; }
    public List<SelectListItem> Statuses { get; } = new()
    {
        new SelectListItem("All statuses", ""),
        new SelectListItem("In Progress", "InProgress"),
        new SelectListItem("Completed", "Completed"),
    };
}

public class MaintenanceOpenViewModel
{
    public MaintenanceOpenDto Form { get; set; } = new();
    public List<EquipmentLookupDto> AvailableEquipment { get; set; } = new();
    public List<EquipmentItemLookupDto> AvailableItems { get; set; } = new();
}

public class MaintenanceDetailsViewModel
{
    public MaintenanceDetailsDto Record { get; set; } = new();
    public MaintenanceCloseDto CloseForm { get; set; } = new();
}
