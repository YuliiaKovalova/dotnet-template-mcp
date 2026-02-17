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
    [Description("Search for templates by name, tags, language, or type. Searches both locally installed templates and NuGet.org, returning a unified ranked list with local templates first.")]
    public static async Task<string> SearchTemplatesAsync(
        TemplateEngineService engineService,
        [Description("Search query string to match against template names, short names, tags, and descriptions")] string query,
        [Description("Optional language filter (e.g., 'C#', 'F#', 'VB')")] string? language = null,
        [Description("Optional type filter (e.g., 'project', 'item', 'solution')")] string? type = null,
        CancellationToken cancellationToken = default)
    {
        var resultList = new List<object>();

        // 1. Search locally installed templates (appear first — ready to use)
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

        var localIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in filtered)
        {
            localIdentities.Add(t.Identity);
            resultList.Add(new
            {
                t.Identity,
                ShortNames = t.ShortNameList,
                t.Name,
                t.Description,
                t.Author,
                t.Classifications,
                Language = t.TagsCollection.GetValueOrDefault("language"),
                Type = t.TagsCollection.GetValueOrDefault("type"),
                Source = "local",
            });
        }

        // 2. Search NuGet.org (appear after local results)
        if (!string.IsNullOrWhiteSpace(query))
        {
            var nugetResults = await engineService.SearchNuGetTemplatesAsync(query, language, type, cancellationToken).ConfigureAwait(false);

            foreach (var (packageInfo, matchedTemplates) in nugetResults)
            {
                foreach (var t in matchedTemplates)
                {
                    // Skip if already found locally
                    if (localIdentities.Contains(t.Identity))
                    {
                        continue;
                    }

                    resultList.Add(new
                    {
                        t.Identity,
                        ShortNames = t.ShortNameList,
                        t.Name,
                        t.Description,
                        t.Author,
                        t.Classifications,
                        Language = t.TagsCollection.GetValueOrDefault("language"),
                        Type = t.TagsCollection.GetValueOrDefault("type"),
                        Source = "nuget",
                        PackageId = packageInfo.Name,
                        PackageVersion = packageInfo.Version,
                        TotalDownloads = packageInfo.TotalDownloads,
                    });
                }
            }
        }

        return JsonSerializer.Serialize(resultList, new JsonSerializerOptions { WriteIndented = true });
    }
}
