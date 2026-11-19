using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartArchive.Application.Common.Interfaces;
using SmartArchive.Domain.Entities;
using SmartArchive.Domain.Enums;

namespace SmartArchive.Infrastructure.BackgroundJobs;

public sealed class DocumentProcessingQueue : IBackgroundTaskQueue
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
        => _queue.Writer.WriteAsync(documentId, cancellationToken);

    public ValueTask<Guid> DequeueDocumentAsync(CancellationToken cancellationToken)
        => _queue.Reader.ReadAsync(cancellationToken);
}

public sealed class DocumentAiWorker : BackgroundService
{
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentAiWorker> _logger;

    public DocumentAiWorker(
        IBackgroundTaskQueue taskQueue,
        IServiceScopeFactory scopeFactory,
        ILogger<DocumentAiWorker> logger)
    {
        _taskQueue = taskQueue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SmartArchive AI Background Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var documentId = await _taskQueue.DequeueDocumentAsync(stoppingToken);

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var blobStorage = scope.ServiceProvider.GetRequiredService<IBlobStorageService>();
            var aiService = scope.ServiceProvider.GetRequiredService<IDocumentAiService>();
            var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();

            var document = await dbContext.Documents
                .Include(d => d.MetadataItems)
                .FirstOrDefaultAsync(d => d.Id == documentId, stoppingToken);

            if (document is null) continue;

            try
            {
                document.Status = DocumentStatus.Processing;
                await dbContext.SaveChangesAsync(stoppingToken);

                await using var pdfStream = await blobStorage.DownloadAsync(document.BlobContainer, document.BlobPath, stoppingToken);
                var outcome = await aiService.AnalyzeDocumentAsync(pdfStream, document.OriginalFileName, stoppingToken);

                var docTypes = await dbContext.DocumentTypes.AsNoTracking().ToListAsync(stoppingToken);
                var departments = await dbContext.Departments.AsNoTracking().ToListAsync(stoppingToken);

                var matchedType = docTypes.FirstOrDefault(dt => dt.Code == outcome.SuggestedDocumentTypeCode);
                var matchedDept = departments.FirstOrDefault(d => d.Code == outcome.SuggestedDepartmentCode);

                document.PageCount = outcome.PageCount;
                document.DetectedLanguage = outcome.DetectedLanguage;
                document.Title = outcome.Title;
                document.ReferenceNumber = outcome.ReferenceNumber;
                document.DocumentDate = outcome.DocumentDate;
                document.SenderName = outcome.SenderName;
                document.RecipientName = outcome.RecipientName;
                document.RelatedPersonName = outcome.RelatedPersonName;
                document.TotalAmount = outcome.TotalAmount;
                document.Currency = outcome.Currency;
                document.Keywords = outcome.Keywords;
                document.DocumentTypeId = matchedType?.Id;
                document.SuggestedDepartmentId = matchedDept?.Id ?? document.DepartmentId;
                document.DepartmentId ??= matchedDept?.Id;
                document.Status = DocumentStatus.AwaitingValidation;

                dbContext.AIProcessingResults.Add(new AIProcessingResult
                {
                    DocumentId = document.Id,
                    ModelUsed = outcome.ModelUsed,
                    OverallConfidence = outcome.OverallConfidence,
                    RequiresManualReview = outcome.RequiresManualReview,
                    DetectedLanguage = outcome.DetectedLanguage,
                    ExtractedOcrText = outcome.ExtractedOcrText,
                    RawResponseJson = outcome.RawResponseJson,
                    ProcessingDurationMs = outcome.ProcessingDurationMs
                });

                foreach (var field in outcome.AdditionalFields)
                {
                    document.MetadataItems.Add(new DocumentMetadata
                    {
                        DocumentId = document.Id,
                        Key = field.Key,
                        Value = field.Value,
                        Confidence = field.Confidence
                    });
                }

                await dbContext.SaveChangesAsync(stoppingToken);
                await auditService.LogAsync(AuditActionType.AiProcessed, nameof(Document), document.Id,
                    $"AI analysis completed ({outcome.ModelUsed}, Confidence: {outcome.OverallConfidence:P0})", cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI processing failed for document {DocumentId}", documentId);
                document.Status = DocumentStatus.ProcessingFailed;
                dbContext.AIProcessingResults.Add(new AIProcessingResult
                {
                    DocumentId = document.Id,
                    ModelUsed = "Error",
                    ErrorMessage = ex.Message
                });
                await dbContext.SaveChangesAsync(stoppingToken);
            }
        }
    }
}
