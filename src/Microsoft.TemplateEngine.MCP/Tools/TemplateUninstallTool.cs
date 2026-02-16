// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Microsoft.TemplateEngine.MCP.Tools;

[McpServerToolType]
internal sealed class TemplateUninstallTool
{
    [McpServerTool(Name = "template_uninstall")]
    [Description("Uninstall a template package. After uninstallation, templates from the package are no longer available.")]
    public static async Task<string> UninstallTemplateAsync(
        TemplateEngineService engineService,
        [Description("Package identifier to uninstall (e.g., 'Microsoft.DotNet.Web.ProjectTemplates.8.0')")] string packageId,
        CancellationToken cancellationToken = default)
    {
        var managedPackages = await engineService.GetManagedTemplatePackagesAsync(cancellationToken).ConfigureAwait(false);

        var packageToUninstall = managedPackages.FirstOrDefault(p =>
            p.Identifier.Equals(packageId, StringComparison.OrdinalIgnoreCase));

        if (packageToUninstall == null)
        {
            var installed = managedPackages.Select(p => p.Identifier).ToList();
            return JsonSerializer.Serialize(
                new
                {
                    error = $"Package '{packageId}' not found among installed packages.",
                    installedPackages = installed,
                },
                new JsonSerializerOptions { WriteIndented = true });
        }

        var results = await engineService.UninstallTemplatePackagesAsync(
            new[] { packageToUninstall },
            cancellationToken).ConfigureAwait(false);

        var response = results.Select(r => new
        {
            r.Success,
            r.ErrorMessage,
            PackageIdentifier = r.TemplatePackage?.Identifier,
        }).ToList();

        return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
    }
}
