using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SmartArchive.Application.Common.Interfaces;
using SmartArchive.Application.Common.Models;
using SmartArchive.Application.DTOs.Documents;
using SmartArchive.Domain.Entities;
using SmartArchive.Domain.Enums;
using SmartArchive.Domain.Exceptions;

namespace SmartArchive.Application.Services;

public sealed class DocumentWorkflowService
{
    private readonly IApplicationDbContext _context;
    private readonly IBlobStorageService _blobStorage;
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;

    public DocumentWorkflowService(
        IApplicationDbContext context,
        IBlobStorageService blobStorage,
        IBackgroundTaskQueue taskQueue,
        ICurrentUserService currentUser,
        IAuditService auditService)
    {
        _context = context;
        _blobStorage = blobStorage;
        _taskQueue = taskQueue;
        _currentUser = currentUser;
        _auditService = auditService;
    }

    public async Task<Document> IngestPdfAsync(
        Stream fileStream,
        string originalFileName,
        string contentType,
        Guid? explicitDepartmentId = null,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedAccessException("User must be authenticated to upload documents.");

        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        // Compute SHA-256 Hash for duplicate detection
        var hashBytes = await SHA256.HashDataAsync(memoryStream, cancellationToken);
        var sha256Hex = Convert.ToHexString(hashBytes);
        memoryStream.Position = 0;

        var existing = await _context.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.FileSha256Hash == sha256Hex, cancellationToken);

        if (existing is not null)
        {
            throw new DuplicateDocumentException(existing.Id, existing.DocumentNumber);
        }

        var docId = Guid.CreateVersion7();
        var year = DateTime.UtcNow.Year;
        var sequence = await _context.Documents.IgnoreQueryFilters().CountAsync(cancellationToken) + 1;
        var documentNumber = $"ARC-{year}-{sequence:D6}";
        var stagingBlobPath = $"{year}/{docId}.pdf";

        await _blobStorage.UploadAsync("staging", stagingBlobPath, memoryStream, contentType, cancellationToken);

        var document = new Document
        {
            Id = docId,
            DocumentNumber = documentNumber,
            OriginalFileName = originalFileName,
            FileSha256Hash = sha256Hex,
            FileSizeBytes = memoryStream.Length,
            ContentType = contentType,
            BlobContainer = "staging",
            BlobPath = stagingBlobPath,
            Status = DocumentStatus.Uploaded,
            DepartmentId = explicitDepartmentId ?? _currentUser.DepartmentId,
            UploadedByUserId = userId
        };

        document.Versions.Add(new DocumentVersion
        {
            DocumentId = docId,
            VersionNumber = 1,
            BlobContainer = "staging",
            BlobPath = stagingBlobPath,
            FileSha256Hash = sha256Hex,
            FileSizeBytes = memoryStream.Length,
            ChangeNote = "Initial scan/upload"
        });

        _context.Documents.Add(document);
        await _context.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(AuditActionType.Uploaded, nameof(Document), docId, $"Uploaded '{originalFileName}' ({documentNumber})", cancellationToken: cancellationToken);
        await _taskQueue.EnqueueDocumentAsync(docId, cancellationToken);

