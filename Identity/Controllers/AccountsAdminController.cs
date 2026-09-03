using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Identity.Models;
using RentalSphere.Identity.Models.ViewModels;
using RentalSphere.Identity.Services;

namespace RentalSphere.Identity.Controllers;

/// <summary>
/// Admin-only management of user accounts and role assignments.
/// Every successful action is written to the audit log by IAccountsAdminService.
/// </summary>
[Authorize(Roles = RoleNames.Admin)]
public class AccountsAdminController : Controller
{
    private readonly IAccountsAdminService _service;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly ICurrentUser _current;

    public AccountsAdminController(
        IAccountsAdminService service,
        RoleManager<IdentityRole> roles,
        ICurrentUser current)
    {
        _service = service;
        _roles = roles;
        _current = current;
    }

    public async Task<IActionResult> Index(string? search)
    {
        var users = await _service.ListAsync(search);
        ViewData["Search"] = search;
        return View(users);
    }

    public async Task<IActionResult> Details(string id)
    {
        if (string.IsNullOrEmpty(id)) return BadRequest();
        var user = await _service.GetAsync(id);
        return View(user);
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewData["AllRoles"] = _roles.Roles.Select(r => r.Name!).OrderBy(n => n).ToList();
        return View(new UserCreateViewModel { Role = RoleNames.Customer });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["AllRoles"] = _roles.Roles.Select(r => r.Name!).OrderBy(n => n).ToList();
            return View(model);
        }
        try
        {
            await _service.CreateAsync(model, _current.UserId ?? "system");
            TempData["Success"] = $"User '{model.Email}' created.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            ViewData["AllRoles"] = _roles.Roles.Select(r => r.Name!).OrderBy(n => n).ToList();
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        if (string.IsNullOrEmpty(id)) return BadRequest();
        var u = await _service.GetAsync(id);
        var vm = new UserEditViewModel
        {
            Id = u.Id,
            Email = u.Email,
            FirstName = u.FirstName,
            LastName = u.LastName,
            PhoneNumber = u.PhoneNumber,
            IsActive = u.IsActive,
        };
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(UserEditViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        try
        {
            await _service.UpdateAsync(model, _current.UserId ?? "system");
            TempData["Success"] = $"User '{model.Email}' updated.";
            return RedirectToAction(nameof(Index));
        }
        catch (NotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Delete(string id)
    {
        if (string.IsNullOrEmpty(id)) return BadRequest();
        var u = await _service.GetAsync(id);
        return View(u);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(string id)
    {
        try
        {
            await _service.DeleteAsync(id, _current.UserId ?? "system");
            TempData["Success"] = "User deleted.";
            return RedirectToAction(nameof(Index));
        }
        catch (NotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(string id)
    {
        try
        {
            await _service.ToggleActiveAsync(id, _current.UserId ?? "system");
            TempData["Success"] = "Account status updated.";
        }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        catch (NotFoundException) { return NotFound(); }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> ManageRoles(string id)
    {
        if (string.IsNullOrEmpty(id)) return BadRequest();
        var user = await _service.GetAsync(id);
        ViewData["AllRoles"] = _roles.Roles.Select(r => r.Name!).OrderBy(n => n).ToList();
        return View(user);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignRole(string userId, string role)
    {
        try
        {
            await _service.AssignRoleAsync(userId, role, _current.UserId ?? "system");
            TempData["Success"] = $"Role '{role}' assigned.";
        }
        catch (NotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(ManageRoles), new { id = userId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveRole(string userId, string role)
    {
        try
        {
            await _service.RemoveRoleAsync(userId, role, _current.UserId ?? "system");
            TempData["Success"] = $"Role '{role}' removed.";
        }
        catch (NotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(ManageRoles), new { id = userId });
    }
}
