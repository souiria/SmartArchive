namespace SmartArchive.Domain.Enums;

public enum AuditActionType
{
    Uploaded = 1,
    AiProcessed = 2,
    Validated = 3,
    Archived = 4,
    Modified = 5,
    Viewed = 6,
    Downloaded = 7,
    Rejected = 8,
    SoftDeleted = 9,
    Restored = 10,
    PermissionChanged = 11
}
