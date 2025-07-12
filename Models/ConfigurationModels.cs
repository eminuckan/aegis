namespace SyncPermissions.Models;

public class AppConfig
{
    public SyncPermissionsConfig SyncPermissions { get; set; } = new();
    public ConventionsConfig Conventions { get; set; } = new();
}

public class SyncPermissionsConfig
{
    public string? DefaultScanPath { get; set; }
    public string? DefaultOutputPath { get; set; }
    public bool Verbose { get; set; } = false;
    public bool MissingOnly { get; set; } = false;
    public bool AutoGenerate { get; set; } = false;
    public bool AcceptAllSuggestedPermissions { get; set; } = false;
    public bool GenerateCSharpFile { get; set; } = false;
    public string? CSharpFileName { get; set; } = "AppPermissions.cs";
    public string? CSharpNamespace { get; set; } = "Application.Constants";
}

public class ConventionsConfig
{
    public Dictionary<string, string> HttpMethodActions { get; set; } = new();
}

 