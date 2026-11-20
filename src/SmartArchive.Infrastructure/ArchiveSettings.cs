using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using SmartArchive.Application;
using SmartArchive.Domain;

namespace SmartArchive.Infrastructure;

public sealed class ArchiveSettingsService(IConfiguration configuration, IWebHostEnvironment environment) : IArchiveSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _settingsPath = Path.Combine(environment.ContentRootPath, "App_Data", "smartarchive-settings.json");

    public async Task<SmartArchiveSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(_settingsPath))
            {
                await using var input = File.OpenRead(_settingsPath);
                var saved = await JsonSerializer.DeserializeAsync<SmartArchiveSettings>(input, JsonOptions, cancellationToken);
                if (saved is not null) return Validate(saved);
            }
            return Validate(Defaults());
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(SmartArchiveSettings settings, CancellationToken cancellationToken = default)
    {
        var valid = Validate(settings);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = _settingsPath + ".tmp";
            await using (var output = File.Create(temporaryPath))
                await JsonSerializer.SerializeAsync(output, valid, JsonOptions, cancellationToken);
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally { _gate.Release(); }
    }

    private SmartArchiveSettings Defaults() => new(
        configuration["SmartArchive:User:DisplayName"] ?? "Abderrahim Souirite",
        configuration["SmartArchive:Storage:LocalFolder"] ?? "documents",
        configuration.GetValue("SmartArchive:Compression:Enabled", false));

    private static SmartArchiveSettings Validate(SmartArchiveSettings settings)
    {
        var name = (settings.DisplayName ?? "").Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl))
            throw new ArgumentException("Enter a display name between 1 and 80 characters.", nameof(settings));

        var folder = (settings.LocalStorageFolder ?? "").Trim().Replace('\\', '/');
        if (folder.Length is < 1 or > 128 || !Regex.IsMatch(folder, @"^[A-Za-z0-9_-]+(?:/[A-Za-z0-9_-]+)*$"))
            throw new ArgumentException("Use a relative folder such as 'documents' or 'archive/pdfs'. It must stay inside App_Data.", nameof(settings));

        return new SmartArchiveSettings(name, folder, settings.CompressPdfs);
    }
}
