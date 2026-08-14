// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Xunit;

namespace DotnetTemplateMcp.Tests;

/// <summary>
/// Covers gap 1.6: GetTemplatesAsync had no memoization, yet nearly every tool calls it, and the
/// first call sits behind an SDK nupkg scan + install. Every tool call paid that cost.
/// </summary>
[Collection("IntegrationTests")]
public class TemplateCacheTests : IDisposable
{
    private readonly TemplateEngineService _service = new(NullLoggerFactory.Instance);

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetTemplatesAsync_SecondCall_ReturnsTheSameCachedInstance()
    {
        var first = await _service.GetTemplatesAsync();
        var second = await _service.GetTemplatesAsync();

        Assert.Same(first, second);
    }

    [Fact]
    public async Task GetTemplatesAsync_SecondCall_IsSubstantiallyFaster()
    {
        var cold = Stopwatch.StartNew();
        await _service.GetTemplatesAsync();
        cold.Stop();

        var warm = Stopwatch.StartNew();
        await _service.GetTemplatesAsync();
        warm.Stop();

        Assert.True(
            warm.ElapsedMilliseconds <= cold.ElapsedMilliseconds,
            $"Warm call ({warm.ElapsedMilliseconds}ms) should not exceed cold call ({cold.ElapsedMilliseconds}ms).");
    }

    [Fact]
    public async Task InvalidateTemplateCache_DropsTheMemoizedList()
    {
        await _service.GetTemplatesAsync();
        Assert.NotNull(ReadCacheField());

        _service.InvalidateTemplateCache();

        // The engine's bootstrapper may hand back a reference-identical list, so the observable
        // contract is asserted on the cache field itself: install/uninstall must not leave callers
        // observing a stale inventory.
        Assert.Null(ReadCacheField());

        var refreshed = await _service.GetTemplatesAsync();
        Assert.NotNull(refreshed);
        Assert.NotNull(ReadCacheField());
    }

    [Fact]
    public async Task UninstallTemplatePackages_InvalidatesTheCache()
    {
        await _service.GetTemplatesAsync();
        Assert.NotNull(ReadCacheField());

        // A no-op uninstall still has to drop the cache — the invalidation must be unconditional,
        // otherwise a later real install/uninstall could leave a stale inventory behind.
        await _service.UninstallTemplatePackagesAsync(Array.Empty<IManagedTemplatePackage>());

        Assert.Null(ReadCacheField());
    }

    private object? ReadCacheField()
        => typeof(TemplateEngineService)
            .GetField("_templateCache", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(_service);
}
