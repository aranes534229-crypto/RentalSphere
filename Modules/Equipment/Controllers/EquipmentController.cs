using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Modules.Equipment.DTOs;
using RentalSphere.Modules.Equipment.Services;
using RentalSphere.Modules.Equipment.ViewModels;

namespace RentalSphere.Modules.Equipment.Controllers;

/// <summary>
/// Equipment catalog CRUD. Read access for everyone signed in; write access for Admin/Staff;
/// delete reserved for Admin (also enforced at the service layer).
/// </summary>
[Authorize]
public class EquipmentController : Controller
{
    private readonly IEquipmentService _service;
    private readonly ICategoryService _categories;
    private readonly ICurrentUser _current;
    private readonly IWebHostEnvironment _env;

    public EquipmentController(
        IEquipmentService service,
        ICategoryService categories,
        ICurrentUser current,
        IWebHostEnvironment env)
    {
        _service = service;
        _categories = categories;
        _current = current;
        _env = env;
    }

    // ponytail: 2 MB cap, images only, jpg/png/gif/webp. Bump the cap or whitelist
    // a CDN when product requests larger previews.
    private static readonly HashSet<string> AllowedImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
    private const long MaxImageBytes = 2L * 1024 * 1024;

    private async Task<string> SaveImageAsync(IFormFile file, CancellationToken ct)
    {
        var ext = Path.GetExtension(file.FileName);
        if (!AllowedImageExtensions.Contains(ext))
            throw new InvalidOperationException("Only JPG, PNG, GIF, or WEBP images are allowed.");
        if (file.Length > MaxImageBytes)
            throw new InvalidOperationException("Image must be 2 MB or smaller.");

        var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "equipment");
        Directory.CreateDirectory(uploadsDir);

        var fileName = $"{Guid.NewGuid()}{ext}";
        var fullPath = Path.Combine(uploadsDir, fileName);
        await using var stream = System.IO.File.Create(fullPath);
        await file.CopyToAsync(stream, ct);

        return $"/uploads/equipment/{fileName}";
    }

    [AllowAnonymous]
    public async Task<IActionResult> Index(string? search, int? categoryId, string? sort, int page = 1, int? pageSize = null)
    {
        // Anonymous users see nothing — they must log in to browse.
        if (!User.Identity?.IsAuthenticated ?? true)
            return RedirectToAction("Login", "Account");

        page = page < 1 ? 1 : page;
        // ponytail: Customers browsing the catalog get 12/page by default;
        // Staff/Admin get 10/page. Independent defaults per the spec.
        // URL-supplied pageSize always overrides (a bookmarked ?pageSize=20
        // still wins for either role).
        var defaultSize = _current.IsCustomer ? 12 : 10;
        var effectiveSize = pageSize is null or < 1 ? defaultSize : Math.Min(pageSize.Value, 200);
        var skip = (page - 1) * effectiveSize;

        var (items, total) = await _service.ListPagedAsync(search, categoryId, sort, skip, effectiveSize);

        // Customers still see only Available; the service already filters at row level.
        if (_current.IsCustomer)
            items = items.Where(e => e.Status == "Available").ToList();

        var vm = new EquipmentIndexViewModel
        {
            Items = items,
            Search = search,
            CategoryFilter = categoryId,
            Sort = sort,
            Page = page,
            PageSize = effectiveSize,
            TotalCount = total,
            Categories = (await _categories.GetLookupAsync())
                .Select(c => new SelectListItem(c.Name, c.CategoryID.ToString()))
                .ToList(),
        };
        return View(vm);
    }

    [AllowAnonymous]
    public async Task<IActionResult> Details(int id, DateTime? start = null, DateTime? end = null)
    {
        if (!User.Identity?.IsAuthenticated ?? true)
            return RedirectToAction("Login", "Account");

        var dto = await _service.GetDetailsAsync(id, start, end);
        if (_current.IsCustomer && dto.Status != "Available")
            throw new NotFoundException("Equipment not found."); // hide existence from non-eligible customers

        return View(dto);
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Staff}")]
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var vm = new EquipmentFormViewModel
        {
            Categories = (await _categories.GetLookupAsync())
                .Select(c => new SelectListItem(c.Name, c.CategoryID.ToString()))
                .ToList(),
        };
        return View(vm);
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Staff}")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(EquipmentFormViewModel vm)
    {
        if (vm.Form.ImageFile is not null)
        {
            try
            {
                vm.Form.ImageURL = await SaveImageAsync(vm.Form.ImageFile, HttpContext.RequestAborted);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(nameof(vm.Form.ImageFile), ex.Message);
            }
        }

        if (!ModelState.IsValid)
        {
            vm.Categories = (await _categories.GetLookupAsync())
                .Select(c => new SelectListItem(c.Name, c.CategoryID.ToString()))
                .ToList();
            return View(vm);
        }
        try
        {
            var id = await _service.CreateAsync(vm.Form, _current.UserId ?? "system");
            TempData["Success"] = $"Equipment '{vm.Form.Name}' created.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (NotFoundException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            vm.Categories = (await _categories.GetLookupAsync())
                .Select(c => new SelectListItem(c.Name, c.CategoryID.ToString()))
                .ToList();
            return View(vm);
        }
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Staff}")]
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var dto = await _service.GetDetailsAsync(id);
        var vm = new EquipmentEditViewModel
        {
            Form = new EquipmentUpdateDto
            {
                EquipmentID = dto.EquipmentID,
                Name = dto.Name,
                CategoryID = dto.CategoryID,
                Description = dto.Description,
                DailyRate = dto.DailyRate,
                StockQuantity = dto.StockQuantity,
                Status = dto.Status,
                ImageURL = dto.ImageURL,
                SerialPrefix = dto.SerialPrefix,
            },
            Categories = (await _categories.GetLookupAsync())
                .Select(c => new SelectListItem(c.Name, c.CategoryID.ToString()))
                .ToList(),
        };
        return View(vm);
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Staff}")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EquipmentEditViewModel vm)
    {
        var dto = await _service.GetDetailsAsync(vm.Form.EquipmentID);
        var oldImage = dto.ImageURL;

        if (vm.Form.ImageFile is not null)
        {
            try
            {
                vm.Form.ImageURL = await SaveImageAsync(vm.Form.ImageFile, HttpContext.RequestAborted);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(nameof(vm.Form.ImageFile), ex.Message);
            }
        }
        else
        {
            vm.Form.ImageURL = oldImage;
        }

        if (!ModelState.IsValid)
        {
            vm.Categories = (await _categories.GetLookupAsync())
                .Select(c => new SelectListItem(c.Name, c.CategoryID.ToString()))
                .ToList();
            return View(vm);
        }
        try
        {
            await _service.UpdateAsync(vm.Form, _current.UserId ?? "system");
            TempData["Success"] = "Equipment updated.";
            return RedirectToAction(nameof(Details), new { id = vm.Form.EquipmentID });
        }
        catch (NotFoundException) { return NotFound(); }
    }

    [Authorize(Roles = RoleNames.Admin)]
    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var dto = await _service.GetDetailsAsync(id);
        return View(dto);
    }

    [Authorize(Roles = RoleNames.Admin)]
    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        try
        {
            await _service.DeleteAsync(id, _current.UserId ?? "system");
            TempData["Success"] = "Equipment deleted.";
            return RedirectToAction(nameof(Index));
        }
        catch (ForbiddenException ex)
        {
            // Defense-in-depth: the service rejects non-admins even if the attribute is bypassed.
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
        catch (NotFoundException) { return NotFound(); }
    }
}
