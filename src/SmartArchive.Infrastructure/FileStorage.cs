using System.IO.Compression;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using SmartArchive.Application;

namespace SmartArchive.Infrastructure;

public sealed class FileStorageService(IConfiguration configuration, IWebHostEnvironment env, IArchiveSettingsService settings) : IFileStorageService
{
    public async Task<string> SaveAsync(Guid id, string fileName, Stream stream, CancellationToken ct = default)
    {
        using var originalBuffer = new MemoryStream();
        await stream.CopyToAsync(originalBuffer, ct);
        var original = originalBuffer.ToArray();
        var options = await settings.GetAsync(ct);
        var compressed = options.CompressPdfs ? Compress(original) : Array.Empty<byte>();
        var useCompression = compressed.Length > 0 && compressed.Length < original.Length;
        var connection = configuration["AzureBlobStorage:ConnectionString"];

        if (!string.IsNullOrWhiteSpace(connection))
        {
            var container = new BlobContainerClient(connection, configuration["AzureBlobStorage:Container"] ?? "smartarchive");
            await container.CreateIfNotExistsAsync(cancellationToken: ct);
            if (useCompression)
            {
                var compressedName = $"{id:N}.pdf.gz";
                await container.GetBlobClient(compressedName).UploadAsync(BinaryData.FromBytes(compressed), overwrite: false, cancellationToken: ct);
                return $"azure-gzip:{compressedName}";
            }

            var blob = container.GetBlobClient($"{id:N}.pdf");
            await blob.UploadAsync(BinaryData.FromBytes(original), overwrite: false, cancellationToken: ct);
            return $"azure:{blob.Name}";
        }

        var appDataRoot = Path.Combine(env.ContentRootPath, "App_Data");
        var root = Path.GetFullPath(Path.Combine(appDataRoot, options.LocalStorageFolder.Replace('/', Path.DirectorySeparatorChar)));
        Directory.CreateDirectory(root);
        if (useCompression)
        {
            var compressedPath = Path.Combine(root, $"{id:N}.pdf.gz");
            await File.WriteAllBytesAsync(compressedPath, compressed, ct);
            return $"local-gzip:{compressedPath}";
        }

        var path = Path.Combine(root, $"{id:N}.pdf");
        await File.WriteAllBytesAsync(path, original, ct);
        return $"local:{path}";
    }

    public async Task<Stream?> OpenAsync(string reference, CancellationToken ct = default)
    {
        if (reference.StartsWith("azure-gzip:", StringComparison.Ordinal))
        {
            var connection = configuration["AzureBlobStorage:ConnectionString"];
            if (string.IsNullOrWhiteSpace(connection)) return null;
            var container = new BlobContainerClient(connection, configuration["AzureBlobStorage:Container"] ?? "smartarchive");
            var response = await container.GetBlobClient(reference[11..]).DownloadStreamingAsync(cancellationToken: ct);
            return new GZipStream(response.Value.Content, CompressionMode.Decompress);
        }

        if (reference.StartsWith("azure:", StringComparison.Ordinal))
        {
            var connection = configuration["AzureBlobStorage:ConnectionString"];
            if (string.IsNullOrWhiteSpace(connection)) return null;
            var container = new BlobContainerClient(connection, configuration["AzureBlobStorage:Container"] ?? "smartarchive");
            var response = await container.GetBlobClient(reference[6..]).DownloadStreamingAsync(cancellationToken: ct);
            return response.Value.Content;
        }

        var compressedLocal = reference.StartsWith("local-gzip:", StringComparison.Ordinal);
        if (!compressedLocal && !reference.StartsWith("local:", StringComparison.Ordinal)) return null;
        var path = Path.GetFullPath(reference[(compressedLocal ? 11 : 6)..]);
        if (!IsWithinAppData(path) || !File.Exists(path)) return null;
        var file = File.OpenRead(path);
        return compressedLocal ? new GZipStream(file, CompressionMode.Decompress) : file;
    }

    private bool IsWithinAppData(string path)
    {
        var root = Path.GetFullPath(Path.Combine(env.ContentRootPath, "App_Data")) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.StartsWith(root, comparison);
    }

    private static byte[] Compress(byte[] original)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(original);
        return output.ToArray();
    }
}
