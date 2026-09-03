using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Modules.Billing.Services;
using RentalSphere.Modules.RentalTransaction.DTOs;
using RentalSphere.Modules.RentalTransaction.Services;
using RentalSphere.Modules.RentalTransaction.ViewModels;

namespace RentalSphere.Modules.RentalTransaction.Controllers;

/// <summary>
/// Lifecycle of a checked-out rental. Single-record model: Return updates the same row.
/// </summary>
[Authorize]
public class RentalTransactionsController : Controller
{
    private readonly IRentalTransactionService _service;
    private readonly IBillingService _billing;
    private readonly ICurrentUser _current;

    public RentalTransactionsController(
        IRentalTransactionService service,
        IBillingService billing,
        ICurrentUser current)
    {
        _service = service;
        _billing = billing;
        _current = current;
    }

    public async Task<IActionResult> Index(string? statusFilter, int page = 1, int pageSize = 20)
    {
        var mine = _current.IsCustomerScoped;
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 200);
        var skip = (page - 1) * pageSize;

        var (items, total) = await _service.ListPagedAsync(statusFilter, mine, skip, pageSize);

        // Per-status counts for the customer pill row (so "Active (5)" reflects the
        // customer's full set, not just the current page slice).
        int activeCount = 0, returnedCount = 0;
        if (mine)
        {
            activeCount = await _service.CountAsync("Active", mine);
            returnedCount = await _service.CountAsync("Returned", mine);
        }

        var vm = new RentalTransactionIndexViewModel
        {
            Items = items,
            StatusFilter = statusFilter,
            IsMineView = mine,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            ActiveCount = activeCount,
            ReturnedCount = returnedCount,
        };
        return View(vm);
    }

    public async Task<IActionResult> Details(int id)
    {
        var dto = await _service.GetDetailsAsync(id);
        // Invoices are Admin/Staff-only via BillingService. Skip the fetch for
        // Customers so the rental details still render; they can request invoice
        // access from staff if needed.
        var invoice = (dto.Status == "Returned" && !_current.IsCustomerScoped)
            ? await _billing.GetByTransactionAsync(id)
            : null;
        var vm = new RentalTransactionDetailsViewModel
        {
            Transaction = dto,
            Invoice = invoice,
            FlashMessage = TempData["Flash"] as string,
        };
        return View(vm);
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Staff}")]
    [HttpGet]
    public async Task<IActionResult> Checkout(int reservationId)
    {
        var vm = await BuildCheckoutViewAsync(reservationId);
        if (vm is null) return RedirectToAction("Details", "Reservations", new { id = reservationId });
        return View(vm);
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Staff}")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(int reservationId, IFormCollection _)
    {
        try
        {
            var id = await _service.CheckoutAsync(reservationId, _current.UserId ?? "system");
            TempData["Success"] = $"Rental transaction #{id} created.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction("Details", "Reservations", new { id = reservationId });
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Staff}")]
    [HttpGet]
    public async Task<IActionResult> Return(int id)
    {
        var dto = await _service.GetDetailsAsync(id);
        var vm = new RentalTransactionReturnViewModel
        {
            Transaction = dto,
            Form = new RentalTransactionReturnDto { RentalTransactionID = id },
        };
        return View(vm);
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Staff}")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Return(int id, RentalTransactionReturnDto dto)
    {
        // Trust the route id; the hidden field in the form is just a fallback.
        if (dto.RentalTransactionID == 0) dto.RentalTransactionID = id;
        if (!ModelState.IsValid) return View(new RentalTransactionReturnViewModel
        {
            Transaction = await _service.GetDetailsAsync(dto.RentalTransactionID),
            Form = dto,
        });

        try
        {
            await _service.ReturnAsync(dto, _current.UserId ?? "system");

            // After a successful Return, an invoice is auto-generated. Bounce the
            // operator straight to it so they can review lines / record payment.
            var invoice = await _billing.GetByTransactionAsync(dto.RentalTransactionID);
            if (invoice is not null)
            {
                TempData["Success"] = dto.FlaggedForDamage
                    ? "Return recorded and invoice generated. Flagged for damage review."
                    : "Return recorded and invoice generated.";
                return RedirectToAction("Details", "Invoices", new { id = invoice.InvoiceID });
            }

            TempData["Success"] = dto.FlaggedForDamage
                ? "Return recorded. Flagged for damage review."
                : "Return recorded.";
            return RedirectToAction(nameof(Details), new { id = dto.RentalTransactionID });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(new RentalTransactionReturnViewModel
            {
                Transaction = await _service.GetDetailsAsync(dto.RentalTransactionID),
                Form = dto,
            });
        }
        catch (NotFoundException) { return NotFound(); }
    }

    private async Task<RentalTransactionCheckoutViewModel?> BuildCheckoutViewAsync(int reservationId)
    {
        // The service surfaces the lifecycle only; we need reservation details to render
        // the confirmation page. We resolve via the existing list+details chain — simplest
        // path is to re-fetch the reservation from the ReservationService.
        var resSvc = HttpContext.RequestServices.GetService<RentalSphere.Modules.ReservationManagement.Services.IReservationService>();
        if (resSvc is null) return null;
        try
        {
            var r = await resSvc.GetDetailsAsync(reservationId);
            if (r.Status != "Confirmed") return null;
            var days = Math.Max(1, (int)Math.Ceiling((r.RentalEndDate.Date - r.RentalStartDate.Date).TotalDays));
            return new RentalTransactionCheckoutViewModel
            {
                ReservationID = r.ReservationID,
                CustomerName = r.CustomerName,
                RentalStartDate = r.RentalStartDate,
                ExpectedReturnDate = r.RentalEndDate,
                TotalAmount = r.TotalEstimatedCost,
                Items = r.Items.Select(i => new RentalTransactionItemDto
                {
                    EquipmentID = i.EquipmentID,
                    EquipmentName = i.EquipmentName,
                    Quantity = i.Quantity,
                    DailyRate = i.DailyRate,
                    LineTotal = i.LineTotal,
                    EquipmentItemID = i.AssignedItemID,
                    SerialNumber = i.AssignedSerialNumber,
                }).ToList(),
            };
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}
