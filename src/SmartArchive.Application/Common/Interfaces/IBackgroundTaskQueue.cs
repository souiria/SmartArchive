namespace SmartArchive.Application.Common.Interfaces;

public interface IBackgroundTaskQueue
{
    ValueTask EnqueueDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);
    ValueTask<Guid> DequeueDocumentAsync(CancellationToken cancellationToken);
}
