using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Modules.Equipment.DTOs;
using RentalSphere.Modules.Equipment.Services;

namespace RentalSphere.Modules.Equipment.Controllers;

/// <summary>
/// Per-unit equipment item tracking. Read access for everyone; write access for Admin/Staff;
/// delete is Admin-only. Status transitions automatically sync the parent Equipment.StockQuantity.
/// </summary>
[Authorize]
public class EquipmentItemsController : Controller
{
    private readonly IEquipmentItemService _items;
    private readonly IEquipmentService _equipment;
    private readonly ICategoryService _categories;
    private readonly ICurrentUser _current;

    public EquipmentItemsController(
        IEquipmentItemService items,
        IEquipmentService equipment,
        ICategoryService categories,
        ICurrentUser current)
    {
        _items = items;
        _equipment = equipment;
        _categories = categories;
        _current = current;
    }

    [AllowAnonymous]
    public async Task<IActionResult> Index(
        int? equipmentId, int? categoryId, string? status, string? search,
        int page = 1, int pageSize = 10)
    {
        if (!User.Identity?.IsAuthenticated ?? true)
            return RedirectToAction("Login", "Account");

        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 10 : Math.Min(pageSize, 200);
        var skip = (page - 1) * pageSize;

        // When a specific equipment is selected, drill in to its items
        // (the deep-link / single-SKU case). Otherwise, show the parent table —
        // one row per equipment type, with rolled-up item counts.
        var equipment = await _equipment.GetLookupAsync();
        var categories = await _categories.GetLookupAsync();

        var equipmentLookup = equipment
            .Select(e => new SelectListItem($"{e.Name} (₱{e.DailyRate:N2})", e.EquipmentID.ToString(), e.EquipmentID == equipmentId))
            .ToList();
        var categoryLookup = categories
            .Select(c => new SelectListItem(c.Name, c.CategoryID.ToString(), c.CategoryID == categoryId))
            .ToList();

        if (equipmentId.HasValue)
        {
            var (rows, total) = await _items.ListPagedAsync(equipmentId, status, search, skip, pageSize, categoryId);
            var vm = new EquipmentItemsIndexViewModel
            {
                Items = rows,
                EquipmentID = equipmentId,
                CategoryId = categoryId,
                StatusFilter = status,
                Search = search,
                EquipmentLookup = equipmentLookup,
                CategoryLookup = categoryLookup,
                Page = page,
                PageSize = pageSize,
                TotalCount = total,
            };
            return View(vm);
        }

        var (parents, parentTotal) = await _items.ListParentsWithCountsPagedAsync(categoryId, search, skip, pageSize);
        var vmParents = new EquipmentItemsIndexViewModel
        {
            Parents = parents,
            EquipmentID = null,
            CategoryId = categoryId,
            StatusFilter = status,
            Search = search,
            EquipmentLookup = equipmentLookup,
            CategoryLookup = categoryLookup,
            Page = page,
            PageSize = pageSize,
            TotalCount = parentTotal,
        };
        return View(vmParents);
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Staff}")]
    [HttpGet]
    public async Task<IActionResult> Create(int? equipmentId)
    {
        var equipment = await _equipment.GetLookupAsync();
        var vm = new EquipmentItemFormViewModel
        {
            Form = new EquipmentItemCreateDto
            {
                EquipmentID = equipmentId ?? 0,
                AvailabilityStatus = "Available",
            },
            EquipmentLookup = equipment
                .Select(e => new SelectListItem($"{e.Name} (₱{e.DailyRate:N2})", e.EquipmentID.ToString()))
                .ToList(),
        };
        return View(vm);
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Staff}")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(EquipmentItemFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateEquipmentLookupAsync(vm);
            return View(vm);
        }
        try
        {
            var (id, newSerial) = await _items.CreateAsync(vm.Form, _current.UserId ?? "system");
            TempData["Success"] = $"Item '{newSerial}' created.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (NotFoundException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateEquipmentLookupAsync(vm);
            return View(vm);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateEquipmentLookupAsync(vm);
            return View(vm);
        }
        catch (ForbiddenException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [AllowAnonymous]
    public async Task<IActionResult> Details(int id, string? returnUrl = null)
    {
        if (!User.Identity?.IsAuthenticated ?? true)
            return RedirectToAction("Login", "Account");
        var dto = await _items.GetDetailsAsync(id);
        ViewData["ReturnUrl"] = returnUrl;
        return View(dto);
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Staff}")]
    [HttpGet]
    public async Task<IActionResult> Edit(int id, string? returnUrl = null)
    {
        var dto = await _items.GetDetailsAsync(id);
        var equipment = await _equipment.GetLookupAsync();
        var vm = new EquipmentItemEditViewModel
        {
            Form = new EquipmentItemEditDto
            {
                ItemID = dto.ItemID,
                SerialNumber = dto.SerialNumber,
                AvailabilityStatus = dto.AvailabilityStatus,
                Location = dto.Location,
            },
            EquipmentName = dto.EquipmentName,
        };
        ViewData["ReturnUrl"] = returnUrl;
        return View(vm);
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Staff}")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EquipmentItemEditViewModel vm, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View(vm);
        }
        try
        {
            await _items.UpdateAsync(vm.Form, _current.UserId ?? "system");
            TempData["Success"] = "Item updated.";
            return RedirectToAction(nameof(Details), new { id = vm.Form.ItemID, returnUrl });
        }
        catch (NotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            ViewData["ReturnUrl"] = returnUrl;
            return View(vm);
        }
        catch (ForbiddenException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [Authorize(Roles = RoleNames.Admin)]
    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var dto = await _items.GetDetailsAsync(id);
        return View(dto);
    }

    [Authorize(Roles = RoleNames.Admin)]
    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        try
        {
            await _items.DeleteAsync(id, _current.UserId ?? "system");
            TempData["Success"] = "Item deleted.";
            return RedirectToAction(nameof(Index));
        }
        catch (NotFoundException) { return NotFound(); }
        catch (ForbiddenException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    private async Task PopulateEquipmentLookupAsync(EquipmentItemFormViewModel vm)
    {
        var equipment = await _equipment.GetLookupAsync();
        vm.EquipmentLookup = equipment
            .Select(e => new SelectListItem($"{e.Name} (₱{e.DailyRate:N2})", e.EquipmentID.ToString()))
            .ToList();
    }
}
