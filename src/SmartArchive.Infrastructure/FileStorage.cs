using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using SmartArchive.Application;

namespace SmartArchive.Infrastructure;

public sealed class FileStorageService(IConfiguration configuration, IWebHostEnvironment env, IArchiveSettingsService settings) : IFileStorageService
{
    public Task<string> SaveAsync(Guid id, string fileName, Stream stream, CancellationToken ct = default) =>
        SaveAsync(id, fileName, stream, DateTimeOffset.UtcNow, "Unclassified", null, ct);

    public async Task<string> SaveAsync(Guid id, string fileName, Stream stream, DateTimeOffset uploadedAt, string department, DateOnly? documentDate = null, CancellationToken ct = default)
    {
        using var originalBuffer = new MemoryStream();
        await stream.CopyToAsync(originalBuffer, ct);
        var original = originalBuffer.ToArray();
        var options = await settings.GetAsync(ct);
        var partition = BuildPartition(uploadedAt, department, documentDate);
        var compressed = options.CompressPdfs ? Compress(original) : Array.Empty<byte>();
        var useCompression = compressed.Length > 0 && compressed.Length < original.Length;
        var connection = configuration["AzureBlobStorage:ConnectionString"];

        if (!string.IsNullOrWhiteSpace(connection))
        {
            var container = new BlobContainerClient(connection, configuration["AzureBlobStorage:Container"] ?? "smartarchive");
            await container.CreateIfNotExistsAsync(cancellationToken: ct);
            var blobName = $"{options.LocalStorageFolder}/{partition}/{id:N}.pdf{(useCompression ? ".gz" : "")}";
            await container.GetBlobClient(blobName).UploadAsync(BinaryData.FromBytes(useCompression ? compressed : original), overwrite: false, cancellationToken: ct);
            return useCompression ? $"azure-gzip:{blobName}" : $"azure:{blobName}";
        }

        var root = GetLocalPartitionDirectory(options.LocalStorageFolder, partition);
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

    public async Task<string> MoveToArchivePathAsync(Guid id, string fileName, string reference, DateTimeOffset uploadedAt, string department, DateOnly? documentDate = null, CancellationToken ct = default)
    {
        var options = await settings.GetAsync(ct);
        var partition = BuildPartition(uploadedAt, department, documentDate);
        if (IsAlreadyInPartition(id, reference, options.LocalStorageFolder, partition)) return reference;

        await using var input = await OpenAsync(reference, ct) ?? throw new IOException("Stored PDF not found while changing its archive path.");
        return await SaveAsync(id, fileName, input, uploadedAt, department, documentDate, ct);
    }

    public async Task DeleteAsync(string reference, CancellationToken ct = default)
    {
        if (reference.StartsWith("azure-gzip:", StringComparison.Ordinal) || reference.StartsWith("azure:", StringComparison.Ordinal))
        {
            var connection = configuration["AzureBlobStorage:ConnectionString"];
            if (string.IsNullOrWhiteSpace(connection)) return;
            var blobName = reference.StartsWith("azure-gzip:", StringComparison.Ordinal) ? reference[11..] : reference[6..];
            var container = new BlobContainerClient(connection, configuration["AzureBlobStorage:Container"] ?? "smartarchive");
            await container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: ct);
            return;
        }

        var compressedLocal = reference.StartsWith("local-gzip:", StringComparison.Ordinal);
        if (!compressedLocal && !reference.StartsWith("local:", StringComparison.Ordinal)) return;
        var path = Path.GetFullPath(reference[(compressedLocal ? 11 : 6)..]);
        if (IsWithinAppData(path) && File.Exists(path)) File.Delete(path);
    }

    public async Task<Stream?> OpenAsync(string reference, CancellationToken ct = default)
    {
        if (reference.StartsWith("azure-gzip:", StringComparison.Ordinal) || reference.StartsWith("azure:", StringComparison.Ordinal))
        {
            var connection = configuration["AzureBlobStorage:ConnectionString"];
            if (string.IsNullOrWhiteSpace(connection)) return null;
            var compressed = reference.StartsWith("azure-gzip:", StringComparison.Ordinal);
            var blobName = compressed ? reference[11..] : reference[6..];
            var container = new BlobContainerClient(connection, configuration["AzureBlobStorage:Container"] ?? "smartarchive");
            var response = await container.GetBlobClient(blobName).DownloadStreamingAsync(cancellationToken: ct);
            return compressed ? new GZipStream(response.Value.Content, CompressionMode.Decompress) : response.Value.Content;
        }

        var compressedLocal = reference.StartsWith("local-gzip:", StringComparison.Ordinal);
        if (!compressedLocal && !reference.StartsWith("local:", StringComparison.Ordinal)) return null;
        var path = Path.GetFullPath(reference[(compressedLocal ? 11 : 6)..]);
        if (!IsWithinAppData(path) || !File.Exists(path)) return null;
        var file = File.OpenRead(path);
        return compressedLocal ? new GZipStream(file, CompressionMode.Decompress) : file;
    }

    private string GetLocalPartitionDirectory(string localFolder, string partition)
    {
        var appDataRoot = Path.Combine(env.ContentRootPath, "App_Data");
        var segments = localFolder.Split('/').Concat(partition.Split('/')).ToArray();
        var path = Path.GetFullPath(Path.Combine(new[] { appDataRoot }.Concat(segments).ToArray()));
        if (!IsWithinAppData(path + Path.DirectorySeparatorChar)) throw new InvalidOperationException("The configured storage path must remain inside App_Data.");
        return path;
    }

    private bool IsAlreadyInPartition(Guid id, string reference, string localFolder, string partition)
    {
        var fileName = $"{id:N}.pdf";
        if (reference.StartsWith("local-gzip:", StringComparison.Ordinal) || reference.StartsWith("local:", StringComparison.Ordinal))
        {
            var compressed = reference.StartsWith("local-gzip:", StringComparison.Ordinal);
            var path = Path.GetFullPath(reference[(compressed ? 11 : 6)..]);
            var expectedDirectory = GetLocalPartitionDirectory(localFolder, partition);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return IsWithinAppData(path) && string.Equals(Path.GetDirectoryName(path), expectedDirectory, comparison) && Path.GetFileName(path) == fileName + (compressed ? ".gz" : "");
        }

        var blobName = reference.StartsWith("azure-gzip:", StringComparison.Ordinal)
            ? reference[11..]
            : reference.StartsWith("azure:", StringComparison.Ordinal) ? reference[6..] : "";
        var expectedSuffix = $"/{localFolder}/{partition}/{fileName}";
        if (blobName.EndsWith(expectedSuffix + ".gz", StringComparison.Ordinal)) return true;
        return blobName.EndsWith(expectedSuffix, StringComparison.Ordinal);
    }

    private static string BuildPartition(DateTimeOffset uploadedAt, string department, DateOnly? documentDate)
    {
        var date = documentDate ?? DateOnly.FromDateTime(uploadedAt.UtcDateTime);
        var year = date.Year.ToString("0000", CultureInfo.InvariantCulture);
        var month = date.Month.ToString("00", CultureInfo.InvariantCulture);
        var day = date.Day.ToString("00", CultureInfo.InvariantCulture);
        var normalized = department.Normalize(NormalizationForm.FormD);
        var withoutMarks = new string(normalized.Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark).ToArray());
        var departmentFolder = Regex.Replace(withoutMarks, @"[^A-Za-z0-9_-]+", "-").Trim('-', '_');
        if (string.IsNullOrWhiteSpace(departmentFolder)) departmentFolder = "Unclassified";
        return $"{year}/{month}/{day}/{departmentFolder}";
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
