using SmartArchive.Domain.Common;

namespace SmartArchive.Domain.Entities;

public class DocumentMetadata : BaseAuditableEntity
{
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public float? Confidence { get; set; }
    public bool IsModifiedByUser { get; set; }
}
