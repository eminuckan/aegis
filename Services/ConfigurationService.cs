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
    private readonly IFolderBrowserService _folderBrowser;

    public ConfigurationService(IFolderBrowserService folderBrowser)
    {
        _folderBrowser = folderBrowser;
    }

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
        var selectedPath = await _folderBrowser.SelectFolderAsync(
            Directory.GetCurrentDirectory(), 
            "Select folder to scan for .NET projects:");

        if (selectedPath == null)
        {
            throw new OperationCanceledException("Folder selection was cancelled.");
        }

        var resolvedPath = ResolvePath(selectedPath);

        if (!Directory.Exists(resolvedPath))
        {
            throw new DirectoryNotFoundException($"Selected directory does not exist: {resolvedPath}");
        }

        var projectFiles = Directory.GetFiles(resolvedPath, "*.csproj", SearchOption.AllDirectories);
        if (projectFiles.Length == 0)
        {
            throw new InvalidOperationException($"No .csproj files found in '{resolvedPath}'. Please select a folder containing .NET projects.");
        }

        return resolvedPath;
    }
} 