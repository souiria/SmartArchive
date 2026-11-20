using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using SmartArchive.Application;
using SmartArchive.Infrastructure;
using System.Text;

namespace SmartArchive.Tests;

public sealed class FileStorageTests
{
    [Fact]
    public async Task Compression_is_lossless_and_pdf_can_be_moved_to_document_date_department_partition()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"smartarchive-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contentRoot);
        try
        {
            var environment = new TestWebHostEnvironment(contentRoot);
            var storage = new FileStorageService(
                new ConfigurationBuilder().Build(),
                environment,
                new FixedSettingsService(new SmartArchiveSettings("Test User", "archive/pdfs", true)));
            var original = Encoding.ASCII.GetBytes("%PDF-1.7\n" + new string('A', 40_000) + "\n%%EOF");
            var id = Guid.NewGuid();

            var uploadedAt = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
            using var input = new MemoryStream(original);
            var reference = await storage.SaveAsync(id, "test.pdf", input, uploadedAt, "Unclassified");

            Assert.StartsWith("local-gzip:", reference, StringComparison.Ordinal);
            var storedPath = reference["local-gzip:".Length..];
            Assert.True(new FileInfo(storedPath).Length < original.Length);
            Assert.True(File.Exists(Path.Combine(contentRoot, "App_Data", "archive", "pdfs", "2026", "11", "20", "Unclassified", $"{id:N}.pdf.gz")));
            Assert.False(File.Exists(Path.Combine(contentRoot, "App_Data", "archive", "pdfs", "2026", "11", "20", "Unclassified", $"{id:N}.pdf")));

            var targetReference = await storage.MoveToArchivePathAsync(id, "test.pdf", reference, uploadedAt, "Quality", new DateOnly(2026, 11, 19));
            Assert.StartsWith("local-gzip:", targetReference, StringComparison.Ordinal);
            Assert.Contains(Path.Combine("2026", "11", "19", "Quality", $"{id:N}.pdf.gz"), targetReference, StringComparison.Ordinal);
            Assert.True(File.Exists(storedPath));
            await storage.DeleteAsync(reference);
            Assert.False(File.Exists(storedPath));

            await using var restored = await storage.OpenAsync(targetReference);
            Assert.NotNull(restored);
            using var output = new MemoryStream();
            await restored.CopyToAsync(output);
            Assert.Equal(original, output.ToArray());
        }
        finally
        {
            if (Directory.Exists(contentRoot)) Directory.Delete(contentRoot, recursive: true);
        }
    }

    private sealed class FixedSettingsService(SmartArchiveSettings settings) : IArchiveSettingsService
    {
        public Task<SmartArchiveSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings);
        public Task SaveAsync(SmartArchiveSettings value, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestWebHostEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "SmartArchive.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
