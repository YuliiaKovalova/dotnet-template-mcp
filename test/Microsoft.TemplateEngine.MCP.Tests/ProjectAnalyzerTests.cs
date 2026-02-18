// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.TemplateEngine.MCP.Analysis;
using Xunit;

namespace Microsoft.TemplateEngine.MCP.Tests;

public class ProjectAnalyzerTests : IDisposable
{
    private readonly string _tempDir;

    public ProjectAnalyzerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mcp-analyzer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string WriteCsproj(string content)
    {
        var path = Path.Combine(_tempDir, "Test.csproj");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Analyze_BasicProject_ExtractsSdk()
    {
        var path = WriteCsproj("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var result = ProjectAnalyzer.Analyze(path);

        Assert.Equal("Microsoft.NET.Sdk", result.Sdk);
        Assert.Contains(result.Properties, p => p.Name == "TargetFramework" && p.Value == "net8.0");
    }

    [Fact]
    public void Analyze_MSTestSdk_ExtractsSdk()
    {
        var path = WriteCsproj("""
            <Project Sdk="MSTest.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
            </Project>
            """);

        var result = ProjectAnalyzer.Analyze(path);

        Assert.Equal("MSTest.Sdk", result.Sdk);
        Assert.Contains(result.Properties, p => p.Name == "OutputType" && p.Value == "Exe");
    }

    [Fact]
    public void Analyze_PackageReferences_ExtractsMetadata()
    {
        var path = WriteCsproj("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="xunit" Version="2.9.0" />
                <PackageReference Include="xunit.runner.visualstudio" Version="2.8.0">
                  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
                  <PrivateAssets>all</PrivateAssets>
                </PackageReference>
                <PackageReference Include="coverlet.collector">
                  <PrivateAssets>all</PrivateAssets>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);

        var result = ProjectAnalyzer.Analyze(path);

        Assert.Equal(3, result.PackageReferences.Count);

        var xunit = result.PackageReferences.First(p => p.Include == "xunit");
        Assert.Equal("2.9.0", xunit.Version);
        Assert.Null(xunit.PrivateAssets);

        var runner = result.PackageReferences.First(p => p.Include == "xunit.runner.visualstudio");
        Assert.Equal("all", runner.PrivateAssets);
        Assert.Contains("analyzers", runner.IncludeAssets!);

        var coverlet = result.PackageReferences.First(p => p.Include == "coverlet.collector");
        Assert.Equal("all", coverlet.PrivateAssets);
        Assert.Null(coverlet.Version);
    }

    [Fact]
    public void Analyze_CentralPackageManagement_Detected()
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

        var result = ProjectAnalyzer.Analyze(path);

        Assert.True(result.UsesCentralPackageManagement);
        Assert.All(result.PackageReferences, p => Assert.Null(p.Version));
    }

    [Fact]
    public void Analyze_NonCPM_Detected()
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

        var result = ProjectAnalyzer.Analyze(path);

        Assert.False(result.UsesCentralPackageManagement);
    }

    [Fact]
    public void Analyze_ProjectReferences_Extracted()
    {
        var path = WriteCsproj("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\..\src\MyLib\MyLib.csproj" />
                <ProjectReference Include="..\TestHelper\TestHelper.csproj" />
              </ItemGroup>
            </Project>
            """);

        var result = ProjectAnalyzer.Analyze(path);

        Assert.Equal(2, result.ProjectReferences.Count);
        Assert.Contains(result.ProjectReferences, p => p.Contains("MyLib"));
        Assert.Contains(result.ProjectReferences, p => p.Contains("TestHelper"));
    }

    [Fact]
    public void Analyze_SharedCompiles_Extracted()
    {
        var path = WriteCsproj("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="..\Shared\**\*.cs" Link="%(RecursiveDir)%(Filename)%(Extension)" />
              </ItemGroup>
            </Project>
            """);

        var result = ProjectAnalyzer.Analyze(path);

        Assert.Single(result.SharedCompiles);
        Assert.Equal(@"..\Shared\**\*.cs", result.SharedCompiles[0].Include);
        Assert.Equal("%(RecursiveDir)%(Filename)%(Extension)", result.SharedCompiles[0].Link);
    }

    [Fact]
    public void Analyze_ContentItems_Extracted()
    {
        var path = WriteCsproj("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <None Include="Resources\**\*" CopyToOutputDirectory="Always" />
                <Compile Remove="Resources\**\*" />
              </ItemGroup>
            </Project>
            """);

        var result = ProjectAnalyzer.Analyze(path);

        Assert.Equal(2, result.ContentItems.Count);
        Assert.Contains(result.ContentItems, c => c.ItemType == "None" && c.CopyToOutputDirectory == "Always");
        Assert.Contains(result.ContentItems, c => c.ItemType == "Compile" && c.Remove == @"Resources\**\*");
    }

    [Fact]
    public void Analyze_ConditionalProperties_Captured()
    {
        var path = WriteCsproj("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <TreatWarningsAsErrors Condition="'$(Configuration)' == 'Release'">true</TreatWarningsAsErrors>
              </PropertyGroup>
            </Project>
            """);

        var result = ProjectAnalyzer.Analyze(path);

        var prop = result.Properties.First(p => p.Name == "TreatWarningsAsErrors");
        Assert.Equal("true", prop.Value);
        Assert.Contains("Release", prop.Condition!);
    }

    [Fact]
    public void Analyze_FileNotFound_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            ProjectAnalyzer.Analyze(Path.Combine(_tempDir, "nonexistent.csproj")));
    }

    [Fact]
    public void Analyze_ComplexTestProject_AllGapsDetected()
    {
        var path = WriteCsproj("""
            <Project Sdk="MSTest.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <OutputType>Exe</OutputType>
                <IsTestProject>true</IsTestProject>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <GenerateErrorForMissingTargetingPacks>false</GenerateErrorForMissingTargetingPacks>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" />
                <PackageReference Include="MSTest.TestAdapter" />
                <PackageReference Include="coverlet.collector">
                  <PrivateAssets>all</PrivateAssets>
                </PackageReference>
              </ItemGroup>
              <ItemGroup>
                <ProjectReference Include="..\..\src\Ordering\Ordering.csproj" />
              </ItemGroup>
              <ItemGroup>
                <Compile Include="..\Shared\**\*.cs" Link="%(RecursiveDir)%(Filename)%(Extension)" />
              </ItemGroup>
            </Project>
            """);

        var result = ProjectAnalyzer.Analyze(path);

        // All 6 gap-relevant signals present
        Assert.Equal("MSTest.Sdk", result.Sdk);
        Assert.Contains(result.Properties, p => p.Name == "OutputType" && p.Value == "Exe");
        Assert.True(result.UsesCentralPackageManagement);
        Assert.Contains(result.PackageReferences, p => p.PrivateAssets == "all");
        Assert.Contains(result.Properties, p => p.Name == "TreatWarningsAsErrors");
        Assert.Single(result.SharedCompiles);
    }
}
