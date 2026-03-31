// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Microsoft.TemplateEngine.Abstractions;
using ITemplateCreationResult = Microsoft.TemplateEngine.Edge.Template.ITemplateCreationResult;

namespace Microsoft.TemplateEngine.MCP.Host;

/// <summary>
/// High-level facade that composes TemplateEngineService with smart defaults,
/// validation, constraint checking, and creation effects analysis into unified operations.
/// </summary>
internal class TemplateEngineFacade
{
    private readonly TemplateEngineService _engineService;

    public TemplateEngineFacade(TemplateEngineService engineService)
    {
        _engineService = engineService;
    }

    /// <summary>
    /// Resolve a template by name, auto-installing from NuGet if necessary.
    /// </summary>
    public async Task<TemplateResolveResult> ResolveTemplateAsync(
        string templateName,
        CancellationToken cancellationToken = default)
    {
        var template = await _engineService.FindTemplateAsync(templateName, cancellationToken).ConfigureAwait(false);
        if (template != null)
        {
            return new TemplateResolveResult(template);
        }

        McpTelemetry.AutoResolves.Add(1);
        var (resolved, message) = await _engineService.AutoResolveAndInstallAsync(templateName, cancellationToken).ConfigureAwait(false);
        return new TemplateResolveResult(resolved, message);
    }

    /// <summary>
    /// Prepare parameters for creation: parse JSON, apply smart defaults, validate, check constraints.
    /// Returns a fully prepared result or errors.
    /// </summary>
    public ParameterPreparationResult PrepareParameters(
        ITemplateInfo template,
        string? parametersJson)
    {
        var parameters = ParseParameters(parametersJson, out var parseError);

        if (parseError != null)
        {
            return ParameterPreparationResult.Failed(
                new List<string> { parseError },
                template.Identity);
        }

        // Apply smart defaults
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

        // Validate
        var validationErrors = TemplateEngineService.ValidateParameters(template, parameters);
        if (validationErrors.Count > 0)
        {
            McpTelemetry.ValidationFailures.Add(1);
            return ParameterPreparationResult.Failed(validationErrors, template.Identity);
        }

        // Check constraints
        var constraintWarnings = TemplateEngineService.CheckConstraints(template);

        return ParameterPreparationResult.Succeeded(parameters, smartDefaults, constraintWarnings);
    }

