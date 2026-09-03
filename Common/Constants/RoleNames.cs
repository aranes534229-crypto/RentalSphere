namespace RentalSphere.Common.Constants;

/// <summary>
/// Canonical role names used across [Authorize] attributes and Identity seeding.
/// Keep these values stable — they are written to the AspNetRoles table and into audit logs.
/// </summary>
public static class RoleNames
{
    public const string Admin = "Admin";
    public const string Staff = "Staff";
    public const string Customer = "Customer";
}
