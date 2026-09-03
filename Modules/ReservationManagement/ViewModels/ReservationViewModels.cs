using Microsoft.AspNetCore.Mvc.Rendering;
using RentalSphere.Modules.CustomerManagement.DTOs;
using RentalSphere.Modules.Equipment.Services;
using RentalSphere.Modules.ReservationManagement.DTOs;
using RentalSphere.Modules.ReservationManagement.Models;

namespace RentalSphere.Modules.ReservationManagement.ViewModels;

public class ReservationIndexViewModel
{
    public List<ReservationListItemDto> Items { get; set; } = new();
    public string? Search { get; set; }
    public string? StatusFilter { get; set; }
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);

    public List<SelectListItem> Statuses { get; } = new()
    {
        new SelectListItem("All statuses", ""),
        new SelectListItem("Pending", nameof(ReservationStatus.Pending)),
        new SelectListItem("Confirmed", nameof(ReservationStatus.Confirmed)),
        new SelectListItem("Checked Out", nameof(ReservationStatus.CheckedOut)),
        new SelectListItem("Cancelled", nameof(ReservationStatus.Cancelled)),
        new SelectListItem("Expired", nameof(ReservationStatus.Expired)),
        new SelectListItem("Completed", nameof(ReservationStatus.Completed)),
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

public class ReservationCreateViewModel
{
    public ReservationCreateDto Form { get; set; } = new();
    public List<CustomerLookupDto> Customers { get; set; } = new();
    public List<EquipmentLookupDto> Equipment { get; set; } = new();
}

public class ReservationDetailsViewModel
{
    public ReservationDetailsDto Reservation { get; set; } = new();
}

public class MyReservationsViewModel
{
    public List<ReservationListItemDto> Items { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);

    public string PageUrl(int page) => $"?page={page}&pageSize={PageSize}";
}