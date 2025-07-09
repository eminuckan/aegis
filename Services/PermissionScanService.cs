using Spectre.Console;
using SyncPermissions.Models;

namespace SyncPermissions.Services;

public interface IPermissionScanService
{
    Task<PermissionDiscoveryResult> ScanAsync(ScanOptions options, ProgressTask? progress = null);
}

public class PermissionScanService : IPermissionScanService
{
    private readonly IProjectScanner _projectScanner;
    private readonly IEndpointDiscoverer _endpointDiscoverer;
    private readonly IPermissionGenerator _permissionGenerator;
    private readonly IConsoleUIService _consoleUIService;

    public PermissionScanService(
        IProjectScanner projectScanner,
        IEndpointDiscoverer endpointDiscoverer,
        IPermissionGenerator permissionGenerator,
        IConsoleUIService consoleUIService)
    {
        _projectScanner = projectScanner;
        _endpointDiscoverer = endpointDiscoverer;
        _permissionGenerator = permissionGenerator;
        _consoleUIService = consoleUIService;
    }

    public async Task<PermissionDiscoveryResult> ScanAsync(ScanOptions options, ProgressTask? progress = null)
    {
        var result = new PermissionDiscoveryResult();
        var mismatchedPermissions = new List<(string Project, EndpointInfo Endpoint)>();
        var allEndpoints = new List<(string Project, EndpointInfo Endpoint)>();
        
        if (string.IsNullOrEmpty(options.ScanPath))
        {
            throw new ArgumentException("Scan path cannot be null or empty.", nameof(options));
        }

        try
        {
            // Initialize PermissionGenerator with default conventions
            var conventions = new ConventionsConfig
            {
                HttpMethodActions = new Dictionary<string, string>
                {
                    { "GET", "Read" },
                    { "POST", "Create" },
                    { "PUT", "Update" },
                    { "PATCH", "Update" },
                    { "DELETE", "Delete" }
                }
                // No more FeatureToResource - using pure auto-inference!
            };
            _permissionGenerator.Initialize(conventions);

            // Step 1: Discover projects (20% of progress)
            progress?.Increment(5);
            var projects = await _projectScanner.DiscoverProjectsAsync(options.ScanPath, progress);
            progress?.Increment(15);
            
            if (!projects.Any())
            {
                result.Summary.Warnings.Add(new ScanWarning
                {
                    Type = "No Projects",
                    Message = $"No .csproj files found in {options.ScanPath}",
                    Suggestion = "Verify the scan path contains valid .NET projects"
                });
                return result;
            }

            // Step 2: Analyze each project (60% of progress)
            var progressPerProject = 60.0 / projects.Count;
            
            foreach (var project in projects)
            {
                try
                {
                    var (projectResult, projectEndpoints) = await AnalyzeProjectAsync(project, options, progress);
                    result.Projects.Add(projectResult);

                    // Collect all endpoints for summary generation
                    allEndpoints.AddRange(projectEndpoints.Select(e => (project.Name, e)));

                    // Collect mismatched permissions for interactive handling
                    var projectMismatches = projectEndpoints
                        .Where(e => e.AuthorizationStatus == EndpointAuthorizationStatus.MismatchedPermission)
                        .Select(e => (project.Name, e))
                        .ToList();
                    
                    mismatchedPermissions.AddRange(projectMismatches);
                    
                    progress?.Increment(progressPerProject);
                }
                catch (Exception ex)
                {
                    result.Summary.Warnings.Add(new ScanWarning
                    {
                        Type = "Project Analysis Error",
                        Endpoint = project.Name,
                        Message = $"Failed to analyze project: {ex.Message}",
                        Suggestion = "Check project structure and dependencies"
                    });
                }
            }

            // Step 3: Generate summary (20% of progress)
            progress?.Increment(10);
            GenerateSummary(result, allEndpoints, options);
            progress?.Increment(10);

            // Step 4: Handle mismatched permissions interactively (after progress bar completes)
            if (mismatchedPermissions.Any())
            {
                if (options.AcceptAllSuggestedPermissions)
                {
                    // Auto-accept all suggestions, just show what was done
                    foreach (var (projectName, endpoint) in mismatchedPermissions)
                    {
                        _consoleUIService.ShowSuccess($"✅ Auto-accepted suggestion for {projectName}/{endpoint.ClassName}: '{endpoint.ExistingPermission}' → '{endpoint.SuggestedPermission}'");
                    }
                }
                else
                {
                    // Show mismatched permissions table and handle interactively
                    _consoleUIService.ShowMismatchedPermissionsTable(mismatchedPermissions);
                    
                    AnsiConsole.WriteLine();
                    var handleInteractively = AnsiConsole.Confirm(
                        $"[yellow]Would you like to review and update these mismatched permissions interactively?[/]", 
                        defaultValue: true);
                    
                    if (handleInteractively)
                    {
                        await HandleMismatchedPermissionsInteractivelyAsync(mismatchedPermissions, options);
                    }
                    else
                    {
                        _consoleUIService.ShowInfo("Skipped interactive permission review. Warnings remain in the report.");
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Scan failed: {ex.Message}", ex);
        }
    }



    private async Task<(ProjectScanResult, List<EndpointInfo>)> AnalyzeProjectAsync(ProjectInfo project, ScanOptions options, ProgressTask? progress = null)
    {
        var projectResult = new ProjectScanResult
        {
            Name = project.Name,
            Path = project.Path
        };

        // Keep endpoints for internal processing (mismatch detection) but don't add to result
        var endpoints = new List<EndpointInfo>();

        try
        {
            // Discover endpoints
            endpoints = await _endpointDiscoverer.DiscoverEndpointsAsync(project, progress);

            // Generate permissions and validate existing ones
            foreach (var endpoint in endpoints)
            {
                await ProcessEndpointAsync(endpoint, project.Name, projectResult, options);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to analyze project {project.Name}: {ex.Message}", ex);
        }

        return (projectResult, endpoints);
    }

    private Task ProcessEndpointAsync(EndpointInfo endpoint, string projectName, ProjectScanResult projectResult, ScanOptions options)
    {
        // For endpoints that need permission generation
        if (endpoint.AuthorizationStatus == EndpointAuthorizationStatus.AuthOnly)
        {
            var permission = _permissionGenerator.GeneratePermission(endpoint, projectName);
            if (permission != null)
            {
                // Only add if not filtering for missing only, or if this is a missing permission
                if (!options.MissingOnly || ShouldIncludePermission(permission, options))
                {
                    projectResult.DiscoveredPermissions.Add(permission);
                    endpoint.SuggestedPermission = permission.Name;
                    endpoint.AuthorizationStatus = EndpointAuthorizationStatus.NeedsPermission;
                }
            }
        }
        // For endpoints that already have permissions, validate them
        else if (endpoint.AuthorizationStatus == EndpointAuthorizationStatus.AlreadyProtected)
        {
            // Validate the existing permission against conventions
            var isValid = _permissionGenerator.ValidatePermissionConvention(endpoint, out var suggestedName);
            
            // If validation failed and we have a suggestion, the endpoint will be marked as MismatchedPermission
            // This will be picked up by the mismatch detection in ScanAsync
        }

        return Task.CompletedTask;
    }

    private bool ShouldIncludePermission(DiscoveredPermission permission, ScanOptions options)
    {
        // Add logic here to check if permission already exists in the target project
        // For now, include all permissions
        return true;
    }

    private void GenerateSummary(PermissionDiscoveryResult result, List<(string Project, EndpointInfo Endpoint)> allEndpoints, ScanOptions options)
    {
        var summary = result.Summary;
        var warnings = new List<ScanWarning>();

        foreach (var (projectName, endpoint) in allEndpoints)
        {
            summary.TotalEndpoints++;

            switch (endpoint.AuthorizationStatus)
            {
                case EndpointAuthorizationStatus.Public:
                    summary.PublicEndpoints++;
                    break;
                case EndpointAuthorizationStatus.AuthOnly:
                    summary.AuthOnlyEndpoints++;
                    break;
                case EndpointAuthorizationStatus.NeedsPermission:
                    summary.NeedsPermissionEndpoints++;
                    break;
                case EndpointAuthorizationStatus.AlreadyProtected:
                    summary.AlreadyProtectedEndpoints++;
                    break;
                case EndpointAuthorizationStatus.MismatchedPermission:
                    summary.AlreadyProtectedEndpoints++; // Still protected, but with warning
                    warnings.Add(new ScanWarning
                    {
                        Type = "MismatchedPermission",
                        Endpoint = $"{projectName}/{endpoint.FilePath}",
                        Message = $"RequirePermission('{endpoint.ExistingPermission}') does not match convention (expected: '{endpoint.SuggestedPermission}')",
                        Suggestion = $"Update to use '{endpoint.SuggestedPermission}' or mark as custom permission"
                    });
                    break;
            }
        }

        // Count generated permissions from all projects
        summary.GeneratedPermissions = result.Projects.Sum(p => p.DiscoveredPermissions.Count);

        summary.Warnings = warnings;
    }

    private async Task HandleMismatchedPermissionsInteractivelyAsync(
        List<(string Project, EndpointInfo Endpoint)> mismatchedPermissions, 
        ScanOptions options)
    {
        _consoleUIService.ShowInfo($"🔍 Reviewing {mismatchedPermissions.Count} mismatched permission(s) interactively...");
        AnsiConsole.WriteLine();

        var toUpdate = new List<(string Project, EndpointInfo Endpoint, string NewPermission)>();
        var toKeep = new List<(string Project, EndpointInfo Endpoint)>();

        foreach (var (projectName, endpoint) in mismatchedPermissions)
        {
            try
            {
                var decision = await _consoleUIService.PromptPermissionCorrectionAsync(endpoint, projectName);
                
                if (decision)
                {
                    // User wants to accept the suggestion
                    toUpdate.Add((projectName, endpoint, endpoint.SuggestedPermission!));
                }
                else
                {
                    // User wants to keep existing permission
                    toKeep.Add((projectName, endpoint));
                }
            }
            catch (Exception ex)
            {
                _consoleUIService.ShowWarning($"Error handling permission for {projectName}/{endpoint.ClassName}: {ex.Message}");
                toKeep.Add((projectName, endpoint));
            }
        }

        // Show summary of decisions
        AnsiConsole.WriteLine();
        _consoleUIService.ShowInfo("📋 Interactive Review Summary:");

        if (toUpdate.Any())
        {
            AnsiConsole.MarkupLine("[green]✅ Permissions to update:[/]");
            foreach (var (project, endpoint, newPermission) in toUpdate)
            {
                AnsiConsole.MarkupLine($"   • {project}/{endpoint.ClassName}: [red]{endpoint.ExistingPermission}[/] → [green]{newPermission}[/]");
            }
            AnsiConsole.WriteLine();
            
            _consoleUIService.ShowWarning("⚠️  Note: These changes need to be applied manually in your code.");
            _consoleUIService.ShowInfo("💡 Tip: Use Find & Replace in your IDE to update RequirePermission() calls.");
        }

        if (toKeep.Any())
        {
            AnsiConsole.MarkupLine("[yellow]📌 Permissions to keep as custom:[/]");
            foreach (var (project, endpoint) in toKeep)
            {
                AnsiConsole.MarkupLine($"   • {project}/{endpoint.ClassName}: [yellow]{endpoint.ExistingPermission}[/] (marked as custom)");
            }
            AnsiConsole.WriteLine();
        }

        // Show final action summary
        if (toUpdate.Any())
        {
            _consoleUIService.ShowSuccess($"🎯 Review completed: {toUpdate.Count} permission(s) approved for update, {toKeep.Count} kept as custom.");
        }
        else
        {
            _consoleUIService.ShowInfo("✅ All mismatched permissions were kept as custom permissions.");
        }
    }
} 