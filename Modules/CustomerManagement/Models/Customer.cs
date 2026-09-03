using RentalSphere.Identity;

namespace RentalSphere.Modules.CustomerManagement.Models;

/// <summary>
/// Customer profile. Optional link to an Identity user
/// (self-registered accounts auto-create a Customer row).
/// </summary>
public class Customer
{
    public int CustomerID { get; set; }

    /// <summary>FK to AspNetUsers.Id. Nullable so walk-in Customers may exist without a login.</summary>
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }

    public LoyaltyTier LoyaltyTier { get; set; } = LoyaltyTier.Bronze;

    /// <summary>Denormalized; updated when Billing lands.</summary>
    public decimal TotalSpent { get; set; }

    public DateTime DateRegistered { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // Manual override: when set, the effective tier is the override rather than
    // the spend-based computation. Only Admin can set/clear this. The override
    // is preserved across spend recomputes in Billing.
    public LoyaltyTier? ManualOverrideTier { get; set; }
    public string? ManualOverrideReason { get; set; }
    public string? ManualOverrideByUserId { get; set; }
    public DateTime? ManualOverrideAt { get; set; }
}

public enum LoyaltyTier
{
    Bronze,
    Silver,
    Gold,
    Platinum,
}