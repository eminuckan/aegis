using Spectre.Console;

namespace SyncPermissions.Services;

public interface IFolderBrowserService
{
    Task<string?> SelectFolderAsync(string? startPath = null, string title = "Select a folder:");
}

public class FolderBrowserService : IFolderBrowserService
{
    // Theme colors matching the app
    private static readonly Color PrimaryAccent = new Color(180, 120, 60);
    private static readonly Color SuccessColor = new Color(120, 140, 80);
    private static readonly Color WarningColor = new Color(160, 100, 50);
    private static readonly Color DefaultTextColor = new Color(200, 190, 180);
    private static readonly Color DimTextColor = new Color(120, 100, 80);

    public async Task<string?> SelectFolderAsync(string? startPath = null, string title = "Select a folder:")
    {
        var currentPath = startPath ?? Directory.GetCurrentDirectory();
        
        while (true)
        {
            AnsiConsole.Clear();
            
            // Show current path
            AnsiConsole.MarkupLine($"[bold {PrimaryAccent.ToMarkup()}]📁 {title}[/]");
            AnsiConsole.MarkupLine($"[{DimTextColor.ToMarkup()}]Current location: {currentPath}[/]");
            AnsiConsole.WriteLine();

            var options = await GetFolderOptionsAsync(currentPath);
            
            if (!options.Any())
            {
                AnsiConsole.MarkupLine($"[{WarningColor.ToMarkup()}]No accessible folders found in this location.[/]");
                AnsiConsole.WriteLine();
                
                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title($"[{PrimaryAccent.ToMarkup()}]What would you like to do?[/]")
                        .HighlightStyle(new Style(PrimaryAccent))
                        .AddChoices(new[] { 
                            "🔙 Go back to parent folder", 
                            "✅ Use current folder",
                            "❌ Cancel" 
                        }));
                
                switch (choice)
                {
                    case "🔙 Go back to parent folder":
                        var parentPath = Directory.GetParent(currentPath)?.FullName;
                        if (parentPath != null)
                        {
                            currentPath = parentPath;
                            continue;
                        }
                        else
                        {
                            AnsiConsole.MarkupLine($"[{WarningColor.ToMarkup()}]Already at root directory.[/]");
                            await Task.Delay(1500);
                            continue;
                        }
                    case "✅ Use current folder":
                        return currentPath;
                    case "❌ Cancel":
                        return null;
                }
                continue;
            }

            // Add navigation options
            var choices = new List<string>();
            
            // Add parent directory option if not at root
            var parentDir = Directory.GetParent(currentPath);
            if (parentDir != null)
            {
                choices.Add("🔙 .. (Go back to parent folder)");
            }
            
            // Add current directory option
            choices.Add("✅ Use this folder");
            
            // Add subdirectories
            foreach (var option in options)
            {
                choices.Add($"📁 {option}");
            }
            
            // Add cancel option
            choices.Add("❌ Cancel");

            // Check if current folder has .csproj files
            var hasCsprojFiles = Directory.GetFiles(currentPath, "*.csproj", SearchOption.AllDirectories).Any();
            if (hasCsprojFiles)
            {
                var projectCount = Directory.GetFiles(currentPath, "*.csproj", SearchOption.AllDirectories).Length;
                AnsiConsole.MarkupLine($"[{SuccessColor.ToMarkup()}]✨ This folder contains {projectCount} .NET project(s)[/]");
                AnsiConsole.WriteLine();
            }

            var selectedChoice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[{PrimaryAccent.ToMarkup()}]Choose an option:[/]")
                    .PageSize(15)
                    .MoreChoicesText($"[{DimTextColor.ToMarkup()}](Move up and down to see more folders)[/]")
                    .HighlightStyle(new Style(PrimaryAccent))
                    .AddChoices(choices));

            if (selectedChoice == "❌ Cancel")
            {
                return null;
            }
            else if (selectedChoice == "✅ Use this folder")
            {
                return currentPath;
            }
            else if (selectedChoice == "🔙 .. (Go back to parent folder)")
            {
                currentPath = parentDir!.FullName;
                continue;
            }
            else if (selectedChoice.StartsWith("📁 "))
            {
                var folderName = selectedChoice[2..].Trim(); // Remove "📁 " prefix
                var newPath = Path.Combine(currentPath, folderName);
                
                if (Directory.Exists(newPath))
                {
                    currentPath = newPath;
                    continue;
                }
                else
                {
                    AnsiConsole.MarkupLine($"[{WarningColor.ToMarkup()}]Folder no longer exists: {folderName}[/]");
                    await Task.Delay(1500);
                    continue;
                }
            }
        }
    }

    private async Task<List<string>> GetFolderOptionsAsync(string path)
    {
        try
        {
            var directories = Directory.GetDirectories(path)
                .Where(dir => 
                {
                    var dirName = Path.GetFileName(dir);
                    // Skip hidden, system, and common build folders
                    return !dirName.StartsWith('.') && 
                           !dirName.Equals("bin", StringComparison.OrdinalIgnoreCase) &&
                           !dirName.Equals("obj", StringComparison.OrdinalIgnoreCase) &&
                           !dirName.Equals("node_modules", StringComparison.OrdinalIgnoreCase) &&
                           !dirName.Equals("packages", StringComparison.OrdinalIgnoreCase) &&
                           !dirName.StartsWith("__");
                })
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Take(50) // Limit to prevent overwhelming UI
                .ToList();

            return directories;
        }
        catch (UnauthorizedAccessException)
        {
            return new List<string>();
        }
        catch (DirectoryNotFoundException)
        {
            return new List<string>();
        }
        catch (Exception)
        {
            return new List<string>();
        }
    }
} 