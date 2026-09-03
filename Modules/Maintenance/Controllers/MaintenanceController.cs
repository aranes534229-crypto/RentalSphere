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

    public async Task<IActionResult> Index(string? statusFilter)
    {
        var vm = new MaintenanceIndexViewModel
        {
            Items = await _service.ListAsync(statusFilter),
            StatusFilter = statusFilter,
        };
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var availableItems = await _items.ListAllAvailableAsync();
        var vm = new MaintenanceOpenViewModel
        {
            Form = new MaintenanceOpenDto(),
            AvailableItems = availableItems
                .Select(i => new EquipmentItemLookupDto(
                    i.ItemID, i.EquipmentID, i.Equipment.Name, i.SerialNumber, i.AvailabilityStatus.ToString()))
                .ToList(),
        };
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MaintenanceOpenViewModel vm)
    {
        async Task<IActionResult> RepopulateAsync()
        {
            var items = await _items.ListAllAvailableAsync();
            vm.AvailableItems = items
                .Select(i => new EquipmentItemLookupDto(
                    i.ItemID, i.EquipmentID, i.Equipment.Name, i.SerialNumber, i.AvailabilityStatus.ToString()))
                .ToList();
            return View(vm);
        }

        if (!ModelState.IsValid) return await RepopulateAsync();

        try
        {
            var id = await _service.OpenAsync(vm.Form, _current.UserId ?? "system");
            TempData["Success"] = $"Maintenance record #{id} opened.";
            return RedirectToAction(nameof(Details), new { id });
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

    public async Task<IActionResult> Details(int id)
    {
        var dto = await _service.GetDetailsAsync(id);
        var vm = new MaintenanceDetailsViewModel
        {
            Record = dto,
            CloseForm = new MaintenanceCloseDto { MaintenanceRecordID = id },
        };
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(MaintenanceCloseDto dto)
    {
        try
        {
            await _service.CloseAsync(dto, _current.UserId ?? "system");
            TempData["Success"] = $"Maintenance record #{dto.MaintenanceRecordID} closed.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (NotFoundException) { return NotFound(); }
        return RedirectToAction(nameof(Details), new { id = dto.MaintenanceRecordID });
    }
}
