using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace RentalSphere.Identity.Models.ViewModels;

/// <summary>
/// Paged index for the Accounts & Roles list. Mirrors RentalTransactionIndexViewModel
/// so the two admin list views share one visual pattern (card, filter form, pagination pills).
/// </summary>
public class AccountsAdminIndexViewModel
{
    public List<UserListItemViewModel> Items { get; set; } = new();
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);

    /// <summary>Builds the query string for a pagination link, preserving search + page size.</summary>
    public string PageUrl(int page) =>
        $"?search={Search}&page={page}&pageSize={PageSize}";

    /// <summary>Page-size options shared by the "Show:" dropdown.</summary>
    public List<SelectListItem> PageSizes { get; } = new()
    {
        new SelectListItem("5", "5"),
        new SelectListItem("10", "10"),
        new SelectListItem("20", "20"),
        new SelectListItem("50", "50"),
    };
}

public class UserListItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime DateRegistered { get; set; }
    public List<string> Roles { get; set; } = new();
}

public class UserDetailsViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; }
    public DateTime DateRegistered { get; set; }
    public List<string> Roles { get; set; } = new();
}

public class UserCreateViewModel
{
    [Required, StringLength(50)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(50)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Phone]
    [Display(Name = "Phone number")]
    public string? PhoneNumber { get; set; }

    [Required, StringLength(100, MinimumLength = 8)]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Display(Name = "Role")]
    public string? Role { get; set; }
}

public class UserEditViewModel
{
    public string Id { get; set; } = string.Empty;

    [Required, StringLength(50)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(50)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Phone]
    [Display(Name = "Phone number")]
    public string? PhoneNumber { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; }
}
