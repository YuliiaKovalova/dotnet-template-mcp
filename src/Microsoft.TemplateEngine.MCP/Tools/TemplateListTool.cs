// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
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
        [Description("Optional language filter (e.g., 'C#', 'F#', 'VB')")] string? language = null,
        [Description("Optional type filter (e.g., 'project', 'item')")] string? type = null,
        [Description("Optional classification filter (e.g., 'Web', 'Console', 'Library')")] string? classification = null,
        CancellationToken cancellationToken = default)
    {
        var templates = await engineService.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);

        IEnumerable<Abstractions.ITemplateInfo> filtered = templates;

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

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }
}
