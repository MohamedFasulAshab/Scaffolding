// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.DotNet.Scaffolding.CodeModification.Helpers;
using Microsoft.DotNet.Tools.Scaffold.AspNet.Helpers;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.DotNet.Tools.Scaffold.Tests.AspNet.Helpers;

public class ReferencedProjectModelResolverTests : IDisposable
{
    private const string ContactUsSource = "namespace Lib; public class ContactUs { public int Id { get; set; } public string Name { get; set; } }";
    private readonly AdhocWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task FindMatches_DoesNotSearchRootProject()
    {
        var web = CreateSolution(("Web", "namespace Web; public class ContactUs { public int Id { get; set; } }", []));

        Assert.Empty(await ReferencedProjectModelResolver.FindMatchesAsync(web, "ContactUs"));
    }

    [Fact]
    public async Task FindMatches_FindsPublicClassInDirectlyReferencedProject()
    {
        var web = CreateSolution(("Web", "", ["Lib"]), ("Lib", ContactUsSource, []));

        var match = Assert.Single(await ReferencedProjectModelResolver.FindMatchesAsync(web, "ContactUs"));
        Assert.Equal("Lib.ContactUs", match.ToDisplayString());
        Assert.Equal("Lib", match.ContainingAssembly.Name);
    }

    [Fact]
    public async Task FindMatches_SameNameInRootAndReference_ReturnsOnlyReferencedType()
    {
        // The step only calls the resolver after the existing local lookup misses, so a local match always wins.
        var web = CreateSolution(
            ("Web", "namespace Web; public class ContactUs { public int Id { get; set; } }", ["Lib"]),
            ("Lib", ContactUsSource, []));

        var match = Assert.Single(await ReferencedProjectModelResolver.FindMatchesAsync(web, "ContactUs"));
        Assert.Equal("Lib.ContactUs", match.ToDisplayString());
    }

    [Fact]
    public async Task SelectSingleMatch_SameSimpleNameInTwoReferences_LogsAmbiguityAndSelectsNothing()
    {
        var web = CreateSolution(
            ("Web", "", ["LibB", "LibA"]),
            ("LibA", "namespace LibA; public class ContactUs { public int Id { get; set; } }", []),
            ("LibB", "namespace LibB; public class ContactUs { public int Id { get; set; } }", []));
        var logger = new ListLogger();

        var matches = await ReferencedProjectModelResolver.FindMatchesAsync(web, "ContactUs");
        var selected = ReferencedProjectModelResolver.SelectSingleMatch(matches, "ContactUs", logger);

        Assert.Null(selected);
        Assert.Equal(
            "Multiple classes match --model 'ContactUs' in referenced projects: 'LibA.ContactUs' (LibA), 'LibB.ContactUs' (LibB). Use a fully qualified class name.",
            Assert.Single(logger.Errors));

        var qualified = await ReferencedProjectModelResolver.FindMatchesAsync(web, "LibB.ContactUs");
        Assert.Equal("LibB.ContactUs", ReferencedProjectModelResolver.SelectSingleMatch(qualified, "LibB.ContactUs", logger)!.ToDisplayString());
    }

    [Theory]
    [InlineData("Lib.ContactUs", true)]
    [InlineData("Lib.Missing", false)]
    [InlineData("Other.ContactUs", false)]
    [InlineData("lib.contactus", false)]
    public async Task FindMatches_QualifiedName_MatchesExactNamespaceQualifiedNameOnly(string modelName, bool expectedFound)
    {
        var web = CreateSolution(("Web", "", ["Lib"]), ("Lib", ContactUsSource, []));

        var matches = await ReferencedProjectModelResolver.FindMatchesAsync(web, modelName);

        Assert.Equal(expectedFound, matches.Count == 1);
    }

    [Theory]
    [InlineData("contactus")]
    [InlineData("CONTACTUS")]
    public async Task FindMatches_SimpleName_IsCaseInsensitiveLikeLocalLookup(string modelName)
    {
        var web = CreateSolution(("Web", "", ["Lib"]), ("Lib", ContactUsSource, []));

        Assert.Single(await ReferencedProjectModelResolver.FindMatchesAsync(web, modelName));
    }

    [Fact]
    public async Task FindMatches_OnlySearchesProjectsReferencedByRootCompilation()
    {
        // MSBuildWorkspace adds visible transitive projects as direct project references of the root;
        // an unflattened Web -> A -> B graph models DisableTransitiveProjectReferences/PrivateAssets.
        const string bSource = "namespace B; public class ContactUs { public int Id { get; set; } }";
        var unflattened = CreateSolution(("Web", "", ["A"]), ("A", "namespace A; public class Other { }", ["B"]), ("B", bSource, []));
        var flattened = CreateSolution(("Web", "", ["A", "B"]), ("A", "namespace A; public class Other { }", ["B"]), ("B", bSource, []));

        Assert.Empty(await ReferencedProjectModelResolver.FindMatchesAsync(unflattened, "ContactUs"));
        Assert.Equal("B.ContactUs", Assert.Single(await ReferencedProjectModelResolver.FindMatchesAsync(flattened, "ContactUs")).ToDisplayString());
    }

