using Spectre.Console;
using SyncPermissions.Models;

namespace SyncPermissions.Services;

public interface IProjectScanner
{
    Task<List<ProjectInfo>> DiscoverProjectsAsync(string scanPath, ProgressTask? progress = null);
}

public class ProjectScanner : IProjectScanner
{
    public async Task<List<ProjectInfo>> DiscoverProjectsAsync(string scanPath, ProgressTask? progress = null)
    {
        var projects = new List<ProjectInfo>();
        
        if (!Directory.Exists(scanPath))
        {
            throw new DirectoryNotFoundException($"Scan path not found: {scanPath}");
        }

        // Find all .csproj files
        var projectFiles = Directory.GetFiles(scanPath, "*.csproj", SearchOption.AllDirectories);
        
        if (projectFiles.Length == 0)
        {
            return projects;
        }
        
        var progressPerProject = 15.0 / projectFiles.Length; // Using 15% of total progress for project discovery
        
        foreach (var projectFile in projectFiles)
        {
            var projectInfo = await AnalyzeProjectAsync(projectFile);
            if (projectInfo != null)
            {
                projects.Add(projectInfo);
            }
            progress?.Increment(progressPerProject);
        }

        return projects;
    }

    private async Task<ProjectInfo?> AnalyzeProjectAsync(string projectFilePath)
    {
        try
        {
            var projectDirectory = Path.GetDirectoryName(projectFilePath)!;
            var projectName = Path.GetFileNameWithoutExtension(projectFilePath);

            // Get all C# files in the project
            var csharpFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains("\\bin\\") && !f.Contains("\\obj\\"))
                .ToList();

            return new ProjectInfo
            {
                Name = projectName,
                Path = projectDirectory,
                ProjectFilePath = projectFilePath,
                SourceFiles = csharpFiles
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to analyze project {projectFilePath}: {ex.Message}");
            return null;
        }
    }
}

 