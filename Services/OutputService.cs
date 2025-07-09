using System.Text.Json;
using SyncPermissions.Models;

namespace SyncPermissions.Services;

public interface IOutputService
{
    Task WriteJsonAsync(PermissionDiscoveryResult result, string? outputFile);
    void WriteConsoleOutput(PermissionDiscoveryResult result, ScanOptions options);
}

public class OutputService : IOutputService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task WriteJsonAsync(PermissionDiscoveryResult result, string? outputFile)
    {
        var json = JsonSerializer.Serialize(result, JsonOptions);

        if (!string.IsNullOrEmpty(outputFile))
        {
            await File.WriteAllTextAsync(outputFile, json);
            Console.WriteLine($"Results written to: {outputFile}");
        }
        else
        {
            Console.WriteLine(json);
        }
    }

    public void WriteConsoleOutput(PermissionDiscoveryResult result, ScanOptions options)
    {
        if (!string.IsNullOrEmpty(options.OutputFile))
        {
            // JSON output mode - minimal console output
            WriteSummaryOnly(result);
            return;
        }

        // Full console output mode
        WriteFullConsoleOutput(result, options);
    }

    private void WriteSummaryOnly(PermissionDiscoveryResult result)
    {
        var summary = result.Summary;
        
        Console.WriteLine();
        Console.WriteLine("=== Scan Summary ===");
        Console.WriteLine($"Total Endpoints: {summary.TotalEndpoints}");
        Console.WriteLine($"  Public: {summary.PublicEndpoints}");
        Console.WriteLine($"  Auth Only: {summary.AuthOnlyEndpoints}");
        Console.WriteLine($"  Needs Permission: {summary.NeedsPermissionEndpoints}");
        Console.WriteLine($"  Already Protected: {summary.AlreadyProtectedEndpoints}");
        Console.WriteLine();
        Console.WriteLine($"Generated Permissions: {summary.GeneratedPermissions}");
        
        if (summary.Warnings.Any())
        {
            Console.WriteLine($"Warnings: {summary.Warnings.Count}");
        }
    }

    private void WriteFullConsoleOutput(PermissionDiscoveryResult result, ScanOptions options)
    {
        Console.WriteLine();
        Console.WriteLine("=== Permission Discovery Results ===");
        Console.WriteLine();

        foreach (var project in result.Projects)
        {
            Console.WriteLine($"Project: {project.Name}");
            Console.WriteLine($"Path: {project.Path}");
            Console.WriteLine();

            if (project.DiscoveredPermissions.Any())
            {
                Console.WriteLine("  Generated Permissions:");
                foreach (var permission in project.DiscoveredPermissions)
                {
                    Console.WriteLine($"    - {permission.Name}");
                    if (options.Verbose)
                    {
                        Console.WriteLine($"      Description: {permission.Description}");
                        Console.WriteLine($"      HTTP Method: {permission.Metadata.HttpMethod}");
                        Console.WriteLine($"      Route: {permission.Metadata.Route}");
                    }
                }
                Console.WriteLine();
            }

            // Note: Endpoint details removed since they're no longer in JSON output
        }

        WriteSummaryOnly(result);
        WriteWarnings(result.Summary.Warnings);
    }

    private void WriteWarnings(List<ScanWarning> warnings)
    {
        if (!warnings.Any()) return;

        Console.WriteLine();
        Console.WriteLine("=== Warnings ===");
        Console.WriteLine();

        foreach (var warning in warnings)
        {
            Console.WriteLine($"⚠️  {warning.Type}: {warning.Endpoint}");
            Console.WriteLine($"   {warning.Message}");
            if (!string.IsNullOrEmpty(warning.Suggestion))
            {
                Console.WriteLine($"   Suggestion: {warning.Suggestion}");
            }
            Console.WriteLine();
        }
    }
} 