    /// <summary>
    /// Get parameter suggestions with rationale for the given template and partial parameter values.
    /// Unlike SuggestSmartDefaults which returns just values, this returns human-readable rationale.
    /// </summary>
    public static IReadOnlyList<ParameterSuggestion> GetParameterSuggestions(
        ITemplateInfo template,
        IReadOnlyDictionary<string, string?> userParameters)
    {
        var suggestions = new List<ParameterSuggestion>();

        bool aotEnabled = userParameters.Any(p =>
            (p.Key.Equals("EnableAot", StringComparison.OrdinalIgnoreCase) ||
             p.Key.Equals("PublishAot", StringComparison.OrdinalIgnoreCase) ||
             p.Key.Equals("nativeAot", StringComparison.OrdinalIgnoreCase)) &&
            p.Value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true);

        if (aotEnabled)
        {
            var frameworkParam = template.ParameterDefinitions.FirstOrDefault(p =>
                p.Name.Equals("Framework", StringComparison.OrdinalIgnoreCase));
            if (frameworkParam?.Choices != null && !userParameters.ContainsKey("Framework"))
            {
                var bestFramework = frameworkParam.Choices.Keys
                    .OrderByDescending(k => TemplateEngineService.ParseFrameworkVersion(k))
                    .FirstOrDefault();
                if (bestFramework != null)
                {
                    suggestions.Add(new ParameterSuggestion(
                        "Framework",
                        bestFramework,
                        "NativeAOT works best with the latest framework version for maximum compatibility and performance."));
                }
            }

            var noHttpsParam = template.ParameterDefinitions.FirstOrDefault(p =>
                p.Name.Equals("NoHttps", StringComparison.OrdinalIgnoreCase));
            if (noHttpsParam != null && !userParameters.ContainsKey("NoHttps"))
            {
                suggestions.Add(new ParameterSuggestion(
                    "NoHttps",
                    "true",
                    "NativeAOT deployment is simpler without HTTPS. Consider disabling for containerized scenarios."));
            }
        }

        bool hasAuth = userParameters.Any(p =>
            p.Key.Equals("auth", StringComparison.OrdinalIgnoreCase) &&
            p.Value != null &&
            !p.Value.Equals("None", StringComparison.OrdinalIgnoreCase));

        if (hasAuth)
        {
            if (!userParameters.ContainsKey("NoHttps"))
            {
                var noHttpsParam = template.ParameterDefinitions.FirstOrDefault(p =>
                    p.Name.Equals("NoHttps", StringComparison.OrdinalIgnoreCase));
                if (noHttpsParam != null)
                {
                    suggestions.Add(new ParameterSuggestion(
                        "NoHttps",
                        "false",
                        "Authentication requires HTTPS for secure token transmission. HTTPS is kept enabled."));
                }
            }

            if (!userParameters.ContainsKey("Framework"))
            {
                var frameworkParam = template.ParameterDefinitions.FirstOrDefault(p =>
                    p.Name.Equals("Framework", StringComparison.OrdinalIgnoreCase));
                if (frameworkParam?.Choices != null)
                {
                    var bestFramework = frameworkParam.Choices.Keys
                        .OrderByDescending(k => k)
                        .FirstOrDefault();
                    if (bestFramework != null)
                    {
                        suggestions.Add(new ParameterSuggestion(
                            "Framework",
                            bestFramework,
                            "Latest framework recommended for authentication templates — includes the newest Identity and OAuth improvements."));
                    }
                }
            }
        }

        bool useControllers = userParameters.Any(p =>
            p.Key.Equals("UseControllers", StringComparison.OrdinalIgnoreCase) &&
            p.Value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true);

        if (useControllers && !userParameters.ContainsKey("UseMinimalAPIs"))
        {
            var minimalParam = template.ParameterDefinitions.FirstOrDefault(p =>
                p.Name.Equals("UseMinimalAPIs", StringComparison.OrdinalIgnoreCase));
            if (minimalParam != null)
            {
                suggestions.Add(new ParameterSuggestion(
                    "UseMinimalAPIs",
                    "false",
                    "Controllers and Minimal APIs are mutually exclusive patterns. Disabling Minimal APIs for consistency."));
            }
        }

        bool enableDocker = userParameters.Any(p =>
            (p.Key.Equals("EnableDocker", StringComparison.OrdinalIgnoreCase) ||
             p.Key.Equals("Docker", StringComparison.OrdinalIgnoreCase)) &&
            p.Value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true);

        if (enableDocker && !userParameters.ContainsKey("NoHttps"))
        {
            var noHttpsParam = template.ParameterDefinitions.FirstOrDefault(p =>
                p.Name.Equals("NoHttps", StringComparison.OrdinalIgnoreCase));
            if (noHttpsParam != null && !hasAuth)
            {
                suggestions.Add(new ParameterSuggestion(
                    "NoHttps",
                    "true",
                    "Docker containers typically use a reverse proxy for TLS termination. Disabling HTTPS simplifies the container configuration."));
            }
        }

        return suggestions;
    }

    /// <summary>
    /// Analyze creation effects to produce an AI-readable summary.
    /// </summary>
    public static CreationEffectsAnalysis AnalyzeCreationEffects(ITemplateCreationResult result, ITemplateInfo template)
    {
        var fileChanges = result.CreationEffects?.FileChanges?.ToList() ?? [];

        var filesByDirectory = fileChanges
            .GroupBy(fc => Path.GetDirectoryName(fc.TargetRelativePath) ?? "(root)")
            .OrderBy(g => g.Key)
            .ToDictionary(
                g => g.Key,
                g => g.Select(fc => Path.GetFileName(fc.TargetRelativePath)).ToList());

        var totalFiles = fileChanges.Count;

        var fileExtensions = fileChanges
            .Select(fc => Path.GetExtension(fc.TargetRelativePath))
            .Where(ext => !string.IsNullOrEmpty(ext))
            .GroupBy(ext => ext.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.Count());

        var postActions = result.CreationResult?.PostActions?.Select(pa => new PostActionSummary(
            pa.Description ?? "Unnamed action",
            pa.ActionId.ToString(),
            pa.ManualInstructions ?? string.Empty,
            pa.ContinueOnError)).ToList() ?? [];

        var primaryOutputs = result.CreationResult?.PrimaryOutputs?.Select(po => po.Path).ToList() ?? [];

        string projectType = template.TagsCollection.GetValueOrDefault("type") ?? "unknown";
        string language = template.TagsCollection.GetValueOrDefault("language") ?? "unknown";

        string summary = $"Creates a {language} {projectType} with {totalFiles} file(s) across {filesByDirectory.Count} director{(filesByDirectory.Count == 1 ? "y" : "ies")}.";
        if (postActions.Count > 0)
        {
            summary += $" {postActions.Count} post-action(s) will run after creation.";
        }

        return new CreationEffectsAnalysis(
            summary,
            totalFiles,
            filesByDirectory,
            fileExtensions,
            postActions,
            primaryOutputs);
    }

