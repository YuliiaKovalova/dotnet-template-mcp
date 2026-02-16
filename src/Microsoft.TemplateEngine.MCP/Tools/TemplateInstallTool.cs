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
    [Description("Install a template package from NuGet or a local folder/nupkg path. After installation, templates from the package become available for use.")]
    public static async Task<string> InstallTemplateAsync(
        TemplateEngineService engineService,
        [Description("NuGet package ID (e.g., 'Microsoft.DotNet.Web.ProjectTemplates.8.0') or local path to a folder or .nupkg file")] string packageId,
        [Description("Optional package version (e.g., '8.0.0'). If not specified, the latest version is used.")] string? version = null,
        CancellationToken cancellationToken = default)
    {
        var installRequest = new InstallRequest(packageId, version);
        var results = await engineService.InstallTemplatePackagesAsync(new[] { installRequest }, cancellationToken).ConfigureAwait(false);

        var response = results.Select(r => new
        {
            r.Success,
            r.ErrorMessage,
            r.InstallRequest.PackageIdentifier,
            PackageVersion = r.InstallRequest.Version,
        }).ToList();

        return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
    }
}
