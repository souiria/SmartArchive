using SmartArchive.Domain;

namespace SmartArchive.Application;

public interface IDocumentRepository
{
    Task<IReadOnlyList<ArchiveDocument>> ListAsync(string? search = null, DocumentStatus? status = null, int? departmentId = null, CancellationToken ct = default);
    Task<ArchiveDocument?> GetAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(ArchiveDocument document, CancellationToken ct = default);
    Task SaveAsync(ArchiveDocument document, CancellationToken ct = default);
    Task<IReadOnlyList<Department>> DepartmentsAsync(CancellationToken ct = default);
    Task<object> StatisticsAsync(CancellationToken ct = default);
}

public interface IFileStorageService
{
    Task<string> SaveAsync(Guid id, string fileName, Stream stream, CancellationToken ct = default);
    Task<Stream?> OpenAsync(string reference, CancellationToken ct = default);
}

public interface IDocumentIntelligenceService
{
    Task<AiSuggestion> AnalyzeAsync(string fileName, Stream pdf, string? suppliedText = null, CancellationToken ct = default);
}

public interface IDocumentWorkflow
{
    Task<UploadResult> UploadAsync(string fileName, string contentType, Stream stream, string uploadedBy, CancellationToken ct = default);
    Task RetryAnalysisAsync(Guid id, CancellationToken ct = default);
    Task ReviewAsync(Guid id, string title, string department, string category, DateOnly? date, string reference, string comment, string reviewer, bool approve, CancellationToken ct = default);
    Task<byte[]?> DownloadAsync(Guid id, CancellationToken ct = default);
}
