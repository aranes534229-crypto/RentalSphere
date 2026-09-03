using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentalSphere.Common.Constants;
using RentalSphere.Modules.Reports.DTOs;
using RentalSphere.Modules.Reports.Services;

namespace RentalSphere.Modules.Reports.Controllers;

/// <summary>
/// Read-only Reports dashboard. Admin + Staff only — Customer role is forbidden
/// (CLAUDE.md: Customers have no access to Reports).
/// </summary>
[Authorize(Roles = RoleNames.Admin + "," + RoleNames.Staff)]
public class ReportsController : Controller
{
    private readonly IReportsService _service;

    public ReportsController(IReportsService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Index(DateTime? start, DateTime? end, string? granularity)
    {
        var s = (start ?? DateTime.UtcNow.Date.AddDays(-30)).Date;
        var e = (end ?? DateTime.UtcNow.Date).Date;
        if (e <= s) e = s.AddDays(1);
        granularity = string.IsNullOrWhiteSpace(granularity) ? "Daily" : granularity;

        var bundle = await _service.GetBundleAsync(s, e, granularity);

        ViewBag.Start = s;
        ViewBag.End = e;
        ViewBag.Granularity = granularity;
        ViewBag.ActiveTab = HttpContext.Request.Query["tab"].ToString();
        if (string.IsNullOrEmpty(ViewBag.ActiveTab)) ViewBag.ActiveTab = "revenue";

        return View(bundle);
    }
}
