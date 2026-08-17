// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using Microsoft.TemplateEngine.MCP.PostCreation;
using Xunit;

namespace Microsoft.TemplateEngine.MCP.Tests;

/// <summary>
/// Covers gap 1.2: NuGetVersionResolver previously hardcoded
/// https://api.nuget.org/v3-flatcontainer/, so it ignored NuGet.config entirely — no private feeds,
/// no credentials, no packageSourceMapping, no proxy. Combined with the old
/// resolveLatestVersions=true default, that silently wrote public nuget.org versions into repos
/// whose policy is an internal feed.
///
/// These tests assert configuration resolution only; they never require a reachable feed.
/// </summary>
public class NuGetSourceResolverTests : IDisposable
{
    private readonly string _tempDir;

    public NuGetSourceResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mcp-nugetcfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        NuGetSourceResolver.ClearCache();
    }

    public void Dispose()
    {
        NuGetSourceResolver.ClearCache();
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

    private string WriteNuGetConfig(string contents)
    {
        var path = Path.Combine(_tempDir, "NuGet.config");
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public void DescribeSources_ReadsFeedsFromNearestNuGetConfig()
    {
        WriteNuGetConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="contoso-internal" value="https://nuget.contoso.example/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var description = NuGetSourceResolver.DescribeSources(_tempDir, logger: null);

        Assert.Contains("contoso-internal", description.EnabledSources);

        // The whole point of the fix: nuget.org is no longer assumed when the repo cleared it.
        Assert.DoesNotContain("nuget.org", description.EnabledSources);
    }

    [Fact]
    public void DescribeSources_ExcludesDisabledSources()
    {
        WriteNuGetConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="enabled-feed" value="https://enabled.example/v3/index.json" />
                <add key="disabled-feed" value="https://disabled.example/v3/index.json" />
              </packageSources>
              <disabledPackageSources>
                <add key="disabled-feed" value="true" />
              </disabledPackageSources>
            </configuration>
            """);

        var description = NuGetSourceResolver.DescribeSources(_tempDir, logger: null);

        Assert.Contains("enabled-feed", description.EnabledSources);
        Assert.DoesNotContain("disabled-feed", description.EnabledSources);
    }

    [Fact]
    public void DescribeSources_DetectsPackageSourceMapping()
    {
        WriteNuGetConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="contoso-internal" value="https://nuget.contoso.example/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="contoso-internal">
                  <package pattern="Contoso.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);

        var description = NuGetSourceResolver.DescribeSources(_tempDir, logger: null);

        Assert.True(description.PackageSourceMappingEnabled);
    }

    [Fact]
    public void DescribeSources_ReportsTheConfigFilesInEffect()
    {
        var configPath = WriteNuGetConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="only" value="https://only.example/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var description = NuGetSourceResolver.DescribeSources(_tempDir, logger: null);

        Assert.Contains(
            description.ConfigFiles,
            f => string.Equals(Path.GetFullPath(f), Path.GetFullPath(configPath), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DescribeSources_DifferentRoots_ResolveDifferentFeeds()
    {
        WriteNuGetConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="feed-a" value="https://a.example/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var otherRoot = Path.Combine(_tempDir, "nested-repo");
        Directory.CreateDirectory(otherRoot);
        File.WriteAllText(Path.Combine(otherRoot, "NuGet.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="feed-b" value="https://b.example/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var a = NuGetSourceResolver.DescribeSources(_tempDir, logger: null);
        var b = NuGetSourceResolver.DescribeSources(otherRoot, logger: null);

        // Per-root resolution is why the version cache must be keyed by feed scope, not package id.
        Assert.Contains("feed-a", a.EnabledSources);
        Assert.DoesNotContain("feed-a", b.EnabledSources);
        Assert.Contains("feed-b", b.EnabledSources);
    }

    [Fact]
    public async Task GetLatestStableVersion_UnreachableFeed_ReturnsNullInsteadOfThrowing()
    {
        WriteNuGetConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="dead" value="https://feed.invalid.localhost.example/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var version = await NuGetSourceResolver.GetLatestStableVersionAsync(
            "Newtonsoft.Json", _tempDir, logger: null);

        Assert.Null(version);
    }

    [Fact]
    public async Task GetLatestStableVersion_NoEnabledSources_ReturnsNull()
    {
        WriteNuGetConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
              </packageSources>
            </configuration>
            """);

        var version = await NuGetSourceResolver.GetLatestStableVersionAsync(
            "Newtonsoft.Json", _tempDir, logger: null);

        Assert.Null(version);
    }
}
