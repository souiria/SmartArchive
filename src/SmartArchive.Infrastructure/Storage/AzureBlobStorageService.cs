using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using SmartArchive.Application.Common.Interfaces;

namespace SmartArchive.Infrastructure.Storage;

public sealed class AzureBlobStorageOptions
{
    public const string SectionName = "AzureStorage";
    public string ConnectionString { get; set; } = string.Empty;
    public string StagingContainer { get; set; } = "staging";
    public string ArchiveContainer { get; set; } = "archive";
}

public sealed class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly AsyncRetryPolicy _retryPolicy;

    public AzureBlobStorageService(IOptions<AzureBlobStorageOptions> options)
    {
        _blobServiceClient = new BlobServiceClient(options.Value.ConnectionString);
        _retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
    }

    public async Task<string> UploadAsync(string containerName, string blobPath, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var container = _blobServiceClient.GetBlobContainerClient(containerName);
            await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

            var blobClient = container.GetBlobClient(blobPath);
            content.Position = 0;
            await blobClient.UploadAsync(content, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: cancellationToken);
            return blobClient.Uri.ToString();
        });
    }

    public async Task<Stream> DownloadAsync(string containerName, string blobPath, CancellationToken cancellationToken = default)
    {
        var container = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = container.GetBlobClient(blobPath);
        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return response.Value.Content;
    }

    public async Task MoveAsync(string sourceContainer, string sourcePath, string destinationContainer, string destinationPath, CancellationToken cancellationToken = default)
    {
        await _retryPolicy.ExecuteAsync(async () =>
        {
            var srcContainerClient = _blobServiceClient.GetBlobContainerClient(sourceContainer);
            var dstContainerClient = _blobServiceClient.GetBlobContainerClient(destinationContainer);
            await dstContainerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

            var srcBlob = srcContainerClient.GetBlobClient(sourcePath);
            var dstBlob = dstContainerClient.GetBlobClient(destinationPath);

            var copyOperation = await dstBlob.StartCopyFromUriAsync(srcBlob.Uri, cancellationToken: cancellationToken);
            await copyOperation.WaitForCompletionAsync(cancellationToken);
            await srcBlob.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
        });
    }

    public async Task DeleteAsync(string containerName, string blobPath, CancellationToken cancellationToken = default)
    {
        var container = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = container.GetBlobClient(blobPath);
        await blobClient.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
    }

    public Task<Uri> GenerateReadSasUriAsync(string containerName, string blobPath, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        var container = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = container.GetBlobClient(blobPath);

        if (!blobClient.CanGenerateSasUri)
        {
            return Task.FromResult(blobClient.Uri);
        }

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = containerName,
            BlobName = blobPath,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.Add(expiry)
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        return Task.FromResult(blobClient.GenerateSasUri(sasBuilder));
    }
}
