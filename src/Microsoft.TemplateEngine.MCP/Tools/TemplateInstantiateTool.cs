// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.TemplateEngine.MCP.Host;
using Microsoft.TemplateEngine.MCP.PostCreation;
using ModelContextProtocol.Server;
using ITemplateCreationResult = Microsoft.TemplateEngine.Edge.Template.ITemplateCreationResult;

namespace Microsoft.TemplateEngine.MCP.Tools;

[McpServerToolType]
internal sealed class TemplateInstantiateTool
{
    [McpServerTool(Name = "template_instantiate")]
    [Description("Create a project or item from a template. If the template is not installed, it will automatically search NuGet, install, and create in one call. Validates parameters and checks constraints before writing to disk. Automatically detects CPM (Central Package Management) and adapts package references. Can resolve latest stable NuGet package versions.")]
    public static async Task<string> InstantiateTemplateAsync(
        TemplateEngineService engineService,
        PostCreationProcessor postProcessor,
        McpFeatureFlags featureFlags,
        McpServer server,
        [Description("Template identity or short name")] string templateName,
        [Description("Name for the created project/item")] string? name = null,
        [Description("Output directory path where files will be created")] string? outputPath = null,
        [Description("JSON object of parameter name-value pairs (e.g., {\"Framework\": \"net8.0\", \"EnableAot\": \"true\"})")] string? parametersJson = null,
        [Description("If true, resolve latest stable NuGet versions for all package references (default: true)")] bool resolveLatestVersions = true,
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
                return McpErrorResponse.Serialize("template_not_found",
                    message ?? $"Template '{templateName}' not found locally or on NuGet.",
                    "Check the template name or try template_search to find available templates.");
            }

            template = resolved;
            autoInstallMessage = message;
        }

        var parameters = ParseParameters(parametersJson);

        // 3. Elicit missing required parameters interactively (if supported)
        if (featureFlags.ElicitationEnabled && ElicitationHelper.IsElicitationSupported(server))
        {
            var elicited = await ElicitationHelper.ElicitMissingParametersAsync(
                server, template, parameters, cancellationToken).ConfigureAwait(false);

            if (elicited is not null)
            {
                foreach (var (key, value) in elicited)
                {
                    parameters[key] = value;
                }

                McpTelemetry.ElicitedParameters.Add(elicited.Count);
            }
        }

        // 4. Apply smart defaults based on cross-parameter relationships
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

        // 5. Validate parameters before creation
        var validationErrors = TemplateEngineService.ValidateParameters(template, parameters);
        if (validationErrors.Count > 0)
        {
            McpTelemetry.ValidationFailures.Add(1);
            McpTelemetry.RecordError(activity, "template_instantiate", "Parameter validation failed");
            return McpErrorResponse.Serialize("validation_failed",
                "Parameter validation failed. No files were written.",
                "Fix the parameter values and retry. Use template_inspect to see valid parameter options.",
                retryable: true,
                details: new { validationErrors, templateName = template.Identity });
        }

        // 6. Check constraints
        var constraintWarnings = TemplateEngineService.CheckConstraints(template);

        // 7. Instantiate
        string resolvedOutputPath = outputPath ?? Path.Combine(Environment.CurrentDirectory, name ?? template.DefaultName ?? "NewProject");

        var result = await engineService.CreateAsync(template, name, resolvedOutputPath, parameters, cancellationToken).ConfigureAwait(false);

        McpTelemetry.TemplatesCreated.Add(1);
        activity?.SetTag("mcp.template.identity", template.Identity);

        // 8. Post-creation processing: CPM adaptation + NuGet version upgrades
        PostCreationResult? postCreationResult = null;
        if (result.Status == Microsoft.TemplateEngine.Edge.Template.CreationResultStatus.Success)
        {
            postCreationResult = await postProcessor.ProcessAsync(
                resolvedOutputPath, resolveLatestVersions, cancellationToken).ConfigureAwait(false);
        }

        return SerializeCreationResult(result, autoInstallMessage, constraintWarnings,
            smartDefaults.Count > 0 ? smartDefaults : null, postCreationResult);
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
        IReadOnlyDictionary<string, string>? appliedSmartDefaults = null,
        PostCreationResult? postCreationResult = null)
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

        // Build post-creation summary
        object? postCreationSummary = null;
        if (postCreationResult?.HasChanges == true)
        {
            var allUpgrades = postCreationResult.ProcessedFiles
                .SelectMany(f => f.VersionUpgrades)
                .Select(u => new { u.PackageName, u.OldVersion, u.NewVersion })
                .ToList();

            var allStripped = postCreationResult.ProcessedFiles
                .SelectMany(f => f.VersionsStripped)
                .Distinct()
                .ToList();

            var allAddedToProps = postCreationResult.ProcessedFiles
                .SelectMany(f => f.AddedToDirectoryPackagesProps)
                .Select(e => new { e.PackageName, e.Version })
                .ToList();

            postCreationSummary = new
            {
                CpmDetected = postCreationResult.CpmDetected,
                DirectoryPackagesPropsPath = postCreationResult.DirectoryPackagesPropsPath,
                VersionUpgrades = allUpgrades.Count > 0 ? allUpgrades : null,
                VersionsStrippedFromCsproj = allStripped.Count > 0 ? allStripped : null,
                AddedToDirectoryPackagesProps = allAddedToProps.Count > 0 ? allAddedToProps : null,
            };
        }

        var response = new
        {
            Status = result.Status.ToString(),
            result.ErrorMessage,
            result.OutputBaseDirectory,
            result.TemplateFullName,
            AutoInstalled = autoInstallMessage,
            ConstraintWarnings = constraintWarnings?.Count > 0 ? constraintWarnings : null,
            AppliedSmartDefaults = appliedSmartDefaults?.Count > 0 ? appliedSmartDefaults : null,
            PostCreation = postCreationSummary,
            PrimaryOutputs = primaryOutputs,
            PostActions = postActions,
            FileChanges = fileChanges,
        };

        return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
    }
}
