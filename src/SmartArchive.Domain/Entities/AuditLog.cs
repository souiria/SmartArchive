using SmartArchive.Domain.Enums;

namespace SmartArchive.Domain.Entities;

public class AuditLog
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public Guid? UserId { get; set; }
    public string UserEmail { get; set; } = "System";
    public string? IpAddress { get; set; }

    public AuditActionType Action { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public Guid EntityId { get; set; }

    public string? Details { get; set; }
    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
}
