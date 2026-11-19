using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using SmartArchive.Application;

namespace SmartArchive.Infrastructure;

public sealed class FileStorageService(IConfiguration configuration, IWebHostEnvironment env) : IFileStorageService
{
    public async Task<string> SaveAsync(Guid id, string fileName, Stream stream, CancellationToken ct = default)
    {
        var connection = configuration["AzureBlobStorage:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(connection))
        {
            var container = new BlobContainerClient(connection, configuration["AzureBlobStorage:Container"] ?? "smartarchive");
            await container.CreateIfNotExistsAsync(cancellationToken: ct);
            var blob = container.GetBlobClient($"{id:N}.pdf");
            await blob.UploadAsync(stream, overwrite: false, cancellationToken: ct);
            return $"azure:{blob.Name}";
        }
        var root = Path.Combine(env.ContentRootPath, "App_Data", "documents"); Directory.CreateDirectory(root);
        var path = Path.Combine(root, $"{id:N}.pdf");
        await using var output = File.Create(path); await stream.CopyToAsync(output, ct);
        return $"local:{path}";
    }
    public async Task<Stream?> OpenAsync(string reference, CancellationToken ct = default)
    {
        if (reference.StartsWith("azure:", StringComparison.Ordinal))
        {
            var connection = configuration["AzureBlobStorage:ConnectionString"]; if (string.IsNullOrWhiteSpace(connection)) return null;
            var container = new BlobContainerClient(connection, configuration["AzureBlobStorage:Container"] ?? "smartarchive");
            var response = await container.GetBlobClient(reference[6..]).DownloadStreamingAsync(cancellationToken: ct);
            return response.Value.Content;
        }
        if (!reference.StartsWith("local:", StringComparison.Ordinal)) return null;
        var path = Path.GetFullPath(reference[6..]);
        var root = Path.GetFullPath(Path.Combine(env.ContentRootPath, "App_Data", "documents")) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.Ordinal) || !File.Exists(path)) return null;
        return File.OpenRead(path);
    }
}
