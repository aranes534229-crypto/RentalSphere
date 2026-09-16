using Microsoft.AspNetCore.Mvc.Rendering;
using RentalSphere.Modules.Equipment.DTOs;

namespace RentalSphere.Modules.Equipment.ViewModels;

public class EquipmentIndexViewModel
{
    public List<EquipmentListItemDto> Items { get; set; } = new();
    public string? Search { get; set; }
    public int? CategoryFilter { get; set; }
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;
    // ponytail: this default is the Staff/Admin fallback. The controller
    // overrides it per-role: Customer browsing the catalog gets 12/page.
    // URL-supplied ?pageSize= always wins (a bookmarked value still applies).
    public int PageSize { get; set; } = 10;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public List<SelectListItem> Categories { get; set; } = new();

    public string SortUrl(string column) =>
        $"?search={Uri.EscapeDataString(Search ?? "")}&categoryId={CategoryFilter}&sort={ToggleSort(column)}&page={Page}&pageSize={PageSize}";

    public string PageUrl(int page) =>
        $"?search={Uri.EscapeDataString(Search ?? "")}&categoryId={CategoryFilter}&sort={Sort}&page={page}&pageSize={PageSize}";

    private string ToggleSort(string column)
    {
        var current = Sort?.ToLowerInvariant();
        var col = column.ToLowerInvariant();
        if (current == col) return $"{col}_desc";
        if (current == $"{col}_desc") return col;
        return col;
    }
}

public class EquipmentFormViewModel
{
    public EquipmentCreateDto Form { get; set; } = new();
    public List<SelectListItem> Categories { get; set; } = new();
    public List<SelectListItem> Statuses { get; } = new()
    {
        new SelectListItem("Available",   "Available"),
        new SelectListItem("Unavailable", "Unavailable"),
        new SelectListItem("Retired",     "Retired"),
    };
}

public class EquipmentEditViewModel
{
    public EquipmentUpdateDto Form { get; set; } = new();
    public List<SelectListItem> Categories { get; set; } = new();
    public List<SelectListItem> Statuses { get; } = new()
    {
        new SelectListItem("Available",   "Available"),
        new SelectListItem("Unavailable", "Unavailable"),
        new SelectListItem("Retired",     "Retired"),
    };
}

public class CategoryIndexViewModel
{
    public List<CategoryListItemDto> Items { get; set; } = new();
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    // ponytail: 10 items/page here is a deliberate local choice for
    // Categories. The system default is 20; change back to 20 for
    // system-wide consistency.
    public int PageSize { get; set; } = 10;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);

    public string PageUrl(int page) =>
        $"?search={Uri.EscapeDataString(Search ?? "")}&page={page}&pageSize={PageSize}";
}

public class CategoryListItemDto
{
    public int CategoryID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int EquipmentCount { get; set; }
}
