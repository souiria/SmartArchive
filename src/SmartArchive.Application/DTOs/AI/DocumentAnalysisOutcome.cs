namespace SmartArchive.Application.DTOs.AI;

public sealed record ExtractedFieldDto(string Key, string Value, float Confidence);

public sealed class DocumentAnalysisOutcome
{
    public string ModelUsed { get; set; } = string.Empty;
    public float OverallConfidence { get; set; }
    public bool RequiresManualReview { get; set; }
    public string? DetectedLanguage { get; set; }
    public int PageCount { get; set; }

    public string? SuggestedDocumentTypeCode { get; set; }
    public string? SuggestedDepartmentCode { get; set; }

    public string? Title { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateOnly? DocumentDate { get; set; }
    public string? SenderName { get; set; }
    public string? RecipientName { get; set; }
    public string? RelatedPersonName { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? Currency { get; set; }
    public string? Keywords { get; set; }

    public string ExtractedOcrText { get; set; } = string.Empty;
    public string RawResponseJson { get; set; } = string.Empty;
    public long ProcessingDurationMs { get; set; }

    public List<ExtractedFieldDto> AdditionalFields { get; set; } = new();
}
