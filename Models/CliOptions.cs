namespace SyncPermissions.Models;

public class ScanOptions
{
    public string? ScanPath { get; set; }
    public string? OutputFile { get; set; }
    public bool Verbose { get; set; }
    public bool MissingOnly { get; set; }
    public bool AutoGenerate { get; set; }
    public bool AcceptAllSuggestedPermissions { get; set; }
    public string? TargetProject { get; set; }
    public string? ConfigFile { get; set; }
} 