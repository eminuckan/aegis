using Spectre.Console;
using SyncPermissions.Models;
using System.Text.Json;

namespace SyncPermissions.Services;

public interface ISetupWizardService
{
    Task<bool> ShouldRunSetupAsync();
    Task<AppConfig> RunSetupWizardAsync();
    Task SaveConfigurationAsync(AppConfig config, string? configPath = null);
}

public class SetupWizardService : ISetupWizardService
{
    public async Task<bool> ShouldRunSetupAsync()
    {
        var configPath = "appsettings.json";
        
        // Check if config file exists and has required settings
        if (!File.Exists(configPath))
        {
            return true;
        }
        
        try
        {
            var configContent = await File.ReadAllTextAsync(configPath);
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };
            var config = JsonSerializer.Deserialize<AppConfig>(configContent, options);
            
            // Check if essential settings are missing
            return config?.SyncPermissions?.DefaultScanPath == null;
        }
        catch
        {
            return true;
        }
    }

    public async Task<AppConfig> RunSetupWizardAsync()
    {
        ShowWelcomeMessage();
        
        var config = new AppConfig
        {
            SyncPermissions = new SyncPermissionsConfig(),
            Conventions = new ConventionsConfig()
        };
        
        // Step 1: Scan Path
        await ConfigureScanPathAsync(config);
        
        // Step 2: Output Settings  
        await ConfigureOutputSettingsAsync(config);
        
        // Step 3: Convention Settings
        await ConfigureConventionsAsync(config);
        
        // Step 4: Additional Options
        await ConfigureAdditionalOptionsAsync(config);
        
        ShowSetupComplete(config);
        
        return config;
    }

    public async Task SaveConfigurationAsync(AppConfig config, string? configPath = null)
    {
        var path = configPath ?? "appsettings.json";
        
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        
        var json = JsonSerializer.Serialize(config, options);
        await File.WriteAllTextAsync(path, json);
        
        AnsiConsole.MarkupLine($"[green]✅ Configuration saved to {path}[/]");
    }

    private void ShowWelcomeMessage()
    {
        var panel = new Panel(
            "[bold yellow]🚀 Welcome to Propmate Permission Scanner Setup![/]\n\n" +
            "[dim]This wizard will help you configure the tool for your project.[/]\n" +
            "[dim]You can modify these settings later in appsettings.json[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .Header("[bold white] Setup Wizard [/]")
            .Padding(1, 1);
            
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    private async Task ConfigureScanPathAsync(AppConfig config)
    {
        AnsiConsole.MarkupLine("[bold cyan1]📁 Step 1: Configure Scan Path[/]");
        AnsiConsole.WriteLine();
        
        var scanPath = AnsiConsole.Ask<string>(
            "[green]Enter the path to scan for projects[/] [dim](e.g., ./src/Services, ../../src/Services)[/]:");
        
        // Validate path
        while (!await ValidateScanPathAsync(scanPath))
        {
            AnsiConsole.MarkupLine("[red]❌ Invalid path. Please try again.[/]");
            scanPath = AnsiConsole.Ask<string>("[green]Enter a valid path to scan[/]:");
        }
        
        config.SyncPermissions.DefaultScanPath = scanPath;
        AnsiConsole.MarkupLine($"[green]✅ Scan path set to: {scanPath}[/]");
        AnsiConsole.WriteLine();
    }

    private async Task<bool> ValidateScanPathAsync(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            
            if (!Directory.Exists(fullPath))
            {
                AnsiConsole.MarkupLine($"[yellow]⚠️  Directory does not exist: {fullPath}[/]");
                return false;
            }
            
            var projectFiles = Directory.GetFiles(fullPath, "*.csproj", SearchOption.AllDirectories);
            if (projectFiles.Length == 0)
            {
                AnsiConsole.MarkupLine($"[yellow]⚠️  No .csproj files found in: {fullPath}[/]");
                return false;
            }
            
            AnsiConsole.MarkupLine($"[dim]Found {projectFiles.Length} project(s) ✅[/]");
            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Path error: {ex.Message}[/]");
            return false;
        }
    }

    private async Task ConfigureOutputSettingsAsync(AppConfig config)
    {
        AnsiConsole.MarkupLine("[bold cyan1]📄 Step 2: Configure Output Settings[/]");
        AnsiConsole.WriteLine();
        
        var generateJson = AnsiConsole.Confirm(
            "[green]Generate JSON output file by default?[/]", 
            defaultValue: true);
        
        if (generateJson)
        {
            var outputPath = AnsiConsole.Ask<string>(
                "[green]Default output filename[/]:", 
                "permissions.json");
                
            config.SyncPermissions.DefaultOutputPath = outputPath;
        }
        else
        {
            config.SyncPermissions.DefaultOutputPath = null;
        }
        
        config.SyncPermissions.Verbose = AnsiConsole.Confirm(
            "[green]Enable verbose output by default?[/]", 
            defaultValue: false);
        
        AnsiConsole.MarkupLine("[green]✅ Output settings configured[/]");
        AnsiConsole.WriteLine();
    }

    private async Task ConfigureConventionsAsync(AppConfig config)
    {
        AnsiConsole.MarkupLine("[bold cyan1]⚙️ Step 3: Configure Conventions[/]");
        AnsiConsole.WriteLine();
        
        var useDefaults = AnsiConsole.Confirm(
            "[green]Use default HTTP method and feature folder mappings?[/]", 
            defaultValue: true);
        
        if (useDefaults)
        {
            // Set default conventions
            config.Conventions.HttpMethodActions = new Dictionary<string, string>
            {
                { "GET", "Read" },
                { "POST", "Create" },
                { "PUT", "Update" },
                { "PATCH", "Update" },
                { "DELETE", "Delete" }
            };
            
            config.Conventions.FeatureToResource = new Dictionary<string, string>
            {
                { "RoleManagement", "Roles" },
                { "UserManagement", "Users" },
                { "TenantManagement", "Tenants" },
                { "PermissionManagement", "Permissions" },
                { "ClientManagement", "Clients" },
                { "TenantInvitations", "TenantInvitations" },
                { "TenantSettings", "TenantSettings" },
                { "Authentication", "Auth" },
                { "Authorization", "Auth" },
                { "UserProfile", "UserProfile" },
                { "Templates", "Templates" },
                { "Emails", "Emails" },
                { "Sms", "Sms" },
                { "NotificationLogs", "NotificationLogs" }
            };
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]ℹ️  You can customize conventions manually in appsettings.json[/]");
            config.Conventions.HttpMethodActions = new Dictionary<string, string>();
            config.Conventions.FeatureToResource = new Dictionary<string, string>();
        }
        
        AnsiConsole.MarkupLine("[green]✅ Conventions configured[/]");
        AnsiConsole.WriteLine();
    }

    private async Task ConfigureAdditionalOptionsAsync(AppConfig config)
    {
        AnsiConsole.MarkupLine("[bold cyan1]🔧 Step 4: Additional Options[/]");
        AnsiConsole.WriteLine();
        
        config.SyncPermissions.MissingOnly = AnsiConsole.Confirm(
            "[green]Show only missing permissions by default?[/]", 
            defaultValue: false);
            
        config.SyncPermissions.AutoGenerate = AnsiConsole.Confirm(
            "[green]Enable auto-generation features by default?[/]", 
            defaultValue: false);

        config.SyncPermissions.AcceptAllSuggestedPermissions = AnsiConsole.Confirm(
            "[green]Auto-accept all suggested permission corrections?[/]\n" +
            "[dim]If enabled, mismatched permissions will be auto-corrected without prompting.[/]", 
            defaultValue: false);
        
        AnsiConsole.MarkupLine("[green]✅ Additional options configured[/]");
        AnsiConsole.WriteLine();
    }

    private void ShowSetupComplete(AppConfig config)
    {
        var summary = new Panel(
            $"[bold green]✅ Setup Complete![/]\n\n" +
            $"[dim]Scan Path:[/] [cyan1]{config.SyncPermissions.DefaultScanPath}[/]\n" +
            $"[dim]Output File:[/] [cyan1]{config.SyncPermissions.DefaultOutputPath ?? "Console only"}[/]\n" +
            $"[dim]Verbose:[/] [cyan1]{config.SyncPermissions.Verbose}[/]\n" +
            $"[dim]Auto-Accept Suggestions:[/] [cyan1]{config.SyncPermissions.AcceptAllSuggestedPermissions}[/]\n" +
            $"[dim]Conventions:[/] [cyan1]{(config.Conventions.HttpMethodActions.Any() ? "Default" : "Custom")}[/]\n\n" +
            "[dim]You can now run scans or modify appsettings.json for advanced options.[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Green)
            .Header("[bold white] Configuration Summary [/]")
            .Padding(1, 1);
            
        AnsiConsole.Write(summary);
        AnsiConsole.WriteLine();
    }
} 