using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console;
using Spectre.Console.Cli;
using SyncPermissions.Models;
using SyncPermissions.Services;
using System.Text;
using System.Text.Json;

// Configure console for emoji and Unicode support
try
{
    Console.OutputEncoding = Encoding.UTF8;
    Console.InputEncoding = Encoding.UTF8;
}
catch
{
    // Ignore encoding errors on unsupported platforms
}

// Check if we should run in interactive mode (no arguments) or CLI mode (with arguments)
if (args.Length == 0)
{
    // Interactive menu mode
    return await AppHost.RunInteractiveModeAsync();
}
else
{
    // Traditional CLI mode
    return await AppHost.RunCliModeAsync(args);
}

public class AppHost
{
    public static async Task<int> RunInteractiveModeAsync()
    {
        try
        {
            var services = CreateServices();
            var menuService = services.GetRequiredService<IInteractiveMenuService>();
            
            await menuService.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
            return 1;
        }
    }

    public static async Task<int> RunCliModeAsync(string[] args)
    {
        var app = new CommandApp();

        app.Configure(config =>
        {
            config.SetApplicationName("aegis");
            config.AddCommand<ScanCommand>("scan")
                .WithDescription("Scan projects for permission requirements")
                .WithExample(new[] { "scan", "--path", "./src/Services" })
                .WithExample(new[] { "scan", "--path", "./src/Services", "--output", "permissions.json" })
                .WithExample(new[] { "scan", "--path", "./src/Services", "--verbose", "--missing-only" });
                
            config.AddCommand<SetupCommand>("setup")
                .WithDescription("Run interactive setup wizard")
                .WithExample(new[] { "setup" });
                
            config.AddCommand<ValidateCommand>("validate")
                .WithDescription("Validate existing permissions configuration")
                .WithExample(new[] { "validate", "--path", "./src/Services" });
        });

        return await app.RunAsync(args);
    }

    public static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConsoleUIService, ConsoleUIService>();
        services.AddSingleton<ISetupWizardService, SetupWizardService>();
        services.AddSingleton<IPermissionScanService, PermissionScanService>();
        services.AddSingleton<IProjectScanner, ProjectScanner>();
        services.AddSingleton<IEndpointDiscoverer, EndpointDiscoverer>();
        services.AddSingleton<IPermissionGenerator, PermissionGenerator>();
        services.AddSingleton<IOutputService, OutputService>();
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<ICSharpGeneratorService, CSharpGeneratorService>();
        services.AddSingleton<IInteractiveMenuService, InteractiveMenuService>();
        services.AddSingleton<IFolderBrowserService, FolderBrowserService>();
        
        return services.BuildServiceProvider();
    }
}

