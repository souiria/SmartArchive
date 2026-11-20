namespace SmartArchive.Domain;

public enum DocumentStatus { Uploaded, Processing, PendingValidation, UnderReview, Approved, Rejected, Archived, ProcessingFailed, Deleted }

public sealed class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class DocumentCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int DepartmentId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ArchiveDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string FileName { get; set; } = "";
    public string BlobReference { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string ContentType { get; set; } = "application/pdf";
    public long SizeBytes { get; set; }
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = "";
    public string Category { get; set; } = "";
    public string ReferenceNumber { get; set; } = "";
    public DateOnly? DocumentDate { get; set; }
    public string ExtractedText { get; set; } = "";
    public string Keywords { get; set; } = "";
    public string Description { get; set; } = "";
    public string UploadedBy { get; set; } = "Démo SEWS Maroc";
    public string? ValidatedBy { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ValidatedAt { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Uploaded;
    public double Confidence { get; set; }
    public string Warnings { get; set; } = "";
    public string AuditJson { get; set; } = "[]";
    public string? ReviewerComment { get; set; }
}

public sealed record AiSuggestion(string Title, string Department, string Category, DateOnly? DocumentDate, string ReferenceNumber, string ExtractedText, string Keywords, string Description, double Confidence, string Warnings);
public sealed record UploadResult(Guid Id, string FileName, bool Duplicate);

public static class DocumentLifecycle
{
    public static void Move(ArchiveDocument document, DocumentStatus next)
    {
        var allowed = next == DocumentStatus.Deleted
            ? document.Status != DocumentStatus.Deleted
            : (document.Status, next) switch
        {
            (DocumentStatus.Uploaded, DocumentStatus.Processing) => true,
            (DocumentStatus.Processing, DocumentStatus.PendingValidation or DocumentStatus.ProcessingFailed) => true,
            (DocumentStatus.ProcessingFailed, DocumentStatus.Processing) => true,
            (DocumentStatus.PendingValidation, DocumentStatus.UnderReview or DocumentStatus.Archived or DocumentStatus.Rejected) => true,
            (DocumentStatus.UnderReview, DocumentStatus.Archived or DocumentStatus.Rejected or DocumentStatus.PendingValidation) => true,
            (DocumentStatus.Rejected, DocumentStatus.Processing or DocumentStatus.PendingValidation) => true,
            _ => false
        };
        if (!allowed) throw new InvalidOperationException($"Transition documentaire interdite : {document.Status} → {next}.");
        document.Status = next;
    }
}
