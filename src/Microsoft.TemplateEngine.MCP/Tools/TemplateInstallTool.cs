// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Text.Json;
using Microsoft.TemplateEngine.Abstractions.Installer;
using ModelContextProtocol.Server;

namespace Microsoft.TemplateEngine.MCP.Tools;

[McpServerToolType]
internal sealed class TemplateInstallTool
{
    [McpServerTool(Name = "template_install")]
    [Description("Install a template package from NuGet or a local folder/nupkg path. Returns install status AND full metadata for all templates in the package, so you can immediately proceed to instantiation.")]
    public static async Task<string> InstallTemplateAsync(
        TemplateEngineService engineService,
        [Description("NuGet package ID (e.g., 'Microsoft.DotNet.Web.ProjectTemplates.8.0') or local path to a folder or .nupkg file")] string packageId,
        [Description("Optional package version (e.g., '8.0.0'). If not specified, the latest version is used.")] string? version = null,
        CancellationToken cancellationToken = default)
    {
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

        // After successful install, discover the installed templates and return their metadata
        var allTemplates = await engineService.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);

        // Find templates from the newly installed package by matching the managed package
        var packages = await engineService.GetManagedTemplatePackagesAsync(cancellationToken).ConfigureAwait(false);
        var installedPackage = packages.FirstOrDefault(p =>
            p.Identifier.Contains(packageId, StringComparison.OrdinalIgnoreCase));

        var installedTemplates = new List<object>();
        if (installedPackage != null)
        {
            // Get templates from this specific package via MountPointUri matching
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

        // If MountPointUri matching didn't work, compare template lists before/after
        if (installedTemplates.Count == 0)
        {
            foreach (var t in allTemplates)
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

        var response = new
        {
            installResult.Success,
            installResult.InstallRequest.PackageIdentifier,
            PackageVersion = installResult.InstallRequest.Version,
            InstalledTemplates = installedTemplates,
            Message = $"Successfully installed '{packageId}'. {installedTemplates.Count} template(s) now available.",
        };

        return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
    }
}
