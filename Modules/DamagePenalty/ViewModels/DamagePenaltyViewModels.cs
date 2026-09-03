using Microsoft.AspNetCore.Mvc.Rendering;
using RentalSphere.Modules.DamagePenalty.DTOs;
using RentalSphere.Modules.DamagePenalty.Models;
using RentalSphere.Modules.Equipment.Services;

namespace RentalSphere.Modules.DamagePenalty.ViewModels;

public class DamagePenaltyIndexViewModel
{
    public List<DamagePenaltyListItemDto> Items { get; set; } = new();
    public string? StatusFilter { get; set; }
    public int? RentalTransactionFilter { get; set; }
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);

    public List<SelectListItem> Statuses { get; } = new()
    {
        new SelectListItem("All statuses", ""),
        new SelectListItem("Pending", nameof(DamagePenaltyStatus.Pending)),
        new SelectListItem("Applied to invoice", nameof(DamagePenaltyStatus.AppliedToInvoice)),
        new SelectListItem("Waived", nameof(DamagePenaltyStatus.Waived)),
    };

    public string SortUrl(string column) =>
        $"?statusFilter={StatusFilter}&rentalTransactionId={RentalTransactionFilter}&sort={ToggleSort(column)}&page={Page}&pageSize={PageSize}";

    public string PageUrl(int page) =>
        $"?statusFilter={StatusFilter}&rentalTransactionId={RentalTransactionFilter}&sort={Sort}&page={page}&pageSize={PageSize}";

    private string ToggleSort(string column)
    {
        var current = Sort?.ToLowerInvariant();
        var col = column.ToLowerInvariant();
        if (current == col) return $"{col}_desc";
        if (current == $"{col}_desc") return col;
        return col;
    }
}

public class DamagePenaltyCreateViewModel
{
    public DamagePenaltyCreateDto Form { get; set; } = new();
    public List<EquipmentLookupDto> Equipment { get; set; } = new();
    public List<SelectListItem> PenaltyTypes { get; } = Enum.GetValues<DamagePenaltyType>()
        .Select(t => new SelectListItem(t.ToString(), t.ToString()))
        .ToList();
}

public class DamagePenaltyDetailsViewModel
{
    public DamagePenaltyDetailsDto Record { get; set; } = new();
    public DamagePenaltyAmountUpdateDto AmountForm { get; set; } = new();
    public DamagePenaltyWaiveDto WaiveForm { get; set; } = new();
}
