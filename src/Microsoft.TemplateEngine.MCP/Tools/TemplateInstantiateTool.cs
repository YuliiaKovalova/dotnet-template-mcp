// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Server;
using ITemplateCreationResult = Microsoft.TemplateEngine.Edge.Template.ITemplateCreationResult;

namespace Microsoft.TemplateEngine.MCP.Tools;

[McpServerToolType]
internal sealed class TemplateInstantiateTool
{
    [McpServerTool(Name = "template_instantiate")]
    [Description("Create a project or item from a template. If the template is not installed, it will automatically search NuGet, install, and create in one call. Validates parameters and checks constraints before writing to disk.")]
    public static async Task<string> InstantiateTemplateAsync(
        TemplateEngineService engineService,
        [Description("Template identity or short name")] string templateName,
        [Description("Name for the created project/item")] string? name = null,
        [Description("Output directory path where files will be created")] string? outputPath = null,
        [Description("JSON object of parameter name-value pairs (e.g., {\"Framework\": \"net8.0\", \"EnableAot\": \"true\"})")] string? parametersJson = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = McpTelemetry.StartToolActivity("template_instantiate");
        var sw = Stopwatch.StartNew();
        try
        {
        string? autoInstallMessage = null;

        // 1. Find template locally
        var template = await engineService.FindTemplateAsync(templateName, cancellationToken).ConfigureAwait(false);

        // 2. Auto-resolve: if not found, search NuGet → install → find
        if (template == null)
        {
            McpTelemetry.AutoResolves.Add(1);
            var (resolved, message) = await engineService.AutoResolveAndInstallAsync(templateName, cancellationToken).ConfigureAwait(false);
            if (resolved == null)
            {
                McpTelemetry.RecordError(activity, "template_instantiate", message ?? "auto-resolve failed");
                return JsonSerializer.Serialize(new { error = message }, new JsonSerializerOptions { WriteIndented = true });
            }

            template = resolved;
            autoInstallMessage = message;
        }

        var parameters = ParseParameters(parametersJson);

        // 3. Apply smart defaults based on cross-parameter relationships
        var smartDefaults = TemplateEngineService.SuggestSmartDefaults(template, parameters);
        foreach (var (key, value) in smartDefaults)
        {
            if (!parameters.ContainsKey(key))
            {
                parameters[key] = value;
            }
        }

        if (smartDefaults.Count > 0)
        {
            McpTelemetry.SmartDefaultsApplied.Add(smartDefaults.Count);
        }

        // 4. Validate parameters before creation
        var validationErrors = TemplateEngineService.ValidateParameters(template, parameters);
        if (validationErrors.Count > 0)
        {
            McpTelemetry.ValidationFailures.Add(1);
            McpTelemetry.RecordError(activity, "template_instantiate", "Parameter validation failed");
            return JsonSerializer.Serialize(new
            {
                error = "Parameter validation failed. No files were written.",
                validationErrors,
                templateName = template.Identity,
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        // 5. Check constraints
        var constraintWarnings = TemplateEngineService.CheckConstraints(template);

        // 6. Instantiate
        string resolvedOutputPath = outputPath ?? Path.Combine(Environment.CurrentDirectory, name ?? template.DefaultName ?? "NewProject");

        var result = await engineService.CreateAsync(template, name, resolvedOutputPath, parameters, cancellationToken).ConfigureAwait(false);

        McpTelemetry.TemplatesCreated.Add(1);
        activity?.SetTag("mcp.template.identity", template.Identity);
        return SerializeCreationResult(result, autoInstallMessage, constraintWarnings, smartDefaults.Count > 0 ? smartDefaults : null);
        }
        finally
        {
            McpTelemetry.RecordDuration("template_instantiate", sw.Elapsed.TotalMilliseconds);
        }
    }

    internal static Dictionary<string, string?> ParseParameters(string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson))
        {
            return new Dictionary<string, string?>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(parametersJson);
            if (parsed == null)
            {
                return new Dictionary<string, string?>();
            }

            return parsed.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ValueKind == JsonValueKind.Null ? null : kvp.Value.ToString());
        }
        catch (JsonException)
        {
            return new Dictionary<string, string?>();
        }
    }

    internal static string SerializeCreationResult(
        ITemplateCreationResult result,
        string? autoInstallMessage = null,
        IReadOnlyList<string>? constraintWarnings = null,
        IReadOnlyDictionary<string, string>? appliedSmartDefaults = null)
    {
        var postActions = result.CreationResult?.PostActions?.Select(pa => new
        {
            pa.Description,
            ActionId = pa.ActionId.ToString(),
            pa.ManualInstructions,
            pa.ContinueOnError,
            pa.Args,
        }).ToList();

        var primaryOutputs = result.CreationResult?.PrimaryOutputs?.Select(po => po.Path).ToList();

        var fileChanges = result.CreationEffects?.FileChanges?.Select(fc => new
        {
            fc.TargetRelativePath,
            ChangeKind = fc.ChangeKind.ToString(),
        }).ToList();

        var response = new
        {
            Status = result.Status.ToString(),
            result.ErrorMessage,
            result.OutputBaseDirectory,
            result.TemplateFullName,
            AutoInstalled = autoInstallMessage,
            ConstraintWarnings = constraintWarnings?.Count > 0 ? constraintWarnings : null,
            AppliedSmartDefaults = appliedSmartDefaults?.Count > 0 ? appliedSmartDefaults : null,
            PrimaryOutputs = primaryOutputs,
            PostActions = postActions,
            FileChanges = fileChanges,
        };

        return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
    }
}
