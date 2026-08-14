// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using FakeItEasy;
using Microsoft.TemplateEngine.MCP.Tools;
using Xunit;

namespace Microsoft.TemplateEngine.MCP.Tests;

public class CreateFromExistingToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TemplateEngineService _engineService;
    private readonly McpFeatureFlags _featureFlags;

    public CreateFromExistingToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mcp-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _engineService = A.Fake<TemplateEngineService>();
        _featureFlags = new McpFeatureFlags { WorkspaceRoot = _tempDir };
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string WriteCsproj(string content, string? name = null)
    {
        var path = Path.Combine(_tempDir, name ?? "Test.csproj");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task CreateFromExisting_BasicProject_ReturnsAnalysis()
    {
        var path = WriteCsproj("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="xunit" Version="2.9.0" />
              </ItemGroup>
            </Project>
            """);

        var result = await CreateFromExistingTool.CreateFromExistingAsync(
            _engineService, _featureFlags, path, "Test Template", "test-tmpl", _tempDir);

        var json = JsonDocument.Parse(result);
        var root = json.RootElement;

        Assert.Equal("Success", root.GetProperty("status").GetString());
        Assert.Equal("test-tmpl", root.GetProperty("shortName").GetString());
        Assert.True(Directory.Exists(root.GetProperty("templatePath").GetString()));

        // Verify analysis is included
        var analysis = root.GetProperty("analysis");
        Assert.Equal("Microsoft.NET.Sdk", analysis.GetProperty("sdk").GetString());
    }

    [Fact]
    public async Task CreateFromExisting_MSTestSdk_ReportsSDKGap()
    {
        var path = WriteCsproj("""
            <Project Sdk="MSTest.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
            </Project>
            """);

        var result = await CreateFromExistingTool.CreateFromExistingAsync(
            _engineService, _featureFlags, path, "MSTest Template", "mstest-tmpl", _tempDir);

        var json = JsonDocument.Parse(result);
        var gaps = json.RootElement.GetProperty("gapsAddressed");

        // Should report SDK gap (MSTest.Sdk vs Microsoft.NET.Sdk)
        var gapList = gaps.EnumerateArray().ToList();
        Assert.Contains(gapList, g => g.GetProperty("gap").GetString() == "SDK");
        Assert.Contains(gapList, g => g.GetProperty("gap").GetString() == "OutputType");
    }

    [Fact]
    public async Task CreateFromExisting_CPM_ReportsGap()
    {
        var path = WriteCsproj("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="xunit" />
                <PackageReference Include="Microsoft.NET.Test.Sdk" />
              </ItemGroup>
            </Project>
            """);

        var result = await CreateFromExistingTool.CreateFromExistingAsync(
            _engineService, _featureFlags, path, "CPM Template", "cpm-tmpl", _tempDir);

        var json = JsonDocument.Parse(result);
        var gaps = json.RootElement.GetProperty("gapsAddressed");

        Assert.Contains(gaps.EnumerateArray(), g => g.GetProperty("gap").GetString() == "Central Package Management");
    }

    [Fact]
    public async Task CreateFromExisting_AnalyzerMetadata_ReportsGap()
    {
        var path = WriteCsproj("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="coverlet.collector">
                  <PrivateAssets>all</PrivateAssets>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);

        var result = await CreateFromExistingTool.CreateFromExistingAsync(
            _engineService, _featureFlags, path, "Analyzer Template", "analyzer-tmpl", _tempDir);

        var json = JsonDocument.Parse(result);
        var gaps = json.RootElement.GetProperty("gapsAddressed");

        Assert.Contains(gaps.EnumerateArray(), g => g.GetProperty("gap").GetString() == "Analyzer metadata");
    }

    [Fact]
    public async Task CreateFromExisting_FileNotFound_ReturnsError()
    {
        var result = await CreateFromExistingTool.CreateFromExistingAsync(
            _engineService, _featureFlags, Path.Combine(_tempDir, "nonexistent.csproj"), "Bad", "bad");

        var json = JsonDocument.Parse(result);
        Assert.True(json.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task CreateFromExisting_ComplexProject_AllGapsReported()
    {
        var path = WriteCsproj("""
            <Project Sdk="MSTest.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <OutputType>Exe</OutputType>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" />
                <PackageReference Include="coverlet.collector">
                  <PrivateAssets>all</PrivateAssets>
                </PackageReference>
              </ItemGroup>
              <ItemGroup>
                <Compile Include="..\Shared\**\*.cs" Link="%(RecursiveDir)%(Filename)%(Extension)" />
              </ItemGroup>
            </Project>
            """);

        var result = await CreateFromExistingTool.CreateFromExistingAsync(
            _engineService, _featureFlags, path, "Full Template", "full-tmpl", _tempDir);

        var json = JsonDocument.Parse(result);
        var gaps = json.RootElement.GetProperty("gapsAddressed").EnumerateArray().ToList();

        // All 6 gaps should be reported
        var gapNames = gaps.Select(g => g.GetProperty("gap").GetString()).ToList();
        Assert.Contains("SDK", gapNames);
        Assert.Contains("OutputType", gapNames);
        Assert.Contains("Central Package Management", gapNames);
        Assert.Contains("Analyzer metadata", gapNames);
        Assert.Contains("Custom build properties", gapNames);
        Assert.Contains("Repo conventions (shared compiles)", gapNames);
    }

    [Fact]
    public async Task CreateFromExisting_GeneratesNextSteps()
    {
        var path = WriteCsproj("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var result = await CreateFromExistingTool.CreateFromExistingAsync(
            _engineService, _featureFlags, path, "Simple Template", "simple-tmpl", _tempDir);

        var json = JsonDocument.Parse(result);
        var nextSteps = json.RootElement.GetProperty("nextSteps");

        Assert.True(nextSteps.GetArrayLength() > 0);
        Assert.Contains("dotnet new install", nextSteps.EnumerateArray().First().GetString()!);
    }
}
