using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RentalSphere.Data;
using RentalSphere.Modules.EquipmentAvailability.Services;

namespace RentalSphere.Modules.EquipmentAvailability.Controllers;

/// <summary>
/// Read-only availability calendar. Admin/Staff use it operationally; Customer role sees
/// the same grid but cannot mutate anything.
/// </summary>
[Authorize]
public class AvailabilityController : Controller
{
    private readonly IAvailabilityService _service;
    private readonly ApplicationDbContext _db;

    public AvailabilityController(IAvailabilityService service, ApplicationDbContext db)
    {
        _service = service;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(DateTime? start, DateTime? end, int? categoryId)
    {
        var s = (start ?? DateTime.UtcNow.Date).Date;
        var e = (end ?? s.AddDays(14)).Date;
        if (e <= s) e = s.AddDays(1);

        var calendar = await _service.GetCalendarAsync(s, e, categoryId);

        var categories = await _db.Categories
            .OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.CategoryID.ToString()))
            .ToListAsync();
        categories.Insert(0, new SelectListItem("All categories", ""));

        ViewBag.Start = s;
        ViewBag.End = e;
        ViewBag.CategoryId = categoryId;
        ViewBag.Categories = categories;
        return View(calendar);
    }
}
