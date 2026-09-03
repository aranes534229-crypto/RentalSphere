namespace RentalSphere.Common.Exceptions;

/// <summary>
/// Thrown by services when a caller tries to perform an action outside their RBAC allowance
/// (e.g. Staff trying to delete equipment). Mapped to HTTP 403 by the global exception filter.
/// </summary>
public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
}
