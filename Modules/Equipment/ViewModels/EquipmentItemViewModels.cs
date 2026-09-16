using Microsoft.AspNetCore.Mvc.Rendering;
using RentalSphere.Modules.Equipment.DTOs;

namespace RentalSphere.Modules.Equipment.Controllers;

public class EquipmentItemsIndexViewModel
{
    public List<EquipmentItemListItemDto> Items { get; set; } = new();
    public List<EquipmentParentSummaryDto> Parents { get; set; } = new();
    public int? EquipmentID { get; set; }
    public int? CategoryId { get; set; }
    public string? StatusFilter { get; set; }
    public string? Search { get; set; }
    public List<SelectListItem> EquipmentLookup { get; set; } = new();
    public List<SelectListItem> CategoryLookup { get; set; } = new();

    public int Page { get; set; } = 1;
    // ponytail: 10 items/page here is a deliberate local choice for
    // Equipment units. The system default is 20; change back to 20
    // for system-wide consistency.
    public int PageSize { get; set; } = 10;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);

    public string PageUrl(int page) =>
        $"?search={Uri.EscapeDataString(Search ?? "")}&equipmentId={EquipmentID}&categoryId={CategoryId}&status={StatusFilter}&page={page}&pageSize={PageSize}";

    public List<SelectListItem> StatusOptions { get; } = new()
    {
        new SelectListItem("All statuses", ""),
        new SelectListItem("Available",     "Available"),
        new SelectListItem("Reserved",      "Reserved"),
        new SelectListItem("Checked Out",   "CheckedOut"),
        new SelectListItem("In Maintenance","InMaintenance"),
        new SelectListItem("Retired",       "Retired"),
    };
}

public class EquipmentParentSummaryDto
{
    public int EquipmentID { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CategoryID { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal DailyRate { get; set; }
    public string Status { get; set; } = "Available";
    public int TotalCount { get; set; }
    public int AvailableCount { get; set; }
    public int ReservedCount { get; set; }
    public int CheckedOutCount { get; set; }
    public int MaintCount { get; set; }
    public int RetiredCount { get; set; }
}

public class EquipmentItemFormViewModel
{
    public EquipmentItemCreateDto Form { get; set; } = new();
    public List<SelectListItem> EquipmentLookup { get; set; } = new();

    public List<SelectListItem> StatusOptions { get; } = new()
    {
        new SelectListItem("Available",  "Available"),
        new SelectListItem("Reserved",   "Reserved"),
        new SelectListItem("Checked Out","CheckedOut"),
        new SelectListItem("In Maintenance","InMaintenance"),
        new SelectListItem("Retired",    "Retired"),
    };
}

public class EquipmentItemEditViewModel
{
    public EquipmentItemEditDto Form { get; set; } = new();
    public string EquipmentName { get; set; } = string.Empty;

    public List<SelectListItem> StatusOptions { get; } = new()
    {
        new SelectListItem("Available",  "Available"),
        new SelectListItem("Reserved",   "Reserved"),
        new SelectListItem("Checked Out","CheckedOut"),
        new SelectListItem("In Maintenance","InMaintenance"),
        new SelectListItem("Retired",    "Retired"),
    };
}
