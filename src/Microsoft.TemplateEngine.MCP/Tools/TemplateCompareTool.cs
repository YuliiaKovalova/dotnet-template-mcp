// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Microsoft.TemplateEngine.MCP.Tools;

[McpServerToolType]
internal sealed class TemplateCompareTool
{
    [McpServerTool(Name = "template_compare")]
    [Description("Compare 2 or more templates side by side — parameters, auth support, AOT, framework options, and classifications. Use when deciding between templates (e.g., webapi vs webapp, blazorserver vs blazorwasm).")]
    public static async Task<string> CompareTemplatesAsync(
        TemplateEngineService engineService,
        McpFeatureFlags featureFlags,
        [Description("Comma-separated template identities or short names to compare (e.g., 'webapi,webapp' or 'blazorserver,blazorwasm')")] string templateNames,
        CancellationToken cancellationToken = default)
    {
        using var activity = McpTelemetry.StartToolActivity("template_compare");
        var sw = Stopwatch.StartNew();
        try
        {
            if (!featureFlags.IsToolEnabled("template_compare"))
            {
                return ToolProfileResponse.DisabledMessage("template_compare", "Use template_inspect to inspect templates one at a time.");
            }

            var names = templateNames
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (names.Count < 2)
            {
                return McpErrorResponse.Serialize("invalid_input",
                    "Provide at least 2 template names separated by commas.",
                    "Example: template_compare('webapi,webapp')",
                    retryable: true);
            }

            var allTemplates = await engineService.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);
            var comparisons = new List<object>();
            var notFound = new List<string>();

            foreach (var name in names)
            {
                var template = allTemplates.FirstOrDefault(t =>
                    t.Identity.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    t.ShortNameList.Any(sn => sn.Equals(name, StringComparison.OrdinalIgnoreCase)));

                if (template == null)
                {
                    notFound.Add(name);
                    continue;
                }

                var parameters = template.ParameterDefinitions
                    .Where(p => p.Type == "parameter" && !p.IsName)
                    .Select(p => new
                    {
                        p.Name,
                        p.DataType,
                        p.DefaultValue,
                        p.Description,
                        Choices = p.Choices?.Keys.ToList(),
                        IsRequired = p.Precedence.PrecedenceDefinition.ToString() == "Required",
                    }).ToList();

                // Extract key feature flags
                var paramNames = new HashSet<string>(
                    template.ParameterDefinitions.Select(p => p.Name),
                    StringComparer.OrdinalIgnoreCase);

                comparisons.Add(new
                {
                    template.Identity,
                    ShortNames = template.ShortNameList,
                    template.Name,
                    template.Description,
                    template.Author,
                    template.Classifications,
                    Language = template.TagsCollection.GetValueOrDefault("language"),
                    Type = template.TagsCollection.GetValueOrDefault("type"),
                    Features = new
                    {
                        SupportsAuth = paramNames.Contains("auth"),
                        SupportsAot = paramNames.Contains("PublishAot") || paramNames.Contains("EnableAot") || paramNames.Contains("nativeAot"),
                        SupportsDocker = paramNames.Contains("EnableDocker") || paramNames.Contains("Docker"),
                        SupportsControllers = paramNames.Contains("UseControllers"),
                        SupportsHttps = paramNames.Contains("NoHttps"),
                        SupportsInteractivity = paramNames.Contains("interactivity") || paramNames.Contains("InteractivityPlatform"),
                    },
                    Frameworks = template.ParameterDefinitions
                        .FirstOrDefault(p => p.Name.Equals("Framework", StringComparison.OrdinalIgnoreCase))
                        ?.Choices?.Keys.ToList(),
                    ParameterCount = parameters.Count,
                    Parameters = parameters,
                    ConstraintCount = template.Constraints.Count(),
                });
            }

            activity?.SetTag("mcp.compare.count", comparisons.Count);

            var response = new
            {
                ComparedTemplates = comparisons,
                NotFound = notFound.Count > 0 ? notFound : null,
                NotFoundHint = notFound.Count > 0
                    ? "Some templates were not found locally. Use template_search or template_install to make them available."
                    : null,
            };

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        finally
        {
            McpTelemetry.RecordDuration("template_compare", sw.Elapsed.TotalMilliseconds);
        }
    }
}
