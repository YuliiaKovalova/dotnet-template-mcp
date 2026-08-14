// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using DotnetTemplateMcp.PostCreation;
using Xunit;

namespace DotnetTemplateMcp.Tests;

public class PostCreationProcessorTests : IDisposable
{
    private readonly PostCreationProcessor _processor;
    private readonly string _tempDir;

    public PostCreationProcessorTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        _processor = new PostCreationProcessor(loggerFactory);
        _tempDir = Path.Combine(Path.GetTempPath(), $"mcp-postcreation-{Guid.NewGuid():N}");
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

    // ── CPM Detection ──

    [Theory]
    [InlineData("4.2.0", "3.1.0", true)]   // newer
    [InlineData("3.1.0", "3.1.0", false)]  // equal
    [InlineData("3.0.0", "4.2.0", false)]  // older — must NOT be treated as an upgrade (no downgrade)
    [InlineData("1.0", "1.0.0", false)]    // semantically equal despite different string form
    public void IsNewerVersion_ComparesSemantically(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, PostCreationProcessor.IsNewerVersion(candidate, current));
    }

    [Fact]
    public void FindDirectoryPackagesProps_Found_ReturnsPath()
    {
        var propsPath = Path.Combine(_tempDir, "Directory.Packages.props");
        File.WriteAllText(propsPath, "<Project></Project>");

        var subDir = Path.Combine(_tempDir, "src", "MyProject");
        Directory.CreateDirectory(subDir);

        var found = PostCreationProcessor.FindDirectoryPackagesProps(subDir);

        Assert.Equal(propsPath, found);
    }

    [Fact]
    public void FindDirectoryPackagesProps_NotFound_ReturnsNull()
    {
        var subDir = Path.Combine(_tempDir, "isolated");
        Directory.CreateDirectory(subDir);

        var found = PostCreationProcessor.FindDirectoryPackagesProps(subDir);

        Assert.Null(found);
    }

    // ── CPM: Strip versions from .csproj + add to Directory.Packages.props ──

    [Fact]
    public async Task Process_CpmDetected_StripsVersionsAndUpdatesProps()
    {
        // Set up Directory.Packages.props
        var propsContent = """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="ExistingPackage" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(Path.Combine(_tempDir, "Directory.Packages.props"), propsContent);

        // Set up a generated .csproj with hardcoded versions
        var projectDir = Path.Combine(_tempDir, "src", "MyApp");
        Directory.CreateDirectory(projectDir);
        var csprojContent = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Serilog" Version="3.1.0" />
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                <PackageReference Include="ExistingPackage" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(Path.Combine(projectDir, "MyApp.csproj"), csprojContent);

        var result = await _processor.ProcessAsync(projectDir, resolveLatestVersions: false);

        Assert.True(result.CpmDetected);
        Assert.Single(result.ProcessedFiles);

        var fileResult = result.ProcessedFiles[0];

        // Versions should be stripped from .csproj
        Assert.Equal(3, fileResult.VersionsStripped.Count);
        Assert.Contains("Serilog", fileResult.VersionsStripped);
        Assert.Contains("Newtonsoft.Json", fileResult.VersionsStripped);

        // New packages should be added to Directory.Packages.props
        Assert.Equal(2, fileResult.AddedToDirectoryPackagesProps.Count);
        Assert.Contains(fileResult.AddedToDirectoryPackagesProps, e => e.PackageName == "Serilog");
        Assert.Contains(fileResult.AddedToDirectoryPackagesProps, e => e.PackageName == "Newtonsoft.Json");
        // ExistingPackage already existed — should NOT be added again
        Assert.DoesNotContain(fileResult.AddedToDirectoryPackagesProps, e => e.PackageName == "ExistingPackage");

        // Verify .csproj was actually modified
        var modifiedCsproj = XDocument.Load(Path.Combine(projectDir, "MyApp.csproj"));
        var refs = modifiedCsproj.Descendants("PackageReference").ToList();
        Assert.All(refs, r => Assert.Null(r.Attribute("Version")));

        // Verify Directory.Packages.props was actually modified
        var modifiedProps = XDocument.Load(Path.Combine(_tempDir, "Directory.Packages.props"));
        var pvEntries = modifiedProps.Descendants("PackageVersion").ToList();
        Assert.Equal(3, pvEntries.Count); // ExistingPackage + Serilog + Newtonsoft.Json
    }

    [Fact]
    public async Task Process_NoCpm_LeavesVersionsIntact()
    {
        // No Directory.Packages.props — not a CPM solution
        var projectDir = Path.Combine(_tempDir, "standalone");
        Directory.CreateDirectory(projectDir);
        var csprojContent = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Serilog" Version="3.1.0" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(Path.Combine(projectDir, "MyApp.csproj"), csprojContent);

        var result = await _processor.ProcessAsync(projectDir, resolveLatestVersions: false);

        Assert.False(result.CpmDetected);
        Assert.Single(result.ProcessedFiles);
        Assert.Empty(result.ProcessedFiles[0].VersionsStripped);
    }

    // ── NuGet Version Resolution ──

    [Fact]
    public void IsStableVersion_Stable_ReturnsTrue()
    {
        Assert.True(NuGetVersionResolver.IsStableVersion("13.0.3"));
        Assert.True(NuGetVersionResolver.IsStableVersion("1.0.0"));
        Assert.True(NuGetVersionResolver.IsStableVersion("9.0.0"));
    }

    [Fact]
    public void IsStableVersion_Prerelease_ReturnsFalse()
    {
        Assert.False(NuGetVersionResolver.IsStableVersion("0.2.0-preview.1"));
        Assert.False(NuGetVersionResolver.IsStableVersion("9.0.0-rc.1.24431.7"));
        Assert.False(NuGetVersionResolver.IsStableVersion("1.0.0-alpha"));
    }

    [Fact]
    public async Task GetLatestStableVersion_KnownPackage_ReturnsVersion()
    {
        // Newtonsoft.Json is a well-known package that should always exist on NuGet
        var version = await NuGetVersionResolver.GetLatestStableVersionAsync("Newtonsoft.Json");

        Assert.NotNull(version);
        Assert.DoesNotContain("-", version); // Should be stable
    }

    [Fact]
    public async Task GetLatestStableVersion_UnknownPackage_ReturnsNull()
    {
        var version = await NuGetVersionResolver.GetLatestStableVersionAsync(
            "ThisPackageDefinitelyDoesNotExist12345");

        Assert.Null(version);
    }

    // ── Combined: CPM + Latest Versions ──

    [Fact]
    public async Task Process_CpmWithLatestVersions_UsesLatestInProps()
    {
        var propsContent = """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(Path.Combine(_tempDir, "Directory.Packages.props"), propsContent);

        var projectDir = Path.Combine(_tempDir, "src", "LatestApp");
        Directory.CreateDirectory(projectDir);
        var csprojContent = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="12.0.0" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(Path.Combine(projectDir, "LatestApp.csproj"), csprojContent);

        var result = await _processor.ProcessAsync(projectDir, resolveLatestVersions: true);

        Assert.True(result.CpmDetected);
        var fileResult = result.ProcessedFiles[0];

        // Should detect that 12.0.0 → latest (>= 13.0.0)
        Assert.Single(fileResult.VersionUpgrades);
        Assert.Equal("Newtonsoft.Json", fileResult.VersionUpgrades[0].PackageName);
        Assert.Equal("12.0.0", fileResult.VersionUpgrades[0].OldVersion);
        Assert.DoesNotContain("-", fileResult.VersionUpgrades[0].NewVersion); // Stable

        // Version should be stripped from .csproj
        Assert.Contains("Newtonsoft.Json", fileResult.VersionsStripped);

        // Latest version should be in Directory.Packages.props
        Assert.Single(fileResult.AddedToDirectoryPackagesProps);
        Assert.Equal(fileResult.VersionUpgrades[0].NewVersion,
            fileResult.AddedToDirectoryPackagesProps[0].Version);
    }

    // ── No-CPM with Latest Versions ──

    [Fact]
    public async Task Process_NoCpmWithLatestVersions_UpdatesCsprojDirectly()
    {
        var projectDir = Path.Combine(_tempDir, "standalone-latest");
        Directory.CreateDirectory(projectDir);
        var csprojContent = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="12.0.0" />
              </ItemGroup>
            </Project>
            """;
        var csprojPath = Path.Combine(projectDir, "App.csproj");
        File.WriteAllText(csprojPath, csprojContent);

        var result = await _processor.ProcessAsync(projectDir, resolveLatestVersions: true);

        Assert.False(result.CpmDetected);
        var fileResult = result.ProcessedFiles[0];

        // Should have version upgrade
        Assert.Single(fileResult.VersionUpgrades);
        Assert.Equal("12.0.0", fileResult.VersionUpgrades[0].OldVersion);

        // Verify .csproj was updated with new version
        var modified = XDocument.Load(csprojPath);
        var versionAttr = modified.Descendants("PackageReference")
            .First(pr => pr.Attribute("Include")?.Value == "Newtonsoft.Json")
            .Attribute("Version");
        Assert.NotNull(versionAttr);
        Assert.NotEqual("12.0.0", versionAttr!.Value); // Should be updated
    }

    // ── Edge Cases ──

    [Fact]
    public async Task Process_NoCsprojFiles_ReturnsEmpty()
    {
        var projectDir = Path.Combine(_tempDir, "no-csproj");
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, "readme.md"), "# Hello");

        var result = await _processor.ProcessAsync(projectDir);

        Assert.Empty(result.ProcessedFiles);
        Assert.False(result.HasChanges);
    }

    [Fact]
    public async Task Process_CsprojWithNoPackageRefs_NoChanges()
    {
        var projectDir = Path.Combine(_tempDir, "no-refs");
        Directory.CreateDirectory(projectDir);
        var csprojContent = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """;
        File.WriteAllText(Path.Combine(projectDir, "App.csproj"), csprojContent);

        var result = await _processor.ProcessAsync(projectDir, resolveLatestVersions: false);

        Assert.Single(result.ProcessedFiles);
        Assert.False(result.HasChanges);
    }
}
