// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.DotNet.Scaffolding.Shared;
using Microsoft.DotNet.Scaffolding.Shared.ProjectModel;
using Microsoft.VisualStudio.Web.CodeGeneration;
using Microsoft.Extensions.ProjectModel;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Blazor;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.VisualStudio.Web.CodeGenerators.Mvc
{
    public class BlazorIdentityGeneratorTests
    {
        private readonly ITestOutputHelper _output;

        public BlazorIdentityGeneratorTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void ResolveLayoutNamespaceUsesMsBuildProjectReferenceMetadata()
        {
            using (var fileProvider = new TemporaryFileProvider())
            {
                new MsBuildProjectSetupHelper().SetupReferencedCodeGenerationProject(fileProvider, _output);
                var projectDirectory = Path.Combine(fileProvider.Root, MsBuildProjectStrings.RootProjectFolder);
                var projectPath = Path.Combine(projectDirectory, MsBuildProjectStrings.RootProjectName);
                var projectContext = new MsBuildProjectContextBuilder(projectPath, "Dummy").Build();

                var result = BlazorIdentityGenerator.ResolveLayoutNamespace(
                    projectContext.RootNamespace,
                    projectDirectory,
                    new DefaultFileSystem(),
                    projectContext.ProjectReferenceInformation,
                    projectContext.ProjectReferences);

                Assert.Equal("Custom.Library.Root.Layout.MainLayout", result);
            }
        }

        [Fact]
        public void ResolveLayoutNamespaceUsesLayoutFromReferencedProject()
        {
            var fileSystem = new Mock<IFileSystem>();
            var sharedProjectDirectory = Path.Combine("src", "MyApp.Shared");
            var mainLayoutPath = Path.Combine(sharedProjectDirectory, "Layout", "MainLayout.razor");
            fileSystem
                .Setup(fs => fs.DirectoryExists(sharedProjectDirectory))
                .Returns(true);
            fileSystem
                .Setup(fs => fs.FileExists(mainLayoutPath))
                .Returns(true);
            var projectReferences = new List<ProjectReferenceInformation>
            {
                new ProjectReferenceInformation
                {
                    FullPath = Path.Combine(sharedProjectDirectory, "MyApp.Shared.csproj"),
                    ProjectName = "MyApp.Shared",
                    RootNamespace = "MyApp.Shared"
                }
            };

            var result = BlazorIdentityGenerator.ResolveLayoutNamespace(
                "MyApp.Web",
                Path.Combine("src", "MyApp.Web"),
                fileSystem.Object,
                projectReferences,
                projectReferences.Select(reference => reference.FullPath));

            Assert.Equal("MyApp.Shared.Layout.MainLayout", result);
        }

        [Fact]
        public void ResolveLayoutNamespacePrefersLayoutInServerProject()
        {
            var fileSystem = new Mock<IFileSystem>();
            var applicationBasePath = Path.Combine("src", "MyApp.Web");
            fileSystem
                .Setup(fs => fs.FileExists(Path.Combine(applicationBasePath, "Components", "Layout", "MainLayout.razor")))
                .Returns(true);

            var result = BlazorIdentityGenerator.ResolveLayoutNamespace(
                "MyApp.Web",
                applicationBasePath,
                fileSystem.Object,
                new List<ProjectReferenceInformation>(),
                Array.Empty<string>());

            Assert.Equal("MyApp.Web.Components.Layout.MainLayout", result);
        }

        [Fact]
        public void ResolveLayoutNamespaceFallsBackToClientProjectConvention()
        {
            var fileSystem = new Mock<IFileSystem>();

            var result = BlazorIdentityGenerator.ResolveLayoutNamespace(
                "MyApp",
                Path.Combine("src", "MyApp"),
                fileSystem.Object,
                new List<ProjectReferenceInformation>(),
                Array.Empty<string>());

            Assert.Equal("MyApp.Client.Layout.MainLayout", result);
        }

        [Fact]
        public void ResolveLayoutNamespacePrefersClientProjectOverOtherReferences()
        {
            var fileSystem = new Mock<IFileSystem>();
            var sharedProjectDirectory = Path.Combine("src", "MyApp.Shared");
            var clientProjectDirectory = Path.Combine("src", "MyApp.Client");
            SetupLayout(fileSystem, sharedProjectDirectory);
            SetupLayout(fileSystem, clientProjectDirectory);
            var projectReferences = new List<ProjectReferenceInformation>
            {
                CreateProjectReference(sharedProjectDirectory, "MyApp.Shared"),
                CreateProjectReference(clientProjectDirectory, "MyApp.Client")
            };

            var result = BlazorIdentityGenerator.ResolveLayoutNamespace(
                "MyApp",
                Path.Combine("src", "MyApp"),
                fileSystem.Object,
                projectReferences,
                projectReferences.Select(reference => reference.FullPath));

            Assert.Equal("MyApp.Client.Layout.MainLayout", result);
        }

        [Fact]
        public void ResolveLayoutNamespaceRejectsAmbiguousSharedLayouts()
        {
            var fileSystem = new Mock<IFileSystem>();
            var sharedProjectDirectory = Path.Combine("src", "MyApp.Shared");
            var themeProjectDirectory = Path.Combine("src", "Company.Theme");
            SetupLayout(fileSystem, sharedProjectDirectory);
            SetupLayout(fileSystem, themeProjectDirectory);
            var projectReferences = new List<ProjectReferenceInformation>
            {
                CreateProjectReference(sharedProjectDirectory, "MyApp.Shared"),
                CreateProjectReference(themeProjectDirectory, "Company.Theme")
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                BlazorIdentityGenerator.ResolveLayoutNamespace(
                    "MyApp",
                    Path.Combine("src", "MyApp"),
                    fileSystem.Object,
                    projectReferences,
                    projectReferences.Select(reference => reference.FullPath)));

            Assert.Contains("Multiple MainLayout components were found", exception.Message);
            Assert.Contains("MyApp.Shared.Layout.MainLayout", exception.Message);
            Assert.Contains("Company.Theme.Layout.MainLayout", exception.Message);
        }

        [Fact]
        public void ResolveLayoutNamespaceDoesNotPreferUnrelatedClientProject()
        {
            var fileSystem = new Mock<IFileSystem>();
            var sharedProjectDirectory = Path.Combine("src", "MyApp.Shared");
            var unrelatedClientDirectory = Path.Combine("src", "Company.Client");
            SetupLayout(fileSystem, sharedProjectDirectory);
            SetupLayout(fileSystem, unrelatedClientDirectory);
            var projectReferences = new List<ProjectReferenceInformation>
            {
                CreateProjectReference(sharedProjectDirectory, "MyApp.Shared"),
                CreateProjectReference(unrelatedClientDirectory, "Company.Client")
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                BlazorIdentityGenerator.ResolveLayoutNamespace(
                    "MyApp",
                    Path.Combine("src", "MyApp"),
                    fileSystem.Object,
                    projectReferences,
                    projectReferences.Select(reference => reference.FullPath)));

            Assert.Contains("Multiple MainLayout components were found", exception.Message);
        }

        [Fact]
        public void ResolveLayoutNamespaceIgnoresTransitiveProjectLayouts()
        {
            var fileSystem = new Mock<IFileSystem>();
            var sharedProjectDirectory = Path.Combine("src", "MyApp.Shared");
            var transitiveProjectDirectory = Path.Combine("src", "Company.Theme");
            SetupLayout(fileSystem, sharedProjectDirectory);
            SetupLayout(fileSystem, transitiveProjectDirectory);
            var directReference = CreateProjectReference(sharedProjectDirectory, "MyApp.Shared");
            var projectReferences = new List<ProjectReferenceInformation>
            {
                directReference,
                CreateProjectReference(transitiveProjectDirectory, "Company.Theme")
            };

            var result = BlazorIdentityGenerator.ResolveLayoutNamespace(
                "MyApp.Web",
                Path.Combine("src", "MyApp.Web"),
                fileSystem.Object,
                projectReferences,
                new[] { directReference.FullPath });

            Assert.Equal("MyApp.Shared.Layout.MainLayout", result);
        }

        [Fact]
        public void ResolveLayoutNamespaceUsesNamespaceDirectiveFromImports()
        {
            var fileSystem = new Mock<IFileSystem>();
            var sharedProjectDirectory = Path.Combine("src", "MyApp.Shared");
            var mainLayoutPath = SetupLayout(fileSystem, sharedProjectDirectory);
            var importsPath = Path.Combine(sharedProjectDirectory, "Layout", "_Imports.razor");
            fileSystem.Setup(fs => fs.FileExists(importsPath)).Returns(true);
            fileSystem.Setup(fs => fs.ReadAllText(mainLayoutPath)).Returns("<main>@Body</main>");
            fileSystem.Setup(fs => fs.ReadAllText(importsPath)).Returns("@namespace Custom.Shared.Layout");
            var projectReference = CreateProjectReference(sharedProjectDirectory, "MyApp.Shared");

            var result = BlazorIdentityGenerator.ResolveLayoutNamespace(
                "MyApp.Web",
                Path.Combine("src", "MyApp.Web"),
                fileSystem.Object,
                new[] { projectReference },
                new[] { projectReference.FullPath });

            Assert.Equal("Custom.Shared.Layout.MainLayout", result);
        }

        private static ProjectReferenceInformation CreateProjectReference(string projectDirectory, string projectName)
        {
            return new ProjectReferenceInformation
            {
                FullPath = Path.Combine(projectDirectory, $"{projectName}.csproj"),
                ProjectName = projectName,
                RootNamespace = projectName
            };
        }

        private static string SetupLayout(Mock<IFileSystem> fileSystem, string projectDirectory)
        {
            var mainLayoutPath = Path.Combine(projectDirectory, "Layout", "MainLayout.razor");
            fileSystem
                .Setup(fs => fs.DirectoryExists(projectDirectory))
                .Returns(true);
            fileSystem
                .Setup(fs => fs.FileExists(mainLayoutPath))
                .Returns(true);
            fileSystem
                .Setup(fs => fs.ReadAllText(mainLayoutPath))
                .Returns(string.Empty);
            return mainLayoutPath;
        }
    }
}
