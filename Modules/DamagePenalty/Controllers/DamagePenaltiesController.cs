using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Modules.DamagePenalty.DTOs;
using RentalSphere.Modules.DamagePenalty.Services;
using RentalSphere.Modules.DamagePenalty.ViewModels;

namespace RentalSphere.Modules.DamagePenalty.Controllers;

[Authorize(Roles = RoleNames.Admin + "," + RoleNames.Staff)]
public class DamagePenaltiesController : Controller
{
    private readonly IDamagePenaltyService _service;

    public DamagePenaltiesController(IDamagePenaltyService service) { _service = service; }

    public async Task<IActionResult> Index(string? statusFilter, int? rentalTransactionId, string? sort, int page = 1, int pageSize = 20)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 200);
        var skip = (page - 1) * pageSize;

        var (items, total) = await _service.ListPagedAsync(statusFilter, rentalTransactionId, sort, skip, pageSize);

        var vm = new DamagePenaltyIndexViewModel
        {
            Items = items,
            StatusFilter = statusFilter,
            RentalTransactionFilter = rentalTransactionId,
            Sort = sort,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
        };
        return View(vm);
    }

    [HttpGet]
    public IActionResult Create(int? rentalTransactionId)
    {
        var vm = new DamagePenaltyCreateDto { RentalTransactionID = rentalTransactionId ?? 0 };
        ViewBag.PenaltyTypes = Enum.GetValues<Models.DamagePenaltyType>()
            .Select(p => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
            {
                Value = p.ToString(),
                Text = p.ToString(),
            })
            .ToList();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DamagePenaltyCreateDto dto)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.PenaltyTypes = Enum.GetValues<Models.DamagePenaltyType>()
                .Select(p => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                {
                    Value = p.ToString(),
                    Text = p.ToString(),
                }).ToList();
            return View(dto);
        }
        try
        {
            var id = await _service.CreateAsync(dto, User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "");
            TempData["Success"] = "Damage record created.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (ForbiddenException ex) { TempData["Error"] = ex.Message; return View(dto); }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; return View(dto); }
    }

    public async Task<IActionResult> Details(int id)
    {
        try
        {
            var dto = await _service.GetDetailsAsync(id);
            return View(dto);
        }
        catch (NotFoundException) { return NotFound(); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAmount(int id, decimal amount)
    {
        try
        {
            await _service.UpdateAmountAsync(id, amount,
                User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "");
            TempData["Success"] = "Amount updated and invoice line resynced.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> Waive(int id, decimal amount, string? reason)
    {
        try
        {
            await _service.WaiveAsync(new DamagePenaltyWaiveDto
            {
                DamagePenaltyID = id,
                Amount = amount,
                Reason = reason,
            }, User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "");
            TempData["Success"] = "Damage waived.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }
}
