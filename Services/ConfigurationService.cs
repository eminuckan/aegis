using Microsoft.Extensions.Configuration;
using SyncPermissions.Models;
using System.Text.Json;

namespace SyncPermissions.Services;

public interface IConfigurationService
{
    AppConfig GetConfiguration(string? configFile = null);
    Task<string> ResolveScanPathAsync(ScanOptions options, AppConfig config);
    ScanOptions MergeWithConfiguration(ScanOptions options, AppConfig config);
    Task UpdateConfigurationAsync(AppConfig config, string? configFile = null);
}

public class ConfigurationService : IConfigurationService
{
    public AppConfig GetConfiguration(string? configFile = null)
    {
        var configPath = configFile ?? "appsettings.json";
        
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile(configPath, optional: true, reloadOnChange: false)
            .Build();

        var appConfig = new AppConfig();
        config.Bind(appConfig);

        return appConfig;
    }

    public async Task UpdateConfigurationAsync(AppConfig config, string? configFile = null)
    {
        var configPath = configFile ?? "appsettings.json";
        
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        
        var json = JsonSerializer.Serialize(config, options);
        await File.WriteAllTextAsync(configPath, json);
    }

    public async Task<string> ResolveScanPathAsync(ScanOptions options, AppConfig config)
    {
        // Priority: CLI argument > config file > interactive prompt
        
        // 1. Check CLI argument
        if (!string.IsNullOrEmpty(options.ScanPath))
        {
            return ResolvePath(options.ScanPath);
        }

        // 2. Check config file
        if (!string.IsNullOrEmpty(config.SyncPermissions.DefaultScanPath))
        {
            var resolvedPath = ResolvePath(config.SyncPermissions.DefaultScanPath);
            Console.WriteLine($"Using default scan path from configuration: {config.SyncPermissions.DefaultScanPath}");
            Console.WriteLine($"Resolved to: {resolvedPath}");
            return resolvedPath;
        }

        // 3. Interactive prompt
        return await PromptForScanPathAsync();
    }

    public ScanOptions MergeWithConfiguration(ScanOptions options, AppConfig config)
    {
        var syncConfig = config.SyncPermissions;

        // Apply config defaults only if CLI options are not explicitly set
        if (!options.Verbose && syncConfig.Verbose)
        {
            options.Verbose = syncConfig.Verbose;
        }

        if (!options.MissingOnly && syncConfig.MissingOnly)
        {
            options.MissingOnly = syncConfig.MissingOnly;
        }

        if (!options.AutoGenerate && syncConfig.AutoGenerate)
        {
            options.AutoGenerate = syncConfig.AutoGenerate;
        }

        if (!options.AcceptAllSuggestedPermissions && syncConfig.AcceptAllSuggestedPermissions)
        {
            options.AcceptAllSuggestedPermissions = syncConfig.AcceptAllSuggestedPermissions;
        }

        if (string.IsNullOrEmpty(options.OutputFile) && !string.IsNullOrEmpty(syncConfig.DefaultOutputPath))
        {
            options.OutputFile = syncConfig.DefaultOutputPath;
        }

        return options;
    }

    private string ResolvePath(string path)
    {
        try
        {
            // Convert relative path to absolute path
            var absolutePath = Path.GetFullPath(path);
            
            // Normalize path separators
            return absolutePath.Replace('\\', Path.DirectorySeparatorChar)
                              .Replace('/', Path.DirectorySeparatorChar);
        }
        catch (Exception ex)
        {
            throw new DirectoryNotFoundException($"Could not resolve path '{path}': {ex.Message}");
        }
    }

    private async Task<string> PromptForScanPathAsync()
    {
        Console.WriteLine();
        Console.WriteLine("No scan path provided. Please enter the path to scan for projects:");
        Console.WriteLine("Examples:");
        Console.WriteLine("  ./src/Services");
        Console.WriteLine("  ../../src/Services");
        Console.WriteLine("  C:\\MyProject\\src\\Services");
        Console.WriteLine();

        string? path = null;
        while (string.IsNullOrWhiteSpace(path))
        {
            Console.Write("Scan path: ");
            path = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(path))
            {
                Console.WriteLine("Path cannot be empty. Please try again.");
                continue;
            }

            try
            {
                var resolvedPath = ResolvePath(path);
                
                if (!Directory.Exists(resolvedPath))
                {
                    Console.WriteLine($"Directory '{resolvedPath}' does not exist. Please enter a valid directory path.");
                    path = null;
                    continue;
                }

                var projectFiles = Directory.GetFiles(resolvedPath, "*.csproj", SearchOption.AllDirectories);
                if (projectFiles.Length == 0)
                {
                    Console.WriteLine($"No .csproj files found in '{resolvedPath}'. Please enter a path containing .NET projects.");
                    path = null;
                    continue;
                }

                Console.WriteLine($"Found {projectFiles.Length} project(s). Proceeding with scan...");
                return resolvedPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Invalid path '{path}': {ex.Message}");
                path = null;
            }
        }

        return path; // This should never be reached, but keeps compiler happy
    }
} 