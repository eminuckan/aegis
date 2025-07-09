using SyncPermissions.Models;

namespace SyncPermissions.Services;

public interface IPermissionGenerator
{
    void Initialize(ConventionsConfig conventions);
    List<PermissionInfo> GeneratePermissions(List<EndpointInfo> endpoints, ConventionsConfig conventions);
    DiscoveredPermission? GeneratePermission(EndpointInfo endpoint, string projectName);
    bool ValidatePermissionConvention(EndpointInfo endpoint, out string suggestedName);
    string GeneratePermissionName(string resource, string action);
}

public class PermissionGenerator : IPermissionGenerator
{
    private Dictionary<string, string> _httpMethodToAction = new();

    // Default fallback mappings
    private static readonly Dictionary<string, string> DefaultHttpMethodToAction = new()
    {
        { "GET", "Read" },
        { "POST", "Create" },
        { "PUT", "Update" },
        { "PATCH", "Update" },
        { "DELETE", "Delete" }
    };

    public void Initialize(ConventionsConfig conventions)
    {
        // Use configuration mappings, fall back to defaults
        _httpMethodToAction = conventions.HttpMethodActions.Any() 
            ? conventions.HttpMethodActions 
            : DefaultHttpMethodToAction;
        
        // Note: No more FeatureFolderToResource mapping - using pure auto-inference
    }

    public List<PermissionInfo> GeneratePermissions(List<EndpointInfo> endpoints, ConventionsConfig conventions)
    {
        Initialize(conventions);
        var permissions = new List<PermissionInfo>();

        foreach (var endpoint in endpoints)
        {
            // Only generate permissions for endpoints that need them
            if (endpoint.RequiresAuthorization && !endpoint.HasRequirePermission && !endpoint.IsPublic)
            {
                var resource = ExtractResource(endpoint.FilePath);
                var action = ExtractAction(endpoint.HttpMethod ?? "");

                if (resource != null && action != null)
                {
                    var permissionName = GeneratePermissionName(resource, action);
                    var description = GenerateDescription(resource, action);

                    permissions.Add(new PermissionInfo
                    {
                        Name = permissionName,
                        Description = description,
                        Metadata = new PermissionMetadata
                        {
                            HttpMethod = endpoint.HttpMethod ?? "",
                            Route = endpoint.Route ?? "",
                            Project = "" // Will be set by caller
                        }
                    });
                }
            }
        }

        return permissions;
    }

    public DiscoveredPermission? GeneratePermission(EndpointInfo endpoint, string projectName)
    {
        if (endpoint.HttpMethod == null || endpoint.AuthorizationStatus != EndpointAuthorizationStatus.AuthOnly)
            return null;

        var resource = ExtractResource(endpoint.FilePath);
        var action = ExtractAction(endpoint.HttpMethod);

        if (resource == null || action == null)
            return null;

        var permissionName = GeneratePermissionName(resource, action);
        var description = GenerateDescription(resource, action);

        return new DiscoveredPermission
        {
            Name = permissionName,
            Description = description,
            Metadata = new PermissionMetadata
            {
                HttpMethod = endpoint.HttpMethod,
                Route = endpoint.Route,
                Project = projectName
            }
        };
    }

    public bool ValidatePermissionConvention(EndpointInfo endpoint, out string suggestedName)
    {
        suggestedName = string.Empty;

        if (endpoint.HttpMethod == null || endpoint.ExistingPermission == null)
            return true; // No validation needed

        var resource = ExtractResource(endpoint.FilePath);
        var action = ExtractAction(endpoint.HttpMethod);

        if (resource != null && action != null)
        {
            suggestedName = GeneratePermissionName(resource, action);
            endpoint.SuggestedPermission = suggestedName;

            var matches = endpoint.ExistingPermission.Equals(suggestedName, StringComparison.OrdinalIgnoreCase);
            
            if (!matches)
            {
                endpoint.AuthorizationStatus = EndpointAuthorizationStatus.MismatchedPermission;
            }

            return matches;
        }
        else
        {
            // Cannot generate a suggestion due to missing resource/action mapping
            // Keep existing permission as-is (treat as custom permission)
            endpoint.SuggestedPermission = null;
            // Don't change authorization status - keep as AlreadyProtected
            return true; // Return true to indicate validation passed (custom permission)
        }
    }

