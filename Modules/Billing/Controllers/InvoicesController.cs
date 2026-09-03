using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentalSphere.Common.Constants;
using RentalSphere.Modules.Billing.DTOs;
using RentalSphere.Modules.Billing.Services;
using RentalSphere.Modules.Billing.ViewModels;

namespace RentalSphere.Modules.Billing.Controllers;

[Authorize(Roles = RoleNames.Admin + "," + RoleNames.Staff)]
public class InvoicesController : Controller
{
    private readonly IBillingService _billing;

    public InvoicesController(IBillingService billing) { _billing = billing; }

    public async Task<IActionResult> Index(string? statusFilter, string? search, string? sort, int page = 1, int pageSize = 20)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 200);
        var skip = (page - 1) * pageSize;

        var (items, total) = await _billing.ListPagedAsync(statusFilter, search, sort, skip, pageSize);

        var vm = new InvoiceIndexViewModel
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

    public async Task<IActionResult> Details(int id)
    {
        var dto = await _billing.GetDetailsAsync(id);
        if (dto is null) return NotFound();
        return View(dto);
    }

    [HttpGet]
    public async Task<IActionResult> RecordPayment(int id)
    {
        var dto = await _billing.GetDetailsAsync(id);
        if (dto is null) return NotFound();
        ViewBag.Invoice = dto;
        return View(new PaymentCreateDto
        {
            InvoiceID = id,
            PaymentDate = DateTime.UtcNow,
            Amount = dto.TotalAmount - dto.AmountPaid,
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordPayment(PaymentCreateDto dto)
    {
        if (!ModelState.IsValid) return View(dto);
        try
        {
            await _billing.RecordPaymentAsync(dto, User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "");
        }
        catch (Common.Exceptions.ForbiddenException ex) { TempData["Error"] = ex.Message; }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        catch (Common.Exceptions.NotFoundException ex) { TempData["Error"] = ex.Message; return NotFound(); }
        if (TempData["Error"] is not null) return View(dto);
        TempData["Success"] = $"Payment of ₱{dto.Amount:N2} recorded.";
        return RedirectToAction(nameof(Details), new { id = dto.InvoiceID });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> WaiveLine(int invoiceLineId, decimal amount, string? reason)
    {
        try
        {
            await _billing.WaiveLineAsync(invoiceLineId, amount, reason,
                User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "");
            TempData["Success"] = "Line waived.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return Redirect(Request.Headers["Referer"].ToString());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> Void(int id, string reason)
    {
        try
        {
            await _billing.VoidAsync(id, reason,
                User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "");
            TempData["Success"] = "Invoice voided.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }
}
