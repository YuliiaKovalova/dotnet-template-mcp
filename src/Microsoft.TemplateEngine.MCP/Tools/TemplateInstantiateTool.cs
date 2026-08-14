// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.TemplateEngine.MCP.Host;
using Microsoft.TemplateEngine.MCP.PostCreation;
using Microsoft.TemplateEngine.MCP.Security;
using ModelContextProtocol.Server;
using ITemplateCreationResult = Microsoft.TemplateEngine.Edge.Template.ITemplateCreationResult;

namespace Microsoft.TemplateEngine.MCP.Tools;

[McpServerToolType]
internal sealed class TemplateInstantiateTool
{
    [McpServerTool(Name = "template_instantiate")]
    [Description("Create a project or item from a template — the main tool for project scaffolding. Auto-installs from NuGet if missing, validates parameters before writing files, applies smart defaults, adapts for Central Package Management, and runs the template's restore and add-to-solution post-actions. Prefer this over running 'dotnet new' directly.")]
    public static async Task<string> InstantiateTemplateAsync(
        TemplateEngineService engineService,
        PostCreationProcessor postProcessor,
        PostActionExecutor postActionExecutor,
        McpFeatureFlags featureFlags,
        McpServer server,
        [Description("Template identity or short name")] string templateName,
        [Description("Name for the created project/item")] string? name = null,
        [Description("Output directory path where files will be created")] string? outputPath = null,
        [Description("JSON object of parameter name-value pairs (e.g., {\"Framework\": \"net8.0\", \"EnableAot\": \"true\"})")] string? parametersJson = null,
        [Description("If true, rewrite package references to the latest stable NuGet versions. If false, make no NuGet feed calls at all and leave versions untouched. Omit to use the server default, which queries feeds and reports available upgrades without changing the versions the template author pinned.")] bool? resolveLatestVersions = null,
        [Description("If false, skip the template's restore and add-to-solution post-actions (default: true)")] bool runPostActions = true,
        CancellationToken cancellationToken = default)
    {
        using var activity = McpTelemetry.StartToolActivity("template_instantiate");
        var sw = Stopwatch.StartNew();
        try
        {
        string? autoInstallMessage = null;

        // 0. Reject writes outside the permitted workspace before doing any work.
        // The raw parameter is checked here for a fast, pre-work failure; the *effective* path is
        // re-validated after it is composed (step 7), because it is also built from `name` and from
        // the template's own DefaultName, neither of which is trusted.
        var pathRejection = WorkspaceGuard.Validate(outputPath, featureFlags)
            ?? WorkspaceGuard.ValidateNameSegment(name);
        if (pathRejection != null)
        {
            McpTelemetry.RecordError(activity, "template_instantiate", pathRejection);
            return WorkspaceGuard.PathRejectedError(pathRejection);
        }

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

        var parameters = ParseParameters(parametersJson, out var parseError);

        if (parseError != null)
        {
            McpTelemetry.RecordError(activity, "template_instantiate", parseError);
            return McpErrorResponse.Serialize("invalid_parameters",
                parseError,
                "Provide a valid JSON object, e.g., {\"Framework\": \"net8.0\", \"EnableAot\": \"true\"}.",
                retryable: true);
        }

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
        string resolvedOutputPath = outputPath ?? Path.Combine(featureFlags.WorkspaceRoot, name ?? template.DefaultName ?? "NewProject");

        // Re-validate the path that will actually be written. `name` and `template.DefaultName` are
        // untrusted (DefaultName comes from the template package, which may have just been installed
        // from NuGet by auto-resolve), and Path.Combine happily resolves "../.." out of the root.
        var resolvedRejection = WorkspaceGuard.Validate(resolvedOutputPath, featureFlags);
        if (resolvedRejection != null)
        {
            McpTelemetry.RecordError(activity, "template_instantiate", resolvedRejection);
            return WorkspaceGuard.PathRejectedError(resolvedRejection);
        }

        var result = await engineService.CreateAsync(template, name, resolvedOutputPath, parameters, cancellationToken).ConfigureAwait(false);

        McpTelemetry.TemplatesCreated.Add(1);
        activity?.SetTag("mcp.template.identity", template.Identity);

        // 8. Post-creation processing: CPM adaptation + NuGet version reporting/upgrades
        PostCreationResult? postCreationResult = null;
        string? postCreationError = null;
        PostActionExecutionReport? postActionReport = null;
        string? postActionError = null;

        if (result.Status == Microsoft.TemplateEngine.Edge.Template.CreationResultStatus.Success)
        {
            // Tri-state, deliberately: `true` applies upgrades, `false` is a genuine opt-out that
            // performs no feed lookups at all (the offline path), and omitting the parameter reports
            // available upgrades without writing them. Mapping `false` to Report would make the
            // opt-out still hit the network, which breaks offline and air-gapped callers.
            var versionPolicy = PostCreationProcessor.ResolvePolicy(resolveLatestVersions, featureFlags);

            try
            {
                postCreationResult = await postProcessor.ProcessAsync(
                    resolvedOutputPath, versionPolicy, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                postCreationError = $"Project created successfully, but post-processing failed: {ex.Message}. " +
                    "The project files are on disk but CPM adaptation and NuGet version handling were not applied.";
                McpTelemetry.RecordError(activity, "template_instantiate", $"Post-processing failed: {ex.Message}");
            }

            // 9. Run the template's safe post-actions (restore, add-to-solution). Without this the
            // project is left unrestored and unregistered in the surrounding solution.
            if (runPostActions && featureFlags.PostActionsEnabled)
            {
                try
                {
                    postActionReport = await postActionExecutor.ExecuteAsync(
                        result, resolvedOutputPath, cancellationToken).ConfigureAwait(false);

                    if (postActionReport.Executed.Count > 0)
                    {
                        McpTelemetry.PostActionsExecuted.Add(postActionReport.Executed.Count);
                    }

                    if (postActionReport.HasBlockingFailure)
                    {
                        McpTelemetry.RecordError(activity, "template_instantiate", "A required post-action failed.");
                    }
                }
                catch (Exception ex)
                {
                    postActionError = $"Project created successfully, but post-actions failed: {ex.Message}. " +
                        "Run 'dotnet restore' manually in the output directory.";
                    McpTelemetry.RecordError(activity, "template_instantiate", $"Post-actions failed: {ex.Message}");
                }
            }
        }

        return SerializeCreationResult(result, autoInstallMessage, constraintWarnings,
            smartDefaults.Count > 0 ? smartDefaults : null, postCreationResult, postCreationError,
            postActionReport, postActionError);
        }
        finally
        {
            McpTelemetry.RecordDuration("template_instantiate", sw.Elapsed.TotalMilliseconds);
        }
    }

    internal static Dictionary<string, string?> ParseParameters(string? parametersJson)
    {
        return ParseParameters(parametersJson, out _);
    }

    internal static Dictionary<string, string?> ParseParameters(string? parametersJson, out string? parseError)
    {
        parseError = null;

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
        catch (JsonException ex)
        {
            parseError = $"Invalid JSON in parametersJson: {ex.Message}";
            return new Dictionary<string, string?>();
        }
    }

    internal static string SerializeCreationResult(
        ITemplateCreationResult result,
        string? autoInstallMessage = null,
        IReadOnlyList<string>? constraintWarnings = null,
        IReadOnlyDictionary<string, string>? appliedSmartDefaults = null,
        PostCreationResult? postCreationResult = null,
        string? postCreationError = null,
        PostActionExecutionReport? postActionReport = null,
        string? postActionError = null)
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
        if (postCreationResult?.HasFindings == true)
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

            bool applied = postCreationResult.VersionUpgradesApplied;

            postCreationSummary = new
            {
                CpmDetected = postCreationResult.CpmDetected,
                DirectoryPackagesPropsPath = postCreationResult.DirectoryPackagesPropsPath,
                VersionPolicy = postCreationResult.VersionPolicy.ToString(),

                // Applied upgrades were written to disk; available ones were not.
                VersionUpgrades = applied && allUpgrades.Count > 0 ? allUpgrades : null,
                AvailableVersionUpgrades = !applied && allUpgrades.Count > 0 ? allUpgrades : null,
                AvailableVersionUpgradesHint = !applied && allUpgrades.Count > 0
                    ? "Versions were left as the template author pinned them. Re-run with resolveLatestVersions=true, or use packages_upgrade with apply=true, to write these."
                    : null,

                VersionsStrippedFromCsproj = allStripped.Count > 0 ? allStripped : null,
                AddedToDirectoryPackagesProps = allAddedToProps.Count > 0 ? allAddedToProps : null,
            };
        }

        object? postActionSummary = null;
        if (postActionReport?.HasAnything == true)
        {
            postActionSummary = new
            {
                Executed = postActionReport.Executed.Count > 0
                    ? postActionReport.Executed.Select(e => new
                    {
                        e.ActionId,
                        e.Description,
                        e.Command,
                        e.Success,
                        e.Error,
                        e.ManualInstructions,
                    }).ToList()
                    : null,
                Skipped = postActionReport.Skipped.Count > 0
                    ? postActionReport.Skipped.Select(s => new
                    {
                        s.ActionId,
                        s.Description,
                        s.Reason,
                        s.ManualInstructions,
                    }).ToList()
                    : null,
                HasBlockingFailure = postActionReport.HasBlockingFailure,
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
            PostCreationError = postCreationError,
            PostActionExecution = postActionSummary,
            PostActionError = postActionError,
            PrimaryOutputs = primaryOutputs,
            PostActions = postActions,
            FileChanges = fileChanges,
        };

        return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
    }
}
