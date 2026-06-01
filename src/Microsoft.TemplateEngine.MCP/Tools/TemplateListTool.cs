// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Microsoft.TemplateEngine.MCP.Tools;

[McpServerToolType]
internal sealed class TemplateListTool
{
    [McpServerTool(Name = "template_list")]
    [Description("List all installed templates with optional filtering by language, type, or classification.")]
    public static async Task<string> ListTemplatesAsync(
        TemplateEngineService engineService,
        McpFeatureFlags featureFlags,
        [Description("Optional language filter (e.g., 'C#', 'F#', 'VB')")] string? language = null,
        [Description("Optional type filter (e.g., 'project', 'item')")] string? type = null,
        [Description("Optional classification filter (e.g., 'Web', 'Console', 'Library')")] string? classification = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = McpTelemetry.StartToolActivity("template_list");
        var sw = Stopwatch.StartNew();
        try
        {
            if (!featureFlags.IsToolEnabled("template_list"))
            {
                return ToolProfileResponse.DisabledMessage("template_list", "Use template_search instead, which covers both local and NuGet templates.");
            }

        var templates = await engineService.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);

        IEnumerable<Abstractions.ITemplateInfo> filtered = templates;

        if (!string.IsNullOrWhiteSpace(language))
        {
            filtered = filtered.Where(t =>
                t.TagsCollection.TryGetValue("language", out string? lang) &&
                lang is not null &&
                lang.Contains(language, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            filtered = filtered.Where(t =>
                t.TagsCollection.TryGetValue("type", out string? templateType) &&
                templateType is not null &&
                templateType.Contains(type, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(classification))
        {
            filtered = filtered.Where(t =>
                t.Classifications.Any(c => c.Contains(classification, StringComparison.OrdinalIgnoreCase)));
        }

        var result = filtered.Select(t => new
        {
            t.Identity,
            ShortNames = t.ShortNameList,
            t.Name,
            t.Description,
            t.Author,
            t.Classifications,
            Language = t.TagsCollection.GetValueOrDefault("language"),
            Type = t.TagsCollection.GetValueOrDefault("type"),
        }).ToList();

        activity?.SetTag("mcp.result.count", result.Count);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        finally
        {
            McpTelemetry.RecordDuration("template_list", sw.Elapsed.TotalMilliseconds);
        }
    }
}
