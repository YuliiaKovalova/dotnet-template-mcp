// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using ModelContextProtocol.Server;

namespace DotnetTemplateMcp.Tools;

[McpServerToolType]
internal sealed class SolutionAnalyzeTool
{
    [McpServerTool(Name = "solution_analyze")]
    [Description("Analyze a solution or workspace directory. Returns project structure, target frameworks, CPM status, and NuGet configuration — essential context for template creation decisions.")]
    public static async Task<string> AnalyzeSolutionAsync(
        McpFeatureFlags featureFlags,
        [Description("Path to a .sln/.slnx file or a directory to scan. Defaults to current directory.")] string? path = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = McpTelemetry.StartToolActivity("solution_analyze");
        var sw = Stopwatch.StartNew();
        try
        {
            if (!featureFlags.IsToolEnabled("solution_analyze"))
            {
                return ToolProfileResponse.DisabledMessage("solution_analyze", "Set MCP_TEMPLATE_TOOL_PROFILE=full to analyze solution structure.");
            }

            string resolvedPath = path ?? Environment.CurrentDirectory;

            // Find .sln file
            string? slnFile = null;
            if (File.Exists(resolvedPath) && (resolvedPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                                               resolvedPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)))
            {
                slnFile = resolvedPath;
                resolvedPath = Path.GetDirectoryName(resolvedPath)!;
            }
            else if (Directory.Exists(resolvedPath))
            {
                slnFile = Directory.GetFiles(resolvedPath, "*.sln").FirstOrDefault()
                       ?? Directory.GetFiles(resolvedPath, "*.slnx").FirstOrDefault();
            }
            else
            {
                return McpErrorResponse.Serialize("not_found",
                    $"Path not found: {resolvedPath}",
                    "Provide a valid directory or .sln file path.");
            }

            // Parse solution projects
            var projects = new List<object>();
            if (slnFile != null)
            {
                projects = await ParseSolutionProjectsAsync(slnFile, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // No solution file — scan for .csproj files
                var csprojFiles = Directory.GetFiles(resolvedPath, "*.csproj", SearchOption.AllDirectories);
                foreach (var csproj in csprojFiles.Take(50)) // Cap at 50 to avoid huge responses
                {
                    var info = ParseProjectFile(csproj, resolvedPath);
                    if (info != null) projects.Add(info);
                }
            }

            // Detect CPM
            var (cpmDetected, cpmPath) = DetectCpm(resolvedPath);

            // Check for global.json
            var globalJson = FindGlobalJson(resolvedPath);

            // Check for NuGet.config
            var nugetConfig = File.Exists(Path.Combine(resolvedPath, "NuGet.config"))
                           || File.Exists(Path.Combine(resolvedPath, "nuget.config"));

            // Check for Directory.Build.props (non-CPM)
            var dirBuildProps = File.Exists(Path.Combine(resolvedPath, "Directory.Build.props"));

            var result = new
            {
                WorkspacePath = resolvedPath,
                SolutionFile = slnFile != null ? Path.GetFileName(slnFile) : null,
                ProjectCount = projects.Count,
                Projects = projects,
                CentralPackageManagement = new
                {
                    Detected = cpmDetected,
                    DirectoryPackagesPropsPath = cpmPath,
                },
                GlobalJson = globalJson,
                HasNuGetConfig = nugetConfig,
                HasDirectoryBuildProps = dirBuildProps,
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            McpTelemetry.RecordError(activity, "solution_analyze", ex.Message);
            return McpErrorResponse.Serialize("analysis_failed",
                $"Failed to analyze workspace: {ex.Message}",
                "Ensure the path is accessible and contains valid .NET project files.");
        }
        finally
        {
            McpTelemetry.RecordDuration("solution_analyze", sw.Elapsed.TotalMilliseconds);
        }
    }

    private static async Task<List<object>> ParseSolutionProjectsAsync(string slnFile, CancellationToken cancellationToken)
    {
        var projects = new List<object>();
        var slnDir = Path.GetDirectoryName(slnFile)!;

        var lines = await File.ReadAllLinesAsync(slnFile, cancellationToken).ConfigureAwait(false);
        foreach (var line in lines)
        {
            if (!line.StartsWith("Project(", StringComparison.Ordinal)) continue;

            // Parse: Project("{GUID}") = "Name", "Path", "{GUID}"
            var parts = line.Split('"');
            if (parts.Length < 6) continue;

            string projectName = parts[3];
            string projectRelPath = parts[5].Replace('\\', Path.DirectorySeparatorChar);

            if (!projectRelPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) &&
                !projectRelPath.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) &&
                !projectRelPath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string projectFullPath = Path.Combine(slnDir, projectRelPath);
            if (!File.Exists(projectFullPath)) continue;

            var info = ParseProjectFile(projectFullPath, slnDir);
            if (info != null) projects.Add(info);
        }

        return projects;
    }

