using RentalSphere.Identity.Models;

namespace RentalSphere.Identity.Services;

/// <summary>
/// Writes audit-trail entries for privileged actions. Admin-only readers.
/// </summary>
public interface IAuditLogger
{
    Task LogAsync(
        string actorUserId,
        string action,
        string entityName,
        string entityId,
        object? oldValues = null,
        object? newValues = null,
        string? ipAddress = null);
}

public class AuditLogger : IAuditLogger
{
    private readonly RentalSphere.Data.ApplicationDbContext _db;
    private readonly IHttpContextAccessor _accessor;

    public AuditLogger(RentalSphere.Data.ApplicationDbContext db, IHttpContextAccessor accessor)
    {
        _db = db;
        _accessor = accessor;
    }

    public async Task LogAsync(
        string actorUserId,
        string action,
        string entityName,
        string entityId,
        object? oldValues = null,
        object? newValues = null,
        string? ipAddress = null)
    {
        var entry = new AuditLog
        {
            ActorUserId = actorUserId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            OldValuesJson = oldValues is null ? null : System.Text.Json.JsonSerializer.Serialize(oldValues),
            NewValuesJson = newValues is null ? null : System.Text.Json.JsonSerializer.Serialize(newValues),
            Timestamp = DateTime.UtcNow,
            IpAddress = ipAddress ?? _accessor.HttpContext?.Connection?.RemoteIpAddress?.ToString(),
        };
        _db.AuditLogs.Add(entry);
        await _db.SaveChangesAsync();
    }
}
