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

            // Step 4: Handle mismatched permissions interactively (temporarily disabled to fix progress bar)
            // TODO: Move this outside of progress bar context
            // if (mismatchedPermissions.Any() && !options.AcceptAllSuggestedPermissions)
            // {
            //     await HandleMismatchedPermissionsInteractivelyAsync(mismatchedPermissions, options);
            // }

            // Step 5: Show final mismatch table if needed
            if (mismatchedPermissions.Any())
            {
                // Don't show during progress bar - this will be handled outside
                // _consoleUIService.ShowMismatchedPermissionsTable(mismatchedPermissions);
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
        // Remove the ShowInfo call to prevent console output during progress bar
        var approved = new List<(string Project, EndpointInfo Endpoint)>();
        var remaining = new List<(string Project, EndpointInfo Endpoint)>();

        foreach (var (projectName, endpoint) in mismatchedPermissions)
        {
            try
            {
                var isApproved = await _consoleUIService.PromptPermissionCorrectionAsync(endpoint, projectName);
                
                if (isApproved)
                {
                    approved.Add((projectName, endpoint));
                }
                else
                {
                    remaining.Add((projectName, endpoint));
                }
            }
            catch (Exception ex)
            {
                _consoleUIService.ShowWarning($"Error handling permission for {projectName}/{endpoint.ClassName}: {ex.Message}");
                remaining.Add((projectName, endpoint));
            }
        }

        if (approved.Any())
        {
            _consoleUIService.ShowSuccess($"Approved {approved.Count} permission corrections.");
        }

        // Update the original list to only contain non-approved items
        mismatchedPermissions.Clear();
        mismatchedPermissions.AddRange(remaining);
    }
} 