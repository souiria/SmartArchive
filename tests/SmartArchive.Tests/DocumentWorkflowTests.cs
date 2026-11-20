using Microsoft.Extensions.Logging.Abstractions;
using SmartArchive.Application;
using SmartArchive.Domain;
using SmartArchive.Infrastructure;

namespace SmartArchive.Tests;

public sealed class DocumentWorkflowTests
{
    [Theory]
    [InlineData(true, DocumentStatus.Archived)]
    [InlineData(false, DocumentStatus.Rejected)]
    public async Task Review_saves_date_department_and_action(bool approve, DocumentStatus expectedStatus)
    {
        var date = new DateOnly(2026, 11, 19);
        var document = new ArchiveDocument
        {
            Id = Guid.NewGuid(),
            FileName = "invoice.pdf",
            Title = "Invoice",
            BlobReference = "local:previous-copy",
            DepartmentName = "Production",
            DepartmentId = 3,
            Category = "Invoice",
            ReferenceNumber = "old-reference",
            UploadedAt = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero),
            Status = DocumentStatus.PendingValidation
        };
        var repository = new SingleDocumentRepository(document);
        var storage = new RecordingFileStorage();
        var workflow = new DocumentWorkflow(repository, storage, new UnusedIntelligenceService(), NullLogger<DocumentWorkflow>.Instance);

        var storageActionSucceeded = await workflow.ReviewAsync(
            document.Id,
            "Reviewed invoice",
            "Finance",
            "Accounts payable",
            date,
            " INV-204 ",
            "Reviewed",
            "Archive operator",
            approve);

