using Microsoft.Extensions.DependencyInjection;
using SmartArchive.Application.Services;

namespace SmartArchive.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<DocumentWorkflowService>();
        return services;
    }
}
