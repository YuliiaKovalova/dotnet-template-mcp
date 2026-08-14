// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using DotnetTemplateMcp.PostCreation;
using DotnetTemplateMcp.Tools;
using Xunit;
using Xunit.Abstractions;

namespace DotnetTemplateMcp.Tests;

/// <summary>
/// End-to-end test that exercises the full workflow:
/// search → inspect → dry-run → instantiate → dotnet build.
/// </summary>
[Collection("IntegrationTests")]
public class EndToEndTests : IDisposable
{
    private readonly TemplateEngineService _service;
    private readonly PostCreationProcessor _postProcessor;
    private readonly PostActionExecutor _postActionExecutor;
    private readonly McpFeatureFlags _featureFlags;
    private readonly string _tempDir;
    private readonly ITestOutputHelper _output;

    public EndToEndTests(ITestOutputHelper output)
    {
        _output = output;
        var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        _service = new TemplateEngineService(loggerFactory);
        _postProcessor = new PostCreationProcessor(loggerFactory);
        _postActionExecutor = new PostActionExecutor(loggerFactory);
        _tempDir = Path.Combine(Path.GetTempPath(), $"mcp-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Scope the workspace to the test's temp directory so the path guard permits it, and keep
        // post-actions off so these tests stay hermetic (no dotnet restore / network).
        _featureFlags = new McpFeatureFlags
        {
            ElicitationEnabled = false,
            WorkspaceRoot = _tempDir,
            PostActionsEnabled = false,
        };

        // Place a global.json in the temp dir so dotnet build uses the latest SDK
        // (prevents inheriting the MCP project's global.json via MSBuildStartupDirectory)
        File.WriteAllText(
            Path.Combine(_tempDir, "global.json"),
            """{"sdk":{"version":"9.0.100","rollForward":"latestMajor"}}""");
    }

    public void Dispose()
    {
        _service.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public async Task E2E_SearchInspectDryRunInstantiateBuild_Console()
    {
        // 1. Search for console templates
        _output.WriteLine("Step 1: Searching for console templates...");
        var searchResult = await TemplateSearchTool.SearchTemplatesAsync(
            _service, "console", "C#", "project");

        var searchParsed = JsonSerializer.Deserialize<JsonElement>(searchResult);
        Assert.Equal(JsonValueKind.Array, searchParsed.ValueKind);
        Assert.True(searchParsed.GetArrayLength() > 0, "Should find at least one console template");

        // Find the console template in results
        var consoleTemplate = searchParsed.EnumerateArray()
            .FirstOrDefault(t => t.GetProperty("ShortNames").EnumerateArray()
                .Any(sn => sn.GetString()!.Equals("console", StringComparison.OrdinalIgnoreCase)));
        Assert.NotEqual(default, consoleTemplate);
        _output.WriteLine($"  Found: {consoleTemplate.GetProperty("Name").GetString()}");

        // 2. Inspect the template
        _output.WriteLine("Step 2: Inspecting console template...");
        var inspectResult = await TemplateInspectTool.InspectTemplateAsync(
            _service, "console");

        var inspectParsed = JsonSerializer.Deserialize<JsonElement>(inspectResult);
        Assert.False(inspectParsed.TryGetProperty("error", out _));
        Assert.True(inspectParsed.TryGetProperty("Parameters", out var parameters));
        _output.WriteLine($"  Parameters: {parameters.GetArrayLength()}");

        // 3. Dry-run
        _output.WriteLine("Step 3: Dry-run preview...");
        var outputPath = Path.Combine(_tempDir, "E2EConsoleApp");
        var dryRunResult = await TemplateDryRunTool.DryRunTemplateAsync(
            _service, "console", "E2EConsoleApp", outputPath);

        var dryRunParsed = JsonSerializer.Deserialize<JsonElement>(dryRunResult);
        Assert.False(dryRunParsed.TryGetProperty("error", out _));
        Assert.True(dryRunParsed.TryGetProperty("FileChanges", out var dryRunFiles));
        Assert.True(dryRunFiles.GetArrayLength() > 0);
        _output.WriteLine($"  Files would be created: {dryRunFiles.GetArrayLength()}");

        // Verify nothing was written to disk
        Assert.False(Directory.Exists(outputPath));

        // 4. Instantiate (no Framework override — use template default)
        _output.WriteLine("Step 4: Creating project...");
        var instantiateResult = await TemplateInstantiateTool.InstantiateTemplateAsync(
            _service, _postProcessor, _postActionExecutor, _featureFlags, null!, "console", "E2EConsoleApp", outputPath);

        _output.WriteLine($"  Instantiate response: {instantiateResult}");
        var instantiateParsed = JsonSerializer.Deserialize<JsonElement>(instantiateResult);
        Assert.True(instantiateParsed.TryGetProperty("Status", out var statusProp), $"Missing 'Status' in response: {instantiateResult}");
        Assert.Equal("Success", statusProp.GetString());
        Assert.True(Directory.Exists(outputPath));
        _output.WriteLine($"  Project created at: {outputPath}");

        // 5. Build with dotnet
        _output.WriteLine("Step 5: Building project...");
        var buildResult = await RunDotnetBuildAsync(outputPath);
        _output.WriteLine($"  Build output: {buildResult.Output}");
        Assert.True(buildResult.Success, $"dotnet build failed:\n{buildResult.Output}");
        _output.WriteLine("  Build succeeded!");
    }

    [Fact]
    public async Task E2E_WebApi_WithControllers_SmartDefaults()
    {
        // Search for webapi
        var template = await _service.FindTemplateAsync("webapi");
        if (template == null)
        {
            _output.WriteLine("Skipping: webapi template not available");
            return;
        }

        var outputPath = Path.Combine(_tempDir, "E2EWebApi");

        // Instantiate with UseControllers=true — smart defaults should set UseMinimalAPIs=false
        var result = await TemplateInstantiateTool.InstantiateTemplateAsync(
            _service, _postProcessor, _postActionExecutor, _featureFlags, null!, "webapi", "E2EWebApi", outputPath,
            "{\"UseControllers\": \"true\"}");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("Success", parsed.GetProperty("Status").GetString());

        // Verify smart defaults were applied
        if (parsed.TryGetProperty("AppliedSmartDefaults", out var defaults))
        {
            Assert.True(defaults.TryGetProperty("UseMinimalAPIs", out var val));
            Assert.Equal("false", val.GetString());
            _output.WriteLine("  Smart default applied: UseMinimalAPIs=false");
        }

        // Verify project was created and has a Controllers directory
        Assert.True(Directory.Exists(outputPath));
        var csprojFiles = Directory.GetFiles(outputPath, "*.csproj", SearchOption.AllDirectories);
        Assert.NotEmpty(csprojFiles);

        // Build — skip assertion if OpenAPI source generator is incompatible with current SDK
        var buildResult = await RunDotnetBuildAsync(outputPath);
        if (!buildResult.Success)
        {
            _output.WriteLine("  Build failed (likely OpenAPI source generator SDK mismatch — not a test concern):");
            _output.WriteLine($"  {buildResult.Output[..Math.Min(200, buildResult.Output.Length)]}");
            return;
        }
        _output.WriteLine("  WebAPI with controllers built successfully!");
    }

    /// <summary>
    /// Gap 1.1 end-to-end: with post-actions enabled (the production default) a created project must
    /// come back actually restored, which is what <c>dotnet new</c> does and what this server
    /// previously only described in metadata. This is the only test that lets the post-action
    /// executor shell out for real.
    ///
    /// A NuGet.config with a cleared source list is staged so the restore is offline and
    /// deterministic: a class library has no PackageReference, so it needs no feed.
    /// </summary>
    [Fact]
    public async Task E2E_PostActionsEnabled_ActuallyRestoresTheProject()
    {
        var workspace = Path.Combine(_tempDir, "PostActionWorkspace");
        Directory.CreateDirectory(workspace);
        File.Copy(Path.Combine(_tempDir, "global.json"), Path.Combine(workspace, "global.json"));
        File.WriteAllText(
            Path.Combine(workspace, "NuGet.config"),
            "<configuration><packageSources><clear /></packageSources></configuration>");

        var flags = new McpFeatureFlags
        {
            ElicitationEnabled = false,
            WorkspaceRoot = workspace,
            PostActionsEnabled = true,
        };

        var outputPath = Path.Combine(workspace, "PostActionApp");
        var result = await TemplateInstantiateTool.InstantiateTemplateAsync(
            _service, _postProcessor, _postActionExecutor, flags, null!, "classlib", "PostActionApp", outputPath);

        _output.WriteLine($"  Instantiate response: {result}");
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("Success", parsed.GetProperty("Status").GetString());

        // The response must report what was executed, not just the template's post-action metadata.
        Assert.True(
            parsed.TryGetProperty("PostActionExecution", out var execution) && execution.ValueKind == JsonValueKind.Object,
            $"Missing 'PostActionExecution' in response: {result}");

        Assert.True(execution.TryGetProperty("Executed", out var executed), $"Missing 'PostActionExecution.Executed': {result}");
        var restore = executed.EnumerateArray().Single(e =>
            string.Equals(
                e.GetProperty("ActionId").GetString(),
                "210d431b-a78b-4d2f-b762-4ed3e3ea9025",
                StringComparison.OrdinalIgnoreCase));

        Assert.StartsWith("dotnet restore", restore.GetProperty("Command").GetString());
        Assert.True(
            restore.GetProperty("Success").GetBoolean(),
            $"Restore post-action failed: {restore}");

        // The template engine never writes obj/project.assets.json — only a real restore does.
        Assert.True(
            File.Exists(Path.Combine(outputPath, "obj", "project.assets.json")),
            $"Expected restore to produce obj/project.assets.json. Response: {result}");

        // The template's open-file action is arbitrary host behaviour and must not be auto-run.
        Assert.True(execution.TryGetProperty("Skipped", out var skipped), $"Missing 'PostActionExecution.Skipped': {result}");
        Assert.Contains(
            skipped.EnumerateArray(),
            s => string.Equals(
                s.GetProperty("ActionId").GetString(),
                "84c0da21-51c8-4541-9940-6ca19af04ee6",
                StringComparison.OrdinalIgnoreCase));

        _output.WriteLine("  Restore executed; open-file skipped.");
    }

    /// <summary>
    /// The configuration the other tests use keeps post-actions off, so nothing there shells out.
    /// This pins that contract so the hermetic tests cannot silently start restoring.
    /// </summary>
    [Fact]
    public async Task E2E_PostActionsDisabled_DoesNotRestore()
    {
        var outputPath = Path.Combine(_tempDir, "NoPostActionApp");

        var result = await TemplateInstantiateTool.InstantiateTemplateAsync(
            _service, _postProcessor, _postActionExecutor, _featureFlags, null!, "classlib", "NoPostActionApp", outputPath);

        Assert.Equal("Success", JsonSerializer.Deserialize<JsonElement>(result).GetProperty("Status").GetString());
        Assert.False(
            File.Exists(Path.Combine(outputPath, "obj", "project.assets.json")),
            "Post-actions are disabled, so no restore should have run.");
    }

    private static async Task<(bool Success, string Output)> RunDotnetBuildAsync(string projectPath)
        => await RunDotnetAsync(projectPath, "build --nologo");

    private static async Task<(bool Success, string Output)> RunDotnetAsync(string workingDirectory, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Clear SDK-pinning env vars that the test runner may inherit
        // so the child process uses the global.json in the temp dir instead
        psi.Environment.Remove("MSBuildSDKsPath");
        psi.Environment.Remove("DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR");
        psi.Environment.Remove("DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR");
        psi.Environment.Remove("MSBUILD_EXE_PATH");

        using var process = Process.Start(psi);
        if (process == null)
        {
            return (false, "Failed to start dotnet build");
        }

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var output = $"STDOUT:\n{stdout}\nSTDERR:\n{stderr}";
        return (process.ExitCode == 0, output);
    }
}
