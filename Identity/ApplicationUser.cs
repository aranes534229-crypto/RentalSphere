using Microsoft.AspNetCore.Identity;

namespace RentalSphere.Identity;

/// <summary>
/// Application user. Extends IdentityUser so we get Email, PhoneNumber, security stamp,
/// and the rest of Identity's authentication surface out of the box.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime DateRegistered { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}
