// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using AspNetConstants = Microsoft.DotNet.Tools.Scaffold.AspNet.Common.Constants;

namespace Microsoft.DotNet.Tools.Scaffold.AspNet.Helpers;

/// <summary>
/// Fallback lookup for a --model class declared in a project referenced by the target project.
/// Intended to run only after the existing lookup in the target project found no match.
/// </summary>
internal static class ReferencedProjectModelResolver
{
    /// <summary>
    /// Loads the target project and its referenced projects as source in a separate, short-lived
    /// workspace and returns the single matching public class, or null when there is no match or the match is ambiguous.
    /// </summary>
    internal static async Task<INamedTypeSymbol?> FindModelAsync(string projectPath, string modelName, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(projectPath) || string.IsNullOrEmpty(modelName))
        {
            return null;
        }

        IReadOnlyList<INamedTypeSymbol> matches;
        try
        {
            matches = await FindMatchesWithMSBuildAsync(Path.GetFullPath(projectPath), modelName, logger, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogDebug("Referenced project lookup for '{ModelName}' failed: {Message}", modelName, ex.Message);
            return null;
        }

        return SelectSingleMatch(matches, modelName, logger);
    }

    // Kept separate so MSBuild workspace types are only loaded on this path.
    private static async Task<IReadOnlyList<INamedTypeSymbol>> FindMatchesWithMSBuildAsync(string fullProjectPath, string modelName, ILogger logger, CancellationToken cancellationToken)
    {
        using var workspace = MSBuildWorkspace.Create();
        workspace.LoadMetadataForReferencedProjects = false;
        using var workspaceFailedRegistration = workspace.RegisterWorkspaceFailedHandler(
            e => logger.LogDebug("Referenced project lookup workspace diagnostic: {Message}", e.Diagnostic.Message));
        var project = await workspace.OpenProjectAsync(fullProjectPath, cancellationToken: cancellationToken);
        return await FindMatchesAsync(project, modelName, cancellationToken);
    }

    /// <summary>
    /// Searches only the source projects that are visible to <paramref name="rootProject"/>'s compilation
    /// (<see cref="CompilationReference"/>s). Framework and package references are never searched.
    /// A name containing '.' is matched case-sensitively against the namespace-qualified name;
    /// otherwise the simple name is matched case-insensitively. Only public, top-level, non-generic, non-record classes are candidates.
    /// </summary>
    internal static async Task<IReadOnlyList<INamedTypeSymbol>> FindMatchesAsync(Project rootProject, string modelName, CancellationToken cancellationToken = default)
    {
        var compilation = await rootProject.GetCompilationAsync(cancellationToken);
        if (compilation is null || string.IsNullOrEmpty(modelName))
        {
            return [];
        }

        var isQualifiedName = modelName.Contains('.');
        var matches = new List<INamedTypeSymbol>();
        foreach (var reference in compilation.References.OfType<CompilationReference>())
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
            {
                continue;
            }

            foreach (var type in GetTopLevelTypes(assembly.GlobalNamespace))
            {
                if (IsCandidate(type) && IsNameMatch(type, modelName, isQualifiedName) &&
                    !matches.Contains(type, SymbolEqualityComparer.Default))
                {
                    matches.Add(type);
                }
            }
        }

        return matches
            .OrderBy(t => t.ContainingAssembly.Name, StringComparer.Ordinal)
            .ThenBy(t => t.ToDisplayString(), StringComparer.Ordinal)
            .ToList();
    }

    internal static INamedTypeSymbol? SelectSingleMatch(IReadOnlyList<INamedTypeSymbol> matches, string modelName, ILogger logger)
    {
        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (matches.Count > 1)
        {
            var candidates = string.Join(", ", matches.Select(t => $"'{t.ToDisplayString()}' ({t.ContainingAssembly.Name})"));
            logger.LogError($"Multiple classes match {AspNetConstants.CliOptions.ModelCliOption} '{modelName}' in referenced projects: {candidates}. Use a fully qualified class name.");
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

    private static bool IsCandidate(INamedTypeSymbol type)
    {
        return type.TypeKind == TypeKind.Class &&
            !type.IsRecord &&
            !type.IsImplicitlyDeclared &&
            type.Arity == 0 &&
            type.DeclaredAccessibility == Accessibility.Public;
    }

    private static bool IsNameMatch(INamedTypeSymbol type, string modelName, bool isQualifiedName)
    {
        return isQualifiedName
            ? type.ToDisplayString().Equals(modelName, StringComparison.Ordinal)
            : type.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase);
    }
}
