// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using AspNetConstants = Microsoft.DotNet.Tools.Scaffold.AspNet.Common.Constants;

namespace Microsoft.DotNet.Tools.Scaffold.AspNet.Helpers;

internal enum ReferencedProjectLookupStatus
{
    Found,
    NotFound,
    Ambiguous,
    LookupFailed
}

internal sealed record ReferencedProjectLookupResult(ReferencedProjectLookupStatus Status, INamedTypeSymbol? Symbol = null);

/// <summary>
/// Fallback lookup in source projects visible to the target project's compilation.
/// </summary>
internal static class ReferencedProjectModelResolver
{
    internal static async Task<ReferencedProjectLookupResult> FindModelAsync(string projectPath, string modelName, ILogger logger, CancellationToken cancellationToken = default)
    {
        var results = await ResolveAsync(projectPath, modelName, null, logger, cancellationToken);
        return results.Model;
    }

    // Resolve both local misses together, so at most one MSBuild workspace is opened.
    internal static async Task<(ReferencedProjectLookupResult Model, ReferencedProjectLookupResult Context)> ResolveAsync(
        string projectPath, string? modelName, string? contextName, ILogger logger, CancellationToken cancellationToken = default,
        Func<CancellationToken, Task<Project>>? loadProject = null)
    {
        var notFound = new ReferencedProjectLookupResult(ReferencedProjectLookupStatus.NotFound);
        if (string.IsNullOrEmpty(modelName) && string.IsNullOrEmpty(contextName))
        {
            return (notFound, notFound);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (loadProject is not null)
            {
                return await ResolveProjectAsync(await loadProject(cancellationToken), modelName, contextName, logger, cancellationToken);
            }

            return await ResolveWithMSBuildAsync(Path.GetFullPath(projectPath), modelName, contextName, logger, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning($"Referenced project lookup failed: {ex.Message}");
            var failed = new ReferencedProjectLookupResult(ReferencedProjectLookupStatus.LookupFailed);
            return (failed, failed);
        }
    }

    // Keep MSBuild workspace types off the local-only path.
    private static async Task<(ReferencedProjectLookupResult Model, ReferencedProjectLookupResult Context)> ResolveWithMSBuildAsync(
        string projectPath, string? modelName, string? contextName, ILogger logger, CancellationToken cancellationToken)
    {
        using var workspace = MSBuildWorkspace.Create();
        workspace.LoadMetadataForReferencedProjects = false;
        var failed = 0;
        using var registration = workspace.RegisterWorkspaceFailedHandler(e =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                Interlocked.Exchange(ref failed, 1);
                logger.LogWarning($"Referenced project could not be loaded: {e.Diagnostic.Message}");
            }
        });
        var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken);
        var results = await ResolveProjectAsync(project, modelName, contextName, logger, cancellationToken);
        if (Volatile.Read(ref failed) != 0)
        {
            var failure = new ReferencedProjectLookupResult(ReferencedProjectLookupStatus.LookupFailed);
            return (failure, failure);
        }

        return results;
    }

    private static async Task<(ReferencedProjectLookupResult Model, ReferencedProjectLookupResult Context)> ResolveProjectAsync(
        Project project, string? modelName, string? contextName, ILogger logger, CancellationToken cancellationToken)
    {
        // Even a context-only lookup must reject an unavailable compilation rather than create a context.
        var compilation = await project.GetCompilationAsync(cancellationToken)
            ?? throw new InvalidOperationException("Referenced project compilation is unavailable.");
        var types = GetReferencedTypes(compilation);
        var modelMatches = types.Where(t => !string.IsNullOrEmpty(modelName) && IsNameMatch(t, modelName!, modelName!.Contains('.'))).ToList();
        var contextMatches = types.Where(t => !string.IsNullOrEmpty(contextName) &&
            t.Name.Equals(contextName, StringComparison.OrdinalIgnoreCase) && IsDbContext(t)).ToList();
        return (SelectSingleMatch(modelMatches, modelName ?? string.Empty, logger),
            SelectSingleMatch(contextMatches, contextName ?? string.Empty, logger, isContext: true));
    }

    internal static async Task<IReadOnlyList<INamedTypeSymbol>> FindMatchesAsync(Project rootProject, string modelName, CancellationToken cancellationToken = default)
    {
        var compilation = await rootProject.GetCompilationAsync(cancellationToken)
            ?? throw new InvalidOperationException("Referenced project compilation is unavailable.");
        return GetReferencedTypes(compilation).Where(t => IsNameMatch(t, modelName, modelName.Contains('.'))).ToList();
    }

    private static IReadOnlyList<INamedTypeSymbol> GetReferencedTypes(Compilation compilation)
    {
        var types = new List<INamedTypeSymbol>();
        foreach (var reference in compilation.References.OfType<CompilationReference>())
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
            {
                throw new InvalidOperationException("Referenced project assembly is unavailable.");
            }

            foreach (var type in GetTopLevelTypes(assembly.GlobalNamespace))
            {
                if (IsCandidate(type) && !types.Contains(type, SymbolEqualityComparer.Default))
                {
                    types.Add(type);
                }
            }
        }

        return types.OrderBy(t => t.ContainingAssembly.Name, StringComparer.Ordinal)
            .ThenBy(t => t.ToDisplayString(), StringComparer.Ordinal).ToList();
    }

    internal static ReferencedProjectLookupResult SelectSingleMatch(IReadOnlyList<INamedTypeSymbol> matches, string name, ILogger logger, bool isContext = false)
    {
        if (matches.Count == 1)
        {
            return new(ReferencedProjectLookupStatus.Found, matches[0]);
        }

        if (matches.Count > 1)
        {
            var candidates = string.Join(", ", matches.Select(t => $"'{t.ToDisplayString()}' ({t.ContainingAssembly.Name})"));
            var option = isContext ? AspNetConstants.CliOptions.DataContextOption : AspNetConstants.CliOptions.ModelCliOption;
            var advice = isContext ? "Use a unique DbContext class name." : "Use a fully qualified class name.";
            logger.LogError($"Multiple classes match {option} '{name}' in referenced projects: {candidates}. {advice}");
            return new(ReferencedProjectLookupStatus.Ambiguous);
        }

        return new(ReferencedProjectLookupStatus.NotFound);
    }

    private static bool IsDbContext(INamedTypeSymbol type)
    {
        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.ToDisplayString() == "Microsoft.EntityFrameworkCore.DbContext")
            {
                return true;
            }
        }

        return false;
    }

    internal static string? FindDbSetName(INamedTypeSymbol context, string? modelFullName)
    {
        for (INamedTypeSymbol? type = context; type is not null; type = type.BaseType)
        {
            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.Type is INamedTypeSymbol propertyType && propertyType.Arity == 1 &&
                    propertyType.OriginalDefinition.ToDisplayString() == "Microsoft.EntityFrameworkCore.DbSet<TEntity>" &&
                    propertyType.TypeArguments[0].ToDisplayString() == modelFullName)
                {
                    return property.Name;
                }
            }
        }

        return null;
    }

    private static IEnumerable<INamedTypeSymbol> GetTopLevelTypes(INamespaceSymbol namespaceSymbol)
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            yield return type;
        }

        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            foreach (var type in GetTopLevelTypes(childNamespace))
            {
                yield return type;
            }
        }
    }

    private static bool IsCandidate(INamedTypeSymbol type) => type.TypeKind == TypeKind.Class &&
        !type.IsRecord && !type.IsImplicitlyDeclared && type.Arity == 0 && type.DeclaredAccessibility == Accessibility.Public;

    private static bool IsNameMatch(INamedTypeSymbol type, string name, bool qualified) => qualified
        ? type.ToDisplayString().Equals(name, StringComparison.Ordinal)
        : type.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
}
