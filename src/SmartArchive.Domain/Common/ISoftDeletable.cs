namespace SmartArchive.Domain.Common;

public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedAt { get; set; }
    Guid? DeletedByUserId { get; set; }
}
