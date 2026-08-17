// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.Xml.Linq;
using Microsoft.TemplateEngine.MCP.PostCreation;
using Xunit;

namespace Microsoft.TemplateEngine.MCP.Tests;

public class PackageUpgradeServiceTests : IDisposable
{
    private readonly string _tempDir;

    public PackageUpgradeServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mcp-pkgupgrade-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    private static PackageUpgradeService ServiceWith(IDictionary<string, string?> latest)
        => new((name, _, _) => Task.FromResult(latest.TryGetValue(name, out var v) ? v : null));

    [Fact]
    public async Task AnalyzeAsync_NonCpm_ReportsUpgradeWithoutWritingWhenNotApplied()
    {
        var csproj = Path.Combine(_tempDir, "App.csproj");
        var original = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="12.0.1" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(csproj, original);

        var service = ServiceWith(new Dictionary<string, string?> { ["Newtonsoft.Json"] = "13.0.3" });

        var report = await service.AnalyzeAsync(_tempDir, apply: false);

        Assert.False(report.CpmDetected);
        Assert.Equal(1, report.ProjectsScanned);
        var item = Assert.Single(report.Upgrades);
        Assert.Equal("Newtonsoft.Json", item.PackageName);
        Assert.Equal("12.0.1", item.CurrentVersion);
        Assert.Equal("13.0.3", item.LatestVersion);
        Assert.Equal("csproj", item.Location);
        // Report-only: file must be unchanged.
        Assert.Equal(original, File.ReadAllText(csproj));
    }

    [Fact]
    public async Task AnalyzeAsync_NonCpm_WritesNewVersionWhenApplied()
    {
        var csproj = Path.Combine(_tempDir, "App.csproj");
        File.WriteAllText(csproj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="12.0.1" />
              </ItemGroup>
            </Project>
            """);

        var service = ServiceWith(new Dictionary<string, string?> { ["Newtonsoft.Json"] = "13.0.3" });

        var report = await service.AnalyzeAsync(_tempDir, apply: true);

        Assert.True(report.Applied);
        Assert.Single(report.Upgrades);

        var doc = XDocument.Load(csproj);
        var version = doc.Descendants("PackageReference")
            .First(e => e.Attribute("Include")!.Value == "Newtonsoft.Json")
            .Attribute("Version")!.Value;
        Assert.Equal("13.0.3", version);
    }

    [Fact]
    public async Task AnalyzeAsync_Cpm_BumpsPackageVersionInPropsAndCountsScannedProjects()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Directory.Packages.props"), """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="Newtonsoft.Json" Version="12.0.1" />
                <PackageVersion Include="Serilog" Version="3.0.0" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_tempDir, "App.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" />
                <PackageReference Include="Serilog" />
              </ItemGroup>
            </Project>
            """);

        var service = ServiceWith(new Dictionary<string, string?>
        {
            ["Newtonsoft.Json"] = "13.0.3",
            ["Serilog"] = "3.0.0", // already up to date
        });

        var report = await service.AnalyzeAsync(_tempDir, apply: true);

        Assert.True(report.CpmDetected);
        Assert.Equal(1, report.ProjectsScanned);
        var item = Assert.Single(report.Upgrades);
        Assert.Equal("Newtonsoft.Json", item.PackageName);
        Assert.Equal("Directory.Packages.props", item.Location);
        Assert.Equal(1, report.UpToDateCount);

        var props = XDocument.Load(Path.Combine(_tempDir, "Directory.Packages.props"));
        var newtonsoft = props.Descendants("PackageVersion")
            .First(e => e.Attribute("Include")!.Value == "Newtonsoft.Json")
            .Attribute("Version")!.Value;
        Assert.Equal("13.0.3", newtonsoft);
    }

    [Fact]
    public async Task AnalyzeAsync_DoesNotDowngrade()
    {
        var csproj = Path.Combine(_tempDir, "App.csproj");
        var original = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(csproj, original);

        var service = ServiceWith(new Dictionary<string, string?> { ["Newtonsoft.Json"] = "12.0.1" });

        var report = await service.AnalyzeAsync(_tempDir, apply: true);

        Assert.Empty(report.Upgrades);
        Assert.Equal(1, report.UpToDateCount);
        Assert.Equal(original, File.ReadAllText(csproj));
    }

    [Fact]
    public async Task AnalyzeAsync_SkipsFloatingAndPropertyVersions()
    {
        var csproj = Path.Combine(_tempDir, "App.csproj");
        File.WriteAllText(csproj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Floating" Version="1.*" />
                <PackageReference Include="FromProperty" Version="$(SomeVersion)" />
              </ItemGroup>
            </Project>
            """);

        var service = ServiceWith(new Dictionary<string, string?>
        {
            ["Floating"] = "2.0.0",
            ["FromProperty"] = "2.0.0",
        });

        var report = await service.AnalyzeAsync(_tempDir, apply: true);

        Assert.Empty(report.Upgrades);
        Assert.Equal(0, report.UpToDateCount);
    }

