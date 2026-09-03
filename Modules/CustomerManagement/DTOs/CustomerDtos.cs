using System.ComponentModel.DataAnnotations;
using RentalSphere.Modules.CustomerManagement.Models;

namespace RentalSphere.Modules.CustomerManagement.DTOs;

public class CustomerListItemDto
{
    public int CustomerID { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string LoyaltyTier { get; set; } = "Bronze";
    public DateTime DateRegistered { get; set; }
    public bool IsActive { get; set; }
    public int ReservationCount { get; set; }
}

public class CustomerDetailsDto
{
    public int CustomerID { get; set; }
    public string? UserId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public string LoyaltyTier { get; set; } = "Bronze";
    public bool IsOverridden { get; set; }
    public string? OverrideReason { get; set; }
    public string? OverrideByUserId { get; set; }
    public DateTime? OverrideAt { get; set; }
    public decimal TotalSpent { get; set; }
    public DateTime DateRegistered { get; set; }
    public bool IsActive { get; set; }
    public int ReservationCount { get; set; }
}

public class CustomerCreateDto
{
    [Required, StringLength(100)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(100)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(256)]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Phone, StringLength(40)]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }

    [StringLength(500)]
    [Display(Name = "Address")]
    public string? Address { get; set; }

    [StringLength(100)]
    [Display(Name = "City")]
    public string? City { get; set; }

    [StringLength(20)]
    [Display(Name = "Postal code")]
    public string? PostalCode { get; set; }
}

public class CustomerUpdateDto
{
    public int CustomerID { get; set; }

    [Required, StringLength(100)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(100)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(256)]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Phone, StringLength(40)]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }

    [StringLength(500)]
    [Display(Name = "Address")]
    public string? Address { get; set; }

    [StringLength(100)]
    [Display(Name = "City")]
    public string? City { get; set; }

    [StringLength(20)]
    [Display(Name = "Postal code")]
    public string? PostalCode { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}

public class CustomerTierOverrideDto
{
    public int CustomerID { get; set; }

    [Required, StringLength(20)]
    [Display(Name = "Tier")]
    public string Tier { get; set; } = "Bronze";

    [Required, StringLength(500)]
    [Display(Name = "Reason")]
    public string Reason { get; set; } = string.Empty;
}

public class CustomerLookupDto
{
    public int CustomerID { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}