    /// <summary>
    /// Full instantiation with resolve, prepare, create, and analyze — all in one call.
    /// </summary>
    public async Task<string> InstantiateWithAnalysisAsync(
        string templateName,
        string? name,
        string? outputPath,
        string? parametersJson,
        CancellationToken cancellationToken = default)
    {
        // 1. Resolve template
        var resolveResult = await ResolveTemplateAsync(templateName, cancellationToken).ConfigureAwait(false);
        if (resolveResult.Template == null)
        {
            return SerializeError(resolveResult.Message ?? "Template not found.");
        }

        // 2. Prepare parameters
        var prepResult = PrepareParameters(resolveResult.Template, parametersJson);
        if (!prepResult.IsSuccess)
        {
            return JsonSerializer.Serialize(new
            {
                error = "Parameter validation failed. No files were written.",
                validationErrors = prepResult.ValidationErrors,
                templateName = prepResult.TemplateIdentity,
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        // 3. Create
        string resolvedOutputPath = outputPath ??
            Path.Combine(Environment.CurrentDirectory, name ?? resolveResult.Template.DefaultName ?? "NewProject");

        var result = await _engineService.CreateAsync(
            resolveResult.Template, name, resolvedOutputPath,
            prepResult.Parameters!, cancellationToken).ConfigureAwait(false);

        McpTelemetry.TemplatesCreated.Add(1);

        // 4. Analyze
        var analysis = AnalyzeCreationEffects(result, resolveResult.Template);

        return SerializeCreationWithAnalysis(result, resolveResult, prepResult, analysis);
    }

    /// <summary>
    /// Full dry-run with resolve, prepare, preview, and analyze — all in one call.
    /// </summary>
    public async Task<string> DryRunWithAnalysisAsync(
        string templateName,
        string? name,
        string? outputPath,
        string? parametersJson,
        CancellationToken cancellationToken = default)
    {
        var resolveResult = await ResolveTemplateAsync(templateName, cancellationToken).ConfigureAwait(false);
        if (resolveResult.Template == null)
        {
            return SerializeError(resolveResult.Message ?? "Template not found.");
        }

        var prepResult = PrepareParameters(resolveResult.Template, parametersJson);
        if (!prepResult.IsSuccess)
        {
            return JsonSerializer.Serialize(new
            {
                error = "Parameter validation failed.",
                validationErrors = prepResult.ValidationErrors,
                templateName = prepResult.TemplateIdentity,
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        string resolvedOutputPath = outputPath ??
            Path.Combine(Path.GetTempPath(), name ?? resolveResult.Template.DefaultName ?? "DryRunPreview");

        var result = await _engineService.GetCreationEffectsAsync(
            resolveResult.Template, name, resolvedOutputPath,
            prepResult.Parameters!, cancellationToken).ConfigureAwait(false);

        var analysis = AnalyzeCreationEffects(result, resolveResult.Template);

        return SerializeCreationWithAnalysis(result, resolveResult, prepResult, analysis);
    }

    /// <summary>
    /// Execute a sequence of template operations (project + item templates) in order.
    /// Returns combined results with per-step analysis.
    /// </summary>
    public async Task<string> ComposeAsync(
        IReadOnlyList<ComposeStep> steps,
        CancellationToken cancellationToken = default)
    {
        var stepResults = new List<object>();
        string? projectOutputPath = null;

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];

            // Resolve output path: if step has a target and a project was already created, combine them
            string? effectiveOutputPath = step.OutputPath;
            if (effectiveOutputPath == null && step.Target != null && projectOutputPath != null)
            {
                effectiveOutputPath = Path.Combine(projectOutputPath, step.Target);
            }

            var resolveResult = await ResolveTemplateAsync(step.TemplateName, cancellationToken).ConfigureAwait(false);
            if (resolveResult.Template == null)
            {
                stepResults.Add(new
                {
                    Step = i + 1,
                    step.TemplateName,
                    Error = resolveResult.Message ?? "Template not found.",
                });
                break; // Stop on first failure
            }

            var prepResult = PrepareParameters(resolveResult.Template, step.ParametersJson);
            if (!prepResult.IsSuccess)
            {
                stepResults.Add(new
                {
                    Step = i + 1,
                    step.TemplateName,
                    Error = "Parameter validation failed.",
                    prepResult.ValidationErrors,
                });
                break;
            }

            string resolvedOutputPath = effectiveOutputPath ??
                Path.Combine(Environment.CurrentDirectory, step.Name ?? resolveResult.Template.DefaultName ?? "NewProject");

            // Remember first project's output path for subsequent item templates
            if (i == 0)
            {
                projectOutputPath = resolvedOutputPath;
            }

            var result = await _engineService.CreateAsync(
                resolveResult.Template, step.Name, resolvedOutputPath,
                prepResult.Parameters!, cancellationToken).ConfigureAwait(false);

            McpTelemetry.TemplatesCreated.Add(1);
            var analysis = AnalyzeCreationEffects(result, resolveResult.Template);

            stepResults.Add(new
            {
                Step = i + 1,
                step.TemplateName,
                Status = result.Status.ToString(),
                result.ErrorMessage,
                result.OutputBaseDirectory,
                AutoInstalled = resolveResult.AutoInstallMessage,
                ConstraintWarnings = prepResult.ConstraintWarnings?.Count > 0 ? prepResult.ConstraintWarnings : null,
                AppliedSmartDefaults = prepResult.SmartDefaults?.Count > 0 ? prepResult.SmartDefaults : null,
                Analysis = new
                {
                    analysis.Summary,
                    analysis.TotalFiles,
                    analysis.FileExtensions,
                    PostActionCount = analysis.PostActions.Count,
                },
            });
        }

        return JsonSerializer.Serialize(new
        {
            TotalSteps = steps.Count,
            CompletedSteps = stepResults.Count,
            Steps = stepResults,
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static Dictionary<string, string?> ParseParameters(string? parametersJson)
    {
        return ParseParameters(parametersJson, out _);
    }

    private static Dictionary<string, string?> ParseParameters(string? parametersJson, out string? parseError)
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

    private static string SerializeError(string message)
    {
        return JsonSerializer.Serialize(new { error = message }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string SerializeCreationWithAnalysis(
        ITemplateCreationResult result,
        TemplateResolveResult resolveResult,
        ParameterPreparationResult prepResult,
        CreationEffectsAnalysis analysis)
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
            AutoInstalled = resolveResult.AutoInstallMessage,
            ConstraintWarnings = prepResult.ConstraintWarnings?.Count > 0 ? prepResult.ConstraintWarnings : null,
            AppliedSmartDefaults = prepResult.SmartDefaults?.Count > 0 ? prepResult.SmartDefaults : null,
            Analysis = new
            {
                analysis.Summary,
                analysis.TotalFiles,
                analysis.FilesByDirectory,
                analysis.FileExtensions,
                PostActions = analysis.PostActions.Count > 0 ? analysis.PostActions : null,
            },
            PrimaryOutputs = primaryOutputs,
            PostActions = postActions,
            FileChanges = fileChanges,
        };

        return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
    }
}

/// <summary>Result of resolving a template by name.</summary>
internal record TemplateResolveResult(ITemplateInfo? Template, string? AutoInstallMessage = null)
{
    public string? Message => AutoInstallMessage;
}

/// <summary>Result of preparing parameters (smart defaults + validation + constraints).</summary>
internal class ParameterPreparationResult
{
    public bool IsSuccess { get; private init; }
    public Dictionary<string, string?>? Parameters { get; private init; }
    public Dictionary<string, string>? SmartDefaults { get; private init; }
    public IReadOnlyList<string>? ConstraintWarnings { get; private init; }
    public IReadOnlyList<string>? ValidationErrors { get; private init; }
    public string? TemplateIdentity { get; private init; }

    public static ParameterPreparationResult Succeeded(
        Dictionary<string, string?> parameters,
        Dictionary<string, string> smartDefaults,
        IReadOnlyList<string> constraintWarnings) =>
        new()
        {
            IsSuccess = true,
            Parameters = parameters,
            SmartDefaults = smartDefaults,
            ConstraintWarnings = constraintWarnings,
        };

    public static ParameterPreparationResult Failed(
        IReadOnlyList<string> validationErrors,
        string templateIdentity) =>
        new()
        {
            IsSuccess = false,
            ValidationErrors = validationErrors,
            TemplateIdentity = templateIdentity,
        };
}

/// <summary>A parameter suggestion with its rationale.</summary>
internal record ParameterSuggestion(string ParameterName, string SuggestedValue, string Rationale);

/// <summary>Analysis of what a template creation produces.</summary>
internal record CreationEffectsAnalysis(
    string Summary,
    int TotalFiles,
    Dictionary<string, List<string>> FilesByDirectory,
    Dictionary<string, int> FileExtensions,
    List<PostActionSummary> PostActions,
    List<string> PrimaryOutputs);

/// <summary>Summary of a post-action.</summary>
internal record PostActionSummary(
    string Description,
    string ActionId,
    string ManualInstructions,
    bool ContinueOnError);

/// <summary>A step in a template composition sequence.</summary>
internal class ComposeStep
{
    public required string TemplateName { get; init; }
    public string? Name { get; init; }
    public string? OutputPath { get; init; }
    public string? Target { get; init; }
    public string? ParametersJson { get; init; }
}