    [Theory]
    [InlineData("namespace Lib; internal class ContactUs { public int Id { get; set; } }")]
    [InlineData("[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Web\")] namespace Lib; internal class ContactUs { public int Id { get; set; } }")]
    [InlineData("namespace Lib; public class Outer { public class ContactUs { public int Id { get; set; } } }")]
    [InlineData("namespace Lib; public record ContactUs(int Id);")]
    [InlineData("namespace Lib; public struct ContactUs { public int Id { get; set; } }")]
    [InlineData("namespace Lib; public record struct ContactUs(int Id);")]
    [InlineData("namespace Lib; public interface ContactUs { int Id { get; set; } }")]
    public async Task FindMatches_IgnoresNonPublicNestedRecordAndNonClassTypes(string librarySource)
    {
        var web = CreateSolution(("Web", "", ["Lib"]), ("Lib", librarySource, []));

        Assert.Empty(await ReferencedProjectModelResolver.FindMatchesAsync(web, "ContactUs"));
    }

    [Fact]
    public async Task FindMatches_GenericClass_DoesNotMatchSimpleName()
    {
        var web = CreateSolution(("Web", "", ["Lib"]), ("Lib", "namespace Lib; public class Item<T> { public int Id { get; set; } }", []));

        Assert.Empty(await ReferencedProjectModelResolver.FindMatchesAsync(web, "Item"));
        Assert.Empty(await ReferencedProjectModelResolver.FindMatchesAsync(web, "Lib.Item"));
    }

    [Theory]
    [InlineData("Task")]
    [InlineData("Uri")]
    [InlineData("System.Uri")]
    [InlineData("Object")]
    public async Task FindMatches_DoesNotResolveFrameworkTypes(string modelName)
    {
        var web = CreateSolution(("Web", "", ["Lib"]), ("Lib", ContactUsSource, []));

        Assert.Empty(await ReferencedProjectModelResolver.FindMatchesAsync(web, modelName));
    }

    [Fact]
    public async Task FindMatches_RepeatedCalls_ReturnIdenticalResults()
    {
        var web = CreateSolution(
            ("Web", "", ["LibA", "LibB"]),
            ("LibA", "namespace LibA; public class ContactUs { public int Id { get; set; } }", []),
            ("LibB", "namespace LibB; public class ContactUs { public int Id { get; set; } }", []));

        var first = await ReferencedProjectModelResolver.FindMatchesAsync(web, "ContactUs");
        var second = await ReferencedProjectModelResolver.FindMatchesAsync(web, "ContactUs");

        Assert.Equal(["LibA.ContactUs", "LibB.ContactUs"], first.Select(t => t.ToDisplayString()));
        Assert.Equal(first.Select(t => t.ToDisplayString()), second.Select(t => t.ToDisplayString()));
    }

    [Fact]
    public async Task FindMatches_ProjectWithoutReferences_ReturnsNoMatches()
    {
        var web = CreateSolution(("Web", "namespace Web; public class Home { }", []));

        Assert.Empty(await ReferencedProjectModelResolver.FindMatchesAsync(web, "ContactUs"));
    }

    [Fact]
    public async Task FindMatches_BaseClassInAnotherReferencedProject_ExposesInheritedProperties()
    {
        var web = CreateSolution(
            ("Web", "", ["A", "B"]),
            ("A", "namespace A; public class ContactUs : B.BaseEntity { public string Name { get; set; } }", ["B"]),
            ("B", "namespace B; public class BaseEntity { public int Id { get; set; } }", []));

        var match = Assert.Single(await ReferencedProjectModelResolver.FindMatchesAsync(web, "ContactUs"));
        var properties = EfDbContextHelpers.GetModelProperties(match);

        Assert.NotNull(properties);
        Assert.Equal("Id", properties.PrimaryKeyName);
        Assert.Equal(["Name", "Id"], properties.AllModelProperties.Select(p => p.Name));
    }

    private Project CreateSolution(params (string Name, string Source, string[] References)[] projects)
    {
        var solution = _workspace.CurrentSolution;
        var ids = new Dictionary<string, ProjectId>();
        foreach (var (name, source, _) in projects)
        {
            var project = solution.AddProject(name, name, LanguageNames.CSharp)
                .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .AddMetadataReferences(FrameworkReferences)
                .AddDocument($"{name}.cs", source).Project;
            solution = project.Solution;
            ids[name] = project.Id;
        }

        foreach (var (name, _, references) in projects)
        {
            foreach (var reference in references)
            {
                solution = solution.AddProjectReference(ids[name], new ProjectReference(ids[reference]));
            }
        }

        return solution.GetProject(ids[projects[0].Name])!;
    }

    private static readonly MetadataReference[] FrameworkReferences =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Uri).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
        MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")),
    ];

    private sealed class ListLogger : ILogger
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                Errors.Add(formatter(state, exception));
            }
        }
    }
}
