using Microsoft.AspNetCore.Mvc.Rendering;
using RentalSphere.Modules.RentalTransaction.DTOs;
using RentalSphere.Modules.RentalTransaction.Models;

namespace RentalSphere.Modules.RentalTransaction.ViewModels;

public class RentalTransactionIndexViewModel
{
    public List<RentalTransactionListItemDto> Items { get; set; } = new();
    public string? StatusFilter { get; set; }
    public bool IsMineView { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);

    // Per-status counts for the customer pill row. Only populated when
    // IsMineView; staff branch uses the regular filter dropdown.
    public int ActiveCount { get; set; }
    public int ReturnedCount { get; set; }
    public int OtherCount => Math.Max(0, TotalCount - ActiveCount - ReturnedCount);

    public string PageUrl(int page) =>
        $"?statusFilter={StatusFilter}&page={page}&pageSize={PageSize}";

    public List<SelectListItem> Statuses { get; } = new()
    {
        new SelectListItem("All statuses", ""),
        new SelectListItem("Active", nameof(RentalTransactionStatus.Active)),
        new SelectListItem("Returned", nameof(RentalTransactionStatus.Returned)),
        new SelectListItem("Cancelled", nameof(RentalTransactionStatus.Cancelled)),
        new SelectListItem("Overdue", "Overdue"),
    };
}

public class RentalTransactionDetailsViewModel
{
    public RentalTransactionDetailsDto Transaction { get; set; } = new();
    public string? FlashMessage { get; set; }
    public RentalSphere.Modules.Billing.DTOs.InvoiceDetailsDto? Invoice { get; set; }
}

public class RentalTransactionCheckoutViewModel
{
    public int ReservationID { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime RentalStartDate { get; set; }
    public DateTime ExpectedReturnDate { get; set; }
    public decimal TotalAmount { get; set; }
    public List<RentalTransactionItemDto> Items { get; set; } = new();
}

public class RentalTransactionReturnViewModel
{
    public RentalTransactionDetailsDto Transaction { get; set; } = new();
    public RentalTransactionReturnDto Form { get; set; } = new();

    /// <summary>
    /// One row per transaction line. Pinned lines show the existing serial
    /// (read-only); unpinned lines carry a candidate list and an editable
    /// unit selector. Always populated so the operator can see exactly
    /// which unit will be moved to InMaintenance per line.
    /// </summary>
    public List<DamageAssignmentRow> AllLines { get; set; } = new();
}

public class DamageAssignmentRow
{
    public int RentalTransactionItemID { get; set; }
    public int EquipmentID { get; set; }
    public string EquipmentName { get; set; } = string.Empty;

    /// <summary>True when the line already has a pinned <c>EquipmentItemID</c> at checkout.</summary>
    public bool IsPinned { get; set; }

    /// <summary>Serial of the currently pinned unit, or null if unpinned.</summary>
    public string? PinnedSerialNumber { get; set; }

    /// <summary>Candidate units of <c>EquipmentID</c> the operator may pick from when unpinned.</summary>
    public List<RentalSphere.Modules.Equipment.Services.EquipmentItemLookupDto> Candidates { get; set; } = new();
}
