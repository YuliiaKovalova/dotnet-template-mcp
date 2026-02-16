// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Microsoft.TemplateEngine.MCP.Tools;

[McpServerToolType]
internal sealed class TemplateDryRunTool
{
    [McpServerTool(Name = "template_dry_run")]
    [Description("Preview what files and actions would be created by a template without writing anything to disk. Use this before template_instantiate to review changes.")]
    public static async Task<string> DryRunTemplateAsync(
        TemplateEngineService engineService,
        [Description("Template identity or short name")] string templateName,
        [Description("Name for the created project/item")] string? name = null,
        [Description("Output directory path (used for path resolution only, nothing is written)")] string? outputPath = null,
        [Description("JSON object of parameter name-value pairs (e.g., {\"Framework\": \"net8.0\"})")] string? parametersJson = null,
        CancellationToken cancellationToken = default)
    {
        var templates = await engineService.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);

        var template = templates.FirstOrDefault(t =>
            t.Identity.Equals(templateName, StringComparison.OrdinalIgnoreCase) ||
            t.ShortNameList.Any(sn => sn.Equals(templateName, StringComparison.OrdinalIgnoreCase)));

        if (template == null)
        {
            return JsonSerializer.Serialize(new { error = $"Template '{templateName}' not found." });
        }

        var parameters = TemplateInstantiateTool.ParseParameters(parametersJson);
        string resolvedOutputPath = outputPath ?? Path.Combine(Path.GetTempPath(), name ?? template.DefaultName ?? "DryRunPreview");

        var result = await engineService.GetCreationEffectsAsync(template, name, resolvedOutputPath, parameters, cancellationToken).ConfigureAwait(false);

        return TemplateInstantiateTool.SerializeCreationResult(result);
    }
}
