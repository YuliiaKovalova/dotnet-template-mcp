// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
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
        CancellationToken cancellationToken = default)
    {
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

        return JsonSerializer.Serialize(
            new { totalCount = result.Count, templates = result },
            new JsonSerializerOptions { WriteIndented = true });
    }
}
