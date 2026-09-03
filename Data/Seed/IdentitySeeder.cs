using Microsoft.AspNetCore.Identity;
using RentalSphere.Common.Constants;
using RentalSphere.Identity;

namespace RentalSphere.Data.Seed;

/// <summary>
/// Idempotent seeder: ensures the three roles and one default user per role exist.
/// Run on startup after db.Database.Migrate().
/// </summary>
public static class IdentitySeeder
{
    public static async Task SeedAsync(
        RoleManager<IdentityRole> roleManager,
        UserManager<ApplicationUser> userManager)
    {
        await EnsureRoleAsync(roleManager, RoleNames.Admin);
        await EnsureRoleAsync(roleManager, RoleNames.Staff);
        await EnsureRoleAsync(roleManager, RoleNames.Customer);

        await EnsureUserAsync(userManager, "admin@rentalsphere.local",    "Admin@12345",    "System",   "Administrator", RoleNames.Admin);
        await EnsureUserAsync(userManager, "staff@rentalsphere.local",    "Staff@12345",    "Staff",    "User",         RoleNames.Staff);
        await EnsureUserAsync(userManager, "customer@rentalsphere.local", "Customer@12345", "Default",  "Customer",     RoleNames.Customer);
    }

    private static async Task EnsureRoleAsync(RoleManager<IdentityRole> roleManager, string roleName)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            var result = await roleManager.CreateAsync(new IdentityRole(roleName));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to create role '{roleName}': {string.Join("; ", result.Errors.Select(e => e.Description))}");
            }
        }
    }

    private static async Task EnsureUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string password,
        string firstName,
        string lastName,
        string role)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is not null) return;

        user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            EmailConfirmed = true,
            IsActive = true,
            DateRegistered = DateTime.UtcNow,
        };

        var create = await userManager.CreateAsync(user, password);
        if (!create.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to create user '{email}': {string.Join("; ", create.Errors.Select(e => e.Description))}");
        }

        var addRole = await userManager.AddToRoleAsync(user, role);
        if (!addRole.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to assign role '{role}' to '{email}': {string.Join("; ", addRole.Errors.Select(e => e.Description))}");
        }
    }
}
