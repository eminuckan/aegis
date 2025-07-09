using Spectre.Console;
using SyncPermissions.Models;
using System.Text.Json;

namespace SyncPermissions.Services;

public interface IInteractiveMenuService
{
    Task RunAsync();
}

public class InteractiveMenuService : IInteractiveMenuService
{
    // Shadow gradient colors as the base theme - same as ConsoleUIService
    private static readonly Color PrimaryAccent = new Color(180, 120, 60); // Warm brown-orange
    private static readonly Color SecondaryAccent = new Color(140, 80, 80); // Muted red-brown
    private static readonly Color MutedBlue = new Color(95, 115, 140); // Cooler blue-grey
    private static readonly Color MainBorderColor = new Color(80, 50, 20); // Dark Brown (shadow from)
    private static readonly Color SuccessColor = new Color(120, 140, 80); // Muted olive green
    private static readonly Color WarningColor = new Color(160, 100, 50); // Warm orange-brown
    private static readonly Color ErrorColor = new Color(140, 60, 60); // Muted red
    private static readonly Color DefaultTextColor = new Color(200, 190, 180); // Warm off-white
    private static readonly Color DimTextColor = new Color(120, 100, 80); // Warm dim

    private readonly IConsoleUIService _consoleUI;
    private readonly IPermissionScanService _scanService;
    private readonly ISetupWizardService _setupWizard;
    private readonly IConfigurationService _configService;
    private readonly IOutputService _outputService;
    private readonly IFolderBrowserService _folderBrowser;

    public InteractiveMenuService(
        IConsoleUIService consoleUI,
        IPermissionScanService scanService,
        ISetupWizardService setupWizard,
        IConfigurationService configService,
        IOutputService outputService,
        IFolderBrowserService folderBrowser)
    {
        _consoleUI = consoleUI;
        _scanService = scanService;
        _setupWizard = setupWizard;
        _configService = configService;
        _outputService = outputService;
        _folderBrowser = folderBrowser;
    }

    public async Task RunAsync()
    {
        // İlk çalıştırma kontrolü - konfigürasyon yoksa setup wizard'ı çalıştır
        if (await _setupWizard.ShouldRunSetupAsync())
        {
            _consoleUI.ShowWelcomeScreen();
            
            AnsiConsole.MarkupLine($"[{WarningColor.ToMarkup()}]⚠️  Configuration not found or incomplete.[/]");
            AnsiConsole.MarkupLine($"[{DefaultTextColor.ToMarkup()}]Let's set up Aegis for your project![/]");
            AnsiConsole.WriteLine();
            
            var runSetup = AnsiConsole.Confirm(
                $"[{PrimaryAccent.ToMarkup()}]Would you like to run the setup wizard now?[/]", 
                defaultValue: true);
            
            if (runSetup)
            {
                try
                {
                    var config = await _setupWizard.RunSetupWizardAsync();
                    await _setupWizard.SaveConfigurationAsync(config);
                    
                    AnsiConsole.WriteLine();
                    _consoleUI.ShowSuccess("🎉 Setup completed successfully!");
                    _consoleUI.ShowInfo("You can now use all features of Aegis.");
                    
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[{DimTextColor.ToMarkup()}]Press any key to continue to the main menu...[/]");
                    Console.ReadKey(true);
                }
                catch (Exception ex)
                {
                    _consoleUI.ShowError("Setup failed", ex);
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[{DimTextColor.ToMarkup()}]Press any key to continue anyway...[/]");
                    Console.ReadKey(true);
                }
            }
            else
            {
                _consoleUI.ShowWarning("Skipping setup. Some features may not work properly without configuration.");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[{DimTextColor.ToMarkup()}]You can run setup later from the main menu.[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[{DimTextColor.ToMarkup()}]Press any key to continue...[/]");
                Console.ReadKey(true);
            }
        }
        
        while (true)
        {
            _consoleUI.ShowWelcomeScreen();
            
            var choice = ShowMainMenu();
            
            try
            {
                var shouldExit = await HandleMainMenuChoice(choice);
                if (shouldExit) break;
            }
            catch (Exception ex)
            {
                _consoleUI.ShowError("An error occurred", ex);
                AnsiConsole.MarkupLine($"[{DimTextColor.ToMarkup()}]Press any key to continue...[/]");
                Console.ReadKey(true);
            }
        }
        
        AnsiConsole.MarkupLine($"[{SuccessColor.ToMarkup()}]👋 Thanks for using Propmate Permission Manager![/]");
    }

    private string ShowMainMenu()
    {
        AnsiConsole.WriteLine();
        
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"[bold {PrimaryAccent.ToMarkup()}]🚀 What would you like to do?[/]")
                .PageSize(10)
                .MoreChoicesText($"[{DimTextColor.ToMarkup()}](Move up and down to reveal more options)[/]")
                .HighlightStyle(new Style(PrimaryAccent))
                .AddChoices(new[] {
                    "🎯 Generate Permissions",
                    "⚙️ Edit Configuration", 
                    "📋 List Available Permissions",
                    "📊 View Scan Results",
                    "🔧 Settings",
                    "❌ Quit"
                }));

