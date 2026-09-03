using System.Security.Claims;

namespace RentalSphere.Common.Services;

/// <summary>
/// Scoped per-request wrapper around the current ClaimsPrincipal. Repositories and services
/// use this to apply user-scope filtering and role-aware business rules without taking a hard
/// dependency on HttpContext.
/// </summary>
public interface ICurrentUser
{
    string? UserId { get; }
    string? UserName { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
    bool IsAdmin { get; }
    bool IsStaff { get; }
    bool IsCustomer { get; }

    /// <summary>
    /// True when the caller is a Customer role and therefore must only see their own records.
    /// Repositories use this to append .Where(x => x.Customer.UserId == UserId) automatically.
    /// </summary>
    bool IsCustomerScoped { get; }
}

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public string? UserId => Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
    public string? UserName => Principal?.Identity?.Name;
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;
    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;
    public bool IsAdmin => IsInRole(RentalSphere.Common.Constants.RoleNames.Admin);
    public bool IsStaff => IsInRole(RentalSphere.Common.Constants.RoleNames.Staff);
    public bool IsCustomer => IsInRole(RentalSphere.Common.Constants.RoleNames.Customer);
    public bool IsCustomerScoped => IsAuthenticated && IsCustomer;
}
