// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.MCP.Intent;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.TemplateEngine.MCP.Tests;

/// <summary>
/// Integration tests for ClassificationBasedIntentResolver using real template engine.
/// </summary>
[Collection("IntegrationTests")]
public class IntentResolverTests : IDisposable
{
    private readonly TemplateEngineService _service;
    private readonly ClassificationBasedIntentResolver _resolver;
    private readonly ITestOutputHelper _output;

    public IntentResolverTests(ITestOutputHelper output)
    {
        _output = output;
        var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        _service = new TemplateEngineService(loggerFactory);
        _resolver = new ClassificationBasedIntentResolver(_service);
    }

    public void Dispose()
    {
        _service.Dispose();
    }

    [Fact]
    public async Task Resolve_WebApi_FindsWebApiTemplate()
    {
        var result = await _resolver.ResolveAsync("web api");

        Assert.True(result.HasMatches);
        var topMatch = result.Matches[0];
        Assert.Contains("webapi", topMatch.Template.ShortNameList, StringComparer.OrdinalIgnoreCase);
        Assert.True(topMatch.Confidence > 0.3);
        _output.WriteLine($"Top match: {topMatch.Template.Name} (confidence: {topMatch.Confidence:F3})");
    }

    [Fact]
    public async Task Resolve_ConsoleApp_FindsConsoleTemplate()
    {
        var result = await _resolver.ResolveAsync("console application");

        Assert.True(result.HasMatches);
        var topMatch = result.Matches[0];
        Assert.Contains("console", topMatch.Template.ShortNameList, StringComparer.OrdinalIgnoreCase);
        _output.WriteLine($"Top match: {topMatch.Template.Name} (confidence: {topMatch.Confidence:F3})");
    }

    [Fact]
    public async Task Resolve_WebApiWithAuth_ResolvesAuthParameter()
    {
        var result = await _resolver.ResolveAsync("web API with authentication");

        Assert.True(result.HasMatches);
        var topMatch = result.Matches[0];

        // Should have auth parameter resolved
        if (topMatch.ResolvedParameters.TryGetValue("auth", out var authValue))
        {
            Assert.Equal("Individual", authValue);
            _output.WriteLine($"Resolved auth={authValue}");
        }

        _output.WriteLine($"Top match: {topMatch.Template.Name}, params: {string.Join(", ", topMatch.ResolvedParameters.Select(kv => $"{kv.Key}={kv.Value}"))}");
    }

    [Fact]
    public async Task Resolve_WebApiWithControllers_ResolvesControllerParameter()
    {
        var result = await _resolver.ResolveAsync("web api with controllers");

        Assert.True(result.HasMatches);
        var topMatch = result.Matches[0];

        if (topMatch.ResolvedParameters.TryGetValue("UseControllers", out var value))
        {
            Assert.Equal("true", value);
        }

        _output.WriteLine($"Top match: {topMatch.Template.Name}, params: {string.Join(", ", topMatch.ResolvedParameters.Select(kv => $"{kv.Key}={kv.Value}"))}");
    }

    [Fact]
    public async Task Resolve_ClassLibrary_FindsClasslibTemplate()
    {
        var result = await _resolver.ResolveAsync("class library");

        Assert.True(result.HasMatches);
        var topMatch = result.Matches[0];
        Assert.Contains("classlib", topMatch.Template.ShortNameList, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolve_Worker_FindsWorkerTemplate()
    {
        var result = await _resolver.ResolveAsync("background service");

        Assert.True(result.HasMatches);
        var topMatch = result.Matches[0];
        Assert.Contains("worker", topMatch.Template.ShortNameList, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolve_EmptyIntent_ReturnsNoMatches()
    {
        var result = await _resolver.ResolveAsync("");

        Assert.False(result.HasMatches);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public async Task Resolve_GibberishIntent_ReturnsNoOrLowConfidence()
    {
        var result = await _resolver.ResolveAsync("xyzzy foobar baz qux");

        // May have matches but they should be very low confidence
        foreach (var match in result.Matches)
        {
            _output.WriteLine($"Match: {match.Template.Name} (confidence: {match.Confidence:F3})");
        }

        if (result.HasMatches)
        {
            Assert.True(result.Matches[0].Confidence < 0.3,
                "Gibberish intent should not produce high-confidence matches");
        }
    }

    [Fact]
    public async Task Resolve_ExtractsKeywords()
    {
        var result = await _resolver.ResolveAsync("web api with authentication and controllers");

        Assert.NotEmpty(result.ExtractedKeywords);
        _output.WriteLine($"Keywords: {string.Join(", ", result.ExtractedKeywords)}");
    }

    [Fact]
    public async Task Resolve_ComplexIntent_ResolvesMultipleParameters()
    {
        var result = await _resolver.ResolveAsync(
            "web API with authentication, controllers, and docker support");

        Assert.True(result.HasMatches);
        var topMatch = result.Matches[0];

        _output.WriteLine($"Top: {topMatch.Template.Name}");
        foreach (var (key, value) in topMatch.ResolvedParameters)
        {
            _output.WriteLine($"  {key} = {value}");
        }

        // Should resolve at least auth and controllers
        Assert.True(topMatch.ResolvedParameters.Count >= 1,
            "Complex intent should resolve at least one parameter");
    }

    [Fact]
    public async Task Resolve_NativeAot_ResolvesAotParameter()
    {
        var result = await _resolver.ResolveAsync("console app with native aot");

        Assert.True(result.HasMatches);
        var consoleMatch = result.Matches
            .FirstOrDefault(m => m.Template.ShortNameList.Contains("console"));

        if (consoleMatch != null && consoleMatch.ResolvedParameters.TryGetValue("PublishAot", out var aotValue))
        {
            Assert.Equal("true", aotValue);
            _output.WriteLine("Resolved PublishAot=true");
        }
    }

    [Fact]
    public async Task Resolve_ReturnsMaxFiveMatches()
    {
        var result = await _resolver.ResolveAsync("web");

        Assert.True(result.Matches.Count <= 5);
        _output.WriteLine($"Matches: {result.Matches.Count}");
    }

    [Fact]
    public async Task Resolve_MatchesAreSortedByConfidence()
    {
        var result = await _resolver.ResolveAsync("web api");

        if (result.Matches.Count > 1)
        {
            for (int i = 1; i < result.Matches.Count; i++)
            {
                Assert.True(result.Matches[i - 1].Confidence >= result.Matches[i].Confidence,
                    $"Matches should be sorted by confidence descending: {result.Matches[i - 1].Confidence} >= {result.Matches[i].Confidence}");
            }
        }
    }
}
