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
    public async Task Compression_is_lossless_and_stores_only_the_smaller_representation()
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

            using var input = new MemoryStream(original);
            var reference = await storage.SaveAsync(id, "test.pdf", input);

            Assert.StartsWith("local-gzip:", reference, StringComparison.Ordinal);
            var storedPath = reference["local-gzip:".Length..];
            Assert.True(new FileInfo(storedPath).Length < original.Length);
            Assert.True(File.Exists(Path.Combine(contentRoot, "App_Data", "archive", "pdfs", $"{id:N}.pdf.gz")));
            Assert.False(File.Exists(Path.Combine(contentRoot, "App_Data", "archive", "pdfs", $"{id:N}.pdf")));

            await using var restored = await storage.OpenAsync(reference);
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
