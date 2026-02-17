// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.IDE;
using Microsoft.TemplateEngine.MCP.Host;
using Microsoft.TemplateSearch.Common;
using Microsoft.TemplateSearch.Common.Abstractions;
using ITemplateCreationResult = Microsoft.TemplateEngine.Edge.Template.ITemplateCreationResult;

namespace Microsoft.TemplateEngine.MCP;

/// <summary>
/// Singleton service that manages the template engine Bootstrapper lifecycle
/// and provides a clean API surface for MCP tools to consume.
/// </summary>
internal class TemplateEngineService : IDisposable
{
    private readonly Bootstrapper _bootstrapper;
    private readonly EngineEnvironmentSettings _environmentSettings;
    private readonly ILogger _logger;

    public TemplateEngineService(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<TemplateEngineService>();
        var host = new McpTemplateEngineHost(loggerFactory);
        _environmentSettings = new EngineEnvironmentSettings(host, virtualizeSettings: false);
        _bootstrapper = new Bootstrapper(host, virtualizeConfiguration: false, loadDefaultComponents: true);
    }

    public virtual async Task<IReadOnlyList<ITemplateInfo>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        return await _bootstrapper.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<ITemplateCreationResult> CreateAsync(
        ITemplateInfo template,
        string? name,
        string outputPath,
        IReadOnlyDictionary<string, string?> parameters,
        CancellationToken cancellationToken = default)
    {
        return await _bootstrapper.CreateAsync(template, name, outputPath, parameters, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<ITemplateCreationResult> GetCreationEffectsAsync(
        ITemplateInfo template,
        string? name,
        string outputPath,
        IReadOnlyDictionary<string, string?> parameters,
        CancellationToken cancellationToken = default)
    {
        return await _bootstrapper.GetCreationEffectsAsync(template, name, outputPath, parameters, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<IReadOnlyList<InstallResult>> InstallTemplatePackagesAsync(
        IEnumerable<InstallRequest> installRequests,
        CancellationToken cancellationToken = default)
    {
        return await _bootstrapper.InstallTemplatePackagesAsync(installRequests, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<IReadOnlyList<UninstallResult>> UninstallTemplatePackagesAsync(
        IEnumerable<IManagedTemplatePackage> packages,
        CancellationToken cancellationToken = default)
    {
        return await _bootstrapper.UninstallTemplatePackagesAsync(packages, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<IReadOnlyList<IManagedTemplatePackage>> GetManagedTemplatePackagesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _bootstrapper.GetManagedTemplatePackagesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Search NuGet.org for templates matching filters.
    /// Returns package info + matched templates from the remote search cache.
    /// </summary>
    public virtual async Task<IReadOnlyList<(ITemplatePackageInfo PackageInfo, IReadOnlyList<ITemplateInfo> MatchedTemplates)>> SearchNuGetTemplatesAsync(
        string query,
        string? language,
        string? type,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var coordinator = new TemplateSearchCoordinator(_environmentSettings);

            Func<TemplatePackageSearchData, bool> packFilter = pack =>
                pack.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                pack.Templates.Any(t =>
                    t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    t.ShortNameList.Any(sn => sn.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    t.Classifications.Any(c => c.Contains(query, StringComparison.OrdinalIgnoreCase)));

            Func<TemplatePackageSearchData, IReadOnlyList<ITemplateInfo>> matchingTemplatesFilter = pack =>
            {
                IEnumerable<ITemplateInfo> matches = pack.Templates.Where(t =>
                    t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    t.ShortNameList.Any(sn => sn.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    t.Classifications.Any(c => c.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (t.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
                    t.Identity.Contains(query, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(language))
                {
                    matches = matches.Where(t =>
                        t.TagsCollection.TryGetValue("language", out string? lang) &&
                        lang.Contains(language, StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrWhiteSpace(type))
                {
                    matches = matches.Where(t =>
                        t.TagsCollection.TryGetValue("type", out string? templateType) &&
                        templateType.Contains(type, StringComparison.OrdinalIgnoreCase));
                }

                return matches.ToList();
            };

            var results = await coordinator.SearchAsync(packFilter, matchingTemplatesFilter, cancellationToken).ConfigureAwait(false);

            return results
                .Where(r => r.Success)
                .SelectMany(r => r.SearchHits)
                .Where(hit => hit.MatchedTemplates.Count > 0)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NuGet template search failed");
            return Array.Empty<(ITemplatePackageInfo, IReadOnlyList<ITemplateInfo>)>();
        }
    }

    /// <summary>
    /// Find a local template by identity or short name.
    /// </summary>
    public virtual async Task<ITemplateInfo?> FindTemplateAsync(string templateName, CancellationToken cancellationToken = default)
    {
        var templates = await GetTemplatesAsync(cancellationToken).ConfigureAwait(false);
        return templates.FirstOrDefault(t =>
            t.Identity.Equals(templateName, StringComparison.OrdinalIgnoreCase) ||
            t.ShortNameList.Any(sn => sn.Equals(templateName, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Auto-resolve: search NuGet for a template, install the best match, return the template info.
    /// Returns null if no match found or multiple ambiguous matches.
    /// </summary>
    public virtual async Task<(ITemplateInfo? Template, string? Message)> AutoResolveAndInstallAsync(
        string templateName,
        CancellationToken cancellationToken = default)
    {
        var nugetResults = await SearchNuGetTemplatesAsync(templateName, null, null, cancellationToken).ConfigureAwait(false);

        if (nugetResults.Count == 0)
        {
            return (null, $"Template '{templateName}' not found locally or on NuGet.org.");
        }

        // Find exact match by short name or identity
        var exactMatch = nugetResults.FirstOrDefault(hit =>
            hit.MatchedTemplates.Any(t =>
                t.ShortNameList.Any(sn => sn.Equals(templateName, StringComparison.OrdinalIgnoreCase)) ||
                t.Identity.Equals(templateName, StringComparison.OrdinalIgnoreCase)));

        if (exactMatch.PackageInfo == null)
        {
            // No exact match — return candidates
            var candidates = nugetResults.Take(5).Select(hit => new
            {
                hit.PackageInfo.Name,
                hit.PackageInfo.Version,
                Templates = hit.MatchedTemplates.Select(t => t.ShortNameList.FirstOrDefault() ?? t.Identity).ToList(),
            }).ToList();

            var candidateList = string.Join(", ", candidates.Select(c => $"{c.Name} ({string.Join("/", c.Templates)})"));
            return (null, $"Template '{templateName}' not found. Did you mean one of: {candidateList}? Use template_install to install the package first.");
        }

        // Install the package
        var installRequest = new InstallRequest(exactMatch.PackageInfo.Name, exactMatch.PackageInfo.Version);
        var installResults = await InstallTemplatePackagesAsync(new[] { installRequest }, cancellationToken).ConfigureAwait(false);

        var installResult = installResults.FirstOrDefault();
        if (installResult == null || !installResult.Success)
        {
            return (null, $"Failed to auto-install package '{exactMatch.PackageInfo.Name}': {installResult?.ErrorMessage ?? "Unknown error"}");
        }

        // Now find the template locally
        var template = await FindTemplateAsync(templateName, cancellationToken).ConfigureAwait(false);
        if (template == null)
        {
            return (null, $"Package '{exactMatch.PackageInfo.Name}' was installed but template '{templateName}' was not found in it.");
        }

        return (template, $"Auto-installed package '{exactMatch.PackageInfo.Name}' v{exactMatch.PackageInfo.Version}.");
    }

    /// <summary>
    /// Validate template parameters against the template's parameter definitions.
    /// Returns a list of validation errors (empty if valid).
    /// </summary>
    public static IReadOnlyList<string> ValidateParameters(ITemplateInfo template, IReadOnlyDictionary<string, string?> parameters)
    {
        var errors = new List<string>();

        foreach (var (paramName, paramValue) in parameters)
        {
            var paramDef = template.ParameterDefinitions.FirstOrDefault(p =>
                p.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase));

            if (paramDef == null)
            {
                errors.Add($"Unknown parameter '{paramName}'. Available parameters: {string.Join(", ", template.ParameterDefinitions.Select(p => p.Name))}");
                continue;
            }

            if (paramValue == null)
            {
                continue;
            }

            // Validate choice parameters
            if (paramDef.DataType.Equals("choice", StringComparison.OrdinalIgnoreCase) && paramDef.Choices != null)
            {
                var validChoices = paramDef.Choices.Keys.ToList();
                if (!validChoices.Any(c => c.Equals(paramValue, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"Invalid value '{paramValue}' for parameter '{paramName}'. Valid choices: {string.Join(", ", validChoices)}");
                }
            }

            // Validate bool parameters
            if (paramDef.DataType.Equals("bool", StringComparison.OrdinalIgnoreCase))
            {
                if (!bool.TryParse(paramValue, out _))
                {
                    errors.Add($"Parameter '{paramName}' expects a boolean value (true/false), got '{paramValue}'.");
                }
            }

            // Validate integer parameters
            if (paramDef.DataType.Equals("integer", StringComparison.OrdinalIgnoreCase))
            {
                if (!long.TryParse(paramValue, out _))
                {
                    errors.Add($"Parameter '{paramName}' expects an integer value, got '{paramValue}'.");
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// Check template constraints against the current environment.
    /// Returns a list of warnings (empty if all constraints are met).
    /// </summary>
    public static IReadOnlyList<string> CheckConstraints(ITemplateInfo template)
    {
        var warnings = new List<string>();

        foreach (var constraint in template.Constraints)
        {
            if (constraint.Type.Equals("os", StringComparison.OrdinalIgnoreCase))
            {
                var currentOs = OperatingSystem.IsWindows() ? "Windows" :
                                OperatingSystem.IsMacOS() ? "macOS" :
                                OperatingSystem.IsLinux() ? "Linux" : "Unknown";

                var requiredOs = constraint.Args;
                if (requiredOs != null && !requiredOs.Contains(currentOs, StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"This template has an OS constraint ({requiredOs}) but you are running on {currentOs}.");
                }
            }

            if (constraint.Type.Equals("sdk-version", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"This template has an SDK version constraint: {constraint.Args}. Verify your SDK version meets this requirement.");
            }

            if (constraint.Type.Equals("workload", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"This template requires workload(s): {constraint.Args}. Ensure they are installed via 'dotnet workload install'.");
            }
        }

        return warnings;
    }

    public void Dispose()
    {
        _bootstrapper.Dispose();
    }
}