    [Fact]
    public async Task AnalyzeAsync_UnresolvablePackageIsReported()
    {
        var csproj = Path.Combine(_tempDir, "App.csproj");
        File.WriteAllText(csproj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="DoesNotResolve" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        var service = ServiceWith(new Dictionary<string, string?> { ["DoesNotResolve"] = null });

        var report = await service.AnalyzeAsync(_tempDir, apply: false);

        Assert.Empty(report.Upgrades);
        Assert.Contains("DoesNotResolve", report.UnresolvedPackages);
    }

    [Fact]
    public async Task AnalyzeAsync_SingleCsprojPath_IsSupported()
    {
        var csproj = Path.Combine(_tempDir, "App.csproj");
        File.WriteAllText(csproj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="12.0.1" />
              </ItemGroup>
            </Project>
            """);

        var service = ServiceWith(new Dictionary<string, string?> { ["Newtonsoft.Json"] = "13.0.3" });

        var report = await service.AnalyzeAsync(csproj, apply: false);

        Assert.Equal(1, report.ProjectsScanned);
        Assert.Single(report.Upgrades);
    }

    [Fact]
    public async Task AnalyzeAsync_Cpm_IgnoresPackageVersionsNotReferencedByProjects()
    {
        // Props declares two packages, but the project only references one of them.
        File.WriteAllText(Path.Combine(_tempDir, "Directory.Packages.props"), """
            <Project>
              <ItemGroup>
                <PackageVersion Include="Newtonsoft.Json" Version="12.0.1" />
                <PackageVersion Include="UnusedPackage" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_tempDir, "App.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" />
              </ItemGroup>
            </Project>
            """);

        var service = ServiceWith(new Dictionary<string, string?>
        {
            ["Newtonsoft.Json"] = "13.0.3",
            ["UnusedPackage"] = "2.0.0",
        });

        var report = await service.AnalyzeAsync(_tempDir, apply: true);

        var item = Assert.Single(report.Upgrades);
        Assert.Equal("Newtonsoft.Json", item.PackageName);

        // The unreferenced PackageVersion must be left untouched.
        var props = XDocument.Load(Path.Combine(_tempDir, "Directory.Packages.props"));
        var unused = props.Descendants("PackageVersion")
            .First(e => e.Attribute("Include")!.Value == "UnusedPackage")
            .Attribute("Version")!.Value;
        Assert.Equal("1.0.0", unused);
    }

    [Fact]
    public async Task AnalyzeAsync_NoProjects_ReturnsEmptyReport()
    {
        var service = ServiceWith(new Dictionary<string, string?>());

        var report = await service.AnalyzeAsync(_tempDir, apply: false);

        Assert.Equal(0, report.ProjectsScanned);
        Assert.Empty(report.Upgrades);
    }
}
