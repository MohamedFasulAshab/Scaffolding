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
        Assert.Equal("Lib.ContactUs", Assert.Single(await ReferencedProjectModelResolver.FindMatchesAsync(web, "contactus")).ToDisplayString());
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

        Assert.Equal(ReferencedProjectLookupStatus.Ambiguous, selected.Status);
        Assert.Null(selected.Symbol);
        Assert.Equal(
            "Multiple classes match --model 'ContactUs' in referenced projects: 'LibA.ContactUs' (LibA), 'LibB.ContactUs' (LibB). Use a fully qualified class name.",
            Assert.Single(logger.Errors));

        var qualified = await ReferencedProjectModelResolver.FindMatchesAsync(web, "LibB.ContactUs");
        Assert.Equal("LibB.ContactUs", ReferencedProjectModelResolver.SelectSingleMatch(qualified, "LibB.ContactUs", logger).Symbol!.ToDisplayString());
    }

    [Theory]
    [InlineData("Lib.ContactUs", true)]
    [InlineData("lib.contactus", false)]
    public async Task FindMatches_QualifiedName_MatchesExactNamespaceQualifiedNameOnly(string modelName, bool expectedFound)
    {
        var web = CreateSolution(("Web", "", ["Lib"]), ("Lib", ContactUsSource, []));

        var matches = await ReferencedProjectModelResolver.FindMatchesAsync(web, modelName);

        Assert.Equal(expectedFound, matches.Count == 1);
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
    [InlineData("namespace Lib; public class Outer { public class ContactUs { public int Id { get; set; } } }")]
    [InlineData("namespace Lib; public record ContactUs(int Id);")]
    [InlineData("namespace Lib; public class ContactUs<T> { public int Id { get; set; } }")]
    public async Task FindMatches_IgnoresInternalNestedRecordAndGenericTypes(string librarySource)
    {
        var web = CreateSolution(("Web", "", ["Lib"]), ("Lib", librarySource, []));

        Assert.Empty(await ReferencedProjectModelResolver.FindMatchesAsync(web, "ContactUs"));
    }

    [Fact]
    public async Task FindMatches_DoesNotResolveFrameworkTypes()
    {
        var web = CreateSolution(("Web", "", ["Lib"]), ("Lib", ContactUsSource, []));

        Assert.Empty(await ReferencedProjectModelResolver.FindMatchesAsync(web, "Task"));
    }

    [Fact]
    public async Task FindMatches_DiamondReferences_ReturnsSingleMatchWithoutAmbiguity()
    {
        var web = CreateSolution(
            ("Web", "", ["A", "B"]),
            ("A", "namespace A; public class ContactUs { public int Id { get; set; } }", []),
            ("B", "namespace B; public class Other { }", ["A"]));
        var logger = new ListLogger();

        var matches = await ReferencedProjectModelResolver.FindMatchesAsync(web, "ContactUs");
        var match = Assert.Single(matches);

        Assert.Equal("A.ContactUs", match.ToDisplayString());
        Assert.Same(match, ReferencedProjectModelResolver.SelectSingleMatch(matches, "ContactUs", logger).Symbol);
        Assert.Empty(logger.Errors);
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

    private const string EfSource = "namespace Microsoft.EntityFrameworkCore { public class DbContext {} public class DbSet<TEntity> {} }";

    [Theory]
    [InlineData("public class StoreContext : Microsoft.EntityFrameworkCore.DbContext {}", true)]
    [InlineData("public class BaseContext : Microsoft.EntityFrameworkCore.DbContext {} public class StoreContext : BaseContext {}", true)]
    [InlineData("public class StoreContext {}", false)]
    [InlineData("internal class StoreContext : Microsoft.EntityFrameworkCore.DbContext {}", false)]
    [InlineData("public class StoreContext<T> : Microsoft.EntityFrameworkCore.DbContext {}", false)]
    [InlineData("public record StoreContext;", false)]
    [InlineData("public class Outer { public class StoreContext : Microsoft.EntityFrameworkCore.DbContext {} }", false)]
    public async Task Resolve_ContextRequiresPublicTopLevelNonGenericNonRecordDbContext(string source, bool found)
    {
        var web = CreateSolution(("Web", "", ["DataAccess"]), ("DataAccess", EfSource + " namespace DataAccess { " + source + " }", []));
        var loads = 0;
        var result = await ReferencedProjectModelResolver.ResolveAsync("unused", "Missing", "storecontext", new ListLogger(),
            loadProject: _ => { loads++; return Task.FromResult(web); });

        Assert.Equal(1, loads);
        Assert.Equal(ReferencedProjectLookupStatus.NotFound, result.Model.Status);
        Assert.Equal(found ? ReferencedProjectLookupStatus.Found : ReferencedProjectLookupStatus.NotFound, result.Context.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resolve_CombinedLookupFindsDeclaredOrInheritedDbSet(bool inherited)
    {
        var contextSource = inherited
            ? "public class BaseContext : Microsoft.EntityFrameworkCore.DbContext { public Microsoft.EntityFrameworkCore.DbSet<Entities.Product> Products { get; set; } } public class StoreContext : BaseContext {}"
            : "public class StoreContext : Microsoft.EntityFrameworkCore.DbContext { public Microsoft.EntityFrameworkCore.DbSet<Entities.Product> Products { get; set; } }";
        var web = CreateSolution(("Web", "", ["Entities", "DataAccess"]),
            ("Entities", "namespace Entities; public class Product { public int Id { get; set; } }", []),
            ("DataAccess", EfSource + " namespace DataAccess { " + contextSource + " }", ["Entities"]));
        var loads = 0;
        var result = await ReferencedProjectModelResolver.ResolveAsync("unused", "Product", "StoreContext", new ListLogger(),
            loadProject: _ => { loads++; return Task.FromResult(web); });

        Assert.Equal(1, loads);
        Assert.Equal(ReferencedProjectLookupStatus.Found, result.Model.Status);
        Assert.Equal(ReferencedProjectLookupStatus.Found, result.Context.Status);
        Assert.Equal("Products", ReferencedProjectModelResolver.FindDbSetName(result.Context.Symbol!, "Entities.Product"));
        Assert.Null(ReferencedProjectModelResolver.FindDbSetName(result.Context.Symbol!, "Other.Product"));
    }

    [Fact]
    public async Task Resolve_ContextLookupUsesSimpleNamesOnly()
    {
        var web = CreateSolution(("Web", "", ["DataAccess"]),
            ("DataAccess", EfSource + " namespace DataAccess { public class StoreContext : Microsoft.EntityFrameworkCore.DbContext {} }", []));
        var result = await ReferencedProjectModelResolver.ResolveAsync("unused", null, "DataAccess.StoreContext", new ListLogger(), loadProject: _ => Task.FromResult(web));
        Assert.Equal(ReferencedProjectLookupStatus.NotFound, result.Context.Status);
    }

    [Fact]
    public async Task Resolve_AmbiguousContextDoesNotSelectCandidate()
    {
        var web = CreateSolution(("Web", "", ["DataAccess"]),
            ("DataAccess", EfSource + " namespace A { public class StoreContext : Microsoft.EntityFrameworkCore.DbContext {} } namespace B { public class StoreContext : Microsoft.EntityFrameworkCore.DbContext {} }", []));
        var result = await ReferencedProjectModelResolver.ResolveAsync("unused", null, "StoreContext", new ListLogger(), loadProject: _ => Task.FromResult(web));
        Assert.Equal(ReferencedProjectLookupStatus.Ambiguous, result.Context.Status);
        Assert.Null(result.Context.Symbol);
    }

    [Fact]
    public async Task Resolve_LoadFailureReturnsLookupFailedForBothWithoutPartialResults()
    {
        var result = await ReferencedProjectModelResolver.ResolveAsync("unused", "Product", "StoreContext", new ListLogger(),
            loadProject: _ => throw new InvalidOperationException("Simulated workspace load failure"));
        Assert.Equal(ReferencedProjectLookupStatus.LookupFailed, result.Model.Status);
        Assert.Equal(ReferencedProjectLookupStatus.LookupFailed, result.Context.Status);
        Assert.Null(result.Model.Symbol);
        Assert.Null(result.Context.Symbol);
    }

    [Fact]
    public async Task Resolve_CancellationIsPropagated()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(() => ReferencedProjectModelResolver.ResolveAsync("unused", "Product", "StoreContext", new ListLogger(),
            loadProject: _ => throw new OperationCanceledException()));
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
