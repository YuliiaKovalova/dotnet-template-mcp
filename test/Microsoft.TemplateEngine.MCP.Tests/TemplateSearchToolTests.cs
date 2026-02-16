// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using FakeItEasy;
using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.MCP.Tools;
using Xunit;

namespace Microsoft.TemplateEngine.MCP.Tests;

public class TemplateSearchToolTests
{
    [Fact]
    public async Task SearchTemplates_ByName_ReturnsMatchingTemplates()
    {
        var engineService = CreateServiceWithTemplates(
            CreateTemplate("console", "Console App", "Microsoft.Console", language: "C#", type: "project", description: "A command-line application"),
            CreateTemplate("webapp", "ASP.NET Web App", "Microsoft.WebApp", language: "C#", type: "project", description: "An ASP.NET Core web application"),
            CreateTemplate("classlib", "Class Library", "Microsoft.ClassLib", language: "C#", type: "project", description: "A class library"));

        string result = await TemplateSearchTool.SearchTemplatesAsync(engineService, "console");

        var items = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("Microsoft.Console", items[0].GetProperty("Identity").GetString());
    }

    [Fact]
    public async Task SearchTemplates_ByLanguage_FiltersCorrectly()
    {
        var engineService = CreateServiceWithTemplates(
            CreateTemplate("console", "Console App C#", "Microsoft.Console.CSharp", language: "C#", type: "project"),
            CreateTemplate("console", "Console App F#", "Microsoft.Console.FSharp", language: "F#", type: "project"));

        string result = await TemplateSearchTool.SearchTemplatesAsync(engineService, "console", language: "F#");

        var items = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("Microsoft.Console.FSharp", items[0].GetProperty("Identity").GetString());
    }

    [Fact]
    public async Task SearchTemplates_ByType_FiltersCorrectly()
    {
        var engineService = CreateServiceWithTemplates(
            CreateTemplate("console", "Console App", "Microsoft.Console", language: "C#", type: "project"),
            CreateTemplate("gitignore", "gitignore file", "Microsoft.gitignore", type: "item"));

        string result = await TemplateSearchTool.SearchTemplatesAsync(engineService, "git", type: "item");

        var items = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("Microsoft.gitignore", items[0].GetProperty("Identity").GetString());
    }

    [Fact]
    public async Task SearchTemplates_NoMatches_ReturnsEmptyArray()
    {
        var engineService = CreateServiceWithTemplates(
            CreateTemplate("console", "Console App", "Microsoft.Console"));

        string result = await TemplateSearchTool.SearchTemplatesAsync(engineService, "nonexistent");

        var items = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal(0, items.GetArrayLength());
    }

    private static ITemplateInfo CreateTemplate(
        string shortName,
        string name,
        string identity,
        string? language = null,
        string? type = null,
        string? description = null)
    {
        var template = A.Fake<ITemplateInfo>();
        A.CallTo(() => template.Identity).Returns(identity);
        A.CallTo(() => template.Name).Returns(name);
        A.CallTo(() => template.ShortNameList).Returns(new[] { shortName });
        A.CallTo(() => template.Description).Returns(description);
        A.CallTo(() => template.Classifications).Returns(Array.Empty<string>());

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