    private static object? ParseProjectFile(string projectPath, string rootDir)
    {
        try
        {
            var doc = XDocument.Load(projectPath);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            var sdk = doc.Root?.Attribute("Sdk")?.Value;
            var targetFramework = doc.Root?.Descendants(ns + "TargetFramework").FirstOrDefault()?.Value;
            var targetFrameworks = doc.Root?.Descendants(ns + "TargetFrameworks").FirstOrDefault()?.Value;
            var outputType = doc.Root?.Descendants(ns + "OutputType").FirstOrDefault()?.Value;
            var isTestProject = doc.Root?.Descendants(ns + "IsTestProject").FirstOrDefault()?.Value;
            var isPackable = doc.Root?.Descendants(ns + "IsPackable").FirstOrDefault()?.Value;

            var packageRefs = doc.Root?.Descendants(ns + "PackageReference")
                .Select(pr => pr.Attribute("Include")?.Value)
                .Where(n => n != null)
                .ToList() ?? [];

            string relativePath = Path.GetRelativePath(rootDir, projectPath);

            return new
            {
                Name = Path.GetFileNameWithoutExtension(projectPath),
                Path = relativePath,
                Sdk = sdk,
                TargetFramework = targetFrameworks ?? targetFramework,
                OutputType = outputType,
                IsTestProject = string.Equals(isTestProject, "true", StringComparison.OrdinalIgnoreCase),
                IsPackable = isPackable != null ? string.Equals(isPackable, "true", StringComparison.OrdinalIgnoreCase) : (bool?)null,
                PackageReferenceCount = packageRefs.Count,
                PackageReferences = packageRefs.Take(20).ToList(), // Cap for readability
            };
        }
        catch
        {
            return null;
        }
    }

    private static (bool detected, string? path) DetectCpm(string directory)
    {
        var current = directory;
        while (current != null)
        {
            var propsFile = Path.Combine(current, "Directory.Packages.props");
            if (File.Exists(propsFile))
            {
                try
                {
                    // Parse the actual property value rather than substring-matching the file text
                    // (which false-positives on comments or unrelated "true" occurrences).
                    var doc = XDocument.Load(propsFile);
                    var enabled = doc.Descendants()
                        .Where(e => e.Name.LocalName.Equals("ManagePackageVersionsCentrally", StringComparison.OrdinalIgnoreCase))
                        .Select(e => e.Value.Trim())
                        .Any(v => v.Equals("true", StringComparison.OrdinalIgnoreCase));

                    if (enabled)
                    {
                        return (true, propsFile);
                    }
                }
                catch { }
            }
            current = Path.GetDirectoryName(current);
        }
        return (false, null);
    }

    private static object? FindGlobalJson(string directory)
    {
        var globalJsonPath = Path.Combine(directory, "global.json");
        if (!File.Exists(globalJsonPath)) return null;

        try
        {
            var content = File.ReadAllText(globalJsonPath);
            var parsed = JsonSerializer.Deserialize<JsonElement>(content);

            string? sdkVersion = null;
            string? rollForward = null;
            if (parsed.TryGetProperty("sdk", out var sdk))
            {
                if (sdk.TryGetProperty("version", out var ver)) sdkVersion = ver.GetString();
                if (sdk.TryGetProperty("rollForward", out var rf)) rollForward = rf.GetString();
            }

            return new { SdkVersion = sdkVersion, RollForward = rollForward };
        }
        catch
        {
            return new { SdkVersion = (string?)null, RollForward = (string?)null };
        }
    }
}
