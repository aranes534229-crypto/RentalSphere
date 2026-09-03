using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Identity.Services;
using RentalSphere.Modules.Equipment.DTOs;
using RentalSphere.Modules.Equipment.Services;
using RentalSphere.Modules.Equipment.ViewModels;

namespace RentalSphere.Modules.Equipment.Controllers;

/// <summary>
/// Category CRUD. Admin/Staff can write; Customers are denied.
/// </summary>
[Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Staff}")]
public class CategoriesController : Controller
{
    private readonly ICategoryService _service;
    private readonly ICurrentUser _current;

    public CategoriesController(ICategoryService service, ICurrentUser current)
    {
        _service = service;
        _current = current;
    }

    public async Task<IActionResult> Index(string? search, int page = 1, int pageSize = 10)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 10 : Math.Min(pageSize, 200);
        var skip = (page - 1) * pageSize;

        var (items, total) = await _service.ListPagedAsync(search, skip, pageSize);
        var vm = new CategoryIndexViewModel
        {
            Items = items,
            Search = search,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
        };
        return View(vm);
    }

    [HttpGet]
    public IActionResult Create() => View(new CategoryDto());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CategoryDto dto)
    {
        if (!ModelState.IsValid) return View(dto);
        try
        {
            await _service.CreateAsync(dto, _current.UserId ?? "system");
            TempData["Success"] = $"Category '{dto.Name}' created.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(dto);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        try { return View(await _service.GetAsync(id)); }
        catch (NotFoundException) { return NotFound(); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CategoryDto dto)
    {
        if (!ModelState.IsValid) return View(dto);
        try
        {
            await _service.UpdateAsync(dto, _current.UserId ?? "system");
            TempData["Success"] = "Category updated.";
            return RedirectToAction(nameof(Index));
        }
        catch (NotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(dto);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        try { return View(await _service.GetAsync(id)); }
        catch (NotFoundException) { return NotFound(); }
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        try
        {
            await _service.DeleteAsync(id, _current.UserId ?? "system");
            TempData["Success"] = "Category deleted.";
        }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        catch (NotFoundException) { return NotFound(); }
        return RedirectToAction(nameof(Index));
    }
}
