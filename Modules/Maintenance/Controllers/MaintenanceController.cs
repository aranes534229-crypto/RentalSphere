using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Modules.Equipment.Repositories;
using RentalSphere.Modules.Equipment.Services;
using RentalSphere.Modules.Maintenance.DTOs;
using RentalSphere.Modules.Maintenance.Services;
using RentalSphere.Modules.Maintenance.ViewModels;

namespace RentalSphere.Modules.Maintenance.Controllers;

/// <summary>
/// Maintenance lifecycle: open a record (pulls a unit out of Available), close (returns it).
/// Customer role has no access — module is admin/staff only per role matrix.
/// </summary>
[Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Staff}")]
public class MaintenanceController : Controller
{
    private readonly IMaintenanceService _service;
    private readonly IEquipmentItemRepository _items;
    private readonly ICurrentUser _current;

    public MaintenanceController(
        IMaintenanceService service,
        IEquipmentItemRepository items,
        ICurrentUser current)
    {
        _service = service;
        _items = items;
        _current = current;
    }

    public async Task<IActionResult> Index(string? statusFilter, int page = 1, int pageSize = 20)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 200);
        var skip = (page - 1) * pageSize;

        var (items, total) = await _service.ListPagedAsync(statusFilter, skip, pageSize);

        var vm = new MaintenanceIndexViewModel
        {
            Items = items,
            StatusFilter = statusFilter,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
        };
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var vm = new MaintenanceOpenViewModel();
        await PopulateAsync(vm);
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MaintenanceOpenViewModel vm)
    {
        async Task<IActionResult> RepopulateAsync()
        {
            await PopulateAsync(vm);
            return View(vm);
        }

        if (!ModelState.IsValid) return await RepopulateAsync();

        try
        {
            var ids = await _service.OpenAsync(vm.Form, _current.UserId ?? "system");
            TempData["Success"] = ids.Count == 1
                ? $"Maintenance record #{ids[0]} opened."
                : $"{ids.Count} maintenance records opened: #{string.Join(", #", ids)}.";
            // First new record is the natural landing target for the user to review.
            return RedirectToAction(nameof(Details), new { id = ids[0] });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await RepopulateAsync();
        }
        catch (NotFoundException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await RepopulateAsync();
        }
    }

    private async Task PopulateAsync(MaintenanceOpenViewModel vm)
    {
        var availableItems = await _items.ListAllAvailableWithCategoryAsync();
        vm.AvailableItems = availableItems
            .Select(i => new EquipmentItemLookupDto(
                i.ItemID,
                i.EquipmentID,
                i.Equipment.Name,
                i.Equipment.CategoryID,
                i.Equipment.Category?.Name ?? "(uncategorized)",
                i.Equipment.DailyRate,
                i.SerialNumber,
                i.AvailabilityStatus.ToString()))
            .ToList();
        vm.AvailableItemsGrouped = vm.AvailableItems
            .GroupBy(i => i.EquipmentName)
            .Select(g => new EquipmentItemGroup
            {
                EquipmentID = g.First().EquipmentID,
                EquipmentName = g.Key,
                Items = g.ToList(),
            })
            .ToList();
        vm.Categories = vm.AvailableItemsGrouped
            .GroupBy(g => new { CategoryID = g.Items.First().CategoryID, CategoryName = g.Items.First().CategoryName })
            .Select(cg => new EquipmentCategoryGroup
            {
                CategoryID = cg.Key.CategoryID,
                CategoryName = cg.Key.CategoryName,
                Equipment = cg.ToList(),
            })
            .ToList();
    }

    public async Task<IActionResult> Details(int id)
    {
        var dto = await _service.GetDetailsAsync(id);
        var vm = new MaintenanceDetailsViewModel
        {
            Record = dto,
            // Seed the form with the record's current values so the
            // "Update assessment" form (which posts to UpdateEstimate)
            // pre-fills with what staff last saved. The "Close" form
            // shares the same fields and gets the same pre-fill.
            CloseForm = new MaintenanceCloseDto
            {
                MaintenanceRecordID = id,
                Cost = dto.Cost,
                ExpectedEnd = dto.ExpectedEnd,
                Notes = null, // Don't pre-fill Notes — it's an append-only log
            },
        };
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [ActionName("UpdateEstimate")]
    [Route("Maintenance/UpdateEstimate/{id:int}")]
    public async Task<IActionResult> UpdateEstimate(int id, MaintenanceDetailsViewModel vm)
    {
        var dto = vm.CloseForm ?? new MaintenanceCloseDto();
        if (dto.MaintenanceRecordID == 0) dto.MaintenanceRecordID = id;

        try
        {
            await _service.UpdateEstimateAsync(dto, _current.UserId ?? "system");
            TempData["Success"] = $"Maintenance record #{id} estimate updated.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (NotFoundException) { return NotFound(); }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [ActionName("Close")]
    [Route("Maintenance/Close/{id:int}")]
    public async Task<IActionResult> Close(int id, MaintenanceDetailsViewModel vm)
    {
        // Bind directly off the view-model so the form's `CloseForm.*` field
        // names map to `vm.CloseForm.*` and the model binder populates them
        // (Cost, ExpectedEnd, Notes, MaintenanceRecordID). The route id is
        // still the source of truth — the form's hidden MaintenanceRecordID
        // is just a fallback.
        var dto = vm.CloseForm ?? new MaintenanceCloseDto();
        if (dto.MaintenanceRecordID == 0) dto.MaintenanceRecordID = id;

        try
        {
            await _service.CloseAsync(dto, _current.UserId ?? "system");
            TempData["Success"] = $"Maintenance record #{id} closed.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (NotFoundException) { return NotFound(); }
        return RedirectToAction(nameof(Details), new { id });
    }
}
