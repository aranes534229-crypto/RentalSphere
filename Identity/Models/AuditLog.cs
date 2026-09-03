namespace RentalSphere.Identity.Models;

/// <summary>
/// Admin-only audit trail. Captures actor, action, target entity, before/after JSON snapshots,
/// and IP. Admin-only readers; written by services that perform privileged operations.
/// </summary>
public class AuditLog
{
    public int AuditLogID { get; set; }
    public string ActorUserId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? IpAddress { get; set; }
}
