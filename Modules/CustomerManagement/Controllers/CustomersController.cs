using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Modules.CustomerManagement.DTOs;
using RentalSphere.Modules.CustomerManagement.Models;
using RentalSphere.Modules.CustomerManagement.Services;
using RentalSphere.Modules.CustomerManagement.ViewModels;

namespace RentalSphere.Modules.CustomerManagement.Controllers;

/// <summary>
/// Customer CRUD. Read access scoped by role: Admin/Staff see all, Customer sees own.
/// Write access for Admin/Staff; Customer may edit own profile. Delete is Admin-only.
/// </summary>
[Authorize]
public class CustomersController : Controller
{
    private readonly ICustomerService _service;
    private readonly ICurrentUser _current;

    public CustomersController(ICustomerService service, ICurrentUser current)
    {
        _service = service;
        _current = current;
    }

    public async Task<IActionResult> Index(string? search, string? loyaltyTier, string? sort, int page = 1, int pageSize = 20)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 200);
        var skip = (page - 1) * pageSize;

        var (items, total) = await _service.ListPagedAsync(search, loyaltyTier, sort, skip, pageSize);
        var vm = new CustomerIndexViewModel
        {
            Items = items,
            Search = search,
            LoyaltyFilter = loyaltyTier,
            Sort = sort,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
        };
        return View(vm);
    }

    public async Task<IActionResult> Details(int id)
    {
        var dto = await _service.GetDetailsAsync(id);
        return View(dto);
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Staff}")]
    [HttpGet]
    public IActionResult Create() => View(new CustomerCreateViewModel());

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Staff}")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CustomerCreateViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        try
        {
            var id = await _service.CreateAsync(vm.Form, _current.UserId ?? "system");
            TempData["Success"] = $"Customer '{vm.Form.FirstName} {vm.Form.LastName}' created.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (ForbiddenException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(vm);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(vm);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var dto = await _service.GetDetailsAsync(id);
        var vm = new CustomerEditViewModel
        {
            Form = new CustomerUpdateDto
            {
                CustomerID = dto.CustomerID,
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                Email = dto.Email,
                Phone = dto.Phone,
                Address = dto.Address,
                City = dto.City,
                PostalCode = dto.PostalCode,
                IsActive = dto.IsActive,
            },
        };
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CustomerEditViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        try
        {
            await _service.UpdateAsync(vm.Form, _current.UserId ?? "system");
            TempData["Success"] = "Customer updated.";
            return RedirectToAction(nameof(Details), new { id = vm.Form.CustomerID });
        }
        catch (NotFoundException) { return NotFound(); }
        catch (ForbiddenException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(vm);
        }
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
            TempData["Success"] = "Customer deleted.";
            return RedirectToAction(nameof(Index));
        }
        catch (ForbiddenException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
        catch (NotFoundException) { return NotFound(); }
        catch (DbUpdateException)
        {
            TempData["Error"] = "Cannot delete this customer because they have linked reservations or transactions.";
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    [Authorize(Roles = RoleNames.Customer)]
    [HttpGet]
    public async Task<IActionResult> MyProfile()
    {
        // Read-only profile view for the signed-in Customer. Edit access is via
        // the "Edit profile" button on this page, which routes to MyProfileEdit.
        var me = await ResolveOwnCustomerAsync();
        if (me is null) return RedirectToAction("Index", "Home");
        var dto = await _service.GetDetailsAsync(me.CustomerID);
        var reservations = await _service.GetRecentReservationsAsync(me.CustomerID, take: 5);
        ViewData["RecentReservations"] = reservations;
        return View(dto);
    }

    [Authorize(Roles = RoleNames.Customer)]
    [HttpGet]
    public async Task<IActionResult> MyProfileEdit()
    {
        var me = await ResolveOwnCustomerAsync();
        if (me is null) return RedirectToAction("Index", "Home");
        var dto = await _service.GetDetailsAsync(me.CustomerID);
        var vm = new CustomerEditViewModel
        {
            Form = new CustomerUpdateDto
            {
                CustomerID = dto.CustomerID,
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                Email = dto.Email,
                Phone = dto.Phone,
                Address = dto.Address,
                City = dto.City,
                PostalCode = dto.PostalCode,
                IsActive = dto.IsActive,
            },
        };
        ViewData["IsMyProfile"] = true;
        return View("Edit", vm);
    }

    [Authorize(Roles = RoleNames.Customer)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MyProfileEdit(CustomerEditViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewData["IsMyProfile"] = true;
            return View("Edit", vm);
        }
        try
        {
            await _service.UpdateAsync(vm.Form, _current.UserId ?? "system");
            TempData["Success"] = "Your profile was updated successfully. The changes are now visible on your profile page.";
            return RedirectToAction(nameof(MyProfile));
        }
        catch (ForbiddenException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            ViewData["IsMyProfile"] = true;
            return View("Edit", vm);
        }
    }

    private async Task<CustomerListItemDto?> ResolveOwnCustomerAsync()
    {
        // Customer role is scoped to their own row by the service; this just
        // finds the row's ID without depending on a separate repo call.
        var list = await _service.ListAsync();
        var me = list.FirstOrDefault();
        if (me is null)
        {
            TempData["Error"] = "Your customer profile is not set up yet. Contact an administrator.";
        }
        return me;
    }

    [Authorize(Roles = RoleNames.Admin)]
    [HttpGet]
    public async Task<IActionResult> OverrideTier(int id)
    {
        var dto = await _service.GetDetailsAsync(id);
        var vm = new CustomerOverrideTierViewModel
        {
            CustomerID = dto.CustomerID,
            CustomerName = $"{dto.FirstName} {dto.LastName}",
            CurrentTier = dto.LoyaltyTier,
            IsOverridden = dto.IsOverridden,
            CurrentOverrideReason = dto.OverrideReason,
            Form = new CustomerTierOverrideDto
            {
                CustomerID = dto.CustomerID,
                Tier = dto.LoyaltyTier,
            },
        };
        return View(vm);
    }

    [Authorize(Roles = RoleNames.Admin)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> OverrideTier(CustomerOverrideTierViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            // Repopulate the read-only context for the form view.
            var existing = await _service.GetDetailsAsync(vm.CustomerID);
            vm.CustomerName = $"{existing.FirstName} {existing.LastName}";
            vm.CurrentTier = existing.LoyaltyTier;
            vm.IsOverridden = existing.IsOverridden;
            vm.CurrentOverrideReason = existing.OverrideReason;
            return View(vm);
        }

        try
        {
            if (!Enum.TryParse<LoyaltyTier>(vm.Form.Tier, out var tier))
            {
                ModelState.AddModelError(nameof(vm.Form.Tier), "Unknown loyalty tier.");
                return View(vm);
            }
            await _service.OverrideTierAsync(vm.CustomerID, tier, vm.Form.Reason, _current.UserId ?? "system");
            TempData["Success"] = "Loyalty tier override saved.";
            return RedirectToAction(nameof(Details), new { id = vm.CustomerID });
        }
        catch (ForbiddenException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(vm);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(nameof(vm.Form.Reason), ex.Message);
            return View(vm);
        }
    }

    [Authorize(Roles = RoleNames.Admin)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearTierOverride(int id)
    {
        try
        {
            await _service.ClearTierOverrideAsync(id, _current.UserId ?? "system");
            TempData["Success"] = "Loyalty tier override cleared. Tier will now reflect spend.";
        }
        catch (ForbiddenException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }
}