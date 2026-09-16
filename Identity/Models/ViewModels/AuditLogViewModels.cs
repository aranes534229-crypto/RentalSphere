using Microsoft.AspNetCore.Mvc.Rendering;
using RentalSphere.Identity.Models;

namespace RentalSphere.Identity.Models.ViewModels;

/// <summary>
/// Paged index for the Audit log viewer. Mirrors RentalTransactionIndexViewModel
/// so the admin list views share one visual pattern (card, filter form, pagination pills).
/// </summary>
public class AuditLogIndexViewModel
{
    public List<AuditLog> Items { get; set; } = new();
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);

    /// <summary>Set of actor user IDs seen on this page, resolved to display emails by the controller.</summary>
    public Dictionary<string, string> ActorLookup { get; set; } = new();

    /// <summary>Builds the query string for a pagination link, preserving search + page size.</summary>
    public string PageUrl(int page) =>
        $"?search={Search}&page={page}&pageSize={PageSize}";

    /// <summary>Page-size options shared by the "Show:" dropdown.</summary>
    public List<SelectListItem> PageSizes { get; } = new()
    {
        new SelectListItem("5", "5"),
        new SelectListItem("10", "10"),
        new SelectListItem("20", "20"),
        new SelectListItem("50", "50"),
        new SelectListItem("100", "100"),
    };
}