        Assert.True(storageActionSucceeded);
        Assert.Equal(expectedStatus, document.Status);
        Assert.Equal(date, document.DocumentDate);
        Assert.Equal("Finance", document.DepartmentName);
        Assert.Equal("INV-204", document.ReferenceNumber);
        Assert.Equal("Archive operator", document.ValidatedBy);
        Assert.NotNull(document.ValidatedAt);
        Assert.Equal(1, repository.SaveCount);
        Assert.Contains(approve ? "ApprovedAndArchived" : "RejectedAndPdfRemovalRequested", document.AuditJson, StringComparison.Ordinal);
        Assert.Equal("local:previous-copy", storage.DeletedReferences.Single());
        if (approve)
        {
            Assert.Equal("Finance", storage.MovedDepartment);
            Assert.Equal(date, storage.MovedDocumentDate);
            Assert.Contains("2026/11/19/Finance", document.BlobReference, StringComparison.Ordinal);
        }
        else
        {
            Assert.Null(storage.MovedDepartment);
            Assert.Equal("local:previous-copy", document.BlobReference);
        }
    }

    [Fact]
    public async Task Approve_saves_and_archives_document_when_storage_relocation_fails()
    {
        var document = new ArchiveDocument { Id = Guid.NewGuid(), BlobReference = "local:previous-copy", Status = DocumentStatus.PendingValidation };
        var repository = new SingleDocumentRepository(document);
        var storage = new RecordingFileStorage { ThrowOnMove = true };
        var workflow = new DocumentWorkflow(repository, storage, new UnusedIntelligenceService(), NullLogger<DocumentWorkflow>.Instance);

        var storageActionSucceeded = await workflow.ReviewAsync(document.Id, "Approved", "Finance", "Invoice", null, "", "", "Archive operator", approve: true);

        Assert.False(storageActionSucceeded);
        Assert.Equal(DocumentStatus.Archived, document.Status);
        Assert.Equal(1, repository.SaveCount);
        Assert.Contains("ApprovedAndArchived", document.AuditJson, StringComparison.Ordinal);
        Assert.Contains("existing file location retained", document.Warnings, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(storage.DeletedReferences);
    }

    [Fact]
    public async Task Delete_removes_pdf_and_retains_audit_tombstone()
    {
        var document = new ArchiveDocument { Id = Guid.NewGuid(), BlobReference = "local:previous-copy", Status = DocumentStatus.PendingValidation };
        var repository = new SingleDocumentRepository(document);
        var storage = new RecordingFileStorage();
        var workflow = new DocumentWorkflow(repository, storage, new UnusedIntelligenceService(), NullLogger<DocumentWorkflow>.Instance);

        var pdfRemoved = await workflow.DeleteAsync(document.Id, "Archive operator", "Duplicate upload.");

        Assert.True(pdfRemoved);
        Assert.Equal(DocumentStatus.Deleted, document.Status);
        Assert.Equal(1, repository.SaveCount);
        Assert.Contains("Deleted", document.AuditJson, StringComparison.Ordinal);
        Assert.Contains("Duplicate upload.", document.AuditJson, StringComparison.Ordinal);
        Assert.Equal("Archive operator", document.ValidatedBy);
        Assert.Equal("local:previous-copy", storage.DeletedReferences.Single());
    }

    [Fact]
    public async Task Reject_records_outcome_and_leaves_queue_even_when_pdf_deletion_fails()
    {
        var document = new ArchiveDocument { Id = Guid.NewGuid(), BlobReference = "local:previous-copy", Status = DocumentStatus.PendingValidation };
        var repository = new SingleDocumentRepository(document);
        var storage = new RecordingFileStorage { ThrowOnDelete = true };
        var workflow = new DocumentWorkflow(repository, storage, new UnusedIntelligenceService(), NullLogger<DocumentWorkflow>.Instance);

        var storageActionSucceeded = await workflow.ReviewAsync(document.Id, "Rejected", "Finance", "Invoice", null, "", "Incorrect document.", "Archive operator", approve: false);

        Assert.False(storageActionSucceeded);
        Assert.Equal(DocumentStatus.Rejected, document.Status);
        Assert.Equal(2, repository.SaveCount);
        Assert.Contains("RejectedAndPdfRemovalRequested", document.AuditJson, StringComparison.Ordinal);
        Assert.Contains("PdfRemovalFailed", document.AuditJson, StringComparison.Ordinal);
        Assert.Contains("manual cleanup is required", document.Warnings, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SingleDocumentRepository(ArchiveDocument document) : IDocumentRepository
    {
        public int SaveCount { get; private set; }
        public Task<IReadOnlyList<ArchiveDocument>> ListAsync(string? search = null, DocumentStatus? status = null, int? departmentId = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ArchiveDocument>>([document]);
        public Task<ArchiveDocument?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(id == document.Id ? document : null);
        public Task AddAsync(ArchiveDocument value, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveAsync(ArchiveDocument value, CancellationToken ct = default) { SaveCount++; return Task.CompletedTask; }
        public Task<IReadOnlyList<Department>> DepartmentsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Department>>([new Department { Id = 3, Name = "Production" }, new Department { Id = 6, Name = "Finance" }]);
        public Task<object> StatisticsAsync(CancellationToken ct = default) => Task.FromResult<object>(new { });
    }

    private sealed class RecordingFileStorage : IFileStorageService
    {
        public string? MovedDepartment { get; private set; }
        public DateOnly? MovedDocumentDate { get; private set; }
        public List<string> DeletedReferences { get; } = [];
        public bool ThrowOnMove { get; init; }
        public bool ThrowOnDelete { get; init; }

        public Task<string> SaveAsync(Guid id, string fileName, Stream stream, CancellationToken ct = default) => Task.FromResult("local:new-copy");
        public Task<string> SaveAsync(Guid id, string fileName, Stream stream, DateTimeOffset uploadedAt, string department, DateOnly? documentDate = null, CancellationToken ct = default) => Task.FromResult("local:new-copy");
        public Task<string> MoveToArchivePathAsync(Guid id, string fileName, string reference, DateTimeOffset uploadedAt, string department, DateOnly? documentDate = null, CancellationToken ct = default)
        {
            if (ThrowOnMove) throw new IOException("Simulated storage relocation failure.");
            MovedDepartment = department;
            MovedDocumentDate = documentDate;
            var dateFolder = (documentDate ?? DateOnly.FromDateTime(uploadedAt.UtcDateTime)).ToString("yyyy/MM/dd");
            return Task.FromResult($"local:archive/{dateFolder}/{department}/{id:N}.pdf");
        }
        public Task DeleteAsync(string reference, CancellationToken ct = default)
        {
            DeletedReferences.Add(reference);
            if (ThrowOnDelete) throw new IOException("Simulated PDF deletion failure.");
            return Task.CompletedTask;
        }
        public Task<Stream?> OpenAsync(string reference, CancellationToken ct = default) => Task.FromResult<Stream?>(null);
    }

    private sealed class UnusedIntelligenceService : IDocumentIntelligenceService
    {
        public Task<AiSuggestion> AnalyzeAsync(string fileName, Stream pdf, string? suppliedText = null, CancellationToken ct = default) =>
            throw new NotSupportedException("Analysis is not used in a review test.");
    }
}
