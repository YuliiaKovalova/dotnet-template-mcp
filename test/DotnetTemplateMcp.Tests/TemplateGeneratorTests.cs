// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.Text.Json;
using DotnetTemplateMcp.Analysis;
using Xunit;

namespace DotnetTemplateMcp.Tests;

public class TemplateGeneratorTests : IDisposable
{
    private readonly string _tempDir;

    public TemplateGeneratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mcp-generator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private static ProjectAnalysis CreateAnalysis(
        string sdk = "Microsoft.NET.Sdk",
        IReadOnlyList<ProjectProperty>? properties = null,
        IReadOnlyList<PackageReferenceInfo>? packages = null,
        IReadOnlyList<string>? projectRefs = null,
        IReadOnlyList<SharedCompileInfo>? sharedCompiles = null,
        IReadOnlyList<ContentItemInfo>? contentItems = null,
        bool usesCpm = false)
    {
        return new ProjectAnalysis
        {
            SourceProjectPath = @"C:\repo\test\MyTests\MyTests.csproj",
            Sdk = sdk,
            Properties = properties ?? new List<ProjectProperty>
            {
                new() { Name = "TargetFramework", Value = "net8.0" },
            },
            PackageReferences = packages ?? new List<PackageReferenceInfo>(),
            ProjectReferences = projectRefs ?? new List<string>(),
            SharedCompiles = sharedCompiles ?? new List<SharedCompileInfo>(),
            ContentItems = contentItems ?? new List<ContentItemInfo>(),
            Imports = new List<string>(),
            UsesCentralPackageManagement = usesCpm,
        };
    }

    [Fact]
    public void Generate_CreatesTemplateDirectory()
    {
        var analysis = CreateAnalysis();

        var result = TemplateGenerator.Generate(analysis, _tempDir, "My Test Template", "my-test");

        Assert.True(Directory.Exists(result));
        Assert.True(File.Exists(Path.Combine(result, ".template.config", "template.json")));
        Assert.True(File.Exists(Path.Combine(result, "Template.csproj")));
        Assert.True(File.Exists(Path.Combine(result, "Tests.cs")));
    }

    [Fact]
    public void Generate_TemplateJson_HasCorrectMetadata()
    {
        var analysis = CreateAnalysis();

        var result = TemplateGenerator.Generate(analysis, _tempDir, "My Test Template", "my-test");

        var json = File.ReadAllText(Path.Combine(result, ".template.config", "template.json"));
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("My Test Template", root.GetProperty("name").GetString());
        Assert.Equal("my-test", root.GetProperty("shortName").GetString());
        Assert.Equal("Custom.my-test", root.GetProperty("identity").GetString());
        Assert.Equal("Template", root.GetProperty("sourceName").GetString());
    }

    [Fact]
    public void BuildCsproj_PreservesNonDefaultSdk()
    {
        var analysis = CreateAnalysis(sdk: "MSTest.Sdk");

        var csproj = TemplateGenerator.BuildCsproj(analysis);

        Assert.Contains("Sdk=\"MSTest.Sdk\"", csproj);
    }

    [Fact]
    public void BuildCsproj_PreservesProperties()
    {
        var analysis = CreateAnalysis(properties: new List<ProjectProperty>
        {
            new() { Name = "TargetFramework", Value = "net8.0" },
            new() { Name = "OutputType", Value = "Exe" },
            new() { Name = "TreatWarningsAsErrors", Value = "true" },
        });

        var csproj = TemplateGenerator.BuildCsproj(analysis);

        Assert.Contains("<TargetFramework>net8.0</TargetFramework>", csproj);
        Assert.Contains("<OutputType>Exe</OutputType>", csproj);
        Assert.Contains("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", csproj);
    }

    [Fact]
    public void BuildCsproj_PreservesConditionalProperties()
    {
        var analysis = CreateAnalysis(properties: new List<ProjectProperty>
        {
            new() { Name = "TreatWarningsAsErrors", Value = "true", Condition = "'$(Configuration)' == 'Release'" },
        });

        var csproj = TemplateGenerator.BuildCsproj(analysis);

        Assert.Contains("Condition=", csproj);
        Assert.Contains("Release", csproj);
    }

    [Fact]
    public void BuildCsproj_PreservesPackageReferenceMetadata()
    {
        var analysis = CreateAnalysis(packages: new List<PackageReferenceInfo>
        {
            new()
            {
                Include = "xunit.runner.visualstudio",
                Version = "2.8.0",
                PrivateAssets = "all",
                IncludeAssets = "runtime; build; native; contentfiles; analyzers; buildtransitive",
            },
        });

        var csproj = TemplateGenerator.BuildCsproj(analysis);

        Assert.Contains("<PrivateAssets>all</PrivateAssets>", csproj);
        Assert.Contains("<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>", csproj);
        Assert.Contains("Version=\"2.8.0\"", csproj);
    }