        return choice;
    }

    private async Task<bool> HandleMainMenuChoice(string choice)
    {
        switch (choice)
        {
            case "🎯 Generate Permissions":
                await ShowGeneratePermissionsMenu();
                break;
                
            case "⚙️ Edit Configuration":
                await ShowEditConfigurationMenu();
                break;
                
            case "📋 List Available Permissions":
                await ShowListPermissionsMenu();
                break;
                
            case "📊 View Scan Results":
                await ShowViewResultsMenu();
                break;
                
            case "🔧 Settings":
                await ShowSettingsMenu();
                break;
                
            case "❌ Quit":
                return true; // Exit
        }
        
        return false; // Continue
    }

    private async Task ShowGeneratePermissionsMenu()
    {
        while (true)
        {
            AnsiConsole.Clear();
            _consoleUI.ShowAegisLogo();
            
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[bold {SuccessColor.ToMarkup()}]🎯 Generate Permissions[/]")
                    .HighlightStyle(new Style(SuccessColor))
                    .AddChoices(new[] {
                        "🚀 Scan All Projects",
                        "📁 Scan Specific Project",
                        "⬅️ Back to Main Menu"
                    }));

            switch (choice)
            {
                case "🚀 Scan All Projects":
                    await RunScanAllProjects();
                    break;
                    
                case "📁 Scan Specific Project":
                    await RunScanSpecificProject();
                    break;
                    
                case "⬅️ Back to Main Menu":
                    return; // Exit submenu
            }
            
            if (choice != "⬅️ Back to Main Menu")
            {
                AnsiConsole.WriteLine();
                var continueChoice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title($"[{PrimaryAccent.ToMarkup()}]What would you like to do?[/]")
                        .HighlightStyle(new Style(PrimaryAccent))
                        .AddChoices(new[] { "🔄 Continue in Generate Permissions", "⬅️ Back to Main Menu" }));
                
                if (continueChoice == "⬅️ Back to Main Menu")
                    return;
            }
        }
    }

    private async Task ShowEditConfigurationMenu()
    {
        while (true)
        {
            AnsiConsole.Clear();
            _consoleUI.ShowAegisLogo();
            
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[bold {WarningColor.ToMarkup()}]⚙️ Edit Configuration[/]")
                    .HighlightStyle(new Style(WarningColor))
                    .AddChoices(new[] {
                        "📂 Update Scan Path",
                        "📄 Configure Output Settings", 
                        "🎛️ Manage Conventions",
                        "🔄 Reset to Defaults",
                        "🔧 Run Setup Wizard",
                        "⬅️ Back to Main Menu"
                    }));

            switch (choice)
            {
                case "📂 Update Scan Path":
                    await UpdateScanPath();
                    break;
                    
                case "📄 Configure Output Settings":
                    await ConfigureOutputSettings();
                    break;
                    
                case "🎛️ Manage Conventions":
                    await ManageConventions();
                    break;
                    
                case "🔄 Reset to Defaults":
                    await ResetToDefaults();
                    break;
                    
                case "🔧 Run Setup Wizard":
                    await RunSetupWizard();
                    break;
                    
                case "⬅️ Back to Main Menu":
                    return;
            }
            
            if (choice != "⬅️ Back to Main Menu")
            {
                AnsiConsole.WriteLine();
                var continueChoice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title($"[{PrimaryAccent.ToMarkup()}]What would you like to do?[/]")
                        .HighlightStyle(new Style(PrimaryAccent))
                        .AddChoices(new[] { "🔄 Continue in Configuration", "⬅️ Back to Main Menu" }));
                
                if (continueChoice == "⬅️ Back to Main Menu")
                    return;
            }
        }
    }

    private async Task ShowListPermissionsMenu()
    {
        AnsiConsole.Clear();
        _consoleUI.ShowAegisLogo();
        
        var config = _configService.GetConfiguration();
        var outputFile = config.SyncPermissions.DefaultOutputPath ?? "permissions.json";
        
        if (!File.Exists(outputFile))
        {
            _consoleUI.ShowWarning($"No permissions file found: {outputFile}");
            _consoleUI.ShowInfo("Run a scan first to generate permissions.");
            
            AnsiConsole.WriteLine();
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[{DimTextColor.ToMarkup()}]What would you like to do?[/]")
                    .HighlightStyle(new Style(DimTextColor))
                    .AddChoices(new[] { "⬅️ Back to Main Menu" }));
            return;
        }
        
        try
        {
            var jsonContent = await File.ReadAllTextAsync(outputFile);
            var result = JsonSerializer.Deserialize<PermissionDiscoveryResult>(jsonContent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });
            
            if (result?.Projects?.Any() != true)
            {
                _consoleUI.ShowWarning("No permissions found in the results file.");
                
                AnsiConsole.WriteLine();
                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title($"[{DimTextColor.ToMarkup()}]What would you like to do?[/]")
                        .HighlightStyle(new Style(DimTextColor))
                        .AddChoices(new[] { "⬅️ Back to Main Menu" }));
                return;
            }
            
            // Ana permissions tablosu
            var permissionsTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(SuccessColor)
                .Title($"[bold {SuccessColor.ToMarkup()}]📋 Available Permissions ({outputFile})[/]")
                .AddColumn(new TableColumn("[bold]Permission Name[/]").LeftAligned())
                .AddColumn(new TableColumn("[bold]Project[/]").LeftAligned())
                .AddColumn(new TableColumn("[bold]HTTP Method[/]").Centered())
                .AddColumn(new TableColumn("[bold]Route[/]").LeftAligned())
                .AddColumn(new TableColumn("[bold]Description[/]").LeftAligned());

            foreach (var project in result.Projects)
            {
                foreach (var permission in project.DiscoveredPermissions)
                {
                    var httpMethodColor = permission.Metadata.HttpMethod?.ToUpper() switch
                    {
                        "GET" => SuccessColor.ToMarkup(),
                        "POST" => MutedBlue.ToMarkup(),
                        "PUT" => WarningColor.ToMarkup(),
                        "DELETE" => ErrorColor.ToMarkup(),
                        "PATCH" => SecondaryAccent.ToMarkup(),
                        _ => DimTextColor.ToMarkup()
                    };

                    permissionsTable.AddRow(
                        $"[bold {PrimaryAccent.ToMarkup()}]{permission.Name}[/]",
                        $"[{DefaultTextColor.ToMarkup()}]{project.Name}[/]",
                        $"[{httpMethodColor}]{permission.Metadata.HttpMethod}[/]",
                        $"[{DimTextColor.ToMarkup()}]{permission.Metadata.Route}[/]",
                        $"[{DimTextColor.ToMarkup()}]{permission.Description}[/]"
                    );
                }
            }

            AnsiConsole.Write(permissionsTable);
            
            // Özet bilgiler
            var totalPermissions = result.Projects.Sum(p => p.DiscoveredPermissions.Count);
            var projectCount = result.Projects.Count;
            
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[bold {SuccessColor.ToMarkup()}]📊 Summary:[/] {totalPermissions} permissions across {projectCount} projects");
            AnsiConsole.MarkupLine($"[{DimTextColor.ToMarkup()}]Generated: {result.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC[/]");
            
            AnsiConsole.WriteLine();
            var menuChoice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[{PrimaryAccent.ToMarkup()}]What would you like to do?[/]")
                    .HighlightStyle(new Style(PrimaryAccent))
                    .AddChoices(new[] { "⬅️ Back to Main Menu" }));
        }
        catch (Exception ex)
        {
            _consoleUI.ShowError($"Failed to read permissions file: {ex.Message}");
            
            AnsiConsole.WriteLine();
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[{DimTextColor.ToMarkup()}]What would you like to do?[/]")
                    .HighlightStyle(new Style(DimTextColor))
                    .AddChoices(new[] { "⬅️ Back to Main Menu" }));
        }
    }

    private async Task ShowViewResultsMenu()
    {
        AnsiConsole.Clear();
        _consoleUI.ShowAegisLogo();
        
        var config = _configService.GetConfiguration();
        var outputFile = config.SyncPermissions.DefaultOutputPath ?? "permissions.json";
        
        if (!File.Exists(outputFile))
        {
            _consoleUI.ShowWarning($"No scan results found: {outputFile}");
            _consoleUI.ShowInfo("Run a scan first to generate results.");
            
            AnsiConsole.WriteLine();
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[{DimTextColor.ToMarkup()}]What would you like to do?[/]")
                    .HighlightStyle(new Style(DimTextColor))
                    .AddChoices(new[] { "⬅️ Back to Main Menu" }));
            return;
        }
        
        try
        {
            var jsonContent = await File.ReadAllTextAsync(outputFile);
            var result = JsonSerializer.Deserialize<PermissionDiscoveryResult>(jsonContent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });
            
            if (result == null)
            {
                _consoleUI.ShowError("Failed to parse scan results.");
                
                AnsiConsole.WriteLine();
                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title($"[{DimTextColor.ToMarkup()}]What would you like to do?[/]")
                        .HighlightStyle(new Style(DimTextColor))
                        .AddChoices(new[] { "⬅️ Back to Main Menu" }));
                return;
            }
            
            // Scan özeti
            var summaryTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(PrimaryAccent)
                .Title($"[bold {PrimaryAccent.ToMarkup()}]📊 Last Scan Results ({outputFile})[/]")
                .AddColumn(new TableColumn("[bold]Metric[/]").LeftAligned())
                .AddColumn(new TableColumn("[bold]Count[/]").Centered())
                .AddColumn(new TableColumn("[bold]Status[/]").Centered());

            if (result.Summary != null)
            {
                summaryTable.AddRow("Total Endpoints", result.Summary.TotalEndpoints.ToString(), result.Summary.TotalEndpoints > 0 ? $"[{SuccessColor.ToMarkup()}]✓[/]" : $"[{DimTextColor.ToMarkup()}]-[/]");
                summaryTable.AddRow("Public Endpoints", result.Summary.PublicEndpoints.ToString(), result.Summary.PublicEndpoints > 0 ? $"[{WarningColor.ToMarkup()}]ℹ[/]" : $"[{DimTextColor.ToMarkup()}]-[/]");
                summaryTable.AddRow("Auth Only Endpoints", result.Summary.AuthOnlyEndpoints.ToString(), result.Summary.AuthOnlyEndpoints > 0 ? $"[{WarningColor.ToMarkup()}]⚠[/]" : $"[{DimTextColor.ToMarkup()}]-[/]");
                summaryTable.AddRow("Needs Permission", result.Summary.NeedsPermissionEndpoints.ToString(), result.Summary.NeedsPermissionEndpoints > 0 ? $"[{ErrorColor.ToMarkup()}]⚠[/]" : $"[{SuccessColor.ToMarkup()}]✓[/]");
                summaryTable.AddRow("Already Protected", result.Summary.AlreadyProtectedEndpoints.ToString(), result.Summary.AlreadyProtectedEndpoints > 0 ? $"[{SuccessColor.ToMarkup()}]✓[/]" : $"[{DimTextColor.ToMarkup()}]-[/]");
                summaryTable.AddRow("Generated Permissions", result.Summary.GeneratedPermissions.ToString(), result.Summary.GeneratedPermissions > 0 ? $"[{SuccessColor.ToMarkup()}]✓[/]" : $"[{DimTextColor.ToMarkup()}]-[/]");
            }

            AnsiConsole.Write(summaryTable);
            AnsiConsole.WriteLine();
            
            // Proje bazında permissions
            if (result.Projects?.Any() == true)
            {
                var projectsTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(MainBorderColor)
                    .Title($"[bold {PrimaryAccent.ToMarkup()}]📦 Projects Overview[/]")
                    .AddColumn(new TableColumn("[bold]Project[/]").LeftAligned())
                    .AddColumn(new TableColumn("[bold]Permissions[/]").Centered())
                    .AddColumn(new TableColumn("[bold]Status[/]").Centered());

                foreach (var project in result.Projects)
                {
                    var permissionCount = project.DiscoveredPermissions?.Count ?? 0;
                    var status = permissionCount > 0 ? $"[{SuccessColor.ToMarkup()}]✓[/]" : $"[{DimTextColor.ToMarkup()}]—[/]";
                    
                    projectsTable.AddRow(
                        $"[{DefaultTextColor.ToMarkup()}]{project.Name}[/]",
                        $"[bold {SuccessColor.ToMarkup()}]{permissionCount}[/]",
                        status
                    );
                }
                
                AnsiConsole.Write(projectsTable);
                AnsiConsole.WriteLine();
            }
            
            // Warnings varsa göster
            if (result.Summary?.Warnings?.Any() == true)
            {
                var warningsTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(WarningColor)
                    .Title($"[bold {WarningColor.ToMarkup()}]⚠️ Warnings[/]")
                    .AddColumn(new TableColumn("[bold]Type[/]").LeftAligned())
                    .AddColumn(new TableColumn("[bold]Message[/]").LeftAligned());

                foreach (var warning in result.Summary.Warnings)
                {
                    warningsTable.AddRow(
                        $"[{WarningColor.ToMarkup()}]{warning.Type}[/]",
                        $"[{DefaultTextColor.ToMarkup()}]{warning.Message}[/]"
                    );
                }
                
                AnsiConsole.Write(warningsTable);
                AnsiConsole.WriteLine();
            }
            
            // Meta bilgiler
            AnsiConsole.MarkupLine($"[{DimTextColor.ToMarkup()}]Tool Version: {result.ToolVersion}[/]");
            AnsiConsole.MarkupLine($"[{DimTextColor.ToMarkup()}]Generated: {result.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC[/]");
            AnsiConsole.MarkupLine($"[{DimTextColor.ToMarkup()}]Generated By: {result.GeneratedBy}[/]");
            
            AnsiConsole.WriteLine();
            var menuChoice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[{PrimaryAccent.ToMarkup()}]What would you like to do?[/]")
                    .HighlightStyle(new Style(PrimaryAccent))
                    .AddChoices(new[] { "⬅️ Back to Main Menu" }));
        }
        catch (Exception ex)
        {
            _consoleUI.ShowError($"Failed to read scan results: {ex.Message}");
            
            AnsiConsole.WriteLine();
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[{DimTextColor.ToMarkup()}]What would you like to do?[/]")
                    .HighlightStyle(new Style(DimTextColor))
                    .AddChoices(new[] { "⬅️ Back to Main Menu" }));
        }
    }

    private async Task ShowSettingsMenu()
    {
        while (true)
        {
            AnsiConsole.Clear();
            _consoleUI.ShowAegisLogo();
            
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[bold {MutedBlue.ToMarkup()}]🔧 Settings[/]")
                    .HighlightStyle(new Style(MutedBlue))
                    .AddChoices(new[] {
                        "👁️ View Current Configuration",
                        "🎨 Display Preferences",
                        "📊 Default Scan Options", 
                        "⬅️ Back to Main Menu"
                    }));

            switch (choice)
            {
                case "👁️ View Current Configuration":
                    await ViewCurrentConfiguration();
                    break;
                    
                case "🎨 Display Preferences":
                    await ConfigureDisplayPreferences();
                    break;
                    
                case "📊 Default Scan Options":
                    await ConfigureDefaultScanOptions();
                    break;
                    
                case "⬅️ Back to Main Menu":
                    return;
            }
            
            if (choice != "⬅️ Back to Main Menu")
            {
                AnsiConsole.WriteLine();
                var continueChoice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title($"[{PrimaryAccent.ToMarkup()}]What would you like to do?[/]")
                        .HighlightStyle(new Style(PrimaryAccent))
                        .AddChoices(new[] { "🔄 Continue in Settings", "⬅️ Back to Main Menu" }));
                
                if (continueChoice == "⬅️ Back to Main Menu")
                    return;
            }
        }
    }

    // Implementation methods
    private async Task RunScanAllProjects()
    {
        var config = _configService.GetConfiguration();
        var scanPath = await _configService.ResolveScanPathAsync(new ScanOptions(), config);
        
        // Configuration'dan output file'ı oku
        var outputFile = config.SyncPermissions.DefaultOutputPath ?? "permissions.json";
        
        var options = new ScanOptions 
        { 
            ScanPath = scanPath,
            Verbose = true,
            OutputFile = outputFile // Configuration'dan okunan dosya adı
        };
        
        var result = await _consoleUI.WithProgressAsync("Scanning all projects...", async progress =>
        {
            return await _scanService.ScanAsync(options, progress);
        });
        
        _consoleUI.ShowScanResults(result, options);
        
        // Her scan sonrası otomatik JSON kaydet
        await _outputService.WriteJsonAsync(result, options.OutputFile!);
        _consoleUI.ShowSuccess($"✅ Permissions saved to {options.OutputFile}");
    }

    private async Task RunScanSpecificProject()
    {
        // Önce mevcut projeleri listele
        var config = _configService.GetConfiguration();
        var baseScanPath = await _configService.ResolveScanPathAsync(new ScanOptions(), config);
        
        _consoleUI.ShowInfo("Discovering available projects...");
        
        List<string> availableProjects;
        try
        {
            var projectFiles = Directory.GetFiles(baseScanPath, "*.csproj", SearchOption.AllDirectories);
            availableProjects = projectFiles
                .Select(pf => new { 
                    Name = Path.GetFileNameWithoutExtension(pf),
                    Path = Path.GetDirectoryName(pf)!
                })
                .Select(p => $"{p.Name} ({Path.GetRelativePath(baseScanPath, p.Path)})")
                .ToList();
        }
        catch (Exception ex)
        {
            _consoleUI.ShowError($"Failed to discover projects: {ex.Message}");
            return;
        }
        
        if (!availableProjects.Any())
        {
            _consoleUI.ShowWarning($"No projects found in {baseScanPath}");
            return;
        }
        
        // Kullanıcıya projeleri göster ve seçtir
        var selectedProject = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"[{SuccessColor.ToMarkup()}]Select a project to scan:[/]")
                .PageSize(10)
                .MoreChoicesText($"[{DimTextColor.ToMarkup()}](Move up and down to see more projects)[/]")
                .HighlightStyle(new Style(SuccessColor))
                .AddChoices(availableProjects));
        
        // Seçilen projeden path'i çıkar
        var projectName = selectedProject.Split(' ')[0]; // "Auth.Api (Services/Auth.Api)" -> "Auth.Api"
        var projectPath = Directory.GetFiles(baseScanPath, $"{projectName}.csproj", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .FirstOrDefault();
        
        if (projectPath == null)
        {
            _consoleUI.ShowError($"Could not find project path for {projectName}");
            return;
        }
        
        // Configuration'dan output file'ı oku
        var outputFile = config.SyncPermissions.DefaultOutputPath ?? "permissions.json";
        
        var options = new ScanOptions 
        { 
            ScanPath = projectPath,
            Verbose = true,
            OutputFile = outputFile // Configuration'dan okunan dosya adı
        };
        
        var result = await _consoleUI.WithProgressAsync($"Scanning {projectName}...", async progress =>
        {
            return await _scanService.ScanAsync(options, progress);
        });
        
        _consoleUI.ShowScanResults(result, options);
        
        // Her scan sonrası otomatik JSON kaydet
        await _outputService.WriteJsonAsync(result, options.OutputFile!);
        _consoleUI.ShowSuccess($"✅ {projectName} permissions saved to {options.OutputFile}");
    }

    private async Task UpdateScanPath()
    {
        var config = _configService.GetConfiguration();
        var currentPath = config.SyncPermissions.DefaultScanPath;
        AnsiConsole.MarkupLine($"[{DimTextColor.ToMarkup()}]Current scan path: {currentPath ?? "Not set"}[/]");
        AnsiConsole.WriteLine();
        
        AnsiConsole.MarkupLine("[yellow]Press any key to open folder browser...[/]");
        Console.ReadKey(true);
        
        var selectedPath = await _folderBrowser.SelectFolderAsync(
            currentPath ?? Directory.GetCurrentDirectory(),
            "Select new scan path for .NET projects");
        
        if (selectedPath == null)
        {
            _consoleUI.ShowInfo("Path update cancelled.");
            return;
        }
        
        try
        {
            // Path'i validate et
            var resolvedPath = Path.GetFullPath(selectedPath);
            if (!Directory.Exists(resolvedPath))
            {
                _consoleUI.ShowError($"Directory does not exist: {resolvedPath}");
                return;
            }
            
            var projectFiles = Directory.GetFiles(resolvedPath, "*.csproj", SearchOption.AllDirectories);
            if (projectFiles.Length == 0)
            {
                _consoleUI.ShowWarning($"No .csproj files found in: {resolvedPath}");
                var proceed = AnsiConsole.Confirm($"[{WarningColor.ToMarkup()}]Continue anyway?[/]");
                if (!proceed) return;
            }
            else
            {
                AnsiConsole.MarkupLine($"[{DimTextColor.ToMarkup()}]Found {projectFiles.Length} project(s) ✅[/]");
            }
            
            // Configuration'ı güncelle
            config.SyncPermissions.DefaultScanPath = selectedPath;
            await _configService.UpdateConfigurationAsync(config);
            
            _consoleUI.ShowSuccess($"✅ Scan path updated to: {selectedPath}");
        }
        catch (Exception ex)
        {
            _consoleUI.ShowError($"Failed to update scan path: {ex.Message}");
        }
    }

    private async Task ConfigureOutputSettings()
    {
        var config = _configService.GetConfiguration();
        var currentOutputFile = config.SyncPermissions.DefaultOutputPath;
        var currentVerbose = config.SyncPermissions.Verbose;
        
        AnsiConsole.MarkupLine($"[{DimTextColor.ToMarkup()}]Current output file: {currentOutputFile ?? "Console only"}[/]");
        AnsiConsole.MarkupLine($"[{DimTextColor.ToMarkup()}]Current verbose mode: {currentVerbose}[/]");
        AnsiConsole.WriteLine();
        
        var updateOutputFile = AnsiConsole.Confirm($"[{WarningColor.ToMarkup()}]Update default output file?[/]");
        if (updateOutputFile)
        {
            var newOutputFile = AnsiConsole.Ask<string>(
                $"[{SuccessColor.ToMarkup()}]Enter new default output file[/] [{DimTextColor.ToMarkup()}](or empty for console only)[/]:", 
                currentOutputFile ?? "");
                
            config.SyncPermissions.DefaultOutputPath = string.IsNullOrWhiteSpace(newOutputFile) ? null : newOutputFile;
        }
        
        var updateVerbose = AnsiConsole.Confirm($"[{WarningColor.ToMarkup()}]Update verbose mode default?[/]");
        if (updateVerbose)
        {
            config.SyncPermissions.Verbose = AnsiConsole.Confirm(
                $"[{SuccessColor.ToMarkup()}]Enable verbose mode by default?[/]", 
                currentVerbose);
        }
        
        if (updateOutputFile || updateVerbose)
        {
            try
            {
                await _configService.UpdateConfigurationAsync(config);
                _consoleUI.ShowSuccess("✅ Output settings updated successfully!");
            }
            catch (Exception ex)
            {
                _consoleUI.ShowError($"Failed to update output settings: {ex.Message}");
            }
        }
        else
        {
            _consoleUI.ShowInfo("No changes made.");
        }
    }

    private async Task ManageConventions()
    {
        _consoleUI.ShowInfo("Feature coming soon: Convention Management");
    }

    private async Task ResetToDefaults()
    {
        var confirm = AnsiConsole.Confirm($"[{WarningColor.ToMarkup()}]Are you sure you want to reset all settings to defaults?[/]");
        if (!confirm) return;
        
        try
        {
            // Default configuration oluştur
            var defaultConfig = new AppConfig
            {
                SyncPermissions = new SyncPermissionsConfig
                {
                    DefaultScanPath = "../../src/Services",
                    DefaultOutputPath = "permissions.json",
                    Verbose = false,
                    MissingOnly = false,
                    AutoGenerate = false,
                    AcceptAllSuggestedPermissions = false
                },
                Conventions = new ConventionsConfig
                {
                    HttpMethodActions = new Dictionary<string, string>
                    {
                        { "GET", "Read" },
                        { "POST", "Create" },
                        { "PUT", "Update" },
                        { "PATCH", "Update" },
                        { "DELETE", "Delete" }
                    }
                }
            };
            
            await _configService.UpdateConfigurationAsync(defaultConfig);
            _consoleUI.ShowSuccess("✅ Configuration reset to defaults successfully!");
        }
        catch (Exception ex)
        {
            _consoleUI.ShowError($"Failed to reset configuration: {ex.Message}");
        }
    }

    private async Task RunSetupWizard()
    {
        var config = await _setupWizard.RunSetupWizardAsync();
        await _setupWizard.SaveConfigurationAsync(config);
        _consoleUI.ShowSuccess("Setup wizard completed successfully!");
    }

    private async Task ViewCurrentConfiguration()
    {
        var config = _configService.GetConfiguration();
        
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(PrimaryAccent)
            .Title($"[bold {PrimaryAccent.ToMarkup()}]📋 Current Configuration[/]")
            .AddColumn("[bold]Setting[/]")
            .AddColumn("[bold]Value[/]");

        table.AddRow("Scan Path", config.SyncPermissions.DefaultScanPath ?? "Not set");
        table.AddRow("Output File", config.SyncPermissions.DefaultOutputPath ?? "Console only");
        table.AddRow("Verbose Mode", config.SyncPermissions.Verbose.ToString());
        table.AddRow("Auto-Accept", config.SyncPermissions.AcceptAllSuggestedPermissions.ToString());
        table.AddRow("Missing Only", config.SyncPermissions.MissingOnly.ToString());

        AnsiConsole.Write(table);
    }

    private async Task ConfigureDisplayPreferences()
    {
        _consoleUI.ShowInfo("Feature coming soon: Display Preferences");
    }

    private async Task ConfigureDefaultScanOptions()
    {
        _consoleUI.ShowInfo("Feature coming soon: Default Scan Options");
    }
} 