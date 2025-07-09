using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Spectre.Console;
using SyncPermissions.Models;

namespace SyncPermissions.Services;

public interface IEndpointDiscoverer
{
    Task<List<EndpointInfo>> DiscoverEndpointsAsync(ProjectInfo projectInfo, ProgressTask? progress = null);
}

public class EndpointDiscoverer : IEndpointDiscoverer
{
    public async Task<List<EndpointInfo>> DiscoverEndpointsAsync(ProjectInfo projectInfo, ProgressTask? progress = null)
    {
        var endpoints = new List<EndpointInfo>();

        if (!projectInfo.SourceFiles.Any())
        {
            return endpoints;
        }

        var progressPerFile = 30.0 / projectInfo.SourceFiles.Count; // Using 30% of total progress for endpoint discovery

        foreach (var sourceFile in projectInfo.SourceFiles)
        {
            try
            {
                var endpointInfo = await AnalyzeSourceFileAsync(sourceFile, projectInfo);
                if (endpointInfo != null)
                {
                    endpoints.Add(endpointInfo);
                }
            }
            catch (Exception ex)
            {
                // Silent error handling - could add to warnings if needed
            }
            progress?.Increment(progressPerFile);
        }

        return endpoints;
    }

    private async Task<EndpointInfo?> AnalyzeSourceFileAsync(string filePath, ProjectInfo projectInfo)
    {
        var sourceCode = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
        var root = await tree.GetRootAsync();

        // Find classes that implement IEndpoint
        var endpointClasses = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Where(IsEndpointClass)
            .ToList();

        if (!endpointClasses.Any())
            return null;

        var endpointClass = endpointClasses.First();
        var mapEndpointMethod = endpointClass.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == "MapEndpoint");

        if (mapEndpointMethod == null)
            return null;

        var endpointInfo = new EndpointInfo
        {
            ClassName = endpointClass.Identifier.Text,
            FilePath = GetRelativePath(filePath, projectInfo.Path)
        };

        AnalyzeMapEndpointMethod(mapEndpointMethod, endpointInfo);

        return endpointInfo;
    }

    private bool IsEndpointClass(ClassDeclarationSyntax classDeclaration)
    {
        return classDeclaration.BaseList?.Types
            .Any(baseType => baseType.Type.ToString().Contains("IEndpoint")) == true;
    }

    private void AnalyzeMapEndpointMethod(MethodDeclarationSyntax method, EndpointInfo endpointInfo)
    {
        var statements = method.Body?.Statements;
        if (statements == null) return;

        foreach (var statement in statements)
        {
            AnalyzeStatement(statement, endpointInfo);
        }
    }

    private void AnalyzeStatement(StatementSyntax statement, EndpointInfo endpointInfo)
    {
        // Look for expressions like app.MapPost("/api/v1/roles", ...)
        var expressionStatements = GetExpressionStatements(statement);

        foreach (var expression in expressionStatements)
        {
            if (expression is InvocationExpressionSyntax invocation)
            {
                AnalyzeInvocation(invocation, endpointInfo);
            }
        }
    }

    private List<ExpressionSyntax> GetExpressionStatements(StatementSyntax statement)
    {
        var expressions = new List<ExpressionSyntax>();

        if (statement is ExpressionStatementSyntax expressionStatement)
        {
            expressions.Add(expressionStatement.Expression);
        }

        // Handle chained method calls
        var invocations = statement.DescendantNodes().OfType<InvocationExpressionSyntax>();
        expressions.AddRange(invocations);

        return expressions;
    }

    private void AnalyzeInvocation(InvocationExpressionSyntax invocation, EndpointInfo endpointInfo)
    {
        var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
        if (memberAccess == null) return;

        var methodName = memberAccess.Name.Identifier.Text;

        // Extract HTTP method and route
        if (IsHttpMethodMapping(methodName))
        {
            endpointInfo.HttpMethod = ExtractHttpMethod(methodName);
            endpointInfo.Route = ExtractRoute(invocation);
        }

        // Check for authorization calls
        if (methodName == "RequireAuthorization")
        {
            if (endpointInfo.AuthorizationStatus == EndpointAuthorizationStatus.Public)
            {
                endpointInfo.AuthorizationStatus = EndpointAuthorizationStatus.AuthOnly;
            }
        }
        else if (methodName == "RequirePermission")
        {
            endpointInfo.AuthorizationStatus = EndpointAuthorizationStatus.AlreadyProtected;
            endpointInfo.ExistingPermission = ExtractPermissionName(invocation);
        }
    }

    private bool IsHttpMethodMapping(string methodName)
    {
        return methodName is "MapGet" or "MapPost" or "MapPut" or "MapDelete" or "MapPatch";
    }

    private string ExtractHttpMethod(string mapMethodName)
    {
        return mapMethodName switch
        {
            "MapGet" => "GET",
            "MapPost" => "POST",
            "MapPut" => "PUT",
            "MapDelete" => "DELETE",
            "MapPatch" => "PATCH",
            _ => "UNKNOWN"
        };
    }

    private string? ExtractRoute(InvocationExpressionSyntax invocation)
    {
        var firstArgument = invocation.ArgumentList.Arguments.FirstOrDefault();
        if (firstArgument?.Expression is LiteralExpressionSyntax literal)
        {
            return literal.Token.ValueText;
        }
        return null;
    }

    private string? ExtractPermissionName(InvocationExpressionSyntax invocation)
    {
        var firstArgument = invocation.ArgumentList.Arguments.FirstOrDefault();
        if (firstArgument?.Expression is LiteralExpressionSyntax literal)
        {
            return literal.Token.ValueText;
        }
        return null;
    }

    private string GetRelativePath(string fullPath, string basePath)
    {
        try
        {
            // Normalize both paths to absolute paths
            var normalizedFullPath = Path.GetFullPath(fullPath);
            var normalizedBasePath = Path.GetFullPath(basePath);
            
            // Get relative path
            var relativePath = Path.GetRelativePath(normalizedBasePath, normalizedFullPath);
            
            // Normalize separators
            return relativePath.Replace('\\', '/');
        }
        catch (Exception ex)
        {
            // Fallback: return just the filename if relative path fails
            Console.WriteLine($"Warning: Could not calculate relative path for {fullPath}: {ex.Message}");
            return Path.GetFileName(fullPath);
        }
    }
} 