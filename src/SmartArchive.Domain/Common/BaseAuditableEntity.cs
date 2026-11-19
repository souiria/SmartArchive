namespace SmartArchive.Domain.Common;

public abstract class BaseAuditableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? LastModifiedAt { get; set; }
    public Guid? LastModifiedByUserId { get; set; }
}
