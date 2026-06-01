// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Microsoft.TemplateEngine.MCP.Tools;

[McpServerToolType]
internal sealed class TemplateDryRunTool
{
    [McpServerTool(Name = "template_dry_run")]
    [Description("See exactly what files a template would create before committing. Supports auto-resolve from NuGet, parameter validation, and constraint checking.")]
    public static async Task<string> DryRunTemplateAsync(
        TemplateEngineService engineService,
        [Description("Template identity or short name")] string templateName,
        [Description("Name for the created project/item")] string? name = null,
        [Description("Output directory path (used for path resolution only, nothing is written)")] string? outputPath = null,
        [Description("JSON object of parameter name-value pairs (e.g., {\"Framework\": \"net8.0\"})")] string? parametersJson = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = McpTelemetry.StartToolActivity("template_dry_run");
        var sw = Stopwatch.StartNew();
        try
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
                McpTelemetry.RecordError(activity, "template_dry_run", message ?? "auto-resolve failed");
                return JsonSerializer.Serialize(new { error = message }, new JsonSerializerOptions { WriteIndented = true });
            }

            template = resolved;
            autoInstallMessage = message;
        }

        var parameters = TemplateInstantiateTool.ParseParameters(parametersJson, out var parseError);

        if (parseError != null)
        {
            McpTelemetry.RecordError(activity, "template_dry_run", parseError);
            return JsonSerializer.Serialize(new
            {
                error = parseError,
                hint = "Provide a valid JSON object, e.g., {\"Framework\": \"net8.0\"}.",
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        // 3. Apply smart defaults
        var smartDefaults = TemplateEngineService.SuggestSmartDefaults(template, parameters);
        foreach (var (key, value) in smartDefaults)
        {
            if (!parameters.ContainsKey(key))
            {
                parameters[key] = value;
            }
        }

        // 4. Validate parameters
        var validationErrors = TemplateEngineService.ValidateParameters(template, parameters);
        if (validationErrors.Count > 0)
        {
            McpTelemetry.RecordError(activity, "template_dry_run", "Parameter validation failed");
            return JsonSerializer.Serialize(new
            {
                error = "Parameter validation failed.",
                validationErrors,
                templateName = template.Identity,
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        // 5. Check constraints
        var constraintWarnings = TemplateEngineService.CheckConstraints(template);

        string resolvedOutputPath = outputPath ?? Path.Combine(Path.GetTempPath(), name ?? template.DefaultName ?? "DryRunPreview");

        var result = await engineService.GetCreationEffectsAsync(template, name, resolvedOutputPath, parameters, cancellationToken).ConfigureAwait(false);

        return TemplateInstantiateTool.SerializeCreationResult(result, autoInstallMessage, constraintWarnings, smartDefaults.Count > 0 ? smartDefaults : null);
        }
        finally
        {
            McpTelemetry.RecordDuration("template_dry_run", sw.Elapsed.TotalMilliseconds);
        }
    }
}
