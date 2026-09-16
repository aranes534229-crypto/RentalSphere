using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Modules.Billing.Services;
using RentalSphere.Modules.DamagePenalty.Models;
using RentalSphere.Modules.DamagePenalty.Repositories;
using RentalSphere.Modules.Equipment.Repositories;
using RentalSphere.Modules.Equipment.Services;
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
    private readonly IEquipmentItemRepository _items;
    private readonly IDamagePenaltyRepository _damagePenalties;
    private readonly ICurrentUser _current;

    public RentalTransactionsController(
        IRentalTransactionService service,
        IBillingService billing,
        IEquipmentItemRepository items,
        IDamagePenaltyRepository damagePenalties,
        ICurrentUser current)
    {
        _service = service;
        _billing = billing;
        _items = items;
        _damagePenalties = damagePenalties;
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
        var tx = await _service.GetDetailsAsync(id);
        ViewBag.Transaction = tx;
        ViewBag.AllLines = await BuildDamageAssignmentRowsAsync(tx);
        return View(new RentalTransactionReturnDto { RentalTransactionID = id });
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Staff}")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Return(int id, RentalTransactionReturnDto dto)
    {
        // Trust the route id; the hidden field in the form is just a fallback.
        if (dto.RentalTransactionID == 0) dto.RentalTransactionID = id;
        if (!ModelState.IsValid)
        {
            var tx1 = await _service.GetDetailsAsync(dto.RentalTransactionID);
            ViewBag.Transaction = tx1;
            ViewBag.AllLines = await BuildDamageAssignmentRowsAsync(tx1);
            return View(dto);
        }

        try
        {
            await _service.ReturnAsync(dto, _current.UserId ?? "system");

            // Routing after a successful return:
            //   1. FlagForMaintenance  -> Maintenance dashboard (open tickets
            //      from this return surface there). Maintenance wins when both
            //      flags are on because the unit is being quarantined and the
            //      follow-up is more urgent than billing review.
            //   2. FlagForLatePenalty (only) -> the seeded LateFee penalty's
            //      Details view so the operator can confirm/adjust the
            //      auto-calculated amount. Falls back to the index if the row
            //      can't be located.
            //   3. Neither -> existing behavior: the auto-generated invoice
            //      if present, otherwise the transaction Details.
            if (dto.FlagForMaintenance)
            {
                TempData["Success"] = ReturnSuccessMessage(dto, flaggedSuffix: true)
                    + " Routed to the Maintenance queue.";
                return RedirectToAction("Index", "Maintenance");
            }

            if (dto.FlagForLatePenalty)
            {
                var lateFeeId = await FindLateFeePenaltyIdAsync(dto.RentalTransactionID);
                TempData["Success"] = ReturnSuccessMessage(dto, flaggedSuffix: true)
                    + (lateFeeId.HasValue
                        ? " Routed to the late-fee penalty for review."
                        : " Routed to the Damage & Penalty list.");
                if (lateFeeId.HasValue)
                {
                    return RedirectToAction("Details", "DamagePenalties", new { id = lateFeeId.Value });
                }
                return RedirectToAction("Index", "DamagePenalties");
            }

            // Default path: invoice if it was just generated, otherwise the
            // transaction Details.
            var invoice = await _billing.GetByTransactionAsync(dto.RentalTransactionID);
            if (invoice is not null)
            {
                TempData["Success"] = ReturnSuccessMessage(dto, flaggedSuffix: true);
                return RedirectToAction("Details", "Invoices", new { id = invoice.InvoiceID });
            }

            TempData["Success"] = ReturnSuccessMessage(dto, flaggedSuffix: false);
            return RedirectToAction(nameof(Details), new { id = dto.RentalTransactionID });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var tx = await _service.GetDetailsAsync(dto.RentalTransactionID);
            ViewBag.Transaction = tx;
            ViewBag.AllLines = await BuildDamageAssignmentRowsAsync(tx);
            return View(dto);
        }
        catch (NotFoundException) { return NotFound(); }
    }

    /// <summary>
    /// Compose the success message for a recorded return. The flag combo on
    /// the DTO drives the suffix so the operator sees what follow-up work
    /// was created. <paramref name="flaggedSuffix"/> toggles between
    /// "Return recorded and invoice generated. ..." (after invoice) and
    /// "Return recorded. ..." (when the operator is bounced to Details
    /// because the invoice already existed).
    /// </summary>
    private static string ReturnSuccessMessage(RentalTransactionReturnDto dto, bool flaggedSuffix)
    {
        var prefix = flaggedSuffix ? "Return recorded and invoice generated." : "Return recorded.";
        if (dto.FlagForMaintenance && dto.FlagForLatePenalty) return $"{prefix} Flagged for maintenance and late-penalty review.";
        if (dto.FlagForMaintenance) return $"{prefix} Flagged for maintenance review.";
        if (dto.FlagForLatePenalty) return $"{prefix} Flagged for late-penalty review.";
        return prefix;
    }

    /// <summary>
    /// After a late-penalty return is recorded, find the LateFee penalty row
    /// that was just seeded for this transaction so we can deep-link the
    /// operator to its Details view. Returns null if no LateFee row exists
    /// (caller falls back to the index). Most-recently-reported wins when
    /// multiple LateFee rows somehow exist for one transaction.
    /// </summary>
    private async Task<int?> FindLateFeePenaltyIdAsync(int rentalTransactionId)
    {
        var rows = await _damagePenalties.ListByTransactionAsync(rentalTransactionId);
        return rows
            .Where(p => p.PenaltyType == DamagePenaltyType.LateFee)
            .OrderByDescending(p => p.ReportedAt)
            .Select(p => (int?)p.DamagePenaltyID)
            .FirstOrDefault();
    }

    /// <summary>
    /// Build one <see cref="DamageAssignmentRow"/> per transaction line. Pinned
    /// lines get a read-only badge; unpinned lines get a candidate list so the
    /// operator can pick the unit to quarantine. Always populated.
    /// </summary>
    private async Task<List<DamageAssignmentRow>> BuildDamageAssignmentRowsAsync(RentalTransactionDetailsDto tx)
    {
        var rows = new List<DamageAssignmentRow>();
        foreach (var line in tx.Items)
        {
            var row = new DamageAssignmentRow
            {
                RentalTransactionItemID = line.RentalTransactionItemID,
                EquipmentID = line.EquipmentID,
                EquipmentName = line.EquipmentName,
                IsPinned = line.EquipmentItemID.HasValue,
                PinnedSerialNumber = line.SerialNumber,
            };

            if (!row.IsPinned)
            {
                // Available units of the same equipment. The service still
                // re-validates that the chosen unit isn't already InMaintenance
                // or pinned to a different active rental.
                var units = await _items.ListAvailableByEquipmentAsync(line.EquipmentID);
                row.Candidates = units.Select(i => new EquipmentItemLookupDto(
                    i.ItemID,
                    i.EquipmentID,
                    i.Equipment.Name,
                    i.Equipment.CategoryID,
                    i.Equipment.Category?.Name ?? string.Empty,
                    i.Equipment.DailyRate,
                    i.SerialNumber,
                    i.AvailabilityStatus.ToString()))
                    .ToList();
            }

            rows.Add(row);
        }
        return rows;
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
