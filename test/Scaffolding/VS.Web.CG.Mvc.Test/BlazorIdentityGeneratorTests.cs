// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.DotNet.Scaffolding.Shared;
using Microsoft.DotNet.Scaffolding.Shared.ProjectModel;
using Microsoft.VisualStudio.Web.CodeGeneration;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Blazor;
using Xunit;

namespace Microsoft.VisualStudio.Web.CodeGenerators.Mvc
{
    public class BlazorIdentityGeneratorTests
    {
        [Fact]
        public void ResolveDocumentToModifyPrefersTargetProject()
        {
            using var fileProvider = new TemporaryFileProvider();
            using var workspace = new AdhocWorkspace();
            var target = AddProject(workspace, fileProvider.Root, "StandardWeb");
            var shared = AddProject(workspace, fileProvider.Root, "StandardWeb.Shared");
            var targetRoutes = AddFile(target, "Components", "Routes.razor");
            var targetNavMenuCss = AddFile(target, "Components", "Layout", "NavMenu.razor.css");
            AddFile(shared, "Routes.razor");
            AddFile(shared, "Layout", "NavMenu.razor.css");
            var projectReferences = CreateProjectReferences(shared);
            target = workspace.CurrentSolution.GetProject(target.Id);

            var routesResult = BlazorIdentityGenerator.ResolveDocumentToModify(
                target,
                "Routes.razor",
                new DefaultFileSystem(),
                projectReferences,
                new[] { shared.FilePath },
                required: true);
            var navMenuCssResult = BlazorIdentityGenerator.ResolveDocumentToModify(
                target,
                "NavMenu.razor.css",
                new DefaultFileSystem(),
                projectReferences,
                new[] { shared.FilePath },
                required: false);

            Assert.Equal(targetRoutes, routesResult.Name);
            Assert.Equal(targetNavMenuCss, navMenuCssResult.Name);
        }

        [Fact]
        public void ResolveDocumentToModifyRejectsDirectlyReferencedSharedProject()
        {
            using var fileProvider = new TemporaryFileProvider();
            using var workspace = new AdhocWorkspace();
            var target = AddProject(workspace, fileProvider.Root, "MauiApp.Web");
            var shared = AddProject(workspace, fileProvider.Root, "MauiApp.Shared");
            AddFile(shared, "Routes.razor");
            var sharedNavMenuCss = AddFile(shared, "Layout", "NavMenu.razor.css");
            var projectReferences = CreateProjectReferences(shared);
            target = workspace.CurrentSolution.GetProject(target.Id);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                BlazorIdentityGenerator.ResolveDocumentToModify(
                    target,
                    "Routes.razor",
                    new DefaultFileSystem(),
                    projectReferences,
                    new[] { Path.GetRelativePath(Path.GetDirectoryName(target.FilePath), shared.FilePath) },
                    required: true));
            var navMenuCssResult = BlazorIdentityGenerator.ResolveDocumentToModify(
                target,
                "NavMenu.razor.css",
                new DefaultFileSystem(),
                projectReferences,
                new[] { Path.GetRelativePath(Path.GetDirectoryName(target.FilePath), shared.FilePath) },
                required: false);

            Assert.Contains("not currently supported", exception.Message);
            Assert.Contains(shared.FilePath, exception.Message);
            Assert.Contains(target.FilePath, exception.Message);
            Assert.Contains("No files were generated", exception.Message);
            Assert.Null(navMenuCssResult);
            Assert.True(File.Exists(sharedNavMenuCss));
        }

        [Fact]
        public void ResolveDocumentToModifyIgnoresTransitiveProject()
        {
            using var fileProvider = new TemporaryFileProvider();
            using var workspace = new AdhocWorkspace();
            var target = AddProject(workspace, fileProvider.Root, "Web");
            var direct = AddProject(workspace, fileProvider.Root, "Features");
            var transitive = AddProject(workspace, fileProvider.Root, "SharedUi");
            AddFile(transitive, "Routes.razor");
            var projectReferences = CreateProjectReferences(direct, transitive);
            target = workspace.CurrentSolution.GetProject(target.Id);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                BlazorIdentityGenerator.ResolveDocumentToModify(
                    target,
                    "Routes.razor",
                    new DefaultFileSystem(),
                    projectReferences,
                    new[] { direct.FilePath },
                    required: true));

            Assert.Contains("Could not find", exception.Message);
            Assert.Contains("Routes.razor", exception.Message);
        }

        [Fact]
        public void ResolveDocumentToModifyRejectsAmbiguousDirectReferences()
        {
            using var fileProvider = new TemporaryFileProvider();
            using var workspace = new AdhocWorkspace();
            var target = AddProject(workspace, fileProvider.Root, "Web");
            var shared = AddProject(workspace, fileProvider.Root, "SharedUi");
            var theme = AddProject(workspace, fileProvider.Root, "ThemeUi");
            AddFile(shared, "Routes.razor");
            AddFile(theme, "Routes.razor");
            var projectReferences = CreateProjectReferences(shared, theme);
            target = workspace.CurrentSolution.GetProject(target.Id);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                BlazorIdentityGenerator.ResolveDocumentToModify(
                    target,
                    "Routes.razor",
                    new DefaultFileSystem(),
                    projectReferences,
                    new[] { shared.FilePath, theme.FilePath },
                    required: true));

            Assert.Contains("Multiple directly referenced projects", exception.Message);
            Assert.Contains(shared.FilePath, exception.Message);
            Assert.Contains(theme.FilePath, exception.Message);
        }

        [Fact]
        public void ResolveDocumentToModifyAllowsMissingOptionalFile()
        {
            using var fileProvider = new TemporaryFileProvider();
            using var workspace = new AdhocWorkspace();
            var target = AddProject(workspace, fileProvider.Root, "EmptyWeb");

            var result = BlazorIdentityGenerator.ResolveDocumentToModify(
                target,
                "NavMenu.razor.css",
                new DefaultFileSystem(),
                Array.Empty<ProjectReferenceInformation>(),
                Array.Empty<string>(),
                required: false);

            Assert.Null(result);
        }

        private static Project AddProject(AdhocWorkspace workspace, string root, string name)
        {
            var projectDirectory = Path.Combine(root, name);
            Directory.CreateDirectory(projectDirectory);
            var projectPath = Path.Combine(projectDirectory, $"{name}.csproj");
            File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk.Razor\" />");
            return workspace.AddProject(ProjectInfo.Create(
                ProjectId.CreateNewId(),
                VersionStamp.Default,
                name,
                name,
                LanguageNames.CSharp,
                filePath: projectPath));
        }

        private static string AddFile(Project project, params string[] relativePath)
        {
            return AddFileWithContents(project, "<Router />", relativePath);
        }

        private static string AddFileWithContents(Project project, string contents, params string[] relativePath)
        {
            var filePath = Path.Combine(
                Path.GetDirectoryName(project.FilePath),
                Path.Combine(relativePath));
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, contents);
            return filePath;
        }

        private static IReadOnlyList<ProjectReferenceInformation> CreateProjectReferences(params Project[] projects)
        {
            var references = new List<ProjectReferenceInformation>();
            foreach (var project in projects)
            {
                references.Add(new ProjectReferenceInformation
                {
                    ProjectName = project.Name,
                    AssemblyName = project.AssemblyName,
                    FullPath = project.FilePath,
                    CompilationItems = new List<string>()
                });
            }

            return references;
        }
    }
}
