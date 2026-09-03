using Microsoft.AspNetCore.Mvc.Rendering;
using RentalSphere.Modules.Billing.DTOs;
using RentalSphere.Modules.Billing.Models;

namespace RentalSphere.Modules.Billing.ViewModels;

public class InvoiceIndexViewModel
{
    public List<InvoiceListItemDto> Items { get; set; } = new();
    public string? StatusFilter { get; set; }
    public string? Search { get; set; }
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);

    public List<SelectListItem> Statuses { get; } = new()
    {
        new SelectListItem("All statuses", ""),
        new SelectListItem("Unpaid", nameof(InvoiceStatus.Unpaid)),
        new SelectListItem("Partially Paid", nameof(InvoiceStatus.PartiallyPaid)),
        new SelectListItem("Paid", nameof(InvoiceStatus.Paid)),
        new SelectListItem("Voided", nameof(InvoiceStatus.Voided)),
    };

    public string SortUrl(string column) =>
        $"?search={Search}&statusFilter={StatusFilter}&sort={ToggleSort(column)}&page={Page}&pageSize={PageSize}";

    public string PageUrl(int page) =>
        $"?search={Search}&statusFilter={StatusFilter}&sort={Sort}&page={page}&pageSize={PageSize}";

    private string ToggleSort(string column)
    {
        var current = Sort?.ToLowerInvariant();
        var col = column.ToLowerInvariant();
        if (current == col) return $"{col}_desc";
        if (current == $"{col}_desc") return col;
        return col;
    }
}

public class InvoiceDetailsViewModel
{
    public InvoiceDetailsDto Invoice { get; set; } = new();
}

public class InvoiceRecordPaymentViewModel
{
    public InvoiceDetailsDto Invoice { get; set; } = new();
    public PaymentCreateDto Form { get; set; } = new();
    public List<SelectListItem> Methods { get; } = Enum.GetValues<PaymentMethod>()
        .Select(m => new SelectListItem(m.ToString(), m.ToString()))
        .ToList();
}
