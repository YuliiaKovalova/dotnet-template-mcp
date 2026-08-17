// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.MCP.PostCreation;
using Microsoft.TemplateEngine.MCP.Tools;
using Xunit;

namespace Microsoft.TemplateEngine.MCP.Tests;

/// <summary>
/// Integration tests that use the real template engine (not mocked).
/// These tests require .NET SDK to be installed and exercise the actual
/// template discovery, inspection, and instantiation pipeline.
/// </summary>
[Collection("IntegrationTests")]
public class IntegrationTests : IDisposable
{
    private readonly TemplateEngineService _service;
    private readonly PostCreationProcessor _postProcessor;
    private readonly PostActionExecutor _postActionExecutor;
    private readonly McpFeatureFlags _featureFlags;
    private readonly string _tempDir;

    public IntegrationTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        _service = new TemplateEngineService(loggerFactory);
        _postProcessor = new PostCreationProcessor(loggerFactory);
        _postActionExecutor = new PostActionExecutor(loggerFactory);
        _tempDir = Path.Combine(Path.GetTempPath(), $"mcp-integration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Scope the workspace to the test's temp directory so the path guard permits it, and keep
        // post-actions off so these tests stay hermetic (no dotnet restore / network).
        _featureFlags = new McpFeatureFlags
        {
            ElicitationEnabled = false,
            WorkspaceRoot = _tempDir,
            PostActionsEnabled = false,
        };
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
    public async Task GetTemplatesAsync_ReturnsTemplates()
    {
        var templates = await _service.GetTemplatesAsync();

        // SDK templates should be auto-discovered
        Assert.NotEmpty(templates);
    }

    [Fact]
    public async Task FindTemplate_Console_ReturnsMatch()
    {
        var template = await _service.FindTemplateAsync("console");

        Assert.NotNull(template);
        Assert.Contains("console", template.ShortNameList, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FindTemplate_NonExistent_ReturnsNull()
    {
        var template = await _service.FindTemplateAsync("totally-nonexistent-template-xyz");

        Assert.Null(template);
    }

    [Fact]
    public async Task TemplateSearch_Console_ReturnsResults()
    {
        var result = await TemplateSearchTool.SearchTemplatesAsync(
            _service, "console");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal(JsonValueKind.Array, parsed.ValueKind);
        Assert.True(parsed.GetArrayLength() > 0);

        // First result should be local (SDK template)
        var first = parsed[0];
        Assert.Equal("local", first.GetProperty("Source").GetString());
    }

    [Fact]
    public async Task TemplateInspect_Console_ReturnsFullMetadata()
    {
        var result = await TemplateInspectTool.InspectTemplateAsync(
            _service, "console");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.False(parsed.TryGetProperty("error", out _));
        Assert.True(parsed.TryGetProperty("Identity", out _));
        Assert.True(parsed.TryGetProperty("Parameters", out var parameters));
        Assert.True(parameters.GetArrayLength() > 0);
    }

    [Fact]
    public async Task TemplateDryRun_Console_ReturnsFileList()
    {
        var outputPath = Path.Combine(_tempDir, "DryRunTest");

        var result = await TemplateDryRunTool.DryRunTemplateAsync(
            _service, "console", "DryRunApp", outputPath);

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.False(parsed.TryGetProperty("error", out _));

        // Dry run should list files but NOT create them
        Assert.True(parsed.TryGetProperty("FileChanges", out var fileChanges));
        Assert.True(fileChanges.GetArrayLength() > 0);
        Assert.False(Directory.Exists(outputPath), "Dry run should not create the output directory.");
    }

    [Fact]
    public async Task TemplateInstantiate_Console_CreatesProject()
    {
        var outputPath = Path.Combine(_tempDir, "InstantiateTest");

        var result = await TemplateInstantiateTool.InstantiateTemplateAsync(
            _service, _postProcessor, _postActionExecutor, _featureFlags, null!, "console", "TestConsoleApp", outputPath);

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("Success", parsed.GetProperty("Status").GetString());
        Assert.True(Directory.Exists(outputPath));

        // Verify the .csproj was created
        var csprojFiles = Directory.GetFiles(outputPath, "*.csproj", SearchOption.AllDirectories);
        Assert.NotEmpty(csprojFiles);
    }

    [Fact]
    public async Task TemplateInstantiate_InvalidParam_ReturnsValidationError()
    {
        var outputPath = Path.Combine(_tempDir, "ValidationTest");

        var result = await TemplateInstantiateTool.InstantiateTemplateAsync(
            _service, _postProcessor, _postActionExecutor, _featureFlags, null!, "console", "ValidationApp", outputPath,
            "{\"Framework\": \"net3.0\"}");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.True(parsed.TryGetProperty("error", out var error));
        Assert.Contains("validation failed", error.GetString()!.ToLowerInvariant());

        // No files should be written
        Assert.False(Directory.Exists(outputPath));
    }

    [Fact]
    public async Task TemplateList_ReturnsInstalledTemplates()
    {
        var result = await TemplateListTool.ListTemplatesAsync(_service, new McpFeatureFlags());

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal(JsonValueKind.Array, parsed.ValueKind);
        Assert.True(parsed.GetArrayLength() > 0);
    }

    [Fact]
    public async Task ValidateParameters_RealTemplate_Works()
    {
        var template = await _service.FindTemplateAsync("console");
        Assert.NotNull(template);

        // Valid parameter
        var errors = TemplateEngineService.ValidateParameters(template, new Dictionary<string, string?> { { "skipRestore", "true" } });
        Assert.Empty(errors);

        // Invalid parameter name
        errors = TemplateEngineService.ValidateParameters(template, new Dictionary<string, string?> { { "FakeParam", "value" } });
        Assert.NotEmpty(errors);
    }

    [Fact]
    public async Task SmartDefaults_RealWebApiTemplate_Works()
    {
        var template = await _service.FindTemplateAsync("webapi");
        if (template == null)
        {
            // webapi may not be available in all environments
            return;
        }

        // UseControllers=true should suggest UseMinimalAPIs=false
        var suggestions = TemplateEngineService.SuggestSmartDefaults(
            template,
            new Dictionary<string, string?> { { "UseControllers", "true" } });

        Assert.Contains("UseMinimalAPIs", suggestions.Keys);
        Assert.Equal("false", suggestions["UseMinimalAPIs"]);
    }
}
