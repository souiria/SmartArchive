namespace SmartArchive.Application.Common.Interfaces;

public interface IBlobStorageService
{
    Task<string> UploadAsync(string containerName, string blobPath, Stream content, string contentType, CancellationToken cancellationToken = default);
    Task<Stream> DownloadAsync(string containerName, string blobPath, CancellationToken cancellationToken = default);
    Task MoveAsync(string sourceContainer, string sourcePath, string destinationContainer, string destinationPath, CancellationToken cancellationToken = default);
    Task DeleteAsync(string containerName, string blobPath, CancellationToken cancellationToken = default);
    Task<Uri> GenerateReadSasUriAsync(string containerName, string blobPath, TimeSpan expiry, CancellationToken cancellationToken = default);
}
