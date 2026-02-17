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
    [Description("Preview what files and actions would be created by a template without writing anything to disk. Supports auto-resolve from NuGet, parameter validation, and constraint checking.")]
    public static async Task<string> DryRunTemplateAsync(
        TemplateEngineService engineService,
        [Description("Template identity or short name")] string templateName,
        [Description("Name for the created project/item")] string? name = null,
        [Description("Output directory path (used for path resolution only, nothing is written)")] string? outputPath = null,
        [Description("JSON object of parameter name-value pairs (e.g., {\"Framework\": \"net8.0\"})")] string? parametersJson = null,
        CancellationToken cancellationToken = default)
    {
        string? autoInstallMessage = null;

        // 1. Find template locally
        var template = await engineService.FindTemplateAsync(templateName, cancellationToken).ConfigureAwait(false);

        // 2. Auto-resolve from NuGet if not found
        if (template == null)
        {
            var (resolved, message) = await engineService.AutoResolveAndInstallAsync(templateName, cancellationToken).ConfigureAwait(false);
            if (resolved == null)
            {
                return JsonSerializer.Serialize(new { error = message }, new JsonSerializerOptions { WriteIndented = true });
            }

            template = resolved;
            autoInstallMessage = message;
        }

        var parameters = TemplateInstantiateTool.ParseParameters(parametersJson);

        // 3. Validate parameters
        var validationErrors = TemplateEngineService.ValidateParameters(template, parameters);
        if (validationErrors.Count > 0)
        {
            return JsonSerializer.Serialize(new
            {
                error = "Parameter validation failed.",
                validationErrors,
                templateName = template.Identity,
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        // 4. Check constraints
        var constraintWarnings = TemplateEngineService.CheckConstraints(template);

        string resolvedOutputPath = outputPath ?? Path.Combine(Path.GetTempPath(), name ?? template.DefaultName ?? "DryRunPreview");

        var result = await engineService.GetCreationEffectsAsync(template, name, resolvedOutputPath, parameters, cancellationToken).ConfigureAwait(false);

        return TemplateInstantiateTool.SerializeCreationResult(result, autoInstallMessage, constraintWarnings);
    }
}
