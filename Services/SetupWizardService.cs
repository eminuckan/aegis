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
    private readonly IFolderBrowserService _folderBrowser;

    public SetupWizardService(IFolderBrowserService folderBrowser)
    {
        _folderBrowser = folderBrowser;
    }

    public async Task<bool> ShouldRunSetupAsync()
    {
        var configPath = "aegis-config.json";
        
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
        var path = configPath ?? "aegis-config.json";
        
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
            "[dim]You can modify these settings later in aegis-config.json[/]")
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
        AnsiConsole.MarkupLine("[dim]Select the folder that contains your .NET projects to scan.[/]");
        AnsiConsole.WriteLine();
        
        AnsiConsole.MarkupLine("[yellow]Press any key to open folder browser...[/]");
        Console.ReadKey(true);
        
        var selectedPath = await _folderBrowser.SelectFolderAsync(
            Directory.GetCurrentDirectory(),
            "Step 1: Select folder to scan for .NET projects");
        
        if (selectedPath == null)
        {
            AnsiConsole.MarkupLine("[yellow]⚠️ Setup cancelled. You can run setup later.[/]");
            throw new OperationCanceledException("Setup was cancelled by user.");
        }
        
        // Validate the selected path
        if (!await ValidateScanPathAsync(selectedPath))
        {
            AnsiConsole.MarkupLine("[red]❌ Selected folder is not valid for scanning.[/]");
            throw new InvalidOperationException("Selected path does not contain .NET projects.");
        }
        
        config.SyncPermissions.DefaultScanPath = selectedPath;
        AnsiConsole.MarkupLine($"[green]✅ Scan path set to: {selectedPath}[/]");
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
            "[green]Use default HTTP method mappings?[/]", 
            defaultValue: true);
        
        if (useDefaults)
        {
            // Set default conventions - only HTTP method mappings
            config.Conventions.HttpMethodActions = new Dictionary<string, string>
            {
                { "GET", "Read" },
                { "POST", "Create" },
                { "PUT", "Update" },
                { "PATCH", "Update" },
                { "DELETE", "Delete" }
            };
            
            AnsiConsole.MarkupLine("[dim]✅ HTTP method to action mappings configured[/]");
            AnsiConsole.MarkupLine("[dim]🤖 Resource names will be auto-inferred from folder structure[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]ℹ️  You can customize HTTP method mappings manually in aegis-config.json[/]");
            AnsiConsole.MarkupLine("[dim]🤖 Resource names will be auto-inferred from folder structure[/]");
            config.Conventions.HttpMethodActions = new Dictionary<string, string>();
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
        
        // C# file generation settings
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold cyan1]🔧 C# File Generation[/]");
        
        config.SyncPermissions.GenerateCSharpFile = AnsiConsole.Confirm(
            "[green]Generate strongly-typed C# permissions file by default?[/]\n" +
            "[dim]Creates a C# class with all discovered permissions for easy integration.[/]", 
            defaultValue: false);
        
        if (config.SyncPermissions.GenerateCSharpFile)
        {
            config.SyncPermissions.CSharpFileName = AnsiConsole.Ask<string>(
                "[green]C# file name[/]:", 
                "AppPermissions.cs");
                
            config.SyncPermissions.CSharpNamespace = AnsiConsole.Ask<string>(
                "[green]Namespace for the generated class[/]:", 
                "Application.Constants");
        }
        
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
            $"[dim]Generate C# File:[/] [cyan1]{config.SyncPermissions.GenerateCSharpFile}[/]\n" +
            $"[dim]Conventions:[/] [cyan1]{(config.Conventions.HttpMethodActions.Any() ? "Default" : "Custom")}[/]\n\n" +
            "[dim]You can now run scans or modify aegis-config.json for advanced options.[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Green)
            .Header("[bold white] Configuration Summary [/]")
            .Padding(1, 1);
            
        AnsiConsole.Write(summary);
        AnsiConsole.WriteLine();
    }
} 