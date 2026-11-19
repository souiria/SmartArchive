using SmartArchive.Domain.Common;
using SmartArchive.Domain.Enums;

namespace SmartArchive.Domain.Entities;

public class Document : BaseAuditableEntity, ISoftDeletable
{
    public string DocumentNumber { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string FileSha256Hash { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ContentType { get; set; } = "application/pdf";
    public int PageCount { get; set; }

    public string BlobContainer { get; set; } = "staging";
    public string BlobPath { get; set; } = string.Empty;

    public DocumentStatus Status { get; set; } = DocumentStatus.Uploaded;
    public string? RejectionReason { get; set; }

    public string? Title { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateOnly? DocumentDate { get; set; }
    public string? SenderName { get; set; }
    public string? RecipientName { get; set; }
    public string? RelatedPersonName { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? Currency { get; set; }
    public string? DetectedLanguage { get; set; }
    public string? Keywords { get; set; }

    public DateOnly? RetentionExpiryDate { get; set; }

    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public Guid? SuggestedDepartmentId { get; set; }
    public Department? SuggestedDepartment { get; set; }

    public Guid? DocumentTypeId { get; set; }
    public DocumentType? DocumentType { get; set; }

    public Guid UploadedByUserId { get; set; }
    public ApplicationUser UploadedByUser { get; set; } = null!;

    public Guid? ValidatedByUserId { get; set; }
    public ApplicationUser? ValidatedByUser { get; set; }
    public DateTimeOffset? ValidatedAt { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByUserId { get; set; }

    public AIProcessingResult? AIProcessingResult { get; set; }
    public ICollection<DocumentMetadata> MetadataItems { get; set; } = new List<DocumentMetadata>();
    public ICollection<DocumentVersion> Versions { get; set; } = new List<DocumentVersion>();
    public ICollection<DocumentPermission> Permissions { get; set; } = new List<DocumentPermission>();
}
