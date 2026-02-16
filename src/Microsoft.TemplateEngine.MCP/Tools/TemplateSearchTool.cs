// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Microsoft.TemplateEngine.MCP.Tools;

[McpServerToolType]
internal sealed class TemplateSearchTool
{
    [McpServerTool(Name = "template_search")]
    [Description("Search for templates by name, tags, language, or type. Returns matching templates ranked by relevance.")]
    public static async Task<string> SearchTemplatesAsync(
        TemplateEngineService engineService,
        [Description("Search query string to match against template names, short names, tags, and descriptions")] string query,
        [Description("Optional language filter (e.g., 'C#', 'F#', 'VB')")] string? language = null,
        [Description("Optional type filter (e.g., 'project', 'item', 'solution')")] string? type = null,
        CancellationToken cancellationToken = default)
    {
        var allTemplates = await engineService.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);

        IEnumerable<Abstractions.ITemplateInfo> filtered = allTemplates;

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(t =>
                t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.ShortNameList.Any(sn => sn.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                t.Classifications.Any(c => c.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (t.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
                t.Identity.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(language))
        {
            filtered = filtered.Where(t =>
                t.TagsCollection.TryGetValue("language", out string? lang) &&
                lang.Contains(language, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            filtered = filtered.Where(t =>
                t.TagsCollection.TryGetValue("type", out string? templateType) &&
                templateType.Contains(type, StringComparison.OrdinalIgnoreCase));
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

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }
}
