using Spectre.Console;
using SyncPermissions.Models;

namespace SyncPermissions.Services;

public interface IPermissionScanService
{
    Task<PermissionDiscoveryResult> ScanAsync(ScanOptions options, ProgressTask? progress = null);
    Task HandleMismatchedPermissionsAsync(PermissionDiscoveryResult result, ScanOptions options);
    Task UpdateResultWithCorrectedPermissions(PermissionDiscoveryResult result, List<(string Project, EndpointInfo Endpoint)> correctedPermissions);
    Task<bool> HandleCSharpFileGenerationAsync(PermissionDiscoveryResult result, ScanOptions options, AppConfig config);
}

public class PermissionScanService : IPermissionScanService
{
    private readonly IProjectScanner _projectScanner;
    private readonly IEndpointDiscoverer _endpointDiscoverer;
    private readonly IPermissionGenerator _permissionGenerator;
    private readonly IConsoleUIService _consoleUIService;
    private readonly ICSharpGeneratorService _csharpGenerator;

    public PermissionScanService(
        IProjectScanner projectScanner,
        IEndpointDiscoverer endpointDiscoverer,
        IPermissionGenerator permissionGenerator,
        IConsoleUIService consoleUIService,
        ICSharpGeneratorService csharpGenerator)
    {
        _projectScanner = projectScanner;
        _endpointDiscoverer = endpointDiscoverer;
        _permissionGenerator = permissionGenerator;
        _consoleUIService = consoleUIService;
        _csharpGenerator = csharpGenerator;
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

            // Return result with mismatched permissions data for handling outside of progress scope
            if (mismatchedPermissions.Any())
            {
                // Store mismatched permissions in result for handling after progress bar closes
                result.MismatchedPermissions = mismatchedPermissions;
            }

            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Scan failed: {ex.Message}", ex);
        }
    }

    public async Task HandleMismatchedPermissionsAsync(PermissionDiscoveryResult result, ScanOptions options)
    {
        var mismatchedPermissions = result.MismatchedPermissions;
        if (mismatchedPermissions == null || !mismatchedPermissions.Any())
            return;

        var correctedPermissions = new List<(string Project, EndpointInfo Endpoint)>();

        if (options.AcceptAllSuggestedPermissions)
        {
            // Auto-accept all suggestions, just show what was done
            foreach ((string projectName, EndpointInfo endpoint) in mismatchedPermissions)
            {
                var originalPermission = endpoint.ExistingPermission;
                endpoint.ExistingPermission = endpoint.SuggestedPermission;
                endpoint.AuthorizationStatus = EndpointAuthorizationStatus.AlreadyProtected;
                correctedPermissions.Add((projectName, endpoint));
                _consoleUIService.ShowSuccess($"✅ Auto-accepted suggestion for {projectName}/{endpoint.ClassName}: '{originalPermission}' → '{endpoint.SuggestedPermission}'");
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
                correctedPermissions = await HandleMismatchedPermissionsInteractivelyAsync(mismatchedPermissions, options);
            }
            else
            {
                _consoleUIService.ShowInfo("Skipped interactive permission review. Warnings remain in the report.");
            }
        }

        // Update result with corrected permissions
        if (correctedPermissions.Any())
        {
            await UpdateResultWithCorrectedPermissions(result, correctedPermissions);
        }

        // Clear mismatched permissions from result after handling
        result.MismatchedPermissions = null;
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
            
            // If validation passed, add the existing permission to discovered permissions
            if (isValid && !string.IsNullOrEmpty(endpoint.ExistingPermission))
            {
                projectResult.DiscoveredPermissions.Add(new DiscoveredPermission
                {
                    Name = endpoint.ExistingPermission,
                    Description = GeneratePermissionDescription(endpoint.ExistingPermission),
                    Metadata = new PermissionMetadata
                    {
                        HttpMethod = endpoint.HttpMethod,
                        Route = endpoint.Route,
                        Project = projectName
                    }
                });
            }
            
            // If validation failed and we have a suggestion, the endpoint will be marked as MismatchedPermission
            // This will be picked up by the mismatch detection in ScanAsync and handled interactively
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

    private async Task<List<(string Project, EndpointInfo Endpoint)>> HandleMismatchedPermissionsInteractivelyAsync(
        List<(string Project, EndpointInfo Endpoint)> mismatchedPermissions, 
        ScanOptions options)
    {
        _consoleUIService.ShowInfo($"🔍 Reviewing {mismatchedPermissions.Count} mismatched permission(s) interactively...");
        AnsiConsole.WriteLine();

        var updatedPermissions = new List<(string Project, EndpointInfo Endpoint, string NewPermission)>();
        var keptPermissions = new List<(string Project, EndpointInfo Endpoint)>();
        var correctedPermissions = new List<(string Project, EndpointInfo Endpoint)>();

        foreach (var (projectName, endpoint) in mismatchedPermissions)
        {
            try
            {
                var originalPermission = endpoint.ExistingPermission;
                var decision = await _consoleUIService.PromptPermissionCorrectionAsync(endpoint, projectName);
                
                if (decision && endpoint.ExistingPermission != originalPermission)
                {
                    // User accepted suggestion or entered custom permission
                    updatedPermissions.Add((projectName, endpoint, endpoint.ExistingPermission!));
                    correctedPermissions.Add((projectName, endpoint));
                }
                else
                {
                    // User kept current permission
                    keptPermissions.Add((projectName, endpoint));
                    correctedPermissions.Add((projectName, endpoint)); // Also add kept permissions to track them
                }
            }
            catch (Exception ex)
            {
                _consoleUIService.ShowWarning($"Error handling permission for {projectName}/{endpoint.ClassName}: {ex.Message}");
                keptPermissions.Add((projectName, endpoint));
                correctedPermissions.Add((projectName, endpoint));
            }
        }

        // Show summary of decisions
        AnsiConsole.WriteLine();
        _consoleUIService.ShowInfo("📋 Interactive Review Summary:");

        if (updatedPermissions.Any())
        {
            AnsiConsole.MarkupLine("[green]✅ Permissions updated:[/]");
            foreach (var (project, endpoint, newPermission) in updatedPermissions)
            {
                AnsiConsole.MarkupLine($"   • {project}/{endpoint.ClassName}: [green]{newPermission}[/]");
            }
            AnsiConsole.WriteLine();
            
            _consoleUIService.ShowWarning("⚠️  Note: Code changes need to be applied manually.");
            _consoleUIService.ShowInfo("💡 Tip: Update RequirePermission() calls in your source code.");
        }

        if (keptPermissions.Any())
        {
            AnsiConsole.MarkupLine("[yellow]📌 Permissions kept as custom:[/]");
            foreach (var (project, endpoint) in keptPermissions)
            {
                AnsiConsole.MarkupLine($"   • {project}/{endpoint.ClassName}: [yellow]{endpoint.ExistingPermission}[/] (custom)");
            }
            AnsiConsole.WriteLine();
        }

        // Show final action summary
        if (updatedPermissions.Any())
        {
            _consoleUIService.ShowSuccess($"🎯 Review completed: {updatedPermissions.Count} permission(s) updated, {keptPermissions.Count} kept as custom.");
        }
        else
        {
            _consoleUIService.ShowInfo("✅ All mismatched permissions were kept as custom permissions.");
        }

        return correctedPermissions;
    }

    public async Task UpdateResultWithCorrectedPermissions(PermissionDiscoveryResult result, List<(string Project, EndpointInfo Endpoint)> correctedPermissions)
    {
        // Add ALL corrected permissions to the result as DiscoveredPermissions
        // (regardless of which choice user made - suggested, current, or custom)
        foreach (var (projectName, endpoint) in correctedPermissions)
        {
            var project = result.Projects.FirstOrDefault(p => p.Name == projectName);
            if (project != null && !string.IsNullOrEmpty(endpoint.ExistingPermission))
            {
                // Check if this permission already exists in discovered permissions
                var existingPermission = project.DiscoveredPermissions
                    .FirstOrDefault(p => p.Metadata.Route == endpoint.Route && p.Metadata.HttpMethod == endpoint.HttpMethod);
                
                if (existingPermission != null)
                {
                    // Update existing permission with user's final choice
                    existingPermission.Name = endpoint.ExistingPermission;
                    existingPermission.Description = GeneratePermissionDescription(endpoint.ExistingPermission);
                }
                else
                {
                    // Add new permission with user's final choice (suggested, kept, or custom)
                    project.DiscoveredPermissions.Add(new DiscoveredPermission
                    {
                        Name = endpoint.ExistingPermission,
                        Description = GeneratePermissionDescription(endpoint.ExistingPermission),
                        Metadata = new PermissionMetadata
                        {
                            HttpMethod = endpoint.HttpMethod,
                            Route = endpoint.Route,
                            Project = projectName
                        }
                    });
                }
            }
        }
        
        // Update summary counts to reflect the corrected permissions
        result.Summary.GeneratedPermissions = result.Projects.Sum(p => p.DiscoveredPermissions.Count);
    }

    private string GeneratePermissionDescription(string permissionName)
    {
        var parts = permissionName.Split('.');
        if (parts.Length >= 2)
        {
            var resource = parts[0].ToLower();
            var action = parts[1].ToLower();
            return $"{action} {resource} permission";
        }
        return $"{permissionName} permission";
    }

    public async Task<bool> HandleCSharpFileGenerationAsync(PermissionDiscoveryResult result, ScanOptions options, AppConfig config)
    {
        try
        {
            // Check if C# file generation should be performed
            var shouldGenerate = await _csharpGenerator.ShouldGenerateCSharpFileAsync(options, config);
            
            if (!shouldGenerate)
            {
                return false;
            }

            // Generate the C# file
            var success = await _csharpGenerator.GenerateCSharpFileAsync(result, options, config);
            
            if (success)
            {
                var filePath = _csharpGenerator.GetCSharpFilePath(options, config);
                _consoleUIService.ShowInfo($"📁 C# file generated at: {filePath}");
            }

            return success;
        }
        catch (Exception ex)
        {
            _consoleUIService.ShowError($"C# file generation failed: {ex.Message}");
            return false;
        }
    }
} 