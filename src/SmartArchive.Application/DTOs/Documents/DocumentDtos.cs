using SmartArchive.Domain.Enums;

namespace SmartArchive.Application.DTOs.Documents;

public sealed class DocumentSearchRequest
{
    public string? Query { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? DocumentTypeId { get; set; }
    public DocumentStatus? Status { get; set; }
    public DateOnly? DocumentDateFrom { get; set; }
    public DateOnly? DocumentDateTo { get; set; }
    public DateTimeOffset? UploadDateFrom { get; set; }
    public DateTimeOffset? UploadDateTo { get; set; }
    public int? Year { get; set; }
    public int? Month { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? RelatedPersonName { get; set; }
    public string? DocumentNumber { get; set; }
    public string? OriginalFileName { get; set; }
    public string SortBy { get; set; } = "CreatedAt";
    public bool SortDescending { get; set; } = true;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class DocumentSummaryDto
{
    public Guid Id { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public DocumentStatus Status { get; set; }
    public string? Title { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateOnly? DocumentDate { get; set; }
    public string? SenderName { get; set; }
    public string? RelatedPersonName { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? Currency { get; set; }
    public string? DepartmentNameFr { get; set; }
    public string? DepartmentNameAr { get; set; }
    public string? DocumentTypeNameFr { get; set; }
    public string? DocumentTypeNameAr { get; set; }
    public float? AiConfidence { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ValidateDocumentCommand
{
    public Guid DocumentId { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid DocumentTypeId { get; set; }
    public string? Title { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateOnly? DocumentDate { get; set; }
    public string? SenderName { get; set; }
    public string? RecipientName { get; set; }
    public string? RelatedPersonName { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? Currency { get; set; }
    public string? Keywords { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public sealed class DashboardStatsDto
{
    public int TotalArchivedDocuments { get; set; }
    public int AwaitingValidationCount { get; set; }
    public int ProcessingCount { get; set; }
    public int ProcessingFailedCount { get; set; }
    public Dictionary<string, int> DocumentsByDepartment { get; set; } = new();
    public Dictionary<int, int> DocumentsByYear { get; set; } = new();
    public Dictionary<string, int> DocumentsByType { get; set; } = new();
    public List<DocumentSummaryDto> RecentUploads { get; set; } = new();
}
