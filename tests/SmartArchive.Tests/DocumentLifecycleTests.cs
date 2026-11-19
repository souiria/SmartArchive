using SmartArchive.Domain;
using Xunit;

namespace SmartArchive.Tests;

public sealed class DocumentLifecycleTests
{
    [Fact]
    public void Uploaded_document_must_be_processed_before_validation()
    {
        var document = new ArchiveDocument { Status = DocumentStatus.Uploaded };
        DocumentLifecycle.Move(document, DocumentStatus.Processing);
        DocumentLifecycle.Move(document, DocumentStatus.PendingValidation);
        Assert.Equal(DocumentStatus.PendingValidation, document.Status);
    }

    [Fact]
    public void Direct_archive_from_upload_is_forbidden()
    {
        var document = new ArchiveDocument { Status = DocumentStatus.Uploaded };
        Assert.Throws<InvalidOperationException>(() => DocumentLifecycle.Move(document, DocumentStatus.Archived));
    }

    [Fact]
    public void Archived_document_cannot_be_rejected_by_a_second_review()
    {
        var document = new ArchiveDocument { Status = DocumentStatus.Archived };
        Assert.Throws<InvalidOperationException>(() => DocumentLifecycle.Move(document, DocumentStatus.Rejected));
    }

    [Fact]
    public void Analysis_failure_can_be_retried()
    {
        var document = new ArchiveDocument { Status = DocumentStatus.ProcessingFailed };
        DocumentLifecycle.Move(document, DocumentStatus.Processing);
        Assert.Equal(DocumentStatus.Processing, document.Status);
    }
}
