using System.Text.Json.Serialization;

namespace SyncPermissions.Models;

public class PermissionDiscoveryResult
{
    public string ToolVersion { get; set; } = "1.0.0";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string GeneratedBy { get; set; } = "AEGIS v1.0 - Advanced Enterprise Guardian Intelligence System";
    public List<ProjectScanResult> Projects { get; set; } = new();
    public ScanSummary Summary { get; set; } = new();
}

public class ProjectScanResult
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public List<DiscoveredPermission> DiscoveredPermissions { get; set; } = new();
    public List<ScanWarning> Warnings { get; set; } = new();
}

public class DiscoveredPermission
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PermissionMetadata Metadata { get; set; } = new();
}

public class PermissionMetadata
{
    public string? HttpMethod { get; set; }
    public string? Route { get; set; }
    public string? Project { get; set; }
}

public class EndpointInfo
{
    public string ClassName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? HttpMethod { get; set; }
    public string? Route { get; set; }
    public EndpointAuthorizationStatus AuthorizationStatus { get; set; }
    public string? ExistingPermission { get; set; }
    public string? SuggestedPermission { get; set; }
    
    // New properties for better UI support
    public bool RequiresAuthorization => AuthorizationStatus != EndpointAuthorizationStatus.Public;
    public bool HasRequirePermission => AuthorizationStatus == EndpointAuthorizationStatus.AlreadyProtected;
    public bool IsPublic => AuthorizationStatus == EndpointAuthorizationStatus.Public;
}

public class PermissionInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PermissionMetadata Metadata { get; set; } = new();
}

public enum EndpointAuthorizationStatus
{
    Public,           // No RequireAuthorization
    AuthOnly,         // RequireAuthorization only
    NeedsPermission,  // Needs RequirePermission (candidate for generation)
    AlreadyProtected, // Has RequirePermission
    MismatchedPermission // Has RequirePermission but doesn't match convention
}

public class ScanSummary
{
    public int TotalEndpoints { get; set; }
    public int PublicEndpoints { get; set; }
    public int AuthOnlyEndpoints { get; set; }
    public int NeedsPermissionEndpoints { get; set; }
    public int AlreadyProtectedEndpoints { get; set; }
    public int GeneratedPermissions { get; set; }
    public List<ScanWarning> Warnings { get; set; } = new();
}

public class ScanWarning
{
    public string Type { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Suggestion { get; set; }
}

public class ProjectInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string ProjectFilePath { get; set; } = string.Empty;
    public List<string> SourceFiles { get; set; } = new();
} 