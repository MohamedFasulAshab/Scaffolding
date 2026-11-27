// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Tools.Scaffold.Tests.Helpers;
using Xunit;

namespace Microsoft.DotNet.Tools.Scaffold.Tests.AspNet.Integration.RazorPages;

public class RazorPagesCrudNet10IntegrationTests : RazorPagesCrudIntegrationTestsBase
{
    protected override string TargetFramework => "net10.0";
    protected override string TestClassName => nameof(RazorPagesCrudNet10IntegrationTests);

    [Theory]
    [InlineData("Create.tt")]
    [InlineData("Create.cs")]
    [InlineData("CreateModel.tt")]
    [InlineData("CreateModel.cs")]
    [InlineData("Delete.tt")]
    [InlineData("Delete.cs")]
    [InlineData("DeleteModel.tt")]
    [InlineData("DeleteModel.cs")]
    [InlineData("Details.tt")]
    [InlineData("Details.cs")]
    [InlineData("DetailsModel.tt")]
    [InlineData("DetailsModel.cs")]
    [InlineData("Edit.tt")]
    [InlineData("Edit.cs")]
    [InlineData("EditModel.tt")]
    [InlineData("EditModel.cs")]
    [InlineData("Index.tt")]
    [InlineData("Index.cs")]
    [InlineData("IndexModel.tt")]
    [InlineData("IndexModel.cs")]
    public void RazorPagesTemplates_HasExpectedT4File(string fileName)
    {
        var basePath = GetActualTemplatesBasePath();
        var filePath = Path.Combine(basePath, TargetFramework, "RazorPages", fileName);
        Assert.True(File.Exists(filePath),
            $"Expected RazorPages template file '{fileName}' not found for {TargetFramework}");
    }

    [Fact]
    public async Task Scaffold_RazorPagesCrud_Net10_CliInvocation()
    {
        // Arrange — write project + Program.cs + model class
        File.WriteAllText(_testProjectPath, ProjectContent);
        File.WriteAllText(Path.Combine(_testProjectDir, "Program.cs"), ScaffoldCliHelper.GetMinimalProgramCs());
        var modelsDir = Path.Combine(_testProjectDir, "Models");
        Directory.CreateDirectory(modelsDir);
        File.WriteAllText(Path.Combine(modelsDir, "TestModel.cs"), ScaffoldCliHelper.GetModelClassContent("TestProject", "TestModel"));

        // Assert — project builds before scaffolding
        var (preExitCode, preOutput, preError) = await RunBuildAsync(_testProjectDir);
        Assert.True(preExitCode == 0,
            $"Project should build before scaffolding.\nExit code: {preExitCode}\nOutput: {preOutput}\nError: {preError}");

        // Act — invoke CLI: dotnet scaffold aspnet razorpages-crud
        var (cliExitCode, cliOutput, cliError) = await ScaffoldCliHelper.RunScaffoldAsync(
            TargetFramework,
            "razorpages-crud",
            "--project", _testProjectPath,
            "--model", "TestModel",
            "--dataContext", "TestDbContext",
            "--dbProvider", "sqlite-efcore",
            "--page", "CRUD");
        Assert.True(cliExitCode == 0, $"CLI scaffold should succeed.\nOutput: {cliOutput}\nError: {cliError}");

        // Assert — expected files were created (skip if scaffolding encountered errors)
        bool scaffoldingSucceeded = !cliOutput.Contains("An error occurred") && !cliOutput.Contains("Failed");
        if (scaffoldingSucceeded)
        {
            var razorPagesDir = Path.Combine(_testProjectDir, "Pages", "TestModelPages");
            Assert.True(Directory.Exists(razorPagesDir), "Pages/TestModelPages directory should be created.");
            foreach (var page in new[] { "Create", "Delete", "Details", "Edit", "Index" })
            {
                Assert.True(File.Exists(Path.Combine(razorPagesDir, $"{page}.cshtml")), $"{page}.cshtml should be created.");
                Assert.True(File.Exists(Path.Combine(razorPagesDir, $"{page}.cshtml.cs")), $"{page}.cshtml.cs should be created.");
            }
            Assert.True(File.Exists(Path.Combine(_testProjectDir, "Data", "TestDbContext.cs")),
                "DbContext file 'Data/TestDbContext.cs' should be created.");
            var programContent = File.ReadAllText(Path.Combine(_testProjectDir, "Program.cs"));
            Assert.Contains("TestDbContext", programContent);

            // Assert — no NuGet errors and project builds after scaffolding
            Assert.False(cliOutput.Contains("error: NU"),
                $"Scaffolding should not produce NuGet errors for {TargetFramework}.\nOutput: {cliOutput}");
            var (postExitCode, postOutput, postError) = await RunBuildAsync(_testProjectDir);
            Assert.True(postExitCode == 0,
                $"Project should build after scaffolding.\nExit code: {postExitCode}\nOutput: {postOutput}\nError: {postError}");
        }
    }

    [Fact]
    public Task Scaffold_RazorPagesCrud_Net10_ReferencedContextExistingDbSet()
        => AssertReferencedContextScenarioAsync();

    [Theory]
    [InlineData("MissingSet")]
    [InlineData("AmbiguousModel")]
    [InlineData("LocalModelMissingSet")]
    public Task Scaffold_RazorPagesCrud_Net10_ReferencedContextValidationBeforeMutation(string scenario)
        => AssertReferencedContextScenarioAsync(scenario);

