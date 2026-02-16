// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Microsoft.TemplateEngine.MCP.Tools;

[McpServerToolType]
internal sealed class TemplateInspectTool
{
    [McpServerTool(Name = "template_inspect")]
    [Description("Inspect a template by identity or short name. Returns all metadata in one call: parameters (names, types, defaults, choices, descriptions), constraints, post-actions, supported hosts, and classifications.")]
    public static async Task<string> InspectTemplateAsync(
        TemplateEngineService engineService,
        [Description("Template identity or short name to inspect")] string templateName,
        CancellationToken cancellationToken = default)
    {
        var templates = await engineService.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);

        var template = templates.FirstOrDefault(t =>
            t.Identity.Equals(templateName, StringComparison.OrdinalIgnoreCase) ||
            t.ShortNameList.Any(sn => sn.Equals(templateName, StringComparison.OrdinalIgnoreCase)));

        if (template == null)
        {
            return JsonSerializer.Serialize(new { error = $"Template '{templateName}' not found. Use template_search or template_list to find available templates." });
        }

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