    [Fact]
    public void BuildCsproj_CPM_OmitsVersions()
    {
        var analysis = CreateAnalysis(
            packages: new List<PackageReferenceInfo>
            {
                new() { Include = "xunit" },
                new() { Include = "Microsoft.NET.Test.Sdk" },
            },
            usesCpm: true);

        var csproj = TemplateGenerator.BuildCsproj(analysis);

        Assert.Contains("<PackageReference Include=\"xunit\"", csproj);
        Assert.DoesNotContain("Version=", csproj);
    }

    [Fact]
    public void BuildCsproj_PreservesSharedCompiles()
    {
        var analysis = CreateAnalysis(sharedCompiles: new List<SharedCompileInfo>
        {
            new() { Include = @"..\Shared\**\*.cs", Link = "%(RecursiveDir)%(Filename)%(Extension)" },
        });

        var csproj = TemplateGenerator.BuildCsproj(analysis);

        Assert.Contains(@"Compile Include=""..\Shared\**\*.cs""", csproj);
        Assert.Contains("Link=", csproj);
    }

    [Fact]
    public void BuildCsproj_PreservesContentItems()
    {
        var analysis = CreateAnalysis(contentItems: new List<ContentItemInfo>
        {
            new() { ItemType = "None", Include = @"Resources\**\*", CopyToOutputDirectory = "Always" },
            new() { ItemType = "Compile", Remove = @"Resources\**\*" },
        });

        var csproj = TemplateGenerator.BuildCsproj(analysis);

        Assert.Contains("CopyToOutputDirectory=\"Always\"", csproj);
        Assert.Contains("Compile Remove=", csproj);
    }

    [Fact]
    public void BuildPlaceholderClass_Xunit_GeneratesFactTest()
    {
        var analysis = CreateAnalysis(packages: new List<PackageReferenceInfo>
        {
            new() { Include = "xunit" },
        });

        var code = TemplateGenerator.BuildPlaceholderClass(analysis);

        Assert.Contains("[Fact]", code);
        Assert.Contains("Assert.True(true)", code);
    }

    [Fact]
    public void BuildPlaceholderClass_NUnit_GeneratesTestFixture()
    {
        var analysis = CreateAnalysis(packages: new List<PackageReferenceInfo>
        {
            new() { Include = "NUnit" },
        });

        var code = TemplateGenerator.BuildPlaceholderClass(analysis);

        Assert.Contains("[TestFixture]", code);
        Assert.Contains("[Test]", code);
    }

    [Fact]
    public void BuildPlaceholderClass_MSTest_GeneratesTestClass()
    {
        var analysis = CreateAnalysis(packages: new List<PackageReferenceInfo>
        {
            new() { Include = "MSTest.TestAdapter" },
        });

        var code = TemplateGenerator.BuildPlaceholderClass(analysis);

        Assert.Contains("[TestClass]", code);
        Assert.Contains("[TestMethod]", code);
    }

    [Fact]
    public void BuildPlaceholderClass_MSTestSdk_GeneratesTestClass()
    {
        var analysis = CreateAnalysis(sdk: "MSTest.Sdk");

        var code = TemplateGenerator.BuildPlaceholderClass(analysis);

        Assert.Contains("[TestClass]", code);
    }

    [Fact]
    public void BuildPlaceholderClass_NoTestFramework_GeneratesEmptyClass()
    {
        var analysis = CreateAnalysis();

        var code = TemplateGenerator.BuildPlaceholderClass(analysis);

        Assert.Contains("class Class1", code);
        Assert.DoesNotContain("[Fact]", code);
        Assert.DoesNotContain("[Test]", code);
    }

    [Fact]
    public void BuildTemplateJson_ParameterizesTargetFramework()
    {
        var analysis = CreateAnalysis(properties: new List<ProjectProperty>
        {
            new() { Name = "TargetFramework", Value = "net8.0" },
        });

        var json = TemplateGenerator.BuildTemplateJson(analysis, "Test", "test");
        var doc = JsonDocument.Parse(json);
        var symbols = doc.RootElement.GetProperty("symbols");

        Assert.True(symbols.TryGetProperty("TargetFramework", out var tfm));
        Assert.Equal("net8.0", tfm.GetProperty("defaultValue").GetString());
    }