public sealed class ScanCommand : AsyncCommand<ScanCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-p|--path")]
        public string? ScanPath { get; set; }

        [CommandOption("-o|--output")]
        public string? OutputFile { get; set; }

        [CommandOption("-v|--verbose")]
        public bool Verbose { get; set; }

        [CommandOption("-m|--missing-only")]
        public bool MissingOnly { get; set; }

        [CommandOption("-g|--auto-generate")]
        public bool AutoGenerate { get; set; }

        [CommandOption("-a|--accept-all")]
        public bool AcceptAllSuggestedPermissions { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            var services = AppHost.CreateServices();
            var consoleUI = services.GetRequiredService<IConsoleUIService>();
            var setupWizard = services.GetRequiredService<ISetupWizardService>();
            var scanService = services.GetRequiredService<IPermissionScanService>();
            var configService = services.GetRequiredService<IConfigurationService>();
            var outputService = services.GetRequiredService<IOutputService>();

            // Show welcome screen
            consoleUI.ShowWelcomeScreen();

            // Check if setup is needed
            if (await setupWizard.ShouldRunSetupAsync())
            {
                AnsiConsole.MarkupLine("[yellow]⚠️  Configuration not found. Running setup wizard...[/]");
                AnsiConsole.WriteLine();
                
                var config = await setupWizard.RunSetupWizardAsync();
                await setupWizard.SaveConfigurationAsync(config);
                
                AnsiConsole.WriteLine();
                consoleUI.ShowSuccess("Setup completed! You can now run scans.");
                return 0;
            }

            // Load configuration
            var appConfig = configService.GetConfiguration();
            var scanOptions = CreateScanOptions(settings, appConfig);

            // Resolve scan path
            scanOptions.ScanPath = await configService.ResolveScanPathAsync(scanOptions, appConfig);

            // Run scan with progress
            var result = await consoleUI.WithProgressAsync("Scanning projects for permissions...", async progress =>
            {
                return await scanService.ScanAsync(scanOptions, progress);
            });

            // Show results
            consoleUI.ShowScanResults(result, scanOptions);

            // Handle mismatched permissions
            await scanService.HandleMismatchedPermissionsAsync(result, scanOptions);

            // Save to file if requested
            if (!string.IsNullOrEmpty(scanOptions.OutputFile))
            {
                await outputService.WriteJsonAsync(result, scanOptions.OutputFile);
                consoleUI.ShowSuccess($"Results saved to {scanOptions.OutputFile}");

                // Handle C# file generation after JSON is saved
                await scanService.HandleCSharpFileGenerationAsync(result, scanOptions, appConfig);
            }

            return 0;
        }
        catch (Exception ex)
        {
            var consoleUI = AppHost.CreateServices().GetRequiredService<IConsoleUIService>();
            consoleUI.ShowError("An error occurred during scanning", ex);
            return 1;
        }
    }

    private static ScanOptions CreateScanOptions(Settings settings, AppConfig appConfig)
    {
        return new ScanOptions
        {
            ScanPath = settings.ScanPath ?? appConfig.SyncPermissions.DefaultScanPath ?? "",
            OutputFile = settings.OutputFile ?? appConfig.SyncPermissions.DefaultOutputPath,
            Verbose = settings.Verbose || appConfig.SyncPermissions.Verbose,
            MissingOnly = settings.MissingOnly || appConfig.SyncPermissions.MissingOnly,
            AutoGenerate = settings.AutoGenerate || appConfig.SyncPermissions.AutoGenerate,
            AcceptAllSuggestedPermissions = settings.AcceptAllSuggestedPermissions || appConfig.SyncPermissions.AcceptAllSuggestedPermissions
        };
    }
}

public sealed class SetupCommand : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        try
        {
            var services = AppHost.CreateServices();
            var consoleUI = services.GetRequiredService<IConsoleUIService>();
            var setupWizard = services.GetRequiredService<ISetupWizardService>();

            consoleUI.ShowWelcomeScreen();

            var config = await setupWizard.RunSetupWizardAsync();
            await setupWizard.SaveConfigurationAsync(config);

            consoleUI.ShowSuccess("Setup completed successfully!");
            return 0;
        }
        catch (Exception ex)
        {
            var consoleUI = AppHost.CreateServices().GetRequiredService<IConsoleUIService>();
            consoleUI.ShowError("An error occurred during setup", ex);
            return 1;
        }
    }
}

public sealed class ValidateCommand : AsyncCommand<ValidateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-p|--path")]
        public string? ScanPath { get; set; }

        [CommandOption("-v|--verbose")]
        public bool Verbose { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            var services = AppHost.CreateServices();
            var consoleUI = services.GetRequiredService<IConsoleUIService>();

            consoleUI.ShowWelcomeScreen();
            consoleUI.ShowInfo("Validation feature coming soon...");

            return 0;
        }
        catch (Exception ex)
        {
            var consoleUI = AppHost.CreateServices().GetRequiredService<IConsoleUIService>();
            consoleUI.ShowError("An error occurred during validation", ex);
            return 1;
        }
    }
}
