// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.TemplateEngine.MCP.Intent;
using ModelContextProtocol.Server;

namespace Microsoft.TemplateEngine.MCP.Tools;

[McpServerToolType]
internal sealed class TemplateFromIntentTool
{
    [McpServerTool(Name = "template_from_intent")]
    [Description("Skip the CLI flags — describe what you want in plain English and get the right template with pre-filled parameters. No LLM required — uses keyword matching. Example: 'web API with authentication and controllers' → webapi + auth=Individual + UseControllers=true.")]
    public static async Task<string> ResolveIntentAsync(
        TemplateEngineService engineService,
        McpFeatureFlags featureFlags,
        [Description("Natural language description of the project you want to create (e.g., 'web API with authentication and controllers', 'console app with .NET 9', 'MAUI cross-platform app')")] string intent,
        [Description("Maximum number of matches to return (default: 5)")] int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = McpTelemetry.StartToolActivity("template_from_intent");
        var sw = Stopwatch.StartNew();
        try
        {
            if (!featureFlags.IntentResolutionEnabled)
            {
                return JsonSerializer.Serialize(new
                {
                    error = "Intent resolution is disabled. Set environment variable MCP_TEMPLATE_INTENT_RESOLUTION=true to enable.",
                    hint = "You can still use template_search, template_inspect, and template_instantiate directly.",
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            var resolver = new ClassificationBasedIntentResolver(engineService);
            var resolution = await resolver.ResolveAsync(intent, cancellationToken).ConfigureAwait(false);

            McpTelemetry.IntentResolutions.Add(1);
            activity?.SetTag("mcp.intent.keywords_count", resolution.ExtractedKeywords.Count);
            activity?.SetTag("mcp.intent.matches_count", resolution.Matches.Count);

            var limit = Math.Min(maxResults ?? 5, resolution.Matches.Count);
            var matches = resolution.Matches.Take(limit).Select(m => new
            {
                m.Template.Identity,
                ShortNames = m.Template.ShortNameList,
                m.Template.Name,
                m.Template.Description,
                Language = m.Template.TagsCollection.GetValueOrDefault("language"),
                Type = m.Template.TagsCollection.GetValueOrDefault("type"),
                Confidence = Math.Round(m.Confidence, 3),
                ResolvedParameters = m.ResolvedParameters.Count > 0 ? m.ResolvedParameters : null,
                UnresolvedParameters = m.UnresolvedParameters.Count > 0 ? m.UnresolvedParameters : null,
                MatchReasons = m.MatchReasons,
            }).ToList();

            var response = new
            {
                OriginalIntent = resolution.OriginalIntent,
                ExtractedKeywords = resolution.ExtractedKeywords,
                MatchCount = matches.Count,
                Matches = matches,
                Suggestion = matches.Count > 0
                    ? $"Best match: {matches[0].Name} ({string.Join("/", matches[0].ShortNames)}). Use template_instantiate with templateName=\"{matches[0].ShortNames.FirstOrDefault() ?? matches[0].Identity}\" and the resolved parameters."
                    : "No matching templates found. Try template_search with a broader query.",
            };

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            McpTelemetry.RecordError(activity, "template_from_intent", ex.Message);
            return JsonSerializer.Serialize(new { error = ex.Message }, new JsonSerializerOptions { WriteIndented = true });
        }
        finally
        {
            McpTelemetry.RecordDuration("template_from_intent", sw.Elapsed.TotalMilliseconds);
        }
    }
}