        return document;
    }

    public async Task ValidateAndArchiveAsync(ValidateDocumentCommand command, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedAccessException("User must be authenticated.");

        var document = await _context.Documents
            .Include(d => d.MetadataItems)
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.Id == command.DocumentId, cancellationToken)
            ?? throw new DomainException("Document not found.");

        var department = await _context.Departments.FindAsync(new object[] { command.DepartmentId }, cancellationToken)
            ?? throw new DomainException("Selected department does not exist.");

        var docType = await _context.DocumentTypes.FindAsync(new object[] { command.DocumentTypeId }, cancellationToken)
            ?? throw new DomainException("Selected document type does not exist.");

        var docDate = command.DocumentDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var archiveBlobPath = $"Archive/{department.Code}/{docDate.Year}/{docDate.Month:D2}/{docType.Code}/{document.DocumentNumber}.pdf";

        // Move from staging to permanent structured archive path
        if (document.BlobContainer != "archive" || document.BlobPath != archiveBlobPath)
        {
            await _blobStorage.MoveAsync(document.BlobContainer, document.BlobPath, "archive", archiveBlobPath, cancellationToken);
            document.BlobContainer = "archive";
            document.BlobPath = archiveBlobPath;

            var v1 = document.Versions.FirstOrDefault(v => v.VersionNumber == 1);
            if (v1 is not null)
            {
                v1.BlobContainer = "archive";
                v1.BlobPath = archiveBlobPath;
            }
        }

        document.DepartmentId = command.DepartmentId;
        document.DocumentTypeId = command.DocumentTypeId;
        document.Title = command.Title;
        document.ReferenceNumber = command.ReferenceNumber;
        document.DocumentDate = command.DocumentDate;
        document.SenderName = command.SenderName;
        document.RecipientName = command.RecipientName;
        document.RelatedPersonName = command.RelatedPersonName;
        document.TotalAmount = command.TotalAmount;
        document.Currency = command.Currency;
        document.Keywords = command.Keywords;
        document.RetentionExpiryDate = docDate.AddYears(department.DefaultRetentionYears);
        document.Status = DocumentStatus.Archived;
        document.ValidatedByUserId = userId;
        document.ValidatedAt = DateTimeOffset.UtcNow;

        foreach (var kv in command.Metadata)
        {
            var existingMeta = document.MetadataItems.FirstOrDefault(m => m.Key == kv.Key);
            if (existingMeta is not null)
            {
                if (existingMeta.Value != kv.Value)
                {
                    existingMeta.Value = kv.Value;
                    existingMeta.IsModifiedByUser = true;
                }
            }
            else if (!string.IsNullOrWhiteSpace(kv.Value))
            {
                document.MetadataItems.Add(new DocumentMetadata
                {
                    DocumentId = document.Id,
                    Key = kv.Key,
                    Value = kv.Value,
                    IsModifiedByUser = true
                });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(AuditActionType.Archived, nameof(Document), document.Id, $"Validated and archived to {archiveBlobPath}", cancellationToken: cancellationToken);
    }

    public async Task<PaginatedList<DocumentSummaryDto>> SearchDocumentsAsync(DocumentSearchRequest request, CancellationToken cancellationToken = default)
    {
        var query = _context.Documents
            .AsNoTracking()
            .Include(d => d.Department)
            .Include(d => d.DocumentType)
            .Include(d => d.AIProcessingResult)
            .AsQueryable();

        // Enforce Department Security Isolation for non-administrators
        if (!_currentUser.IsAdministrator)
        {
            var userDeptId = _currentUser.DepartmentId;
            var userId = _currentUser.UserId;
            query = query.Where(d =>
                d.DepartmentId == userDeptId ||
                d.SuggestedDepartmentId == userDeptId ||
                d.UploadedByUserId == userId ||
                d.Permissions.Any(p => (p.UserId == userId || p.DepartmentId == userDeptId) && (p.ExpiresAt == null || p.ExpiresAt > DateTimeOffset.UtcNow)));
        }

        if (request.DepartmentId.HasValue)
            query = query.Where(d => d.DepartmentId == request.DepartmentId.Value);

        if (request.DocumentTypeId.HasValue)
            query = query.Where(d => d.DocumentTypeId == request.DocumentTypeId.Value);

        if (request.Status.HasValue)
            query = query.Where(d => d.Status == request.Status.Value);

        if (request.DocumentDateFrom.HasValue)
            query = query.Where(d => d.DocumentDate >= request.DocumentDateFrom.Value);

        if (request.DocumentDateTo.HasValue)
            query = query.Where(d => d.DocumentDate <= request.DocumentDateTo.Value);

        if (request.UploadDateFrom.HasValue)
            query = query.Where(d => d.CreatedAt >= request.UploadDateFrom.Value);

        if (request.UploadDateTo.HasValue)
            query = query.Where(d => d.CreatedAt <= request.UploadDateTo.Value);

        if (request.Year.HasValue)
            query = query.Where(d => d.DocumentDate.HasValue && d.DocumentDate.Value.Year == request.Year.Value);

        if (request.Month.HasValue)
            query = query.Where(d => d.DocumentDate.HasValue && d.DocumentDate.Value.Month == request.Month.Value);

        if (!string.IsNullOrWhiteSpace(request.ReferenceNumber))
            query = query.Where(d => d.ReferenceNumber != null && d.ReferenceNumber.Contains(request.ReferenceNumber));

        if (!string.IsNullOrWhiteSpace(request.RelatedPersonName))
            query = query.Where(d => d.RelatedPersonName != null && d.RelatedPersonName.Contains(request.RelatedPersonName));

        if (!string.IsNullOrWhiteSpace(request.DocumentNumber))
            query = query.Where(d => d.DocumentNumber.Contains(request.DocumentNumber));

        if (!string.IsNullOrWhiteSpace(request.OriginalFileName))
            query = query.Where(d => d.OriginalFileName.Contains(request.OriginalFileName));

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var term = request.Query.Trim();
            query = query.Where(d =>
                (d.Title != null && d.Title.Contains(term)) ||
                (d.Keywords != null && d.Keywords.Contains(term)) ||
                (d.ReferenceNumber != null && d.ReferenceNumber.Contains(term)) ||
                (d.SenderName != null && d.SenderName.Contains(term)) ||
                (d.RelatedPersonName != null && d.RelatedPersonName.Contains(term)) ||
                d.DocumentNumber.Contains(term) ||
                d.OriginalFileName.Contains(term) ||
                (d.AIProcessingResult != null && d.AIProcessingResult.ExtractedOcrText.Contains(term)));
        }

        query = (request.SortBy, request.SortDescending) switch
        {
            ("DocumentDate", true) => query.OrderByDescending(d => d.DocumentDate),
            ("DocumentDate", false) => query.OrderBy(d => d.DocumentDate),
            ("DocumentNumber", true) => query.OrderByDescending(d => d.DocumentNumber),
            ("DocumentNumber", false) => query.OrderBy(d => d.DocumentNumber),
            ("TotalAmount", true) => query.OrderByDescending(d => d.TotalAmount),
            ("TotalAmount", false) => query.OrderBy(d => d.TotalAmount),
            (_, false) => query.OrderBy(d => d.CreatedAt),
            _ => query.OrderByDescending(d => d.CreatedAt)
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(d => new DocumentSummaryDto
            {
                Id = d.Id,
                DocumentNumber = d.DocumentNumber,
                OriginalFileName = d.OriginalFileName,
                Status = d.Status,
                Title = d.Title,
                ReferenceNumber = d.ReferenceNumber,
                DocumentDate = d.DocumentDate,
                SenderName = d.SenderName,
                RelatedPersonName = d.RelatedPersonName,
                TotalAmount = d.TotalAmount,
                Currency = d.Currency,
                DepartmentNameFr = d.Department != null ? d.Department.NameFr : (d.SuggestedDepartment != null ? d.SuggestedDepartment.NameFr : null),
                DepartmentNameAr = d.Department != null ? d.Department.NameAr : (d.SuggestedDepartment != null ? d.SuggestedDepartment.NameAr : null),
                DocumentTypeNameFr = d.DocumentType != null ? d.DocumentType.NameFr : null,
                DocumentTypeNameAr = d.DocumentType != null ? d.DocumentType.NameAr : null,
                AiConfidence = d.AIProcessingResult != null ? d.AIProcessingResult.OverallConfidence : null,
                CreatedAt = d.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return new PaginatedList<DocumentSummaryDto>(items, totalCount, request.PageNumber, request.PageSize);
    }

    public async Task<DashboardStatsDto> GetDashboardStatsAsync(CancellationToken cancellationToken = default)
    {
        var baseQuery = _context.Documents.AsNoTracking();

        if (!_currentUser.IsAdministrator)
        {
            var deptId = _currentUser.DepartmentId;
            baseQuery = baseQuery.Where(d => d.DepartmentId == deptId || d.SuggestedDepartmentId == deptId);
        }

        var stats = new DashboardStatsDto
        {
            TotalArchivedDocuments = await baseQuery.CountAsync(d => d.Status == DocumentStatus.Archived, cancellationToken),
            AwaitingValidationCount = await baseQuery.CountAsync(d => d.Status == DocumentStatus.AwaitingValidation, cancellationToken),
            ProcessingCount = await baseQuery.CountAsync(d => d.Status == DocumentStatus.Processing || d.Status == DocumentStatus.Uploaded, cancellationToken),
            ProcessingFailedCount = await baseQuery.CountAsync(d => d.Status == DocumentStatus.ProcessingFailed, cancellationToken),
            DocumentsByDepartment = await baseQuery
                .Where(d => d.Department != null)
                .GroupBy(d => d.Department!.NameFr)
                .Select(g => new { Dept = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Dept, x => x.Count, cancellationToken),
            DocumentsByYear = await baseQuery
                .Where(d => d.DocumentDate != null)
                .GroupBy(d => d.DocumentDate!.Value.Year)
                .Select(g => new { Year = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Year, x => x.Count, cancellationToken),
            DocumentsByType = await baseQuery
                .Where(d => d.DocumentType != null)
                .GroupBy(d => d.DocumentType!.NameFr)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Type, x => x.Count, cancellationToken),
            RecentUploads = await baseQuery
                .OrderByDescending(d => d.CreatedAt)
                .Take(10)
                .Select(d => new DocumentSummaryDto
                {
                    Id = d.Id,
                    DocumentNumber = d.DocumentNumber,
                    OriginalFileName = d.OriginalFileName,
                    Status = d.Status,
                    Title = d.Title,
                    DepartmentNameFr = d.Department != null ? d.Department.NameFr : null,
                    DepartmentNameAr = d.Department != null ? d.Department.NameAr : null,
                    DocumentTypeNameFr = d.DocumentType != null ? d.DocumentType.NameFr : null,
                    AiConfidence = d.AIProcessingResult != null ? d.AIProcessingResult.OverallConfidence : null,
                    CreatedAt = d.CreatedAt
                })
                .ToListAsync(cancellationToken)
        };

        return stats;
    }
}
