namespace RentalSphere.Common.Exceptions;

/// <summary>
/// Thrown by services when a requested entity doesn't exist. Mapped to HTTP 404.
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}
