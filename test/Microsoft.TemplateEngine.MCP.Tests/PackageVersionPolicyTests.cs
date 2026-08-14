// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.TemplateEngine.MCP.PostCreation;
using Xunit;

namespace Microsoft.TemplateEngine.MCP.Tests;

/// <summary>
/// Covers gap 1.3: resolveLatestVersions defaulted to true, so every created project had its
/// PackageReference versions silently rewritten to "latest stable" — producing untested
/// combinations and overriding the template author's deliberate pinning.
/// The default is now Report: findings are surfaced, nothing is written.
/// </summary>
public class PackageVersionPolicyTests : IDisposable
{
    private readonly string _tempDir;

    public PackageVersionPolicyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mcp-policy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Isolate from the machine's NuGet configuration so these tests never depend on a reachable
        // feed. Without this, Report policy performs real lookups whose NuGet-client defaults
        // (~100s per request, with retries) make the suite slow and flaky on restricted networks.
        File.WriteAllText(
            Path.Combine(_tempDir, "NuGet.config"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
              </packageSources>
            </configuration>
            """);
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

        GC.SuppressFinalize(this);
    }

    private const string CsprojWithPinnedPackage = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Newtonsoft.Json" Version="12.0.1" />
          </ItemGroup>
        </Project>
        """;

    private string WriteCsproj()
    {
        var path = Path.Combine(_tempDir, "App.csproj");
        File.WriteAllText(path, CsprojWithPinnedPackage);
        return path;
    }

    private static PostCreationProcessor CreateProcessor()
        => new(NullLoggerFactory.Instance);

    [Fact]
    public void DefaultPolicy_IsReport_NotApply()
    {
        // The regression this guards: creation-time auto-upgrade must not be the default.
        Assert.Equal(PackageVersionPolicy.Report, PostCreationProcessor.DefaultVersionPolicy);
    }

    [Fact]
    public void FeatureFlags_ResolveLatestVersionsByDefault_IsFalse()
    {
        Assert.False(new McpFeatureFlags().ResolveLatestVersionsByDefault);
    }

    [Fact]
    public async Task Process_SkipPolicy_LeavesCsprojByteForByteIdentical()
    {
        var path = WriteCsproj();
        var before = File.ReadAllText(path);

        var result = await CreateProcessor().ProcessAsync(_tempDir, PackageVersionPolicy.Skip);

        Assert.Equal(before, File.ReadAllText(path));
        Assert.False(result.HasChanges);
        Assert.Equal(PackageVersionPolicy.Skip, result.VersionPolicy);
    }

    [Fact]
    public async Task Process_ReportPolicy_NeverWritesToDisk()
    {
        var path = WriteCsproj();
        var before = File.ReadAllText(path);

        var result = await CreateProcessor().ProcessAsync(_tempDir, PackageVersionPolicy.Report);

        // Report mode may query feeds, but must not mutate the project.
        Assert.Equal(before, File.ReadAllText(path));
        Assert.False(result.VersionUpgradesApplied);
        Assert.Equal(PackageVersionPolicy.Report, result.VersionPolicy);
    }

    [Fact]
    public async Task Process_BoolOverload_MapsFalseToSkipForBackCompat()
    {
        WriteCsproj();

        var result = await CreateProcessor().ProcessAsync(_tempDir, resolveLatestVersions: false);

        Assert.Equal(PackageVersionPolicy.Skip, result.VersionPolicy);
        Assert.False(result.HasChanges);
    }

    [Fact]
    public async Task Process_BoolOverload_MapsTrueToApply()
    {
        WriteCsproj();

        var result = await CreateProcessor().ProcessAsync(_tempDir, resolveLatestVersions: true);

        Assert.Equal(PackageVersionPolicy.Apply, result.VersionPolicy);
    }

    // --- Tool-argument mapping -------------------------------------------------------------------
    // Regression: the tool mapped `false` to Report (which still queries feeds) while the processor
    // mapped it to Skip. Skip was therefore unreachable from any tool, leaving no offline path.

    [Fact]
    public void ResolvePolicy_True_Applies()
        => Assert.Equal(PackageVersionPolicy.Apply, PostCreationProcessor.ResolvePolicy(true, new McpFeatureFlags()));

    [Fact]
    public void ResolvePolicy_False_SkipsWithoutContactingAnyFeed()
        => Assert.Equal(PackageVersionPolicy.Skip, PostCreationProcessor.ResolvePolicy(false, new McpFeatureFlags()));

    [Fact]
    public void ResolvePolicy_Omitted_Reports()
        => Assert.Equal(PackageVersionPolicy.Report, PostCreationProcessor.ResolvePolicy(null, new McpFeatureFlags()));

    [Fact]
    public void ResolvePolicy_Omitted_OfflineMode_Skips()
        => Assert.Equal(
            PackageVersionPolicy.Skip,
            PostCreationProcessor.ResolvePolicy(null, new McpFeatureFlags { OfflineMode = true }));

    [Fact]
    public void ResolvePolicy_Omitted_LegacyEscapeHatch_Applies()
        => Assert.Equal(
            PackageVersionPolicy.Apply,
            PostCreationProcessor.ResolvePolicy(null, new McpFeatureFlags { ResolveLatestVersionsByDefault = true }));

    [Fact]
    public void ResolvePolicy_ExplicitFalse_BeatsLegacyEscapeHatch()
        => Assert.Equal(
            PackageVersionPolicy.Skip,
            PostCreationProcessor.ResolvePolicy(false, new McpFeatureFlags { ResolveLatestVersionsByDefault = true }));
}
