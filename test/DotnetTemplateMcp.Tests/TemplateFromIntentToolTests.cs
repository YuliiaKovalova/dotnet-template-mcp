// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using DotnetTemplateMcp.Tools;
using Xunit;
using Xunit.Abstractions;

namespace DotnetTemplateMcp.Tests;

/// <summary>
/// Integration tests for the template_from_intent MCP tool.
/// </summary>
[Collection("IntegrationTests")]
public class TemplateFromIntentToolTests : IDisposable
{
    private readonly TemplateEngineService _service;
    private readonly ITestOutputHelper _output;

    public TemplateFromIntentToolTests(ITestOutputHelper output)
    {
        _output = output;
        var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        _service = new TemplateEngineService(loggerFactory);
    }

    public void Dispose()
    {
        _service.Dispose();
    }

    [Fact]
    public async Task ResolveIntent_WebApi_ReturnsMatches()
    {
        var flags = new McpFeatureFlags { IntentResolutionEnabled = true };
        var result = await TemplateFromIntentTool.ResolveIntentAsync(
            _service, flags, "web api");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.True(parsed.TryGetProperty("Matches", out var matches));
        Assert.True(matches.GetArrayLength() > 0);

        var topMatch = matches[0];
        _output.WriteLine($"Top: {topMatch.GetProperty("Name").GetString()} " +
            $"(confidence: {topMatch.GetProperty("Confidence").GetDouble():F3})");

        // Should have extracted keywords
        Assert.True(parsed.GetProperty("ExtractedKeywords").GetArrayLength() > 0);
    }

    [Fact]
    public async Task ResolveIntent_WithParameters_IncludesResolvedParams()
    {
        var flags = new McpFeatureFlags { IntentResolutionEnabled = true };
        var result = await TemplateFromIntentTool.ResolveIntentAsync(
            _service, flags, "web API with authentication and controllers");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        var matches = parsed.GetProperty("Matches");
        Assert.True(matches.GetArrayLength() > 0);

        var topMatch = matches[0];
        _output.WriteLine($"Top: {topMatch.GetProperty("Name").GetString()}");

        if (topMatch.TryGetProperty("ResolvedParameters", out var resolvedParams) &&
            resolvedParams.ValueKind != JsonValueKind.Null)
        {
            foreach (var prop in resolvedParams.EnumerateObject())
            {
                _output.WriteLine($"  {prop.Name} = {prop.Value.GetString()}");
            }
        }
    }

    [Fact]
    public async Task ResolveIntent_IncludesSuggestion()
    {
        var flags = new McpFeatureFlags { IntentResolutionEnabled = true };
        var result = await TemplateFromIntentTool.ResolveIntentAsync(
            _service, flags, "console app");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.True(parsed.TryGetProperty("Suggestion", out var suggestion));
        var suggestionText = suggestion.GetString();
        Assert.NotNull(suggestionText);
        Assert.Contains("template_instantiate", suggestionText!);
        _output.WriteLine($"Suggestion: {suggestionText}");
    }

    [Fact]
    public async Task ResolveIntent_Disabled_ReturnsError()
    {
        var flags = new McpFeatureFlags { IntentResolutionEnabled = false };
        var result = await TemplateFromIntentTool.ResolveIntentAsync(
            _service, flags, "web api");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.True(parsed.TryGetProperty("error", out var error));
        Assert.Contains("disabled", error.GetString()!);
        Assert.True(parsed.TryGetProperty("hint", out _));
    }

    [Fact]
    public async Task ResolveIntent_MaxResults_LimitsOutput()
    {
        var flags = new McpFeatureFlags { IntentResolutionEnabled = true };
        var result = await TemplateFromIntentTool.ResolveIntentAsync(
            _service, flags, "web", maxResults: 2);

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        var matches = parsed.GetProperty("Matches");
        Assert.True(matches.GetArrayLength() <= 2);
    }

    [Fact]
    public async Task ResolveIntent_MatchesIncludeReasons()
    {
        var flags = new McpFeatureFlags { IntentResolutionEnabled = true };
        var result = await TemplateFromIntentTool.ResolveIntentAsync(
            _service, flags, "console");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        var matches = parsed.GetProperty("Matches");
        Assert.True(matches.GetArrayLength() > 0);

        var topMatch = matches[0];
        Assert.True(topMatch.TryGetProperty("MatchReasons", out var reasons));
        Assert.True(reasons.GetArrayLength() > 0);

        foreach (var reason in reasons.EnumerateArray())
        {
            _output.WriteLine($"  Reason: {reason.GetString()}");
        }
    }
}
