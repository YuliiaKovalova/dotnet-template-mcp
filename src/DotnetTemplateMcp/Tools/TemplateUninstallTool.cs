// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace DotnetTemplateMcp.Tools;

[McpServerToolType]
internal sealed class TemplateUninstallTool
{
    [McpServerTool(Name = "template_uninstall")]
    [Description("Uninstall a template package. After uninstallation, templates from the package are no longer available.")]
    public static async Task<string> UninstallTemplateAsync(
        TemplateEngineService engineService,
        McpFeatureFlags featureFlags,
        [Description("Package identifier to uninstall (e.g., 'Microsoft.DotNet.Web.ProjectTemplates.8.0')")] string packageId,
        CancellationToken cancellationToken = default)
    {
        using var activity = McpTelemetry.StartToolActivity("template_uninstall");
        var sw = Stopwatch.StartNew();
        try
        {
            if (!featureFlags.IsToolEnabled("template_uninstall"))
            {
                return ToolProfileResponse.DisabledMessage("template_uninstall", "Set MCP_TEMPLATE_TOOL_PROFILE=full to manage template packages.");
            }

        var managedPackages = await engineService.GetManagedTemplatePackagesAsync(cancellationToken).ConfigureAwait(false);

        var packageToUninstall = managedPackages.FirstOrDefault(p =>
            p.Identifier.Equals(packageId, StringComparison.OrdinalIgnoreCase));

        if (packageToUninstall == null)
        {
            var installed = managedPackages.Select(p => p.Identifier).ToList();
            McpTelemetry.RecordError(activity, "template_uninstall", $"Package '{packageId}' not found");
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
        finally
        {
            McpTelemetry.RecordDuration("template_uninstall", sw.Elapsed.TotalMilliseconds);
        }
    }
}
