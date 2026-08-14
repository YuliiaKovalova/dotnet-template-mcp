// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using DotnetTemplateMcp.Host;
using ModelContextProtocol.Server;

namespace DotnetTemplateMcp.Tools;

[McpServerToolType]
internal sealed class TemplateComposeTool
{
    [McpServerTool(Name = "template_compose")]
    [Description("Execute a sequence of template operations (project + item templates) in order. For example, create a MAUI app then add specific pages/views. Each step can reference a different template. If a template is not installed, it will be auto-resolved from NuGet.")]
    public static async Task<string> ComposeTemplatesAsync(
        TemplateEngineService engineService,
        McpFeatureFlags featureFlags,
        [Description("JSON array of steps. Each step: {\"templateName\": \"...\", \"name\": \"...\", \"outputPath\": \"...\", \"target\": \"relative/path\", \"parametersJson\": \"{...}\"}. The first step creates the project; subsequent steps add items. If 'target' is set on later steps, it's resolved relative to the first step's output.")] string stepsJson,
        CancellationToken cancellationToken = default)
    {
        using var activity = McpTelemetry.StartToolActivity("template_compose");
        var sw = Stopwatch.StartNew();
        try
        {
            if (!featureFlags.IsToolEnabled("template_compose"))
            {
                return ToolProfileResponse.DisabledMessage("template_compose", "Use template_instantiate to create one project at a time.");
            }

            List<ComposeStep>? steps;
            try
            {
                steps = JsonSerializer.Deserialize<List<ComposeStep>>(stepsJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
            }
            catch (JsonException ex)
            {
                McpTelemetry.RecordError(activity, "template_compose", "Invalid steps JSON");
                return JsonSerializer.Serialize(new
                {
                    error = $"Invalid stepsJson format: {ex.Message}",
                    expectedFormat = new[]
                    {
                        new { templateName = "console", name = "MyApp", outputPath = (string?)null, target = (string?)null, parametersJson = (string?)null },
                    },
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            if (steps == null || steps.Count == 0)
            {
                McpTelemetry.RecordError(activity, "template_compose", "Empty steps");
                return JsonSerializer.Serialize(new { error = "stepsJson must contain at least one step." },
                    new JsonSerializerOptions { WriteIndented = true });
            }

            var facade = new TemplateEngineFacade(engineService, featureFlags);
            var result = await facade.ComposeAsync(steps, cancellationToken).ConfigureAwait(false);

            activity?.SetTag("mcp.compose.steps", steps.Count);
            return result;
        }
        catch (Exception ex)
        {
            McpTelemetry.RecordError(activity, "template_compose", ex.Message);
            return JsonSerializer.Serialize(new { error = ex.Message }, new JsonSerializerOptions { WriteIndented = true });
        }
        finally
        {
            McpTelemetry.RecordDuration("template_compose", sw.Elapsed.TotalMilliseconds);
        }
    }
}
