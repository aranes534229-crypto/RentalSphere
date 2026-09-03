using RentalSphere.Modules.CustomerManagement.Models;

namespace RentalSphere.Modules.CustomerManagement.Services;

/// <summary>
/// Single source of truth for the loyalty-tier business rule.
/// Tiers are derived from lifetime spend (<see cref="Customer.TotalSpent"/>);
/// an Admin-set <see cref="Customer.ManualOverrideTier"/> takes precedence
/// when present (e.g. for VIP accounts).
/// </summary>
public static class LoyaltyTierRules
{
    public const decimal SilverThreshold   =  20_000m;
    public const decimal GoldThreshold     =  50_000m;
    public const decimal PlatinumThreshold = 100_000m;

    public static LoyaltyTier Compute(decimal totalSpent) =>
        totalSpent >= PlatinumThreshold ? LoyaltyTier.Platinum :
        totalSpent >= GoldThreshold     ? LoyaltyTier.Gold :
        totalSpent >= SilverThreshold   ? LoyaltyTier.Silver :
        LoyaltyTier.Bronze;

    public static LoyaltyTier EffectiveTier(Customer c) =>
        c.ManualOverrideTier ?? Compute(c.TotalSpent);
}
