// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Microsoft.TemplateEngine.MCP.Tools;

[McpServerToolType]
internal sealed class TemplateSearchTool
{
    [McpServerTool(Name = "template_search")]
    [Description("Find templates you don't know the name of. Searches both locally installed templates and NuGet.org, returning a unified ranked list with local templates first.")]
    public static async Task<string> SearchTemplatesAsync(
        TemplateEngineService engineService,
        [Description("Search query string to match against template names, short names, tags, and descriptions")] string query,
        [Description("Optional language filter (e.g., 'C#', 'F#', 'VB')")] string? language = null,
        [Description("Optional type filter (e.g., 'project', 'item', 'solution')")] string? type = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = McpTelemetry.StartToolActivity("template_search");
        var sw = Stopwatch.StartNew();
        try
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
                Relevance = CalculateRelevance(t, query),
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
                        Relevance = CalculateRelevance(t, query),
                    });
                }
            }
        }

        // Sort results by relevance score (highest first)
        resultList.Sort((a, b) =>
        {
            double scoreA = 0, scoreB = 0;
            var propA = a.GetType().GetProperty("Relevance");
            var propB = b.GetType().GetProperty("Relevance");
            if (propA != null) scoreA = (double)(propA.GetValue(a) ?? 0.0);
            if (propB != null) scoreB = (double)(propB.GetValue(b) ?? 0.0);
            return scoreB.CompareTo(scoreA);
        });

        activity?.SetTag("mcp.result.count", resultList.Count);
        return JsonSerializer.Serialize(resultList, new JsonSerializerOptions { WriteIndented = true });
        }
        finally
        {
            McpTelemetry.RecordDuration("template_search", sw.Elapsed.TotalMilliseconds);
        }
    }

    /// <summary>
    /// Calculate a relevance score for a template against a query.
    /// Scores: exact short name match (0.5), name contains (0.3), classification match (0.2),
    /// description match (0.1), identity match (0.15).
    /// </summary>
    private static double CalculateRelevance(Abstractions.ITemplateInfo template, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return 0.5; // No query = equal relevance
        }

        double score = 0.0;

        // Exact short name match — strongest signal
        if (template.ShortNameList.Any(sn => sn.Equals(query, StringComparison.OrdinalIgnoreCase)))
        {
            score += 0.5;
        }
        else if (template.ShortNameList.Any(sn => sn.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            score += 0.3;
        }

        // Name match
        if (template.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.25;
        }

        // Classification match
        if (template.Classifications.Any(c => c.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            score += 0.15;
        }

        // Description match
        if (template.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
        {
            score += 0.1;
        }

        // Identity match
        if (template.Identity.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.1;
        }

        return Math.Min(score, 1.0);
    }
}
