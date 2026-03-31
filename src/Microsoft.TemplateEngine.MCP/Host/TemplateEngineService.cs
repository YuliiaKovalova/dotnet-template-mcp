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
    private readonly SemaphoreSlim _sdkInstallSemaphore = new(1, 1);
    private bool _sdkTemplatesInstalled;

    public TemplateEngineService(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<TemplateEngineService>();
        var host = new McpTemplateEngineHost(loggerFactory);
        _environmentSettings = new EngineEnvironmentSettings(host, virtualizeSettings: false);
        _bootstrapper = new Bootstrapper(host, virtualizeConfiguration: false, loadDefaultComponents: true);
    }

    /// <summary>
    /// Ensures SDK-bundled template packages are installed in the MCP host.
    /// Scans the .NET SDK templates directory for nupkg files and installs any that are missing.
    /// </summary>
    private async Task EnsureSdkTemplatesInstalledAsync(CancellationToken cancellationToken)
    {
        if (_sdkTemplatesInstalled)
        {
            return;
        }

        await _sdkInstallSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring the lock
            if (_sdkTemplatesInstalled)
            {
                return;
            }

            try
            {
            var sdkTemplatePaths = DiscoverSdkTemplatePackages();
            if (sdkTemplatePaths.Count == 0)
            {
                _logger.LogDebug("No SDK template packages found.");
                return;
            }

            // Get already-installed packages to avoid reinstalling
            var installedPackages = await GetManagedTemplatePackagesAsync(cancellationToken).ConfigureAwait(false);
            var installedIds = new HashSet<string>(
                installedPackages
                    .Select(p => p.Identifier)
                    .Where(id => id != null),
                StringComparer.OrdinalIgnoreCase);

            var toInstall = new List<InstallRequest>();
            foreach (var nupkgPath in sdkTemplatePaths)
            {
                // Check if this package is already installed (by path or package name)
                var fileName = Path.GetFileNameWithoutExtension(nupkgPath);
                if (installedIds.Any(id => id.Contains(fileName, StringComparison.OrdinalIgnoreCase) ||
                                           nupkgPath.Contains(id, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                toInstall.Add(new InstallRequest(nupkgPath));
            }

            if (toInstall.Count > 0)
            {
                _logger.LogInformation("Installing {Count} SDK template package(s)...", toInstall.Count);
                var results = await _bootstrapper.InstallTemplatePackagesAsync(toInstall, cancellationToken: cancellationToken).ConfigureAwait(false);
                foreach (var result in results)
                {
                    if (result.Success)
                    {
                        _logger.LogInformation("Installed SDK templates from {Package}", result.InstallRequest.PackageIdentifier);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to install {Package}: {Error}", result.InstallRequest.PackageIdentifier, result.ErrorMessage);
                    }
                }
            }

            // Only mark as installed on success — allows retry on failure
            _sdkTemplatesInstalled = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to discover SDK template packages. SDK templates may not be available. Will retry on next call.");
        }
        }
        finally
        {
            _sdkInstallSemaphore.Release();
        }
    }

    /// <summary>
    /// Discover .nupkg files from the .NET SDK templates directory.
    /// SDK templates are stored at {dotnet_root}/templates/{version}/*.nupkg
    /// </summary>
    internal static IReadOnlyList<string> DiscoverSdkTemplatePackages()
    {
        var results = new List<string>();

        try
        {
            // Find the dotnet root directory
            var dotnetRoot = GetDotnetRoot();
            if (dotnetRoot == null)
            {
                return results;
            }

            var templatesRoot = Path.Combine(dotnetRoot, "templates");
            if (!Directory.Exists(templatesRoot))
            {
                return results;
            }

            // Get the latest version directory
            var versionDirs = Directory.GetDirectories(templatesRoot)
                .OrderByDescending(d => Path.GetFileName(d))
                .ToList();

            foreach (var versionDir in versionDirs)
            {
                var nupkgs = Directory.GetFiles(versionDir, "*.nupkg");
                if (nupkgs.Length > 0)
                {
                    // Deduplicate: when multiple versions of the same package exist,
                    // keep only the highest version (last alphabetically)
                    var byBaseName = nupkgs
                        .GroupBy(path => ExtractPackageBaseName(Path.GetFileName(path)), StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.OrderByDescending(p => p).First());

                    results.AddRange(byBaseName);
                    break; // Use only the latest version directory
                }
            }
        }
        catch
        {
            // Silently ignore errors in discovery
        }

        return results;
    }

    private static string? GetDotnetRoot()
    {
        // Try DOTNET_ROOT env var first
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot) && Directory.Exists(dotnetRoot))
        {
            return dotnetRoot;
        }

        // Try to find dotnet on PATH
        try
        {
            var dotnetPath = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet")
                : "/usr/share/dotnet";

            if (Directory.Exists(dotnetPath))
            {
                return dotnetPath;
            }
        }
        catch
        {
            // Ignore
        }

        return null;
    }

    /// <summary>
    /// Extract the base package name from a nupkg filename, stripping version numbers.
    /// e.g., "microsoft.dotnet.common.projecttemplates.9.0.9.0.311.nupkg" → "microsoft.dotnet.common.projecttemplates"
    /// </summary>
    private static string ExtractPackageBaseName(string fileName)
    {
        // Remove .nupkg extension
        var name = Path.GetFileNameWithoutExtension(fileName);

        // Split on dots and take segments until we hit a numeric segment
        var parts = name.Split('.');
        var baseParts = new List<string>();
        foreach (var part in parts)
        {
            if (int.TryParse(part, out _))
            {
                break;
            }

            baseParts.Add(part);
        }

        return baseParts.Count > 0 ? string.Join(".", baseParts) : name;
    }

    public virtual async Task<IReadOnlyList<ITemplateInfo>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSdkTemplatesInstalledAsync(cancellationToken).ConfigureAwait(false);
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
            if (paramDef.DataType != null && paramDef.DataType.Equals("choice", StringComparison.OrdinalIgnoreCase) && paramDef.Choices != null)
            {
                var validChoices = paramDef.Choices.Keys.ToList();
                if (!validChoices.Any(c => c.Equals(paramValue, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"Invalid value '{paramValue}' for parameter '{paramName}'. Valid choices: {string.Join(", ", validChoices)}");
                }
            }

            // Validate bool parameters
            if (paramDef.DataType != null && paramDef.DataType.Equals("bool", StringComparison.OrdinalIgnoreCase))
            {
                if (!bool.TryParse(paramValue, out _))
                {
                    errors.Add($"Parameter '{paramName}' expects a boolean value (true/false), got '{paramValue}'.");
                }
            }

            // Validate integer parameters
            if (paramDef.DataType != null && paramDef.DataType.Equals("int", StringComparison.OrdinalIgnoreCase))
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
    /// Apply smart default values based on cross-parameter relationships.
    /// For example, if EnableAot=true, suggest the latest AOT-compatible framework.
    /// Returns a dictionary of suggested parameter values (only for params not already specified).
    /// </summary>
    public static Dictionary<string, string> SuggestSmartDefaults(ITemplateInfo template, IReadOnlyDictionary<string, string?> userParameters)
    {
        var suggestions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Rule: if AOT-related parameters are enabled, prefer latest framework
        bool aotEnabled = userParameters.Any(p =>
            (p.Key.Equals("EnableAot", StringComparison.OrdinalIgnoreCase) ||
             p.Key.Equals("PublishAot", StringComparison.OrdinalIgnoreCase) ||
             p.Key.Equals("nativeAot", StringComparison.OrdinalIgnoreCase)) &&
            p.Value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true);

        if (aotEnabled)
        {
            var frameworkParam = template.ParameterDefinitions.FirstOrDefault(p =>
                p.Name.Equals("Framework", StringComparison.OrdinalIgnoreCase));
            if (frameworkParam?.Choices != null &&
                !userParameters.ContainsKey("Framework"))
            {
                // Pick the highest available framework (AOT works best with latest)
                // Use version-aware sorting: extract numeric version from "netX.Y" to avoid
                // lexicographic errors (e.g., "net9.0" > "net10.0" alphabetically)
                var bestFramework = frameworkParam.Choices.Keys
                    .OrderByDescending(k => ParseFrameworkVersion(k))
                    .FirstOrDefault();
                if (bestFramework != null)
                {
                    suggestions["Framework"] = bestFramework;
                }
            }
        }

        // Rule: if auth is set to a non-None value, ensure HTTPS is not disabled
        bool hasAuth = userParameters.Any(p =>
            p.Key.Equals("auth", StringComparison.OrdinalIgnoreCase) &&
            p.Value != null &&
            !p.Value.Equals("None", StringComparison.OrdinalIgnoreCase));

        if (hasAuth && !userParameters.ContainsKey("NoHttps"))
        {
            var noHttpsParam = template.ParameterDefinitions.FirstOrDefault(p =>
                p.Name.Equals("NoHttps", StringComparison.OrdinalIgnoreCase));
            if (noHttpsParam != null)
            {
                suggestions["NoHttps"] = "false";
            }
        }

        // Rule: if UseControllers=true, set UseMinimalAPIs=false (mutually exclusive)
        bool useControllers = userParameters.Any(p =>
            p.Key.Equals("UseControllers", StringComparison.OrdinalIgnoreCase) &&
            p.Value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true);

        if (useControllers && !userParameters.ContainsKey("UseMinimalAPIs"))
        {
            var minimalParam = template.ParameterDefinitions.FirstOrDefault(p =>
                p.Name.Equals("UseMinimalAPIs", StringComparison.OrdinalIgnoreCase));
            if (minimalParam != null)
            {
                suggestions["UseMinimalAPIs"] = "false";
            }
        }

        // Rule: if UseProgramMain=true, it's an explicit style preference — no additional defaults needed
        // Rule: if Framework is set but not in choices, warn via validation (handled by ValidateParameters)

        return suggestions;
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

    /// <summary>
    /// Parse a framework moniker like "net8.0" or "net10.0" into a comparable version.
    /// Falls back to Version(0, 0) for unrecognized formats to keep them at the bottom.
    /// </summary>
    internal static Version ParseFrameworkVersion(string framework)
    {
        // Strip "net" prefix and try to parse as a version
        if (framework.StartsWith("net", StringComparison.OrdinalIgnoreCase))
        {
            var versionPart = framework.Substring(3);
            if (Version.TryParse(versionPart, out var version))
            {
                return version;
            }
        }

        return new Version(0, 0);
    }
}