    [Fact]
    public void BuildTemplateJson_ChoiceSymbol_IncludesChoicesList()
    {
        // Regression: choice-typed symbols (Nullable/ImplicitUsings) were emitted without a
        // "choices" array, producing template.json that the project's own validator rejects.
        var analysis = CreateAnalysis(properties: new List<ProjectProperty>
        {
            new() { Name = "Nullable", Value = "enable" },
        });

        var json = TemplateGenerator.BuildTemplateJson(analysis, "Test", "test");
        var doc = JsonDocument.Parse(json);
        var nullable = doc.RootElement.GetProperty("symbols").GetProperty("Nullable");

        Assert.Equal("choice", nullable.GetProperty("datatype").GetString());
        Assert.True(nullable.TryGetProperty("choices", out var choices));
        var choiceValues = choices.EnumerateArray().Select(c => c.GetProperty("choice").GetString()).ToList();
        Assert.Contains("enable", choiceValues);
        Assert.Contains("disable", choiceValues);
    }

    [Fact]
    public void BuildTemplateJson_DuplicateReplaceValues_OnlyAppliesReplaceOnce()
    {
        // Regression: two properties sharing a value (e.g. both "enable") both set
        // replaces:"enable", producing ambiguous, overlapping substitutions.
        var analysis = CreateAnalysis(properties: new List<ProjectProperty>
        {
            new() { Name = "Nullable", Value = "enable" },
            new() { Name = "ImplicitUsings", Value = "enable" },
        });

        var json = TemplateGenerator.BuildTemplateJson(analysis, "Test", "test");
        var doc = JsonDocument.Parse(json);
        var symbols = doc.RootElement.GetProperty("symbols");

        var replaceCount = symbols.EnumerateObject()
            .Count(s => s.Value.TryGetProperty("replaces", out var r) && r.GetString() == "enable");
        Assert.Equal(1, replaceCount);
    }

    [Fact]
    public void BuildTemplateJson_TestProject_ClassifiedAsTest()
    {
        var analysis = CreateAnalysis(
            properties: new List<ProjectProperty>
            {
                new() { Name = "IsTestProject", Value = "true" },
            },
            packages: new List<PackageReferenceInfo>
            {
                new() { Include = "xunit" },
            });

        var json = TemplateGenerator.BuildTemplateJson(analysis, "Test", "test");
        var doc = JsonDocument.Parse(json);
        var classifications = doc.RootElement.GetProperty("classifications");

        Assert.Contains("Test", classifications.EnumerateArray().Select(e => e.GetString()!));
    }

    [Fact]
    public void Generate_FullRoundTrip_ProducesValidTemplate()
    {
        var analysis = CreateAnalysis(
            sdk: "MSTest.Sdk",
            properties: new List<ProjectProperty>
            {
                new() { Name = "TargetFramework", Value = "net8.0" },
                new() { Name = "OutputType", Value = "Exe" },
                new() { Name = "IsTestProject", Value = "true" },
                new() { Name = "TreatWarningsAsErrors", Value = "true" },
            },
            packages: new List<PackageReferenceInfo>
            {
                new() { Include = "Microsoft.NET.Test.Sdk" },
                new()
                {
                    Include = "coverlet.collector",
                    PrivateAssets = "all",
                },
            },
            projectRefs: new List<string> { @"..\..\src\MyLib\MyLib.csproj" },
            sharedCompiles: new List<SharedCompileInfo>
            {
                new() { Include = @"..\Shared\**\*.cs", Link = "%(RecursiveDir)%(Filename)%(Extension)" },
            },
            usesCpm: true);

        var templatePath = TemplateGenerator.Generate(analysis, _tempDir, "Repo Test Project", "repo-test");

        // Verify all files exist
        Assert.True(File.Exists(Path.Combine(templatePath, ".template.config", "template.json")));
        Assert.True(File.Exists(Path.Combine(templatePath, "Template.csproj")));
        Assert.True(File.Exists(Path.Combine(templatePath, "Tests.cs")));

        // Verify csproj preserves everything
        var csproj = File.ReadAllText(Path.Combine(templatePath, "Template.csproj"));
        Assert.Contains("MSTest.Sdk", csproj);
        Assert.Contains("<OutputType>Exe</OutputType>", csproj);
        Assert.Contains("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", csproj);
        Assert.Contains("<PrivateAssets>all</PrivateAssets>", csproj);
        Assert.Contains("ProjectReference", csproj);
        Assert.Contains(@"Compile Include=""..\Shared\**\*.cs""", csproj);
        Assert.DoesNotContain("Version=", csproj); // CPM

        // Verify template.json
        var json = File.ReadAllText(Path.Combine(templatePath, ".template.config", "template.json"));
        var doc = JsonDocument.Parse(json);
        Assert.Equal("Repo Test Project", doc.RootElement.GetProperty("name").GetString());

        // Verify test class is MSTest
        var testClass = File.ReadAllText(Path.Combine(templatePath, "Tests.cs"));
        Assert.Contains("[TestClass]", testClass);
    }
}