    public string GeneratePermissionName(string resource, string action)
    {
        return $"{resource}.{action}";
    }

    private string? ExtractResource(string filePath)
    {
        // Extract from Features folder structure
        // e.g., Features/RoleManagement/CreateRole/CreateRoleEndpoint.cs -> Roles
        var pathParts = filePath.Split('/', '\\');
        var featuresIndex = Array.FindIndex(pathParts, p => p.Equals("Features", StringComparison.OrdinalIgnoreCase));
        
        if (featuresIndex >= 0 && featuresIndex + 1 < pathParts.Length)
        {
            var featureFolder = pathParts[featuresIndex + 1];
            
            // Use smart inference
            var inferredResource = InferResourceFromFolderName(featureFolder);
            
            // Removed INFO logging to keep progress bar position stable
            // Console.WriteLine($"[INFO] Auto-inferred resource '{inferredResource}' from feature folder '{featureFolder}'");
            
            return inferredResource;
        }

        return null;
    }

    private string? ExtractAction(string httpMethod)
    {
        return _httpMethodToAction.TryGetValue(httpMethod.ToUpper(), out var action) ? action : null;
    }

    private string InferResourceFromFolderName(string folderName)
    {
        var resource = folderName;
        
        // Remove common suffixes
        var suffixesToRemove = new[]
        {
            "Management",
            "Service", 
            "Services",
            "Gateway",
            "Processing",
            "Handler",
            "Handlers",
            "Controller",
            "Controllers",
            "Feature",
            "Features"
        };

        foreach (var suffix in suffixesToRemove)
        {
            if (resource.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                resource = resource[..^suffix.Length];
                break; // Only remove one suffix
            }
        }

        // Handle special cases
        resource = resource switch
        {
            // Common business domain mappings
            "User" => "Users",
            "Role" => "Roles", 
            "Permission" => "Permissions",
            "Tenant" => "Tenants",
            "Client" => "Clients",
            "Auth" => "Auth",
            "Authentication" => "Auth",
            "Authorization" => "Auth",
            "Order" => "Orders",
            "Product" => "Products",
            "Payment" => "Payments",
            "Notification" => "Notifications",
            "Email" => "Emails",
            "Sms" => "Sms",
            "Template" => "Templates",
            "Report" => "Reports",
            "Analytics" => "Analytics",
            "Dashboard" => "Dashboards",
            "Settings" => "Settings",
            "Config" => "Config",
            "Configuration" => "Config",
            "Health" => "Health",
            "Audit" => "Audit",
            "Log" => "Logs",
            "Logging" => "Logs",
            _ => ApplyDefaultPluralization(resource)
        };

        return resource;
    }

    private string ApplyDefaultPluralization(string word)
    {
        if (string.IsNullOrEmpty(word))
            return word;

        // Skip if already plural or doesn't need pluralization
        if (word.EndsWith("s", StringComparison.OrdinalIgnoreCase) || 
            word.EndsWith("Settings", StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("Config", StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("Health", StringComparison.OrdinalIgnoreCase))
        {
            return word;
        }

        // Basic English pluralization rules
        if (word.EndsWith("y", StringComparison.OrdinalIgnoreCase) && 
            word.Length > 1 && 
            !"aeiou".Contains(word[^2], StringComparison.OrdinalIgnoreCase))
        {
            return word[..^1] + "ies"; // party -> parties
        }

        if (word.EndsWith("ch", StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("sh", StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("ss", StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("x", StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("z", StringComparison.OrdinalIgnoreCase))
        {
            return word + "es"; // box -> boxes, class -> classes
        }

        // Default: just add 's'
        return word + "s";
    }

    private string GenerateDescription(string resource, string action)
    {
        return action.ToLower() switch
        {
            "create" => $"Create {resource.ToLower()} permission",
            "read" => $"Read {resource.ToLower()} permission",
            "update" => $"Update {resource.ToLower()} permission",
            "delete" => $"Delete {resource.ToLower()} permission",
            _ => $"{action} {resource.ToLower()} permission"
        };
    }
} 