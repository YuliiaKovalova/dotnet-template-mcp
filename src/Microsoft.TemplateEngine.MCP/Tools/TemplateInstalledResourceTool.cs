// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Microsoft.TemplateEngine.MCP.Tools;

[McpServerToolType]
internal sealed class TemplateInstalledResourceTool
{
    /// <summary>
    /// Exposes installed templates as a tool for broad client compatibility.
    /// Functions as the templates://installed resource.
    /// </summary>
    [McpServerTool(Name = "templates_installed")]
    [Description("Get a structured listing of all installed templates as a resource. Returns identity, short names, name, description, author, classifications, language, and type for each template.")]
    public static async Task<string> GetInstalledTemplatesResourceAsync(
        TemplateEngineService engineService,
        McpFeatureFlags featureFlags,
        CancellationToken cancellationToken = default)
    {
        using var activity = McpTelemetry.StartToolActivity("templates_installed");
        var sw = Stopwatch.StartNew();
        try
        {
            if (!featureFlags.IsToolEnabled("templates_installed"))
            {
                return ToolProfileResponse.DisabledMessage("templates_installed", "Use template_search to find templates by name.");
            }

        var templates = await engineService.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);

        var result = templates.Select(t => new
        {
            t.Identity,
            ShortNames = t.ShortNameList,
            t.Name,
            t.Description,
            t.Author,
            t.Classifications,
            Language = t.TagsCollection.GetValueOrDefault("language"),
            Type = t.TagsCollection.GetValueOrDefault("type"),
            ParameterCount = t.ParameterDefinitions.Count(p => p.Type == "parameter" && !p.IsName),
            ConstraintCount = t.Constraints.Count,
            PostActionCount = t.PostActions.Count,
        }).ToList();

        activity?.SetTag("mcp.result.count", result.Count);
        return JsonSerializer.Serialize(
            new { totalCount = result.Count, templates = result },
            new JsonSerializerOptions { WriteIndented = true });
        }
        finally
        {
            McpTelemetry.RecordDuration("templates_installed", sw.Elapsed.TotalMilliseconds);
        }
    }
}
