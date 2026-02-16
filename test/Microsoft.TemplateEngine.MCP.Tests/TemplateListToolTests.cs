// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using FakeItEasy;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.MCP.Tools;
using Xunit;

namespace Microsoft.TemplateEngine.MCP.Tests;

public class TemplateListToolTests
{
    [Fact]
    public async Task ListTemplates_NoFilters_ReturnsAllTemplates()
    {
        var engineService = CreateServiceWithTemplates(
            CreateTemplate("console", "Console App", "Microsoft.Console", language: "C#", type: "project"),
            CreateTemplate("classlib", "Class Library", "Microsoft.ClassLib", language: "C#", type: "project"));

        string result = await TemplateListTool.ListTemplatesAsync(engineService);

        var items = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal(2, items.GetArrayLength());
    }

    [Fact]
    public async Task ListTemplates_ByLanguage_FiltersCorrectly()
    {
        var engineService = CreateServiceWithTemplates(
            CreateTemplate("console", "Console App", "Microsoft.Console.CSharp", language: "C#"),
            CreateTemplate("console", "Console App F#", "Microsoft.Console.FSharp", language: "F#"));

        string result = await TemplateListTool.ListTemplatesAsync(engineService, language: "C#");

        var items = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("Microsoft.Console.CSharp", items[0].GetProperty("Identity").GetString());
    }

    [Fact]
    public async Task ListTemplates_ByClassification_FiltersCorrectly()
    {
        var engineService = CreateServiceWithTemplates(
            CreateTemplate("webapp", "Web App", "Microsoft.WebApp", classifications: new[] { "Web", "Cloud" }),
            CreateTemplate("console", "Console App", "Microsoft.Console", classifications: new[] { "Console" }));

        string result = await TemplateListTool.ListTemplatesAsync(engineService, classification: "Web");

        var items = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("Microsoft.WebApp", items[0].GetProperty("Identity").GetString());
    }

    private static ITemplateInfo CreateTemplate(
        string shortName,
        string name,
        string identity,
        string? language = null,
        string? type = null,
        string[]? classifications = null)
    {
        var template = A.Fake<ITemplateInfo>();
        A.CallTo(() => template.Identity).Returns(identity);
        A.CallTo(() => template.Name).Returns(name);
        A.CallTo(() => template.ShortNameList).Returns(new[] { shortName });
        A.CallTo(() => template.Classifications).Returns(classifications ?? Array.Empty<string>());

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
