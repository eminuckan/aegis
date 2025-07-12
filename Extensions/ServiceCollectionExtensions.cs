using Microsoft.Extensions.DependencyInjection;
using SyncPermissions.Services;

namespace SyncPermissions.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection RegisterSyncPermissionsServices(this IServiceCollection services)
    {
        // Register core services
        services.AddScoped<IConfigurationService, ConfigurationService>();
        services.AddScoped<IProjectScanner, ProjectScanner>();
        services.AddScoped<IEndpointDiscoverer, EndpointDiscoverer>();
        services.AddScoped<IPermissionGenerator, PermissionGenerator>();
        services.AddScoped<IPermissionScanService, PermissionScanService>();
        services.AddScoped<IOutputService, OutputService>();
        services.AddScoped<ICSharpGeneratorService, CSharpGeneratorService>();
        services.AddSingleton<IInteractiveMenuService, InteractiveMenuService>();

        return services;
    }
} 