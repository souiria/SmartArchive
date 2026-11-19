using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartArchive.Application.Common.Interfaces;
using SmartArchive.Domain.Entities;
using SmartArchive.Infrastructure.AI;
using SmartArchive.Infrastructure.BackgroundJobs;
using SmartArchive.Infrastructure.Identity;
using SmartArchive.Infrastructure.Persistence;
using SmartArchive.Infrastructure.Persistence.Interceptors;
using SmartArchive.Infrastructure.Storage;

namespace SmartArchive.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<AuditableEntityInterceptor>();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            var interceptor = sp.GetRequiredService<AuditableEntityInterceptor>();
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                sqlOptions.EnableRetryOnFailure(maxRetryCount: 5);
            })
            .AddInterceptors(interceptor);
        });

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireUppercase = true;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<AzureBlobStorageOptions>(configuration.GetSection(AzureBlobStorageOptions.SectionName));
        services.Configure<AzureDocumentIntelligenceOptions>(configuration.GetSection(AzureDocumentIntelligenceOptions.SectionName));

        services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();
        services.AddScoped<IDocumentAiService, AzureDocumentIntelligenceService>();

        services.AddSingleton<IBackgroundTaskQueue, DocumentProcessingQueue>();
        services.AddHostedService<DocumentAiWorker>();

        return services;
    }
}
