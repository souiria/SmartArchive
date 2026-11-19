using SmartArchive.Domain.Common;

namespace SmartArchive.Domain.Entities;

public class AIProcessingResult : BaseAuditableEntity
{
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    public string ModelUsed { get; set; } = string.Empty;
    public float OverallConfidence { get; set; }
    public bool RequiresManualReview { get; set; }
    public string? DetectedLanguage { get; set; }
    public string ExtractedOcrText { get; set; } = string.Empty;
    public string RawResponseJson { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public long ProcessingDurationMs { get; set; }
}
