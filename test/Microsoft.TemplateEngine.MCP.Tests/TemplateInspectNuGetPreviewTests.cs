// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using FakeItEasy;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Parameters;
using Microsoft.TemplateEngine.MCP.Tools;
using Microsoft.TemplateSearch.Common.Abstractions;
using Xunit;

namespace Microsoft.TemplateEngine.MCP.Tests;

public class TemplateInspectNuGetPreviewTests
{
    [Fact]
    public async Task InspectTemplate_NotInstalledButOnNuGet_ReturnsPreviewMetadata()
    {
        var engineService = A.Fake<TemplateEngineService>();

        // No local templates
        A.CallTo(() => engineService.GetTemplatesAsync(A<CancellationToken>._))
            .Returns(Array.Empty<ITemplateInfo>());

        // NuGet has it
        var nugetTemplate = CreateFakeTemplate("NuGetTemplate.CSharp", "nugettempl", "NuGet Template");
        var packageInfo = A.Fake<ITemplatePackageInfo>();
        A.CallTo(() => packageInfo.Name).Returns("NuGetTemplate.Package");
        A.CallTo(() => packageInfo.Version).Returns("3.0.0");

        A.CallTo(() => engineService.SearchNuGetTemplatesAsync(
            "nugettempl", null, null, A<CancellationToken>._))
            .Returns(new List<(ITemplatePackageInfo, IReadOnlyList<ITemplateInfo>)>
            {
                (packageInfo, new[] { nugetTemplate }),
            });

        var result = await TemplateInspectTool.InspectTemplateAsync(
            engineService, "nugettempl");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("nuget", parsed.GetProperty("Source").GetString());
        Assert.Equal("NuGetTemplate.Package", parsed.GetProperty("PackageId").GetString());
        Assert.Equal("3.0.0", parsed.GetProperty("PackageVersion").GetString());
        Assert.Contains("not yet installed", parsed.GetProperty("Note").GetString());
        Assert.Equal("NuGetTemplate.CSharp", parsed.GetProperty("Identity").GetString());
    }

    [Fact]
    public async Task InspectTemplate_NotInstalledNotOnNuGet_ReturnsError()
    {
        var engineService = A.Fake<TemplateEngineService>();

        A.CallTo(() => engineService.GetTemplatesAsync(A<CancellationToken>._))
            .Returns(Array.Empty<ITemplateInfo>());

        A.CallTo(() => engineService.SearchNuGetTemplatesAsync(
            "nonexistent", null, null, A<CancellationToken>._))
            .Returns(new List<(ITemplatePackageInfo, IReadOnlyList<ITemplateInfo>)>());

        var result = await TemplateInspectTool.InspectTemplateAsync(
            engineService, "nonexistent");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.True(parsed.TryGetProperty("error", out var error));
        Assert.Contains("not found locally or on NuGet.org", error.GetString());
    }

    [Fact]
    public async Task InspectTemplate_InstalledLocally_ReturnsLocalMetadata()
    {
        var engineService = A.Fake<TemplateEngineService>();

        var localTemplate = CreateFakeTemplate("LocalTemplate.CSharp", "localtempl", "Local Template");
        A.CallTo(() => engineService.GetTemplatesAsync(A<CancellationToken>._))
            .Returns(new[] { localTemplate });

        var result = await TemplateInspectTool.InspectTemplateAsync(
            engineService, "localtempl");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("LocalTemplate.CSharp", parsed.GetProperty("Identity").GetString());
        // Should NOT have NuGet-specific fields
        Assert.False(parsed.TryGetProperty("Source", out _));
        Assert.False(parsed.TryGetProperty("PackageId", out _));
    }

    private static ITemplateInfo CreateFakeTemplate(string identity, string shortName, string name)
    {
        var template = A.Fake<ITemplateInfo>();
        A.CallTo(() => template.Identity).Returns(identity);
        A.CallTo(() => template.ShortNameList).Returns(new[] { shortName });
        A.CallTo(() => template.Name).Returns(name);
        A.CallTo(() => template.Description).Returns("Test template description");
        A.CallTo(() => template.Author).Returns("Test Author");
        A.CallTo(() => template.Classifications).Returns(new[] { "Test" });
        A.CallTo(() => template.DefaultName).Returns("TestProject");
        A.CallTo(() => template.GroupIdentity).Returns(identity);
        A.CallTo(() => template.Precedence).Returns(100);
        A.CallTo(() => template.TagsCollection).Returns(new Dictionary<string, string>
        {
            { "language", "C#" },
            { "type", "project" },
        });
        A.CallTo(() => template.BaselineInfo).Returns(new Dictionary<string, IBaselineInfo>());
        A.CallTo(() => template.Constraints).Returns(Array.Empty<Abstractions.Constraints.TemplateConstraintInfo>());
        A.CallTo(() => template.PostActions).Returns(Array.Empty<Guid>());

        // Create a parameter
        var paramDef = A.Fake<ITemplateParameter>();
        A.CallTo(() => paramDef.Name).Returns("TestParam");
        A.CallTo(() => paramDef.DataType).Returns("string");
        A.CallTo(() => paramDef.Type).Returns("parameter");
        A.CallTo(() => paramDef.IsName).Returns(false);
        A.CallTo(() => paramDef.DefaultValue).Returns("default");
        A.CallTo(() => paramDef.Description).Returns("A test param");
        A.CallTo(() => paramDef.Choices).Returns((IReadOnlyDictionary<string, ParameterChoice>?)null);
        A.CallTo(() => paramDef.Precedence).Returns(new TemplateParameterPrecedence(PrecedenceDefinition.Optional));

        var paramList = new List<ITemplateParameter> { paramDef };
        var paramDefs = A.Fake<IParameterDefinitionSet>();
        A.CallTo(() => paramDefs.GetEnumerator()).ReturnsLazily(() => paramList.GetEnumerator());

        A.CallTo(() => template.ParameterDefinitions).Returns(paramDefs);

        return template;
    }
}
