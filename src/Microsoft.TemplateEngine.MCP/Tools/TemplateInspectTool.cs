// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Microsoft.TemplateEngine.MCP.Tools;

[McpServerToolType]
internal sealed class TemplateInspectTool
{
    [McpServerTool(Name = "template_inspect")]
    [Description("Inspect a template by identity or short name. Returns all metadata in one call: parameters (names, types, defaults, choices, descriptions), constraints, post-actions, supported hosts, and classifications. Can also inspect templates on NuGet that are not yet installed.")]
    public static async Task<string> InspectTemplateAsync(
        TemplateEngineService engineService,
        [Description("Template identity or short name to inspect")] string templateName,
        CancellationToken cancellationToken = default)
    {
        using var activity = McpTelemetry.StartToolActivity("template_inspect");
        var sw = Stopwatch.StartNew();
        try
        {
        var templates = await engineService.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);

        var template = templates.FirstOrDefault(t =>
            t.Identity.Equals(templateName, StringComparison.OrdinalIgnoreCase) ||
            t.ShortNameList.Any(sn => sn.Equals(templateName, StringComparison.OrdinalIgnoreCase)));

        if (template == null)
        {
            var nugetResult = await TryInspectFromNuGetAsync(engineService, templateName, cancellationToken).ConfigureAwait(false);
            if (nugetResult != null)
            {
                activity?.SetTag("mcp.source", "nuget");
                return nugetResult;
            }

            McpTelemetry.RecordError(activity, "template_inspect", $"Template '{templateName}' not found");
            return JsonSerializer.Serialize(new { error = $"Template '{templateName}' not found locally or on NuGet.org. Use template_search to find available templates." });
        }

        activity?.SetTag("mcp.template.identity", template.Identity);
        return SerializeTemplateInspection(template);
        }
        finally
        {
            McpTelemetry.RecordDuration("template_inspect", sw.Elapsed.TotalMilliseconds);
        }
    }

    private static async Task<string?> TryInspectFromNuGetAsync(
        TemplateEngineService engineService,
        string templateName,
        CancellationToken cancellationToken)
    {
        var nugetResults = await engineService.SearchNuGetTemplatesAsync(templateName, null, null, cancellationToken).ConfigureAwait(false);

        // Find exact match by short name or identity
        foreach (var (packageInfo, matchedTemplates) in nugetResults)
        {
            var match = matchedTemplates.FirstOrDefault(t =>
                t.ShortNameList.Any(sn => sn.Equals(templateName, StringComparison.OrdinalIgnoreCase)) ||
                t.Identity.Equals(templateName, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                var parameters = match.ParameterDefinitions
                    .Where(p => p.Type == "parameter" && !p.IsName)
                    .Select(p => new
                    {
                        p.Name,
                        p.DataType,
                        p.DefaultValue,
                        p.Description,
                        p.DisplayName,
                        p.AllowMultipleValues,
                        p.DefaultIfOptionWithoutValue,
                        Precedence = p.Precedence.PrecedenceDefinition.ToString(),
                        Choices = p.Choices?.Select(c => new
                        {
                            Value = c.Key,
                            c.Value.DisplayName,
                            c.Value.Description,
                        }).ToList(),
                    }).ToList();

                var result = new
                {
                    match.Identity,
                    ShortNames = match.ShortNameList,
                    match.Name,
                    match.Description,
                    match.Author,
                    match.Classifications,
                    Language = match.TagsCollection.GetValueOrDefault("language"),
                    Type = match.TagsCollection.GetValueOrDefault("type"),
                    Source = "nuget",
                    PackageId = packageInfo.Name,
                    PackageVersion = packageInfo.Version,
                    Parameters = parameters,
                    Note = $"This template is available on NuGet (package: {packageInfo.Name} v{packageInfo.Version}) but is not yet installed. Use template_install to install it, or template_instantiate which will auto-install.",
                };

                return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            }
        }

        return null;
    }

    internal static string SerializeTemplateInspection(Abstractions.ITemplateInfo template)
    {
        var parameters = template.ParameterDefinitions
            .Where(p => p.Type == "parameter" && !p.IsName)
            .Select(p => new
            {
                p.Name,
                p.DataType,
                p.DefaultValue,
                p.Description,
                p.DisplayName,
                p.AllowMultipleValues,
                p.DefaultIfOptionWithoutValue,
                Precedence = p.Precedence.PrecedenceDefinition.ToString(),
                Choices = p.Choices?.Select(c => new
                {
                    Value = c.Key,
                    c.Value.DisplayName,
                    c.Value.Description,
                }).ToList(),
            }).ToList();

        var constraints = template.Constraints.Select(c => new
        {
            c.Type,
            c.Args,
        }).ToList();

        var postActions = template.PostActions.Select(pa => pa.ToString()).ToList();

        var result = new
        {
            template.Identity,
            ShortNames = template.ShortNameList,
            template.Name,
            template.Description,
            template.Author,
            template.Classifications,
            template.DefaultName,
            template.GroupIdentity,
            template.Precedence,
            Language = template.TagsCollection.GetValueOrDefault("language"),
            Type = template.TagsCollection.GetValueOrDefault("type"),
            Tags = template.TagsCollection,
            Baselines = template.BaselineInfo.Select(b => new
            {
                b.Key,
                b.Value.Description,
                b.Value.DefaultOverrides,
            }).ToList(),
            Parameters = parameters,
            Constraints = constraints,
            PostActionIds = postActions,
            template.ThirdPartyNotices,
            template.PreferDefaultName,
        };

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }
}

