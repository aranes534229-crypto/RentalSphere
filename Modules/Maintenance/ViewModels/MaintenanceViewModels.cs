using Microsoft.AspNetCore.Mvc.Rendering;
using RentalSphere.Modules.Equipment.Services;
using RentalSphere.Modules.Maintenance.DTOs;

namespace RentalSphere.Modules.Maintenance.ViewModels;

public class MaintenanceIndexViewModel
{
    public List<MaintenanceListItemDto> Items { get; set; } = new();
    public string? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);

    public List<SelectListItem> Statuses { get; } = new()
    {
        new SelectListItem("All statuses", ""),
        new SelectListItem("In Progress", "InProgress"),
        new SelectListItem("Completed", "Completed"),
    };

    public string PageUrl(int page) =>
        $"?statusFilter={StatusFilter}&page={page}&pageSize={PageSize}";
}

public class MaintenanceOpenViewModel
{
    public MaintenanceOpenDto Form { get; set; } = new();
    public List<EquipmentLookupDto> AvailableEquipment { get; set; } = new();
    public List<EquipmentItemLookupDto> AvailableItems { get; set; } = new();
    public List<EquipmentCategoryGroup> Categories { get; set; } = new();
    public List<EquipmentItemGroup> AvailableItemsGrouped { get; set; } = new();
}

public class EquipmentItemGroup
{
    public int EquipmentID { get; set; }
    public string EquipmentName { get; set; } = string.Empty;
    public List<EquipmentItemLookupDto> Items { get; set; } = new();
}

public class EquipmentCategoryGroup
{
    public int CategoryID { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public List<EquipmentItemGroup> Equipment { get; set; } = new();
}

public class MaintenanceDetailsViewModel
{
    public MaintenanceDetailsDto Record { get; set; } = new();
    public MaintenanceCloseDto CloseForm { get; set; } = new();
}
