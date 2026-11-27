// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.DotNet.Tools.Scaffold.Tests.Helpers;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.DotNet.Scaffolding.Core.Scaffolders;
using Microsoft.DotNet.Scaffolding.Internal.Services;
using Microsoft.DotNet.Tools.Scaffold.AspNet;
using Microsoft.DotNet.Tools.Scaffold.AspNet.Commands;
using Microsoft.DotNet.Tools.Scaffold.AspNet.Common;
using Microsoft.DotNet.Tools.Scaffold.AspNet.Helpers;
using Microsoft.DotNet.Tools.Scaffold.AspNet.ScaffoldSteps;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.DotNet.Tools.Scaffold.Tests.AspNet.Integration;

/// <summary>
/// Shared base class for Razor Pages CRUD integration tests across .NET versions.
/// Subclasses provide the target framework via <see cref="TargetFramework"/>.
/// </summary>
[Trait("Suite", "ScaffoldIntegration")]
[Trait("Family", "razorpages-crud")]
public abstract class RazorPagesCrudIntegrationTestsBase : IDisposable
{
    protected abstract string TargetFramework { get; }
    protected abstract string TestClassName { get; }

    protected readonly string _testDirectory;
    protected readonly string _testProjectDir;
    protected readonly string _testProjectPath;
    protected readonly Mock<IFileSystem> _mockFileSystem;
    protected readonly TestTelemetryService _testTelemetryService;
    protected readonly Mock<IScaffolder> _mockScaffolder;
    protected readonly ScaffolderContext _context;

