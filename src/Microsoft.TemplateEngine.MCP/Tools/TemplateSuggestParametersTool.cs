// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.TemplateEngine.MCP.Host;
using ModelContextProtocol.Server;

namespace Microsoft.TemplateEngine.MCP.Tools;

[McpServerToolType]
internal sealed class TemplateSuggestParametersTool
{
    [McpServerTool(Name = "template_suggest_parameters")]
    [Description("Given a template and partial parameter values, suggest reasonable defaults based on cross-parameter relationships. Returns suggestions with rationale explaining why each value is recommended. Example: EnableAot=true → suggests Framework=net9.0 because 'NativeAOT works best with the latest framework'.")]
    public static async Task<string> SuggestParametersAsync(
        TemplateEngineService engineService,
        McpFeatureFlags featureFlags,
        [Description("Template identity or short name")] string templateName,
        [Description("JSON object of parameter name-value pairs already chosen (e.g., {\"EnableAot\": \"true\"})")] string? parametersJson = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = McpTelemetry.StartToolActivity("template_suggest_parameters");
        var sw = Stopwatch.StartNew();
        try
        {
            if (!featureFlags.IsToolEnabled("template_suggest_parameters"))
            {
                return ToolProfileResponse.DisabledMessage("template_suggest_parameters", "Use template_inspect to see parameter details, or template_instantiate which applies smart defaults automatically.");
            }

            var template = await engineService.FindTemplateAsync(templateName, cancellationToken).ConfigureAwait(false);
            if (template == null)
            {
                McpTelemetry.RecordError(activity, "template_suggest_parameters", "Template not found");
                return JsonSerializer.Serialize(new
                {
                    error = $"Template '{templateName}' not found locally. Install it first with template_install.",
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            var parameters = TemplateInstantiateTool.ParseParameters(parametersJson);
            var suggestions = TemplateEngineFacade.GetParameterSuggestions(template, parameters);

            var response = new
            {
                TemplateName = template.Identity,
                CurrentParameters = parameters.Count > 0 ? parameters : null,
                Suggestions = suggestions.Select(s => new
                {
                    s.ParameterName,
                    s.SuggestedValue,
                    s.Rationale,
                }).ToList(),
                Message = suggestions.Count > 0
                    ? $"{suggestions.Count} suggestion(s) based on your current parameter selections."
                    : "No additional suggestions — your current parameters look good.",
            };

            activity?.SetTag("mcp.template.identity", template.Identity);
            activity?.SetTag("mcp.suggestions.count", suggestions.Count);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            McpTelemetry.RecordError(activity, "template_suggest_parameters", ex.Message);
            return JsonSerializer.Serialize(new { error = ex.Message }, new JsonSerializerOptions { WriteIndented = true });
        }
        finally
        {
            McpTelemetry.RecordDuration("template_suggest_parameters", sw.Elapsed.TotalMilliseconds);
        }
    }
}
