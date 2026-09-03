using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentalSphere.Common.Constants;
using RentalSphere.Models;
using RentalSphere.Modules.Dashboard;

namespace RentalSphere.Controllers;

public class HomeController : Controller
{
    private readonly DashboardKpiService _kpi;

    public HomeController(DashboardKpiService kpi)
    {
        _kpi = kpi;
    }

    public async Task<IActionResult> Index()
    {
        // KPI tiles only render for Admin/Staff; Customer gets their existing cards.
        if (User.IsInRole(RoleNames.Admin) || User.IsInRole(RoleNames.Staff))
        {
            ViewBag.Kpi = await _kpi.GetAsync();
        }
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