    protected RazorPagesCrudIntegrationTestsBase()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), TestClassName, Guid.NewGuid().ToString());
        _testProjectDir = Path.Combine(_testDirectory, "TestProject");
        _testProjectPath = Path.Combine(_testProjectDir, "TestProject.csproj");
        Directory.CreateDirectory(_testProjectDir);
        File.WriteAllText(Path.Combine(_testProjectDir, "NuGet.config"), ScaffoldCliHelper.GetTestNuGetConfig(TargetFramework));

        _mockFileSystem = new Mock<IFileSystem>();
        _testTelemetryService = new TestTelemetryService();

        _mockScaffolder = new Mock<IScaffolder>();
        _mockScaffolder.Setup(s => s.DisplayName).Returns(AspnetStrings.RazorPage.CrudDisplayName);
        _mockScaffolder.Setup(s => s.Name).Returns(AspnetStrings.RazorPage.Crud);
        _context = new ScaffolderContext(_mockScaffolder.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try { Directory.Delete(_testDirectory, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    protected string ProjectContent => $@"<Project Sdk=""Microsoft.NET.Sdk.Web"">
  <PropertyGroup>
    <TargetFramework>{TargetFramework}</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>";

    #region ValidateRazorPagesStep — Validation Logic

    [Fact]
    public async Task ValidateRazorPagesStep_FailsWithNullProject()
    {
        var step = CreateValidateRazorPagesStep();
        step.Project = null;
        step.Model = "Customer";
        step.Page = "CRUD";
        step.DataContext = "ShopDbContext";
        step.DatabaseProvider = PackageConstants.EfConstants.SQLite;

        var result = await step.ExecuteAsync(_context);
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateRazorPagesStep_FailsWithEmptyProject()
    {
        var step = CreateValidateRazorPagesStep();
        step.Project = string.Empty;
        step.Model = "Customer";
        step.Page = "CRUD";
        step.DataContext = "ShopDbContext";
        step.DatabaseProvider = PackageConstants.EfConstants.SQLite;

        var result = await step.ExecuteAsync(_context);
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateRazorPagesStep_FailsWithNonExistentProject()
    {
        _mockFileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(false);

        var step = CreateValidateRazorPagesStep();
        step.Project = @"C:\NonExistent\Project.csproj";
        step.Model = "Customer";
        step.Page = "CRUD";
        step.DataContext = "ShopDbContext";
        step.DatabaseProvider = PackageConstants.EfConstants.SQLite;

        var result = await step.ExecuteAsync(_context);
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateRazorPagesStep_FailsWithNullModel()
    {
        _mockFileSystem.Setup(fs => fs.FileExists(_testProjectPath)).Returns(true);

        var step = CreateValidateRazorPagesStep();
        step.Project = _testProjectPath;
        step.Model = null;
        step.Page = "CRUD";
        step.DataContext = "ShopDbContext";
        step.DatabaseProvider = PackageConstants.EfConstants.SQLite;

        var result = await step.ExecuteAsync(_context);
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateRazorPagesStep_FailsWithEmptyModel()
    {
        _mockFileSystem.Setup(fs => fs.FileExists(_testProjectPath)).Returns(true);

        var step = CreateValidateRazorPagesStep();
        step.Project = _testProjectPath;
        step.Model = string.Empty;
        step.Page = "CRUD";
        step.DataContext = "ShopDbContext";
        step.DatabaseProvider = PackageConstants.EfConstants.SQLite;

        var result = await step.ExecuteAsync(_context);
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateRazorPagesStep_FailsWithNullPage()
    {
        _mockFileSystem.Setup(fs => fs.FileExists(_testProjectPath)).Returns(true);

        var step = CreateValidateRazorPagesStep();
        step.Project = _testProjectPath;
        step.Model = "Customer";
        step.Page = null;
        step.DataContext = "ShopDbContext";
        step.DatabaseProvider = PackageConstants.EfConstants.SQLite;

        var result = await step.ExecuteAsync(_context);
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateRazorPagesStep_FailsWithEmptyPage()
    {
        _mockFileSystem.Setup(fs => fs.FileExists(_testProjectPath)).Returns(true);

        var step = CreateValidateRazorPagesStep();
        step.Project = _testProjectPath;
        step.Model = "Customer";
        step.Page = string.Empty;
        step.DataContext = "ShopDbContext";
        step.DatabaseProvider = PackageConstants.EfConstants.SQLite;

        var result = await step.ExecuteAsync(_context);
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateRazorPagesStep_FailsWithNullDataContext()
    {
        _mockFileSystem.Setup(fs => fs.FileExists(_testProjectPath)).Returns(true);

        var step = CreateValidateRazorPagesStep();
        step.Project = _testProjectPath;
        step.Model = "Customer";
        step.Page = "CRUD";
        step.DataContext = null;
        step.DatabaseProvider = PackageConstants.EfConstants.SQLite;

        var result = await step.ExecuteAsync(_context);
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateRazorPagesStep_FailsWithEmptyDataContext()
    {
        _mockFileSystem.Setup(fs => fs.FileExists(_testProjectPath)).Returns(true);

        var step = CreateValidateRazorPagesStep();
        step.Project = _testProjectPath;
        step.Model = "Customer";
        step.Page = "CRUD";
        step.DataContext = string.Empty;
        step.DatabaseProvider = PackageConstants.EfConstants.SQLite;

        var result = await step.ExecuteAsync(_context);
        Assert.False(result);
    }

    #endregion

    #region ValidateRazorPagesStep — Telemetry

    [Fact]
    public async Task ValidateRazorPagesStep_TracksTelemetry_OnNullProjectFailure()
    {
        var step = CreateValidateRazorPagesStep();
        step.Project = null;
        step.Model = "Customer";
        step.Page = "CRUD";
        step.DataContext = "ShopDbContext";

        await step.ExecuteAsync(_context);

        Assert.Single(_testTelemetryService.TrackedEvents);
    }

    [Fact]
    public async Task ValidateRazorPagesStep_TracksTelemetry_OnNullModelFailure()
    {
        _mockFileSystem.Setup(fs => fs.FileExists(_testProjectPath)).Returns(true);

        var step = CreateValidateRazorPagesStep();
        step.Project = _testProjectPath;
        step.Model = null;
        step.Page = "CRUD";
        step.DataContext = "ShopDbContext";

        await step.ExecuteAsync(_context);

        Assert.Single(_testTelemetryService.TrackedEvents);
    }

    [Fact]
    public async Task ValidateRazorPagesStep_TracksTelemetry_OnNullPageFailure()
    {
        _mockFileSystem.Setup(fs => fs.FileExists(_testProjectPath)).Returns(true);

        var step = CreateValidateRazorPagesStep();
        step.Project = _testProjectPath;
        step.Model = "Customer";
        step.Page = null;
        step.DataContext = "ShopDbContext";

        await step.ExecuteAsync(_context);

        Assert.Single(_testTelemetryService.TrackedEvents);
    }

    [Fact]
    public async Task ValidateRazorPagesStep_TracksTelemetry_OnNullDataContextFailure()
    {
        _mockFileSystem.Setup(fs => fs.FileExists(_testProjectPath)).Returns(true);

        var step = CreateValidateRazorPagesStep();
        step.Project = _testProjectPath;
        step.Model = "Customer";
        step.Page = "CRUD";
        step.DataContext = null;

        await step.ExecuteAsync(_context);

        Assert.Single(_testTelemetryService.TrackedEvents);
    }

    #endregion

    #region Multiple Validation Failure Theories

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ValidateRazorPagesStep_FailsWithInvalidModel(string? model)
    {
        _mockFileSystem.Setup(fs => fs.FileExists(_testProjectPath)).Returns(true);

        var step = CreateValidateRazorPagesStep();
        step.Project = _testProjectPath;
        step.Model = model;
        step.Page = "CRUD";
        step.DataContext = "ShopDbContext";
        step.DatabaseProvider = PackageConstants.EfConstants.SQLite;

        var result = await step.ExecuteAsync(_context);
        Assert.False(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ValidateRazorPagesStep_FailsWithInvalidPage(string? page)
    {
        _mockFileSystem.Setup(fs => fs.FileExists(_testProjectPath)).Returns(true);

        var step = CreateValidateRazorPagesStep();
        step.Project = _testProjectPath;
        step.Model = "Customer";
        step.Page = page;
        step.DataContext = "ShopDbContext";
        step.DatabaseProvider = PackageConstants.EfConstants.SQLite;

        var result = await step.ExecuteAsync(_context);
        Assert.False(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ValidateRazorPagesStep_FailsWithInvalidDataContext(string? dataContext)
    {
        _mockFileSystem.Setup(fs => fs.FileExists(_testProjectPath)).Returns(true);

        var step = CreateValidateRazorPagesStep();
        step.Project = _testProjectPath;
        step.Model = "Customer";
        step.Page = "CRUD";
        step.DataContext = dataContext;
        step.DatabaseProvider = PackageConstants.EfConstants.SQLite;

        var result = await step.ExecuteAsync(_context);
        Assert.False(result);
    }

    #endregion

    #region CRUD Pages — Collection Tests

    [Fact]
    public void CrudPages_ContainsExpectedPageTypes()
    {
        var pages = BlazorCrudHelper.CRUDPages;
        Assert.Contains("CRUD", pages);
        Assert.Contains("Create", pages);
        Assert.Contains("Delete", pages);
        Assert.Contains("Details", pages);
        Assert.Contains("Edit", pages);
        Assert.Contains("Index", pages);
    }

    [Fact]
    public void CrudPages_ContainsNotFound()
    {
        Assert.Contains("NotFound", BlazorCrudHelper.CRUDPages);
    }

    #endregion

    #region EF Providers — Structure Tests

    [Fact]
    public void EfPackagesDict_ContainsAllFourProviders()
    {
        Assert.Equal(4, PackageConstants.EfConstants.EfPackagesDict.Count);
        Assert.True(PackageConstants.EfConstants.EfPackagesDict.ContainsKey(PackageConstants.EfConstants.SQLite));
        Assert.True(PackageConstants.EfConstants.EfPackagesDict.ContainsKey(PackageConstants.EfConstants.SQLite));
        Assert.True(PackageConstants.EfConstants.EfPackagesDict.ContainsKey(PackageConstants.EfConstants.CosmosDb));
        Assert.True(PackageConstants.EfConstants.EfPackagesDict.ContainsKey(PackageConstants.EfConstants.Postgres));
    }

    [Fact]
    public void UseDatabaseMethods_HasAllFourProviders()
    {
        Assert.Equal(4, PackageConstants.EfConstants.UseDatabaseMethods.Count);
    }

    #endregion

    #region Scaffolder Differentiation

    [Fact]
    public void RazorPagesCrud_IsDifferentFromMvcControllerCrud()
    {
        Assert.NotEqual(AspnetStrings.RazorPage.Crud, AspnetStrings.MVC.ControllerCrud);
    }

    [Fact]
    public void RazorPagesCrud_IsDifferentFromBlazorCrud()
    {
        Assert.NotEqual(AspnetStrings.RazorPage.Crud, AspnetStrings.Blazor.Crud);
    }

    [Fact]
    public void RazorPagesCrud_IsDifferentFromRazorPageEmpty()
    {
        Assert.NotEqual(AspnetStrings.RazorPage.Crud, AspnetStrings.RazorPage.Empty);
    }

    [Fact]
    public void RazorPagesCrud_Category_DiffersFromMvcCategory()
    {
        Assert.NotEqual(AspnetStrings.Catagories.RazorPages, AspnetStrings.Catagories.MVC);
    }

    #endregion

    #region RazorPages Templates

    [Fact]
    public void RazorPagesTemplates_FolderExists()
    {
        var basePath = GetActualTemplatesBasePath();
        var razorPagesDir = Path.Combine(basePath, TargetFramework, "RazorPages");
        Assert.True(Directory.Exists(razorPagesDir),
            $"RazorPages template folder should exist for {TargetFramework}");
    }

    protected void AssertRazorPagesTemplateFileExists(string subfolder, string fileName)
    {
        var basePath = GetActualTemplatesBasePath();
        var filePath = Path.Combine(basePath, TargetFramework, "RazorPages", subfolder, fileName);
        Assert.True(File.Exists(filePath),
            $"Expected RazorPages template file '{subfolder}/{fileName}' not found for {TargetFramework}");
    }

    #endregion

    #region Template Root — Expected Scaffolder Folders

    [Theory]
    [InlineData("BlazorCrud")]
    [InlineData("BlazorIdentity")]
    [InlineData("CodeModificationConfigs")]
    [InlineData("EfController")]
    [InlineData("Files")]
    [InlineData("Identity")]
    [InlineData("MinimalApi")]
    [InlineData("RazorPages")]
    [InlineData("Views")]
    public void Templates_HasExpectedScaffolderFolder(string folderName)
    {
        var basePath = GetActualTemplatesBasePath();
        var folderPath = Path.Combine(basePath, TargetFramework, folderName);
        Assert.True(Directory.Exists(folderPath),
            $"Expected template folder '{folderName}' not found for {TargetFramework}");
    }

    #endregion

    #region Helper Methods

    private ValidateRazorPagesStep CreateValidateRazorPagesStep()
    {
        return new ValidateRazorPagesStep(
            _mockFileSystem.Object,
            NullLogger<ValidateRazorPagesStep>.Instance,
            _testTelemetryService);
    }

    protected static string GetActualTemplatesBasePath()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
        var basePath = Path.Combine(assemblyDirectory!, "..", "..", "..", "..", "..", "src", "dotnet-scaffolding", "dotnet-scaffold", "AspNet", "Templates");
        return Path.GetFullPath(basePath);
    }

    protected async Task AssertReferencedContextScenarioAsync(string scenario = "Success")
    {
        var entitiesDir = Path.Combine(_testDirectory, "Entities");
        var dataDir = Path.Combine(_testDirectory, "DataAccess");
        Directory.CreateDirectory(entitiesDir);
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(_testDirectory, "NuGet.config"), ScaffoldCliHelper.GetTestNuGetConfig(TargetFramework));
        File.WriteAllText(Path.Combine(_testDirectory, "ReferencedContext.slnx"),
            "<Solution><Project Path=\"Entities/Entities.csproj\" /><Project Path=\"DataAccess/DataAccess.csproj\" /><Project Path=\"TestProject/TestProject.csproj\" /></Solution>");
        var libraryProject = $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>{TargetFramework}</TargetFramework></PropertyGroup>{{0}}</Project>";
        File.WriteAllText(Path.Combine(entitiesDir, "Entities.csproj"), string.Format(libraryProject, ""));
        File.WriteAllText(Path.Combine(entitiesDir, "Product.cs"), "namespace Entities; public class Product { public int Id { get; set; } public string Name { get; set; } }");
        var packageVersion = TargetFramework == "net8.0" ? "8.0.0" : "10.0.0";
        var package = $"<PackageReference Include=\"Microsoft.EntityFrameworkCore.Sqlite\" Version=\"{packageVersion}\" />";
        File.WriteAllText(Path.Combine(dataDir, "DataAccess.csproj"), string.Format(libraryProject,
            $"<ItemGroup><ProjectReference Include=\"../Entities/Entities.csproj\" />{package}</ItemGroup>"));
        var hasSet = scenario == "Success";
        File.WriteAllText(Path.Combine(dataDir, "StoreContext.cs"),
            "using Microsoft.EntityFrameworkCore; namespace DataAccess; public class StoreContext : DbContext { " +
            "public StoreContext(DbContextOptions<StoreContext> options) : base(options) {} " +
            (hasSet ? "public DbSet<Entities.Product> Products { get; set; }" : "") + " }");
        File.WriteAllText(_testProjectPath, ProjectContent.Replace("</Project>",
            $"<ItemGroup><ProjectReference Include=\"../Entities/Entities.csproj\" /><ProjectReference Include=\"../DataAccess/DataAccess.csproj\" />{package}</ItemGroup></Project>"));
        if (scenario == "LocalModelMissingSet")
        {
            File.WriteAllText(Path.Combine(_testProjectDir, "Product.cs"), "namespace WebModels; public class Product { public int Id { get; set; } }");
        }
        if (scenario == "AmbiguousModel")
        {
            File.WriteAllText(Path.Combine(dataDir, "Product.cs"), "namespace DataAccess; public class Product { public int Id { get; set; } }");
        }
        const string program = "using DataAccess; using Microsoft.EntityFrameworkCore;\nvar builder = WebApplication.CreateBuilder(args);\nbuilder.Services.AddRazorPages();\nbuilder.Services.AddDbContext<StoreContext>(options => options.UseSqlite(builder.Configuration.GetConnectionString(\"Products\")));\nvar app = builder.Build();\napp.MapRazorPages();\napp.Run();\n";
        const string appSettings = "{\"ConnectionStrings\":{\"Unchanged\":\"original\"}}";
        var programPath = Path.Combine(_testProjectDir, "Program.cs");
        var settingsPath = Path.Combine(_testProjectDir, "appsettings.json");
        File.WriteAllText(programPath, program);
        File.WriteAllText(settingsPath, appSettings);
        var before = await RunReferencedSolutionBuildAsync();
        Assert.True(before.ExitCode == 0, before.Output + before.Error);
        var librarySources = Directory.GetFiles(entitiesDir, "*.cs", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(dataDir, "*.cs", SearchOption.TopDirectoryOnly))
            .Concat(Directory.GetFiles(entitiesDir, "*.csproj"))
            .Concat(Directory.GetFiles(dataDir, "*.csproj"))
            .ToDictionary(p => p, p => Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p))));
        var webSources = Directory.GetFiles(_testProjectDir, "*", SearchOption.AllDirectories)
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)).OrderBy(p => p).ToArray();

        var cli = await ScaffoldCliHelper.RunScaffoldAsync(TargetFramework, "razorpages-crud",
            "--project", _testProjectPath, "--model", "Product", "--dataContext", "StoreContext", "--dbProvider", "sqlite-efcore", "--page", "CRUD");
        var output = cli.Output + cli.Error;
        if (hasSet)
        {
            Assert.True(cli.ExitCode == 0, output);
            Assert.Contains("Using existing DbContext 'DataAccess.StoreContext' from project 'DataAccess'", output);
            Assert.DoesNotContain("An error occurred", output);
            var pagesDir = Path.Combine(_testProjectDir, "Pages", "ProductPages");
            Assert.Equal(10, Directory.GetFiles(pagesDir, "*", SearchOption.TopDirectoryOnly).Length);
            foreach (var page in new[] { "Create", "Delete", "Details", "Edit", "Index" })
            {
                Assert.True(File.Exists(Path.Combine(pagesDir, page + ".cshtml")), output);
                var code = File.ReadAllText(Path.Combine(pagesDir, page + ".cshtml.cs"));
                Assert.Contains("using Entities;", code);
                Assert.Contains("using DataAccess;", code);
                Assert.Contains("_context.Products", code);
            }
            var after = await RunReferencedSolutionBuildAsync();
            Assert.True(after.ExitCode == 0, after.Output + after.Error);
        }
        else
        {
            Assert.NotEqual(0, cli.ExitCode);
            Assert.Contains(scenario == "AmbiguousModel" ? "Multiple classes match --model" : "has no DbSet", output);
            if (scenario != "AmbiguousModel")
            {
                Assert.Contains("DataAccess.StoreContext", output);
                Assert.Contains("Add a DbSet<", output);
            }
            Assert.Equal(webSources, Directory.GetFiles(_testProjectDir, "*", SearchOption.AllDirectories)
                .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                    !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)).OrderBy(p => p).ToArray());
        }
        Assert.False(Directory.Exists(Path.Combine(_testProjectDir, "Data")), output);
        Assert.Equal(program, File.ReadAllText(programPath));
        Assert.Equal(appSettings, File.ReadAllText(settingsPath));
        foreach (var source in librarySources)
        {
            Assert.Equal(source.Value, Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source.Key))));
        }
    }

    private async Task<(int ExitCode, string Output, string Error)> RunReferencedSolutionBuildAsync()
    {
        // Do not leave reusable solution-build nodes holding redirected output pipes open.
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ScaffoldCliHelper.GetDotNetPath(),
                Arguments = $"build -f {TargetFramework} --disable-build-servers -m:1",
                WorkingDirectory = _testDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.Environment.Remove("MSBUILD_EXE_PATH");
        process.StartInfo.Environment.Remove("MSBuildSDKsPath");
        process.StartInfo.Environment.Remove("MSBuildExtensionsPath");
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(output, error);
        await process.WaitForExitAsync();
        return (process.ExitCode, output.Result, error.Result);
    }

    protected Task<(int ExitCode, string Output, string Error)> RunBuildAsync(string workingDirectory)
        => ScaffoldCliHelper.RunBuildForFrameworkAsync(workingDirectory, TargetFramework);

    protected class TestTelemetryService : ITelemetryService
    {
        public List<(string EventName, IReadOnlyDictionary<string, string> Properties, IReadOnlyDictionary<string, double> Measures)> TrackedEvents { get; } = new();
        public void TrackEvent(string eventName, IReadOnlyDictionary<string, string>? properties = null, IReadOnlyDictionary<string, double>? measures = null)
        {
            TrackedEvents.Add((eventName, properties ?? new Dictionary<string, string>(), measures ?? new Dictionary<string, double>()));
        }

        public void Flush() { }
    }

    #endregion
}
