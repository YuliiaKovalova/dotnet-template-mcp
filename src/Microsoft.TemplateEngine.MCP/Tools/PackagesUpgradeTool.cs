// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.TemplateEngine.MCP.PostCreation;
using Microsoft.TemplateEngine.MCP.Security;
using ModelContextProtocol.Server;

namespace Microsoft.TemplateEngine.MCP.Tools;

[McpServerToolType]
internal sealed class PackagesUpgradeTool
{
    [McpServerTool(Name = "packages_upgrade")]
    [Description("Stop hand-editing stale NuGet versions. Scan a .csproj, .sln/.slnx, or directory for outdated PackageReference/PackageVersion entries and report (or apply) upgrades to the latest stable versions. Uses the feeds configured in the repository's NuGet.config. CPM-aware: updates Directory.Packages.props when present. Reports only by default; pass apply=true to write changes.")]
    public static async Task<string> UpgradePackagesAsync(
        PackageUpgradeService upgradeService,
        McpFeatureFlags featureFlags,
        [Description("Path to a .csproj, .sln/.slnx file, or a directory to scan. For a solution or directory, all .csproj files beneath it are scanned. Defaults to the workspace root.")] string? path = null,
        [Description("When true, writes the upgraded versions to disk. When false (default), only reports what would change.")] bool apply = false,
        CancellationToken cancellationToken = default)
    {
        using var activity = McpTelemetry.StartToolActivity("packages_upgrade");
        var sw = Stopwatch.StartNew();
        try
        {
            if (!featureFlags.IsToolEnabled("packages_upgrade"))
            {
                return ToolProfileResponse.DisabledMessage("packages_upgrade", "Set MCP_TEMPLATE_TOOL_PROFILE=full to upgrade NuGet packages.");
            }

            var resolvedPath = path ?? featureFlags.WorkspaceRoot;

            // Only guard the mutating mode — reporting on a path outside the workspace is harmless.
            if (apply)
            {
                var rejection = WorkspaceGuard.Validate(resolvedPath, featureFlags);
                if (rejection != null)
                {
                    McpTelemetry.RecordError(activity, "packages_upgrade", rejection);
                    return WorkspaceGuard.PathRejectedError(rejection);
                }
            }

            if (!File.Exists(resolvedPath) && !Directory.Exists(resolvedPath))
            {
                McpTelemetry.RecordError(activity, "packages_upgrade", "path not found");
                return JsonSerializer.Serialize(new
                {
                    error = $"Path not found: {resolvedPath}",
                    hint = "Provide a valid .csproj, .sln/.slnx file, or a directory path.",
                }, SerializerOptions);
            }

            var report = await upgradeService.AnalyzeAsync(resolvedPath, apply, cancellationToken).ConfigureAwait(false);

            if (report.ProjectsScanned == 0)
            {
                return JsonSerializer.Serialize(new
                {
                    path = resolvedPath,
                    projectsScanned = 0,
                    message = "No .csproj files found to analyze.",
                    hint = "Point the tool at a project, solution, or a folder containing one.",
                }, SerializerOptions);
            }

            return JsonSerializer.Serialize(new
            {
                path = resolvedPath,
                projectsScanned = report.ProjectsScanned,
                cpmDetected = report.CpmDetected,
                directoryPackagesProps = report.DirectoryPackagesPropsPath,
                applied = report.Applied,
                upgradeCount = report.Upgrades.Count,
                upToDateCount = report.UpToDateCount,
                upgrades = report.Upgrades.Select(u => new
                {
                    package = u.PackageName,
                    current = u.CurrentVersion,
                    latest = u.LatestVersion,
                    file = u.File,
                    location = u.Location,
                }),
                unresolvedPackages = report.UnresolvedPackages,
                message = report.Upgrades.Count == 0
                    ? "All resolvable packages are already up to date."
                    : report.Applied
                        ? $"Applied {report.Upgrades.Count} upgrade(s)."
                        : $"Found {report.Upgrades.Count} available upgrade(s). Re-run with apply=true to write them.",
            }, SerializerOptions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            McpTelemetry.RecordError(activity, "packages_upgrade", ex.Message);
            return JsonSerializer.Serialize(new
            {
                error = $"Package upgrade failed: {ex.Message}",
                hint = "Ensure the path points to valid project files and that the feeds in your NuGet.config are reachable and authenticated.",
            }, SerializerOptions);
        }
        finally
        {
            McpTelemetry.RecordDuration("packages_upgrade", sw.Elapsed.TotalMilliseconds);
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
}
