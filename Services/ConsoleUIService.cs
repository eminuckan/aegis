using Spectre.Console;
using SyncPermissions.Models;
using System.Text;
using Figgle;

namespace SyncPermissions.Services;

public interface IConsoleUIService
{
    void ShowWelcomeScreen();
    void ShowAegisLogo(); // Yeni metod
    void ShowScanResults(PermissionDiscoveryResult result, ScanOptions options);
    void ShowError(string message, Exception? exception = null);
    void ShowWarning(string message);
    void ShowSuccess(string message);
    void ShowInfo(string message);
    Task<T> WithProgressAsync<T>(string description, Func<ProgressTask, Task<T>> operation);
    Task<bool> PromptPermissionCorrectionAsync(EndpointInfo endpoint, string projectName);
    void ShowMismatchedPermissionsTable(List<(string Project, EndpointInfo Endpoint)> mismatchedPermissions);
    bool PromptGenerateCSharpFile();
}

public class ConsoleUIService : IConsoleUIService
{
    // Shadow gradient colors as the base theme
    private static readonly Color PrimaryAccent = new Color(180, 120, 60); // Warm brown-orange
    private static readonly Color SecondaryAccent = new Color(140, 80, 80); // Muted red-brown
    private static readonly Color MutedBlue = new Color(95, 115, 140); // Cooler blue-grey
    private static readonly Color MainBorderColor = new Color(80, 50, 20); // Dark Brown (shadow from)
    private static readonly Color SuccessColor = new Color(120, 140, 80); // Muted olive green
    private static readonly Color WarningColor = new Color(160, 100, 50); // Warm orange-brown
    private static readonly Color ErrorColor = new Color(140, 60, 60); // Muted red
    private static readonly Color DefaultTextColor = new Color(200, 190, 180); // Warm off-white
    private static readonly Color DimTextColor = new Color(120, 100, 80); // Warm dim
    
