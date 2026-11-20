using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartArchive.Application;
using SmartArchive.Domain;

namespace SmartArchive.Infrastructure;

public sealed class DocumentWorkflow(
    IDocumentRepository repository,
    IFileStorageService storage,
    IDocumentIntelligenceService intelligence,
    ILogger<DocumentWorkflow> logger) : IDocumentWorkflow
{
    public async Task<UploadResult> UploadAsync(string fileName, string contentType, Stream stream, string uploadedBy, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(fileName);
        if (!string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase) ||
            (contentType != "application/pdf" && contentType != "application/octet-stream"))
            throw new InvalidOperationException("Only PDF files are accepted.");

        const long maxBytes = 50L * 1024 * 1024;
        using var input = new MemoryStream();
        await stream.CopyToAsync(input, ct);
        if (input.Length == 0 || input.Length > maxBytes)
            throw new InvalidOperationException("The PDF must be between 1 byte and 50 MiB.");

        var bytes = input.ToArray();
        if (bytes.Length < 5 || System.Text.Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-")
            throw new InvalidOperationException("The file contents are not a valid PDF.");

        var contentHash = Convert.ToHexString(SHA256.HashData(bytes));
        if ((await repository.ListAsync(ct: ct)).Any(document => document.ContentHash == contentHash))
            return new UploadResult(Guid.Empty, fileName, true);

        var id = Guid.NewGuid();
        var uploadedAt = DateTimeOffset.UtcNow;
        input.Position = 0;
        var reference = await storage.SaveAsync(id, Path.GetFileName(fileName), input, uploadedAt, "Unclassified", null, ct);
        var document = new ArchiveDocument
        {
            Id = id,
            FileName = Path.GetFileName(fileName),
            Title = Path.GetFileNameWithoutExtension(fileName),
            BlobReference = reference,
            ContentHash = contentHash,
            SizeBytes = bytes.Length,
            UploadedBy = uploadedBy,
            UploadedAt = uploadedAt,
            Status = DocumentStatus.Uploaded,
            AuditJson = AddAudit("Upload", uploadedBy, "PDF received")
        };
        DocumentLifecycle.Move(document, DocumentStatus.Processing);
        await repository.AddAsync(document, ct);

        try
        {
            await Analyze(document, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            document.Status = DocumentStatus.Processing;
            DocumentLifecycle.Move(document, DocumentStatus.ProcessingFailed);
            document.Warnings = $"Analysis failed ({ex.GetType().Name}). Check service settings, including Azure endpoint/authentication if configured, then retry.";
            await repository.SaveAsync(document, ct);
        }

        return new UploadResult(id, fileName, false);
    }

    public async Task RetryAnalysisAsync(Guid id, CancellationToken ct = default)
    {
        var document = await repository.GetAsync(id, ct) ?? throw new KeyNotFoundException();
        DocumentLifecycle.Move(document, DocumentStatus.Processing);
        await repository.SaveAsync(document, ct);
        await Analyze(document, ct);
    }

    private async Task Analyze(ArchiveDocument document, CancellationToken ct)
    {
        var previousReference = document.BlobReference;
        await using var storedPdf = await storage.OpenAsync(previousReference, ct) ?? throw new IOException("File not found.");
        using var pdfForAnalysis = new MemoryStream();
        await storedPdf.CopyToAsync(pdfForAnalysis, ct);
        pdfForAnalysis.Position = 0;

        var suggestion = await intelligence.AnalyzeAsync(document.FileName, pdfForAnalysis, ct: ct);
        var newReference = await storage.MoveToArchivePathAsync(
            document.Id,
            document.FileName,
            previousReference,
            document.UploadedAt,
            suggestion.Department,
            suggestion.DocumentDate,
            ct);

        document.Title = suggestion.Title;
        document.DepartmentName = suggestion.Department;
        document.DepartmentId = (await repository.DepartmentsAsync(ct)).FirstOrDefault(x => x.Name == suggestion.Department)?.Id ?? 10;
        document.Category = suggestion.Category;
        document.DocumentDate = suggestion.DocumentDate;
        document.ReferenceNumber = suggestion.ReferenceNumber;
        document.ExtractedText = suggestion.ExtractedText;
        document.Keywords = suggestion.Keywords;
        document.Description = suggestion.Description;
        document.Confidence = suggestion.Confidence;
        document.Warnings = suggestion.Warnings;
        document.BlobReference = newReference;
        DocumentLifecycle.Move(document, DocumentStatus.PendingValidation);
        document.AuditJson = AddAudit("AIAnalysis", "SmartArchive", $"Indicative analysis, confidence {suggestion.Confidence:P0}", document.AuditJson);
        try
        {
            await repository.SaveAsync(document, ct);
        }
        catch
        {
            document.Status = DocumentStatus.Processing;
            document.BlobReference = previousReference;
            await TryDeletePreviousReferenceAsync(document.Id, newReference, previousReference, ct);
            throw;
        }
        await TryDeletePreviousReferenceAsync(document.Id, previousReference, newReference, ct);
    }

    public async Task<bool> ReviewAsync(Guid id, string title, string department, string category, DateOnly? date, string reference, string comment, string reviewer, bool approve, CancellationToken ct = default)
    {
        var document = await repository.GetAsync(id, ct) ?? throw new KeyNotFoundException();
        if (document.Status is not (DocumentStatus.PendingValidation or DocumentStatus.UnderReview))
            throw new InvalidOperationException("This document is not in the review queue.");

        var selectedDepartmentId = (await repository.DepartmentsAsync(ct)).FirstOrDefault(x => x.Name == department)?.Id;
        var previousReference = document.BlobReference;
        var previousWarnings = document.Warnings;
        var newReference = previousReference;
        var storageActionSucceeded = true;
        if (approve)
        {
            try
            {
                newReference = await storage.MoveToArchivePathAsync(document.Id, document.FileName, previousReference, document.UploadedAt, department, date, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not move the approved PDF into its final archive partition for document {DocumentId}; retaining its current location.", id);
                document.Warnings = AppendWarning(document.Warnings, "The PDF could not be moved to the selected archive path. Approval will be saved and the existing file location retained.");
                storageActionSucceeded = false;
            }
        }

        var previousStatus = document.Status;
        var previousTitle = document.Title;
        var previousDepartmentName = document.DepartmentName;
        var previousDepartmentId = document.DepartmentId;
        var previousCategory = document.Category;
        var previousDocumentDate = document.DocumentDate;
        var previousReferenceNumber = document.ReferenceNumber;
        var previousComment = document.ReviewerComment;
        var previousValidatedBy = document.ValidatedBy;
        var previousValidatedAt = document.ValidatedAt;
        var previousAudit = document.AuditJson;

        document.Title = title.Trim();
        document.DepartmentName = department;
        document.DepartmentId = selectedDepartmentId ?? document.DepartmentId;
        document.Category = category.Trim();
        document.DocumentDate = date;
        document.ReferenceNumber = reference.Trim();
        document.ReviewerComment = comment;
        document.ValidatedBy = reviewer;
        document.ValidatedAt = DateTimeOffset.UtcNow;
        document.BlobReference = newReference;
        DocumentLifecycle.Move(document, approve ? DocumentStatus.Archived : DocumentStatus.Rejected);
        var action = approve ? "ApprovedAndArchived" : "RejectedAndPdfRemovalRequested";
        var detail = string.IsNullOrWhiteSpace(comment) ? (approve ? "Document approved and archived." : "Document rejected; PDF removal requested.") : comment;
        document.AuditJson = AddAudit(action, reviewer, detail, document.AuditJson);

        try
        {
            await repository.SaveAsync(document, ct);
        }
        catch
        {
            document.Status = previousStatus;
            document.Title = previousTitle;
            document.DepartmentName = previousDepartmentName;
            document.DepartmentId = previousDepartmentId;
            document.Category = previousCategory;
            document.DocumentDate = previousDocumentDate;
            document.ReferenceNumber = previousReferenceNumber;
            document.ReviewerComment = previousComment;
            document.ValidatedBy = previousValidatedBy;
            document.ValidatedAt = previousValidatedAt;
            document.AuditJson = previousAudit;
            document.Warnings = previousWarnings;
            document.BlobReference = previousReference;
            if (!string.Equals(previousReference, newReference, StringComparison.Ordinal))
                await TryDeletePreviousReferenceAsync(document.Id, newReference, previousReference, ct);
            throw;
        }

        if (approve)
        {
            await TryDeletePreviousReferenceAsync(document.Id, previousReference, newReference, ct);
        }
        else if (!await TryDeleteStoredFileAsync(document.Id, previousReference, ct))
        {
            storageActionSucceeded = false;
            document.Warnings = AppendWarning(document.Warnings, "The rejection was saved, but the PDF could not be removed from storage; manual cleanup is required.");
            document.AuditJson = AddAudit("PdfRemovalFailed", reviewer, "PDF removal after rejection failed.", document.AuditJson);
            await TrySaveRemovalFailureAsync(document, ct);
        }
        return storageActionSucceeded;
    }

    public async Task<bool> DeleteAsync(Guid id, string actor, string comment = "", CancellationToken ct = default)
    {
        var document = await repository.GetAsync(id, ct) ?? throw new KeyNotFoundException();
        if (document.Status != DocumentStatus.Deleted)
        {
            DocumentLifecycle.Move(document, DocumentStatus.Deleted);
            document.ValidatedBy = actor;
            document.ValidatedAt = DateTimeOffset.UtcNow;
            document.ReviewerComment = comment;
            document.AuditJson = AddAudit("Deleted", actor, string.IsNullOrWhiteSpace(comment) ? "Document deleted; audit metadata retained." : comment, document.AuditJson);
            await repository.SaveAsync(document, ct);
        }

        if (!await TryDeleteStoredFileAsync(document.Id, document.BlobReference, ct))
        {
            document.Warnings = AppendWarning(document.Warnings, "The document is marked deleted, but its PDF could not be removed from storage; manual cleanup is required.");
            document.AuditJson = AddAudit("PdfRemovalFailed", actor, "PDF removal after deletion failed.", document.AuditJson);
            await TrySaveRemovalFailureAsync(document, ct);
            return false;
        }
        return true;
    }

    public async Task<byte[]?> DownloadAsync(Guid id, CancellationToken ct = default)
    {
        var document = await repository.GetAsync(id, ct);
        if (document is null) return null;
        await using var stream = await storage.OpenAsync(document.BlobReference, ct);
        if (stream is null) return null;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    private async Task TryDeletePreviousReferenceAsync(Guid documentId, string previousReference, string currentReference, CancellationToken ct)
    {
        if (string.Equals(previousReference, currentReference, StringComparison.Ordinal)) return;
        try
        {
            await storage.DeleteAsync(previousReference, ct);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not remove the superseded PDF copy for document {DocumentId}.", documentId);
        }
    }

    private async Task<bool> TryDeleteStoredFileAsync(Guid documentId, string reference, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reference)) return true;
        try
        {
            await storage.DeleteAsync(reference, ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not remove the PDF for document {DocumentId} from storage.", documentId);
            return false;
        }
    }

    private async Task TrySaveRemovalFailureAsync(ArchiveDocument document, CancellationToken ct)
    {
        try { await repository.SaveAsync(document, ct); }
        catch (Exception exception) { logger.LogError(exception, "Could not persist the PDF-removal failure audit for document {DocumentId}.", document.Id); }
    }

    private static string AppendWarning(string existing, string warning) =>
        string.IsNullOrWhiteSpace(existing) ? warning : $"{existing}\n{warning}";

    private static string AddAudit(string action, string actor, string detail, string existing = "[]")
    {
        var rows = JsonSerializer.Deserialize<List<object>>(existing) ?? [];
        rows.Add(new { action, actor, detail, at = DateTimeOffset.UtcNow });
        return JsonSerializer.Serialize(rows);
    }
}