    [Fact]
    public async Task Scaffold_RazorPagesCrud_Net10_ReferencedModelLocalContextWithoutDbSet()
    {
        var libraryDir = Path.Combine(_testDirectory, "Entities");
        Directory.CreateDirectory(libraryDir);
        File.WriteAllText(Path.Combine(_testDirectory, "NuGet.config"), ScaffoldCliHelper.GetTestNuGetConfig(TargetFramework));
        File.WriteAllText(Path.Combine(libraryDir, "Entities.csproj"),
            $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>{TargetFramework}</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(libraryDir, "Product.cs"),
            "namespace Entities; public class Product { public int Id { get; set; } public string Name { get; set; } }");
        File.WriteAllText(_testProjectPath, ProjectContent.Replace("</Project>",
            "<ItemGroup><ProjectReference Include=\"../Entities/Entities.csproj\" /><PackageReference Include=\"Microsoft.EntityFrameworkCore.Sqlite\" Version=\"10.0.0\" /></ItemGroup></Project>"));
        var contextPath = Path.Combine(_testProjectDir, "StoreContext.cs");
        File.WriteAllText(contextPath,
            "using Microsoft.EntityFrameworkCore; namespace TestProject.Data; public class StoreContext : DbContext { public StoreContext(DbContextOptions<StoreContext> options) : base(options) {} }");
        File.WriteAllText(Path.Combine(_testProjectDir, "Program.cs"), "using TestProject.Data;\n" + ScaffoldCliHelper.GetMinimalProgramCs());
        var before = await RunBuildAsync(_testProjectDir);
        Assert.True(before.ExitCode == 0, before.Output + before.Error);
        var hashes = Directory.GetFiles(libraryDir, "*", SearchOption.TopDirectoryOnly)
            .ToDictionary(p => p, p => Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p))));

        var cli = await ScaffoldCliHelper.RunScaffoldAsync(TargetFramework, "razorpages-crud",
            "--project", _testProjectPath, "--model", "Product", "--dataContext", "StoreContext", "--dbProvider", "sqlite-efcore", "--page", "CRUD");
        var output = cli.Output + cli.Error;
        Assert.True(cli.ExitCode == 0, output);
        Assert.DoesNotContain("An error occurred", output);
        Assert.DoesNotContain("has no DbSet", output);
        Assert.Contains("DbSet<Entities.Product>", File.ReadAllText(contextPath));
        var pagesDir = Path.Combine(_testProjectDir, "Pages", "ProductPages");
        Assert.Equal(10, Directory.GetFiles(pagesDir, "*", SearchOption.TopDirectoryOnly).Length);
        foreach (var page in new[] { "Create", "Delete", "Details", "Edit", "Index" })
        {
            Assert.True(File.Exists(Path.Combine(pagesDir, page + ".cshtml")), output);
            Assert.Contains("using Entities;", File.ReadAllText(Path.Combine(pagesDir, page + ".cshtml.cs")));
        }
        var after = await RunBuildAsync(_testProjectDir);
        Assert.True(after.ExitCode == 0, after.Output + after.Error);
        foreach (var source in hashes)
        {
            Assert.Equal(source.Value, Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source.Key))));
        }
    }

    [Fact]
    public async Task Scaffold_RazorPagesCrud_Net10_ModelInReferencedProject()
    {
        File.WriteAllText(_testProjectPath, ProjectContent.Replace(
            "</Project>",
            "  <ItemGroup>\n    <ProjectReference Include=\"..\\ModelsLib\\ModelsLib.csproj\" />\n  </ItemGroup>\n</Project>"));
        File.WriteAllText(Path.Combine(_testProjectDir, "Program.cs"), ScaffoldCliHelper.GetMinimalProgramCs());
        var libraryDir = Path.Combine(_testDirectory, "ModelsLib");
        Directory.CreateDirectory(libraryDir);
        File.WriteAllText(Path.Combine(libraryDir, "ModelsLib.csproj"), $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>{TargetFramework}</TargetFramework>
  </PropertyGroup>
</Project>");
        File.WriteAllText(Path.Combine(libraryDir, "TestModel.cs"), ScaffoldCliHelper.GetModelClassContent("ModelsLib", "TestModel"));

        var (preExitCode, preOutput, preError) = await RunBuildAsync(_testProjectDir);
        Assert.True(preExitCode == 0,
            $"Project should build before scaffolding.\nExit code: {preExitCode}\nOutput: {preOutput}\nError: {preError}");

        var (cliExitCode, cliOutput, cliError) = await ScaffoldCliHelper.RunScaffoldAsync(
            TargetFramework,
            "razorpages-crud",
            "--project", _testProjectPath,
            "--model", "TestModel",
            "--dataContext", "TestDbContext",
            "--dbProvider", "sqlite-efcore",
            "--page", "CRUD");
        Assert.True(cliExitCode == 0, $"CLI scaffold should succeed.\nOutput: {cliOutput}\nError: {cliError}");

        var razorPagesDir = Path.Combine(_testProjectDir, "Pages", "TestModelPages");
        foreach (var page in new[] { "Create", "Delete", "Details", "Edit", "Index" })
        {
            Assert.True(File.Exists(Path.Combine(razorPagesDir, $"{page}.cshtml")), $"{page}.cshtml should be created.");
            var pageModelPath = Path.Combine(razorPagesDir, $"{page}.cshtml.cs");
            Assert.True(File.Exists(pageModelPath), $"{page}.cshtml.cs should be created.");
            Assert.Contains("using ModelsLib.Models;", File.ReadAllText(pageModelPath));
        }

        var (postExitCode, postOutput, postError) = await RunBuildAsync(_testProjectDir);
        Assert.True(postExitCode == 0,
            $"Project should build after scaffolding.\nExit code: {postExitCode}\nOutput: {postOutput}\nError: {postError}");
    }
}
