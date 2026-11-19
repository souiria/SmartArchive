using SmartArchive.Domain.Common;

namespace SmartArchive.Domain.Entities;

public class DocumentVersion : BaseAuditableEntity
{
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    public int VersionNumber { get; set; }
    public string BlobContainer { get; set; } = string.Empty;
    public string BlobPath { get; set; } = string.Empty;
    public string FileSha256Hash { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ChangeNote { get; set; } = string.Empty;
}
