using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Modules.CustomerManagement.Services;
using RentalSphere.Modules.Equipment.Services;
using RentalSphere.Modules.ReservationManagement.DTOs;
using RentalSphere.Modules.ReservationManagement.Models;
using RentalSphere.Modules.ReservationManagement.Services;
using RentalSphere.Modules.ReservationManagement.ViewModels;

namespace RentalSphere.Modules.ReservationManagement.Controllers;

/// <summary>
/// Reservation lifecycle: list, create, confirm, cancel. Customers see only their own.
/// </summary>
[Authorize]
public class ReservationsController : Controller
{
    private readonly IReservationService _service;
    private readonly ICustomerService _customers;
    private readonly IEquipmentService _equipment;
    private readonly ICurrentUser _current;

    public ReservationsController(
        IReservationService service,
        ICustomerService customers,
        IEquipmentService equipment,
        ICurrentUser current)
    {
        _service = service;
        _customers = customers;
        _equipment = equipment;
        _current = current;
    }

    public async Task<IActionResult> Index(string? search, string? statusFilter, string? sort, int page = 1, int pageSize = 20)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 200);
        var skip = (page - 1) * pageSize;

        var (items, total) = await _service.ListPagedAsync(search, statusFilter, sort, skip, pageSize);

        var vm = new ReservationIndexViewModel
        {
            Items = items,
            Search = search,
            StatusFilter = statusFilter,
            Sort = sort,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
        };
        return View(vm);
    }

    [Authorize(Roles = RoleNames.Customer)]
    public async Task<IActionResult> MyReservations(int page = 1, int pageSize = 20)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 200);
        var skip = (page - 1) * pageSize;

        var (items, total) = await _service.ListMinePagedAsync(skip, pageSize);
        var vm = new MyReservationsViewModel
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
        };
        return View(vm);
    }

    public async Task<IActionResult> Details(int id)
    {
        var dto = await _service.GetDetailsAsync(id);
        var vm = new ReservationDetailsViewModel { Reservation = dto };
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var vm = new ReservationCreateViewModel
        {
            Form = new ReservationCreateDto
            {
                RentalStartDate = DateTime.UtcNow.Date.AddDays(1),
                RentalEndDate = DateTime.UtcNow.Date.AddDays(2),
            },
            Customers = await _customers.GetLookupAsync(),
            Equipment = await _equipment.GetLookupAsync(),
        };
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ReservationCreateViewModel vm)
    {
        async Task<IActionResult> RepopulateAsync()
        {
            vm.Customers = await _customers.GetLookupAsync();
            vm.Equipment = await _equipment.GetLookupAsync();
            return View(vm);
        }

        if (!ModelState.IsValid) return await RepopulateAsync();

        try
        {
            // Customer role: force the CustomerID to the linked customer record.
            if (_current.IsCustomerScoped)
            {
                var myId = await _customers.GetMyCustomerIdAsync();
                if (myId is null)
                {
                    ModelState.AddModelError(string.Empty, "Your account is not linked to a customer record yet. Contact an administrator.");
                    return await RepopulateAsync();
                }
                vm.Form.CustomerID = myId.Value;
            }

            var id = await _service.CreateAsync(vm.Form, _current.UserId ?? "system");
            TempData["Success"] = $"Reservation #{id} created.";
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
        catch (ForbiddenException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await RepopulateAsync();
        }
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Staff}")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(int id)
    {
        try
        {
            await _service.ConfirmAsync(id, _current.UserId ?? "system");
            TempData["Success"] = $"Reservation #{id} confirmed.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (NotFoundException) { return NotFound(); }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, ReservationCancellationReason reason, string? notes)
    {
        try
        {
            await _service.CancelAsync(id, _current.UserId ?? "system", reason, notes);
            TempData["Success"] = $"Reservation #{id} cancelled.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (ForbiddenException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (NotFoundException) { return NotFound(); }
        return RedirectToAction(nameof(Details), new { id });
    }
}