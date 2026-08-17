// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.Text.Json;
using FakeItEasy;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Parameters;
using Microsoft.TemplateEngine.MCP.Tools;
using Microsoft.TemplateEngine.Utils;
using Xunit;

namespace Microsoft.TemplateEngine.MCP.Tests;

public class TemplateInspectToolTests
{
    [Fact]
    public async Task InspectTemplate_ByShortName_ReturnsFullMetadata()
    {
        var template = CreateTemplateWithParameters("console", "Console App", "Microsoft.Console", language: "C#", type: "project");
        var engineService = CreateServiceWithTemplates(template);

        string result = await TemplateInspectTool.InspectTemplateAsync(engineService, "console");

        var json = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("Microsoft.Console", json.GetProperty("Identity").GetString());
        Assert.Equal("Console App", json.GetProperty("Name").GetString());
        Assert.Equal("C#", json.GetProperty("Language").GetString());
    }

    [Fact]
    public async Task InspectTemplate_ByIdentity_ReturnsFullMetadata()
    {
        var template = CreateTemplateWithParameters("console", "Console App", "Microsoft.Console", language: "C#");
        var engineService = CreateServiceWithTemplates(template);

        string result = await TemplateInspectTool.InspectTemplateAsync(engineService, "Microsoft.Console");

        var json = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("Microsoft.Console", json.GetProperty("Identity").GetString());
    }

    [Fact]
    public async Task InspectTemplate_NotFound_ReturnsError()
    {
        var template = CreateTemplateWithParameters("console", "Console App", "Microsoft.Console");
        var engineService = CreateServiceWithTemplates(template);

        string result = await TemplateInspectTool.InspectTemplateAsync(engineService, "nonexistent");

        var json = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.True(json.TryGetProperty("error", out _));
    }

    private static ITemplateInfo CreateTemplateWithParameters(
        string shortName,
        string name,
        string identity,
        string? language = null,
        string? type = null)
    {
        var template = A.Fake<ITemplateInfo>();
        A.CallTo(() => template.Identity).Returns(identity);
        A.CallTo(() => template.Name).Returns(name);
        A.CallTo(() => template.ShortNameList).Returns(new[] { shortName });
        A.CallTo(() => template.Classifications).Returns(Array.Empty<string>());
        A.CallTo(() => template.Constraints).Returns(Array.Empty<Abstractions.Constraints.TemplateConstraintInfo>());
        A.CallTo(() => template.PostActions).Returns(Array.Empty<Guid>());
        A.CallTo(() => template.BaselineInfo).Returns(new Dictionary<string, IBaselineInfo>());

        var parameters = new List<ITemplateParameter>
        {
            new TemplateParameter("Framework", "parameter", "choice",
                precedence: new TemplateParameterPrecedence(PrecedenceDefinition.Optional),
                choices: new Dictionary<string, ParameterChoice>
                {
                    ["net6.0"] = new ParameterChoice(null, null),
                    ["net8.0"] = new ParameterChoice(null, null),
                }),
        };
        A.CallTo(() => template.ParameterDefinitions).Returns(new ParameterDefinitionSet(parameters));

        var tags = new Dictionary<string, string>();
        if (language != null)
        {
            tags["language"] = language;
        }

        if (type != null)
        {
            tags["type"] = type;
        }

        A.CallTo(() => template.TagsCollection).Returns(tags);
        return template;
    }

    private static TemplateEngineService CreateServiceWithTemplates(params ITemplateInfo[] templates)
    {
        var service = A.Fake<TemplateEngineService>();
        A.CallTo(() => service.GetTemplatesAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<IReadOnlyList<ITemplateInfo>>(templates));
        return service;
    }
}
