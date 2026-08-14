// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.TemplateEngine.Abstractions.Installer;
using ModelContextProtocol.Server;

namespace DotnetTemplateMcp.Tools;

[McpServerToolType]
internal sealed class TemplateInstallTool
{
    [McpServerTool(Name = "template_install")]
    [Description("Install a template package from NuGet or a local folder/nupkg path. Idempotent: skips if already installed at the same version, offers upgrade if older. Returns install status AND full metadata for all templates in the package.")]
    public static async Task<string> InstallTemplateAsync(
        TemplateEngineService engineService,
        McpFeatureFlags featureFlags,
        [Description("NuGet package ID (e.g., 'Microsoft.DotNet.Web.ProjectTemplates.8.0') or local path to a folder or .nupkg file")] string packageId,
        [Description("Optional package version (e.g., '8.0.0'). If not specified, the latest version is used.")] string? version = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = McpTelemetry.StartToolActivity("template_install");
        var sw = Stopwatch.StartNew();
        try
        {
            if (!featureFlags.IsToolEnabled("template_install"))
            {
                return ToolProfileResponse.DisabledMessage("template_install", "Use template_instantiate which auto-installs missing templates.");
            }

        // Check if already installed (idempotent)
        var existingPackages = await engineService.GetManagedTemplatePackagesAsync(cancellationToken).ConfigureAwait(false);
        var existingPackage = existingPackages.FirstOrDefault(p =>
            p.Identifier.Equals(packageId, StringComparison.OrdinalIgnoreCase));

        if (existingPackage != null)
        {
            var existingVersion = existingPackage.Version;

            // Same version requested (or no version specified) — skip
            if (version == null || (existingVersion != null && VersionsEqual(existingVersion, version)))
            {
                var templates = await GetTemplatesForPackageAsync(engineService, packageId, cancellationToken).ConfigureAwait(false);
                return JsonSerializer.Serialize(new
                {
                    Success = true,
                    AlreadyInstalled = true,
                    PackageIdentifier = packageId,
                    PackageVersion = existingVersion,
                    InstalledTemplates = templates,
                    Message = $"Package '{packageId}' v{existingVersion} is already installed. {templates.Count} template(s) available.",
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            // Different version — inform about upgrade
            return JsonSerializer.Serialize(new
            {
                Success = true,
                AlreadyInstalled = true,
                UpgradeAvailable = true,
                PackageIdentifier = packageId,
                CurrentVersion = existingVersion,
                RequestedVersion = version,
                Message = $"Package '{packageId}' is already installed at v{existingVersion}. Requested v{version}. Use template_uninstall first, then template_install to upgrade.",
                InstalledTemplates = await GetTemplatesForPackageAsync(engineService, packageId, cancellationToken).ConfigureAwait(false),
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        var installRequest = new InstallRequest(packageId, version);
        var results = await engineService.InstallTemplatePackagesAsync(new[] { installRequest }, cancellationToken).ConfigureAwait(false);

        var installResult = results.FirstOrDefault();
        if (installResult == null)
        {
            return JsonSerializer.Serialize(new { error = "No install result returned." });
        }

        if (!installResult.Success)
        {
            return JsonSerializer.Serialize(new
            {
                installResult.Success,
                installResult.ErrorMessage,
                installResult.InstallRequest.PackageIdentifier,
                PackageVersion = installResult.InstallRequest.Version,
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        var installedTemplates = await GetTemplatesForPackageAsync(engineService, packageId, cancellationToken).ConfigureAwait(false);

        var response = new
        {
            installResult.Success,
            installResult.InstallRequest.PackageIdentifier,
            PackageVersion = installResult.InstallRequest.Version,
            InstalledTemplates = installedTemplates,
            Message = $"Successfully installed '{packageId}'. {installedTemplates.Count} template(s) now available.",
        };

        McpTelemetry.PackagesInstalled.Add(1);
        return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        finally
        {
            McpTelemetry.RecordDuration("template_install", sw.Elapsed.TotalMilliseconds);
        }
    }

    /// <summary>
    /// Compare two NuGet version strings semantically (so "1.0" == "1.0.0" and build metadata is ignored),
    /// falling back to an ordinal comparison for values that aren't valid NuGet versions.
    /// </summary>
    private static bool VersionsEqual(string a, string b)
    {
        if (NuGet.Versioning.NuGetVersion.TryParse(a, out var va) &&
            NuGet.Versioning.NuGetVersion.TryParse(b, out var vb))
        {
            return va.Equals(vb);
        }

        return a.Equals(b, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<List<object>> GetTemplatesForPackageAsync(
        TemplateEngineService engineService,
        string packageId,
        CancellationToken cancellationToken)
    {
        var allTemplates = await engineService.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);
        var packages = await engineService.GetManagedTemplatePackagesAsync(cancellationToken).ConfigureAwait(false);
        var installedPackage = packages.FirstOrDefault(p =>
            p.Identifier.Equals(packageId, StringComparison.OrdinalIgnoreCase));

        var installedTemplates = new List<object>();
        if (installedPackage != null)
        {
            var packageTemplates = allTemplates.Where(t =>
                t.MountPointUri.Contains(installedPackage.Identifier, StringComparison.OrdinalIgnoreCase) ||
                t.MountPointUri.Contains(packageId, StringComparison.OrdinalIgnoreCase));

            foreach (var t in packageTemplates)
            {
                installedTemplates.Add(new
                {
                    t.Identity,
                    ShortNames = t.ShortNameList,
                    t.Name,
                    t.Description,
                    t.Author,
                    t.Classifications,
                    Language = t.TagsCollection.GetValueOrDefault("language"),
                    Type = t.TagsCollection.GetValueOrDefault("type"),
                    ParameterCount = t.ParameterDefinitions.Count,
                });
            }
        }

        // If package matching didn't find templates, return empty — don't return all templates
        return installedTemplates;
    }
}
