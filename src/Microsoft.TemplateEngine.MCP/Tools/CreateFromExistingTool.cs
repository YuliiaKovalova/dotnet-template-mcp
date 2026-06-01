// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.TemplateEngine.MCP.Analysis;
using ModelContextProtocol.Server;

namespace Microsoft.TemplateEngine.MCP.Tools;

[McpServerToolType]
internal sealed class CreateFromExistingTool
{
    [McpServerTool(Name = "template_create_from_existing")]
    [Description(
        "Analyze an existing .csproj project file and generate a reusable dotnet template that preserves its exact conventions: " +
        "SDK type (e.g., MSTest.Sdk), package references with metadata (PrivateAssets, IncludeAssets), " +
        "properties (OutputType, TreatWarningsAsErrors), Central Package Management, shared compiles, " +
        "and content items. Solves the problem of 'dotnet new' creating generic projects that don't match repo conventions.")]
    public static async Task<string> CreateFromExistingAsync(
        TemplateEngineService engineService,
        McpFeatureFlags featureFlags,
        [Description("Full path to the .csproj file to analyze and use as a template source")] string projectPath,
        [Description("Human-readable name for the generated template (e.g., 'Repo Unit Test Project')")] string templateName,
        [Description("Short name for the template (e.g., 'repo-unittest'). Used with 'dotnet new <shortname>'.")] string? shortName = null,
        [Description("Output directory where the template will be generated. Defaults to a 'templates' folder next to the source project.")] string? outputPath = null,
        [Description("If true, also installs the generated template so it can be used immediately.")] bool install = false,
        CancellationToken cancellationToken = default)
    {
        using var activity = McpTelemetry.StartToolActivity("template_create_from_existing");

        var sw = Stopwatch.StartNew();
        try
        {
            if (!featureFlags.IsToolEnabled("template_create_from_existing"))
            {
                return ToolProfileResponse.DisabledMessage("template_create_from_existing", "Set MCP_TEMPLATE_TOOL_PROFILE=full to generate templates from existing projects.");
            }

            // 1. Analyze the project
            ProjectAnalysis analysis;
            try
            {
                analysis = ProjectAnalyzer.Analyze(projectPath);
            }
            catch (Exception ex)
            {
                McpTelemetry.RecordError(activity, "template_create_from_existing", ex.Message);
                return JsonSerializer.Serialize(new { error = $"Failed to analyze project: {ex.Message}" },
                    new JsonSerializerOptions { WriteIndented = true });
            }

            // 2. Generate the template
            var resolvedOutputPath = outputPath ?? Path.Combine(Path.GetDirectoryName(projectPath)!, "..", "templates");
            string templateRoot;
            try
            {
                templateRoot = TemplateGenerator.Generate(analysis, resolvedOutputPath, templateName, shortName);
            }
            catch (Exception ex)
            {
                McpTelemetry.RecordError(activity, "template_create_from_existing", ex.Message);
                return JsonSerializer.Serialize(new { error = $"Failed to generate template: {ex.Message}" },
                    new JsonSerializerOptions { WriteIndented = true });
            }

            // 3. Build the response with analysis details
            var analysisReport = new
            {
                sourceProject = analysis.SourceProjectPath,
                sdk = analysis.Sdk,
                usesCentralPackageManagement = analysis.UsesCentralPackageManagement,
                properties = analysis.Properties.Select(p => new { p.Name, p.Value, p.Condition }).ToList(),
                packageReferences = analysis.PackageReferences.Select(p => new
                {
                    p.Include,
                    p.Version,
                    p.PrivateAssets,
                    p.IncludeAssets,
                    p.ExcludeAssets,
                }).ToList(),
                projectReferences = analysis.ProjectReferences,
                sharedCompiles = analysis.SharedCompiles.Select(s => new { s.Include, s.Link }).ToList(),
                contentItems = analysis.ContentItems.Select(c => new { c.ItemType, c.Include, c.Remove, c.CopyToOutputDirectory }).ToList(),
                imports = analysis.Imports,
            };

            // 4. Optionally install the template
            string? installMessage = null;
            if (install)
            {
                try
                {
                    var installRequest = new Abstractions.Installer.InstallRequest(templateRoot);
                    var results = await engineService.InstallTemplatePackagesAsync(
                        new[] { installRequest }, cancellationToken).ConfigureAwait(false);

                    var result = results.FirstOrDefault();
                    if (result?.Success == true)
                    {
                        McpTelemetry.PackagesInstalled.Add(1);
                        installMessage = $"Template installed successfully. Use 'dotnet new {shortName ?? templateName.ToLowerInvariant().Replace(" ", "-")}' to create new projects.";
                    }
                    else
                    {
                        McpTelemetry.RecordError(activity, "template_create_from_existing", $"Installation failed: {result?.ErrorMessage ?? "Unknown error"}");
                        installMessage = $"Template generated but installation failed: {result?.ErrorMessage ?? "Unknown error"}. You can install manually with 'dotnet new install {templateRoot}'.";
                    }
                }
                catch (Exception ex)
                {
                    McpTelemetry.RecordError(activity, "template_create_from_existing", ex.Message);
                    installMessage = $"Template generated but installation failed: {ex.Message}. You can install manually with 'dotnet new install {templateRoot}'.";
                }
            }

            McpTelemetry.TemplatesCreated.Add(1);
            activity?.SetTag("mcp.template.source_sdk", analysis.Sdk);
            activity?.SetTag("mcp.template.uses_cpm", analysis.UsesCentralPackageManagement);

            var response = new
            {
                status = "Success",
                templatePath = templateRoot,
                templateName,
                shortName = shortName ?? templateName.ToLowerInvariant().Replace(" ", "-").Replace("_", "-"),
                analysis = analysisReport,
                gapsAddressed = BuildGapsReport(analysis),
                installResult = installMessage,
                nextSteps = install
                    ? new[] { $"Create a new project: dotnet new {shortName ?? templateName.ToLowerInvariant().Replace(" ", "-")}" }
                    : new[]
                    {
                        $"Install the template: dotnet new install {templateRoot}",
                        $"Create a new project: dotnet new {shortName ?? templateName.ToLowerInvariant().Replace(" ", "-")}",
                        "Or use template_install tool with the template path",
                    },
            };

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        finally
        {
            McpTelemetry.RecordDuration("template_create_from_existing", sw.Elapsed.TotalMilliseconds);
        }
    }

    /// <summary>
    /// Build a report of which convention gaps this template addresses vs a generic 'dotnet new' template.
    /// </summary>
    private static object BuildGapsReport(ProjectAnalysis analysis)
    {
        var gaps = new List<object>();

        // Gap 1: SDK
        if (!analysis.Sdk.Equals("Microsoft.NET.Sdk", StringComparison.OrdinalIgnoreCase))
        {
            gaps.Add(new
            {
                gap = "SDK",
                cliDefault = "Microsoft.NET.Sdk",
                repoValue = analysis.Sdk,
                impact = "Missing implicit usings and SDK-specific behavior → compile errors",
            });
        }

        // Gap 2: Analyzer metadata
        var analyzersWithMetadata = analysis.PackageReferences
            .Where(p => p.PrivateAssets != null || p.IncludeAssets != null)
            .ToList();
        if (analyzersWithMetadata.Count > 0)
        {
            gaps.Add(new
            {
                gap = "Analyzer metadata",
                cliDefault = "Flat <PackageReference> without asset metadata",
                repoValue = $"{analyzersWithMetadata.Count} package(s) with PrivateAssets/IncludeAssets",
                impact = "Analyzer packages leak into runtime → warnings/errors",
            });
        }

        // Gap 3: OutputType
        var outputType = analysis.Properties.FirstOrDefault(p => p.Name.Equals("OutputType", StringComparison.OrdinalIgnoreCase));
        if (outputType != null && outputType.Value.Equals("Exe", StringComparison.OrdinalIgnoreCase))
        {
            gaps.Add(new
            {
                gap = "OutputType",
                cliDefault = "Missing (defaults to Library)",
                repoValue = "Exe (required for test runner)",
                impact = "Test runner may fail to discover tests",
            });
        }

        // Gap 4: CPM
        if (analysis.UsesCentralPackageManagement)
        {
            gaps.Add(new
            {
                gap = "Central Package Management",
                cliDefault = "Forces ManagePackageVersionsCentrally=false",
                repoValue = "Inherits from Directory.Packages.props",
                impact = "Version conflicts, breaks CPM flow",
            });
        }

        // Gap 5: Custom build props
        var buildProps = analysis.Properties
            .Where(p => p.Name is "TreatWarningsAsErrors" or "WarningsAsErrors" or "NoWarn"
                or "GenerateErrorForMissingTargetingPacks" or "EnableNETAnalyzers"
                or "AnalysisLevel" or "EnforceCodeStyleInBuild")
            .ToList();
        if (buildProps.Count > 0)
        {
            gaps.Add(new
            {
                gap = "Custom build properties",
                cliDefault = "Ignored",
                repoValue = string.Join(", ", buildProps.Select(p => $"{p.Name}={p.Value}")),
                impact = "Build behavior mismatch between new project and rest of repo",
            });
        }

        // Gap 6: Shared compiles / repo conventions
        if (analysis.SharedCompiles.Count > 0)
        {
            gaps.Add(new
            {
                gap = "Repo conventions (shared compiles)",
                cliDefault = "Generic — no shared code references",
                repoValue = $"{analysis.SharedCompiles.Count} shared compile include(s)",
                impact = "Test project looks 'foreign' in the repo",
            });
        }

        return gaps;
    }
}