    public void ShowAegisLogo()
    {
        // Add top padding for consistent positioning
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
        
        // Beautiful gradient AEGIS logo
        WriteGradientFiglet("AEGIS", new Color(255, 180, 84), new Color(224, 85, 122));
        
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"   [bold {PrimaryAccent.ToMarkup()}]🛡️  PERMISSION MANAGEMENT TOOL  🛡️[/]");
        AnsiConsole.WriteLine();
    }
    
    public void ShowWelcomeScreen()
    {
        AnsiConsole.Clear();
        
        ShowAegisLogo();
        
        // Simple informational panel
        var infoPanel = new Panel(
                new Markup($"[bold {WarningColor.ToMarkup()}]⚙️ Internal Permission Management Tool[/]\n\n" +
                           $"[{DimTextColor.ToMarkup()}]• Analyzes .NET endpoints using IEndpoint pattern[/]\n" +
                           $"[{DimTextColor.ToMarkup()}]• Generates convention-based permissions[/]\n" +
                           $"[{DimTextColor.ToMarkup()}]• Validates authorization requirements[/]\n" +
                           $"[{DimTextColor.ToMarkup()}]• Automatic configuration management[/]\n\n" +
                           $"[bold {SecondaryAccent.ToMarkup()}]AEGIS - Internal tool for Propmate microservices[/]"))
            .Border(BoxBorder.Rounded)
            .BorderColor(MainBorderColor)
            .Padding(1, 1);
        
        AnsiConsole.Write(infoPanel);
        AnsiConsole.WriteLine();
    }

    public void ShowScanResults(PermissionDiscoveryResult result, ScanOptions options)
    {
        if (!string.IsNullOrEmpty(options.OutputFile))
        {
            ShowCompactResults(result);
        }
        else
        {
            ShowDetailedResults(result, options);
        }
    }

    private void ShowCompactResults(PermissionDiscoveryResult result)
    {
        var summary = result.Summary;
        
        // Create summary table
        var summaryTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(PrimaryAccent)
            .Title($"[bold {PrimaryAccent.ToMarkup()}]Scan Summary[/]")
            .AddColumn(new TableColumn("[bold]Endpoint Type[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Count[/]").Centered())
            .AddColumn(new TableColumn("[bold]Status[/]").Centered());

        summaryTable.AddRow(
            $"[{DefaultTextColor.ToMarkup()}]Public Endpoints[/]", 
            $"[{DimTextColor.ToMarkup()}]{summary.PublicEndpoints}[/]",
            summary.PublicEndpoints > 0 ? $"[{SuccessColor.ToMarkup()}]✓[/]" : $"[{DimTextColor.ToMarkup()}]-[/]"
        );
        
        summaryTable.AddRow(
            $"[{WarningColor.ToMarkup()}]Auth Only Endpoints[/]", 
            $"[{WarningColor.ToMarkup()}]{summary.AuthOnlyEndpoints}[/]",
            summary.AuthOnlyEndpoints > 0 ? $"[{WarningColor.ToMarkup()}]⚠[/]" : $"[{DimTextColor.ToMarkup()}]-[/]"
        );
        
        summaryTable.AddRow(
            $"[{ErrorColor.ToMarkup()}]Needs Permission[/]", 
            $"[{ErrorColor.ToMarkup()}]{summary.NeedsPermissionEndpoints}[/]",
            summary.NeedsPermissionEndpoints > 0 ? $"[{ErrorColor.ToMarkup()}]⚠[/]" : $"[{SuccessColor.ToMarkup()}]✓[/]"
        );
        
        summaryTable.AddRow(
            $"[{SuccessColor.ToMarkup()}]Already Protected[/]", 
            $"[{SuccessColor.ToMarkup()}]{summary.AlreadyProtectedEndpoints}[/]",
            summary.AlreadyProtectedEndpoints > 0 ? $"[{SuccessColor.ToMarkup()}]✓[/]" : $"[{DimTextColor.ToMarkup()}]-[/]"
        );
        
        summaryTable.AddRow(
            $"[bold {PrimaryAccent.ToMarkup()}]Generated Permissions[/]", 
            $"[bold {PrimaryAccent.ToMarkup()}]{summary.GeneratedPermissions}[/]",
            summary.GeneratedPermissions > 0 ? $"[{SuccessColor.ToMarkup()}]✓[/]" : $"[{DimTextColor.ToMarkup()}]-[/]"
        );

        AnsiConsole.Write(summaryTable);
        AnsiConsole.WriteLine();

        // Show generated permissions table if any exist
        ShowPermissionsTable(result);
        
        if (summary.Warnings.Any())
        {
            ShowWarnings(summary.Warnings);
        }
    }

    private void ShowDetailedResults(PermissionDiscoveryResult result, ScanOptions options)
    {
        // Projects overview table
        ShowProjectsOverviewTable(result);
        
        // Generated permissions by project
        foreach (var project in result.Projects.Where(p => p.DiscoveredPermissions.Any()))
        {
            ShowProjectPermissionsTable(project, options.Verbose);
        }
        
        // Summary
        ShowCompactResults(result);
    }

    private void ShowProjectsOverviewTable(PermissionDiscoveryResult result)
    {
        var projectsTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(MainBorderColor)
            .Title($"[bold {PrimaryAccent.ToMarkup()}]Discovered Projects[/]")
            .AddColumn(new TableColumn("[bold]Project Name[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Generated Permissions[/]").Centered())
            .AddColumn(new TableColumn("[bold]Status[/]").Centered());

        foreach (var project in result.Projects)
        {
            var permissionCount = project.DiscoveredPermissions.Count;
            var statusIcon = permissionCount > 0 ? $"[{SuccessColor.ToMarkup()}]✓[/]" : $"[{DimTextColor.ToMarkup()}]—[/]";
            
            projectsTable.AddRow(
                $"[{DefaultTextColor.ToMarkup()}]{project.Name}[/]",
                $"[bold {SuccessColor.ToMarkup()}]{permissionCount}[/]",
                statusIcon
            );
        }
        
        AnsiConsole.Write(projectsTable);
        AnsiConsole.WriteLine();
    }

    private void ShowPermissionsTable(PermissionDiscoveryResult result)
    {
        var allPermissions = result.Projects.SelectMany(p => 
            p.DiscoveredPermissions.Select(perm => new { Project = p.Name, Permission = perm })
        ).ToList();

        if (!allPermissions.Any()) return;

        var permissionsTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(SuccessColor)
            .Title($"[bold {SuccessColor.ToMarkup()}]Generated Permissions[/]")
            .AddColumn(new TableColumn("[bold]Permission Name[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Project[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Endpoint[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Resource[/]").Centered())
            .AddColumn(new TableColumn("[bold]Action[/]").Centered());

        foreach (var item in allPermissions)
        {
            var perm = item.Permission;
            var httpMethodColor = perm.Metadata.HttpMethod?.ToUpper() switch
            {
                "GET" => SuccessColor.ToMarkup(),
                "POST" => MutedBlue.ToMarkup(), 
                "PUT" => WarningColor.ToMarkup(),
                "DELETE" => ErrorColor.ToMarkup(),
                "PATCH" => SecondaryAccent.ToMarkup(),
                _ => DimTextColor.ToMarkup()
            };

            var endpointInfo = $"[{httpMethodColor}]{perm.Metadata.HttpMethod}[/] [{DimTextColor.ToMarkup()}]{perm.Metadata.Route}[/]";

            // Parse resource and action from permission name (e.g., "Users.Create" -> "Users", "Create")
            var parts = perm.Name.Split('.');
            var resource = parts.Length > 0 ? parts[0] : "—";
            var action = parts.Length > 1 ? parts[1] : "—";

            permissionsTable.AddRow(
                $"[bold {PrimaryAccent.ToMarkup()}]{perm.Name}[/]",
                $"[{DefaultTextColor.ToMarkup()}]{item.Project}[/]",
                endpointInfo,
                $"[{DefaultTextColor.ToMarkup()}]{resource}[/]",
                $"[{DefaultTextColor.ToMarkup()}]{action}[/]"
            );
        }

        AnsiConsole.Write(permissionsTable);
        AnsiConsole.WriteLine();
    }

    private void ShowProjectPermissionsTable(ProjectScanResult project, bool verbose)
    {
        var permissionsTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(SuccessColor)
            .Title($"[bold {SuccessColor.ToMarkup()}]{project.Name} - Generated Permissions[/]")
            .AddColumn(new TableColumn("[bold]Permission Name[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Description[/]").LeftAligned());

        if (verbose)
        {
            permissionsTable.AddColumn(new TableColumn("[bold]HTTP Method[/]").Centered());
            permissionsTable.AddColumn(new TableColumn("[bold]Route[/]").LeftAligned());
        }

        foreach (var permission in project.DiscoveredPermissions)
        {
            if (verbose)
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
                    $"[{DimTextColor.ToMarkup()}]{permission.Description}[/]",
                    $"[{httpMethodColor}]{permission.Metadata.HttpMethod}[/]",
                    $"[{DimTextColor.ToMarkup()}]{permission.Metadata.Route}[/]"
                );
            }
            else
            {
                permissionsTable.AddRow(
                    $"[bold {PrimaryAccent.ToMarkup()}]{permission.Name}[/]",
                    $"[{DimTextColor.ToMarkup()}]{permission.Description}[/]"
                );
            }
        }
        
        AnsiConsole.Write(permissionsTable);
        AnsiConsole.WriteLine();
    }

    private void ShowWarnings(List<ScanWarning> warnings)
    {
        var warningsTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(WarningColor)
            .Title($"[bold {WarningColor.ToMarkup()}]Warnings[/]")
            .AddColumn(new TableColumn("[bold]Type[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Endpoint[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Message[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Suggestion[/]").LeftAligned());

        foreach (var warning in warnings)
        {
            warningsTable.AddRow(
                $"[{WarningColor.ToMarkup()}]{warning.Type}[/]",
                $"[{DimTextColor.ToMarkup()}]{warning.Endpoint}[/]",
                $"[{DefaultTextColor.ToMarkup()}]{warning.Message}[/]",
                $"[{DimTextColor.ToMarkup()}]{warning.Suggestion ?? "—"}[/]"
            );
        }
        
        AnsiConsole.Write(warningsTable);
        AnsiConsole.WriteLine();
    }

    public void ShowError(string message, Exception? exception = null)
    {
        var panel = new Panel($"[{ErrorColor.ToMarkup()}]{message}[/]")
            .Border(BoxBorder.Heavy)
            .BorderColor(ErrorColor)
            .Header($"[bold {ErrorColor.ToMarkup()}] Error [/]");
            
        AnsiConsole.Write(panel);
        
        if (exception != null)
        {
            AnsiConsole.WriteException(exception);
        }
    }

    public void ShowWarning(string message)
    {
        AnsiConsole.MarkupLine($"[{WarningColor.ToMarkup()}]{message}[/]");
    }

    public void ShowSuccess(string message)
    {
        AnsiConsole.MarkupLine($"[{SuccessColor.ToMarkup()}]{message}[/]");
    }

    public void ShowInfo(string message)
    {
        AnsiConsole.MarkupLine($"[{PrimaryAccent.ToMarkup()}]{message}[/]");
    }

    public async Task<T> WithProgressAsync<T>(string description, Func<ProgressTask, Task<T>> operation)
    {
        // Clear screen and show logo first
        AnsiConsole.Clear();
        ShowAegisLogo();
        
        // Add some spacing before progress bar
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
        
        var result = await AnsiConsole.Progress()
            .Columns(new ProgressColumn[] 
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn(),
            })
            .HideCompleted(false)
            .AutoClear(false)
            .StartAsync(async ctx =>
            {
                // Create a single progress task
                var progressTask = ctx.AddTask(description, maxValue: 100);
                
                // Suppress console output during progress to keep bar position stable
                var originalOut = Console.Out;
                var originalError = Console.Error;
                
                try
                {
                    // Redirect console output to null to prevent progress bar from moving
                    Console.SetOut(TextWriter.Null);
                    Console.SetError(TextWriter.Null);
                    
                    // Pass the progress task directly to the operation
                    return await operation(progressTask);
                }
                finally
                {
                    // Restore console output
                    Console.SetOut(originalOut);
                    Console.SetError(originalError);
                    
                    // Ensure progress reaches 100%
                    progressTask.Value = 100;
                }
            });
        
        // Small delay to let progress bar render properly at 100%
        await Task.Delay(500);
        
        // Add some spacing after progress bar
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
        
        return result;
    }

    public async Task<bool> PromptPermissionCorrectionAsync(EndpointInfo endpoint, string projectName)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold {WarningColor.ToMarkup()}]Permission Mismatch Found[/]");
        AnsiConsole.MarkupLine($"Project: [{PrimaryAccent.ToMarkup()}]{projectName}[/]");
        AnsiConsole.MarkupLine($"Endpoint: [{DefaultTextColor.ToMarkup()}]{endpoint.HttpMethod} {endpoint.Route}[/]");
        AnsiConsole.MarkupLine($"Current Permission: [{ErrorColor.ToMarkup()}]{endpoint.ExistingPermission}[/]");
        AnsiConsole.MarkupLine($"Suggested Permission: [{SuccessColor.ToMarkup()}]{endpoint.SuggestedPermission}[/]");
        AnsiConsole.WriteLine();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"[{PrimaryAccent.ToMarkup()}]What would you like to do?[/]")
                .HighlightStyle(new Style(PrimaryAccent))
                .AddChoices(new[] {
                    "Accept suggested permission",
                    "Keep current permission", 
                    "Enter custom permission"
                }));

        switch (choice)
        {
            case "Accept suggested permission":
                endpoint.ExistingPermission = endpoint.SuggestedPermission;
                endpoint.AuthorizationStatus = EndpointAuthorizationStatus.AlreadyProtected;
                return true;
                
            case "Keep current permission":
                endpoint.AuthorizationStatus = EndpointAuthorizationStatus.AlreadyProtected;
                return false;
                
            case "Enter custom permission":
                var customPermission = AnsiConsole.Ask<string>(
                    $"[{SuccessColor.ToMarkup()}]Enter custom permission name:[/]",
                    endpoint.ExistingPermission ?? "");
                endpoint.ExistingPermission = customPermission;
                endpoint.SuggestedPermission = customPermission;
                endpoint.AuthorizationStatus = EndpointAuthorizationStatus.AlreadyProtected;
                return true;
                
            default:
                return false;
        }
    }

    public void ShowMismatchedPermissionsTable(List<(string Project, EndpointInfo Endpoint)> mismatchedPermissions)
    {
        if (!mismatchedPermissions.Any()) return;

        var mismatchedTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(ErrorColor)
            .Title($"[bold {ErrorColor.ToMarkup()}]Mismatched Permissions - Action Required[/]")
            .AddColumn(new TableColumn("[bold]Project[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Endpoint[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Current Permission[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Suggested Permission[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]File Path[/]").LeftAligned());

        foreach (var (projectName, endpoint) in mismatchedPermissions)
        {
            mismatchedTable.AddRow(
                $"[{DefaultTextColor.ToMarkup()}]{projectName}[/]",
                $"[{WarningColor.ToMarkup()}]{endpoint.HttpMethod} {endpoint.Route}[/]",
                $"[{ErrorColor.ToMarkup()}]{endpoint.ExistingPermission ?? "—"}[/]",
                $"[{SuccessColor.ToMarkup()}]{endpoint.SuggestedPermission ?? "—"}[/]",
                $"[{DimTextColor.ToMarkup()}]{endpoint.FilePath}[/]"
            );
        }

        AnsiConsole.Write(mismatchedTable);
        
        var actionPanel = new Panel(
            $"[{WarningColor.ToMarkup()}]Action Required:[/]\n" +
            $"• Update RequirePermission() calls in the listed endpoints\n" +
            $"• Use the suggested permission names to follow conventions\n" +
            $"• Or mark as custom permissions if intentionally different")
            .Border(BoxBorder.Rounded)
            .BorderColor(WarningColor)
            .Header($"[bold {WarningColor.ToMarkup()}] Next Steps [/]")
            .Padding(1, 1);
            
        AnsiConsole.Write(actionPanel);
        AnsiConsole.WriteLine();
    }

    private void WriteGradientFiglet(string text, Color from, Color to)
    {
        // The correct "AEGIS" ASCII art.
    var lines = new[]
    {
        " ████████████  ██████████████  ██████████████  ████████  ██████████████",
        " ████████████  ██████████████  ██████████████    ████    ██████████████",
        " ████    ████  ████            ████              ████    ████",
        " ████    ████  ████            ████              ████    ████",
        " ████████████  ███████████     ████    ██████    ████    ██████████████",
        " ████████████  ███████████     ████    ██████    ████    ██████████████",
        " ████    ████  ████            ████      ████    ████              ████",
        " ████    ████  ████            ████      ████    ████              ████",
        " ████    ████  ██████████████  ██████████████    ████    ██████████████",
        " ████    ████  ██████████████  ██████████████  ████████  ██████████████"
    };

        var height = lines.Length;
        var width = lines.Max(l => l.Length);
        
        // Define a darker gradient for the shadow, as requested.
        var shadowFrom = new Color(80, 50, 20);
        var shadowTo = new Color(70, 25, 40);
        
        // The final grid will be smaller to accommodate the reduced shadow offset.
        var finalHeight = height + 1;
        var finalWidth = width + 1;

        for (var y = 0; y < finalHeight; y++)
        {   
            var sb = new StringBuilder(finalWidth);
            for (var x = 0; x < finalWidth; x++)
            {
                // Main text pixel coordinates
                var mainTextX = x;
                var mainTextY = y;

                // Shadow pixel coordinates (main text shifted right by 1, no vertical shift)
                var shadowOriginX = x - 1;
                var shadowOriginY = y - 1;

                bool isMainTextPixel = mainTextY >= 0 && mainTextY < height && 
                                     mainTextX >= 0 && mainTextX < lines[mainTextY].Length && 
                                     lines[mainTextY][mainTextX] != ' ';
                                     
                bool isShadowPixel = shadowOriginY >= 0 && shadowOriginY < height &&
                                     shadowOriginX >= 0 && shadowOriginX < lines[shadowOriginY].Length &&
                                     lines[shadowOriginY][shadowOriginX] != ' ';

                if (isMainTextPixel)
                {
                    double t = (double)mainTextX / width;
                    var gradientColor = Lerp(from, to, t);
                    
                    var originalChar = lines[mainTextY][mainTextX];
                    sb.Append($"[{gradientColor.ToMarkup()}]{originalChar}[/]");
                }
                else if (isShadowPixel)
                {
                    // The shadow now also has a gradient.
                    double t = (double)shadowOriginX / width;
                    var shadowGradientColor = Lerp(shadowFrom, shadowTo, t);
                    sb.Append($"[{shadowGradientColor.ToMarkup()}]▒[/]");
                }
                else
                {
                    sb.Append(' ');
                }
            }
            AnsiConsole.MarkupLine(sb.ToString());
        }
        AnsiConsole.WriteLine();
    }

    // Basit lineer interpolasyon
    private Color Lerp(Color a, Color b, double t)
        => new(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));

    public bool PromptGenerateCSharpFile()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[{PrimaryAccent.ToMarkup()}]🔧 C# File Generation[/]");
        AnsiConsole.MarkupLine($"[{DimTextColor.ToMarkup()}]Generate a strongly-typed C# permissions file alongside the JSON output.[/]");
        AnsiConsole.WriteLine();

        return AnsiConsole.Confirm(
            $"[{SuccessColor.ToMarkup()}]Would you like to generate a C# permissions file?[/]", 
            defaultValue: true);
    }
}