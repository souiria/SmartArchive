using SmartArchive.Domain.Common;
using SmartArchive.Domain.Enums;

namespace SmartArchive.Domain.Entities;

public class DocumentPermission : BaseAuditableEntity
{
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    public Guid? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public PermissionLevel Permission { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}
