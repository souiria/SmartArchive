using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartArchive.Application;

namespace SmartArchive.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSmartArchiveInfrastructure(this IServiceCollection services, IConfiguration configuration, Microsoft.AspNetCore.Hosting.IWebHostEnvironment env)
    {
        var sqlServer = configuration.GetConnectionString("SqlServer");
        var sqlite = configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<ArchiveDbContext>(options =>
        {
            if (!string.IsNullOrWhiteSpace(sqlServer)) options.UseSqlServer(sqlServer);
            else options.UseSqlite(sqlite ?? $"Data Source={Path.Combine(env.ContentRootPath, "App_Data", "smartarchive.db")}");
        });
        services.AddSingleton<IArchiveSettingsService, ArchiveSettingsService>();
        services.AddScoped<IDocumentRepository, EfDocumentRepository>();
        services.AddScoped<IFileStorageService, FileStorageService>();
        services.AddScoped<IDocumentIntelligenceService, DocumentIntelligenceService>();
        services.AddScoped<IDocumentWorkflow, DocumentWorkflow>();
        return services;
    }
}
