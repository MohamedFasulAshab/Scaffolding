// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Tools.Scaffold.Tests.Helpers;
using Xunit;

namespace Microsoft.DotNet.Tools.Scaffold.Tests.AspNet.Integration.MVC;

public class RazorViewsNet10IntegrationTests : RazorViewsIntegrationTestsBase
{
    protected override string TargetFramework => "net10.0";
    protected override string TestClassName => nameof(RazorViewsNet10IntegrationTests);

    [Fact]
    public async Task Scaffold_Views_Net10_CliInvocation()
    {
        File.WriteAllText(_testProjectPath, ProjectContent);
        File.WriteAllText(Path.Combine(_testProjectDir, "Program.cs"), ScaffoldCliHelper.GetMinimalProgramCs());
        var modelsDir = Path.Combine(_testProjectDir, "Models");
        Directory.CreateDirectory(modelsDir);
        File.WriteAllText(Path.Combine(modelsDir, "TestModel.cs"), ScaffoldCliHelper.GetModelClassContent("TestProject", "TestModel"));

        var (preExitCode, preOutput, preError) = await RunBuildAsync(_testProjectDir);
        Assert.True(preExitCode == 0,
            $"Project should build before scaffolding.\nExit code: {preExitCode}\nOutput: {preOutput}\nError: {preError}");

        var (cliExitCode, cliOutput, cliError) = await ScaffoldCliHelper.RunScaffoldAsync(
            TargetFramework,
            "views",
            "--project", _testProjectPath,
            "--model", "TestModel",
            "--page", "CRUD");
        Assert.True(cliExitCode == 0, $"CLI scaffold should succeed.\nOutput: {cliOutput}\nError: {cliError}");

        // Assert — expected files were created
        var viewsDir = Path.Combine(_testProjectDir, "Views", "TestModel");
        Assert.True(Directory.Exists(viewsDir), "Views/TestModel directory should be created.");
        foreach (var view in new[] { "Create.cshtml", "Delete.cshtml", "Details.cshtml", "Edit.cshtml", "Index.cshtml" })
        {
            Assert.True(File.Exists(Path.Combine(viewsDir, view)), $"View '{view}' should be created.");
        }
        Assert.True(File.Exists(Path.Combine(_testProjectDir, "Views", "Shared", "_ValidationScriptsPartial.cshtml")),
            "_ValidationScriptsPartial.cshtml should be created.");

        // Assert — no NuGet errors and project builds after scaffolding
        Assert.False(cliOutput.Contains("error: NU"),
            $"Scaffolding should not produce NuGet errors for {TargetFramework}.\nOutput: {cliOutput}");
        var (postExitCode, postOutput, postError) = await RunBuildAsync(_testProjectDir);
        Assert.True(postExitCode == 0,
            $"Project should build after scaffolding.\nExit code: {postExitCode}\nOutput: {postOutput}\nError: {postError}");
    }

    [Fact]
    public async Task Scaffold_Views_Net10_ModelInReferencedProject()
    {
        WriteReferencingWebProject("ModelsLib");
        WriteClassLibrary("ModelsLib", ScaffoldCliHelper.GetModelClassContent("ModelsLib", "TestModel"));

        var (preExitCode, preOutput, preError) = await RunBuildAsync(_testProjectDir);
        Assert.True(preExitCode == 0,
            $"Project should build before scaffolding.\nExit code: {preExitCode}\nOutput: {preOutput}\nError: {preError}");

        var (cliExitCode, cliOutput, cliError) = await ScaffoldCliHelper.RunScaffoldAsync(
            TargetFramework,
            "views",
            "--project", _testProjectPath,
            "--model", "TestModel",
            "--page", "CRUD");
        Assert.True(cliExitCode == 0, $"CLI scaffold should succeed.\nOutput: {cliOutput}\nError: {cliError}");

        var viewsDir = Path.Combine(_testProjectDir, "Views", "TestModel");
        foreach (var view in new[] { "Create.cshtml", "Delete.cshtml", "Details.cshtml", "Edit.cshtml", "Index.cshtml" })
        {
            var viewPath = Path.Combine(viewsDir, view);
            Assert.True(File.Exists(viewPath), $"View '{view}' should be created.");
            Assert.Contains("ModelsLib.Models.TestModel", File.ReadAllText(viewPath));
        }

        var (postExitCode, postOutput, postError) = await RunBuildAsync(_testProjectDir);
        Assert.True(postExitCode == 0,
            $"Project should build after scaffolding.\nExit code: {postExitCode}\nOutput: {postOutput}\nError: {postError}");
    }

    [Fact]
    public async Task Scaffold_Views_Net10_AmbiguousModelInReferencedProjects_Fails()
    {
        WriteReferencingWebProject("ModelsA", "ModelsB");
        WriteClassLibrary("ModelsA", ScaffoldCliHelper.GetModelClassContent("ModelsA", "TestModel"));
        WriteClassLibrary("ModelsB", ScaffoldCliHelper.GetModelClassContent("ModelsB", "TestModel"));

        var (preExitCode, preOutput, preError) = await RunBuildAsync(_testProjectDir);
        Assert.True(preExitCode == 0,
            $"Project should build before scaffolding.\nExit code: {preExitCode}\nOutput: {preOutput}\nError: {preError}");

        var (cliExitCode, cliOutput, cliError) = await ScaffoldCliHelper.RunScaffoldAsync(
            TargetFramework,
            "views",
            "--project", _testProjectPath,
            "--model", "TestModel",
            "--page", "CRUD");

        Assert.NotEqual(0, cliExitCode);
        Assert.Contains(
            "Multiple classes match --model 'TestModel' in referenced projects: 'ModelsA.Models.TestModel' (ModelsA), 'ModelsB.Models.TestModel' (ModelsB). Use a fully qualified class name.",
            cliOutput + cliError);
        Assert.False(Directory.Exists(Path.Combine(_testProjectDir, "Views", "TestModel")), "No views should be generated.");
    }

    private void WriteReferencingWebProject(params string[] libraryNames)
    {
        var references = string.Concat(libraryNames.Select(name => $@"
    <ProjectReference Include=""..\{name}\{name}.csproj"" />"));
        File.WriteAllText(_testProjectPath, $@"<Project Sdk=""Microsoft.NET.Sdk.Web"">
  <PropertyGroup>
    <TargetFramework>{TargetFramework}</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>{references}
  </ItemGroup>
</Project>");
        File.WriteAllText(Path.Combine(_testProjectDir, "Program.cs"), ScaffoldCliHelper.GetMinimalProgramCs());
    }

    private void WriteClassLibrary(string name, string modelContent)
    {
        var libraryDir = Path.Combine(_testDirectory, name);
        Directory.CreateDirectory(libraryDir);
        File.WriteAllText(Path.Combine(libraryDir, $"{name}.csproj"), $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>{TargetFramework}</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>");
        File.WriteAllText(Path.Combine(libraryDir, "TestModel.cs"), modelContent);
    }
}
