using Microsoft.AspNetCore.Mvc.Rendering;
using RentalSphere.Modules.CustomerManagement.DTOs;
using RentalSphere.Modules.CustomerManagement.Models;

namespace RentalSphere.Modules.CustomerManagement.ViewModels;

public class CustomerIndexViewModel
{
    public List<CustomerListItemDto> Items { get; set; } = new();
    public string? Search { get; set; }
    public string? LoyaltyFilter { get; set; }
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);

    public List<SelectListItem> LoyaltyTiers { get; } = new()
    {
        new SelectListItem("All tiers", ""),
        new SelectListItem("Bronze", nameof(LoyaltyTier.Bronze)),
        new SelectListItem("Silver", nameof(LoyaltyTier.Silver)),
        new SelectListItem("Gold", nameof(LoyaltyTier.Gold)),
        new SelectListItem("Platinum", nameof(LoyaltyTier.Platinum)),
    };

    public string SortUrl(string column) =>
        $"?search={Uri.EscapeDataString(Search ?? "")}&loyaltyTier={Uri.EscapeDataString(LoyaltyFilter ?? "")}&sort={ToggleSort(column)}&page={Page}&pageSize={PageSize}";

    public string PageUrl(int page) =>
        $"?search={Uri.EscapeDataString(Search ?? "")}&loyaltyTier={Uri.EscapeDataString(LoyaltyFilter ?? "")}&sort={Sort}&page={page}&pageSize={PageSize}";

    private string ToggleSort(string column)
    {
        var current = Sort?.ToLowerInvariant();
        var col = column.ToLowerInvariant();
        if (current == col) return $"{col}_desc";
        if (current == $"{col}_desc") return col;
        return col;
    }
}

public class CustomerCreateViewModel
{
    public CustomerCreateDto Form { get; set; } = new();
}

public class CustomerEditViewModel
{
    public CustomerUpdateDto Form { get; set; } = new();
}

public class CustomerOverrideTierViewModel
{
    public int CustomerID { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CurrentTier { get; set; } = "Bronze";
    public bool IsOverridden { get; set; }
    public string? CurrentOverrideReason { get; set; }
    public CustomerTierOverrideDto Form { get; set; } = new();
    public List<SelectListItem> LoyaltyTiers { get; } = LoyaltyTierOptions.TierOptions;
}

/// <summary>Shared tier option list (Bronze..Platinum). Reused by the override form.</summary>
public static class LoyaltyTierOptions
{
    public static List<SelectListItem> TierOptions { get; } = new()
    {
        new SelectListItem("Bronze", nameof(LoyaltyTier.Bronze)),
        new SelectListItem("Silver", nameof(LoyaltyTier.Silver)),
        new SelectListItem("Gold", nameof(LoyaltyTier.Gold)),
        new SelectListItem("Platinum", nameof(LoyaltyTier.Platinum)),
    };
}