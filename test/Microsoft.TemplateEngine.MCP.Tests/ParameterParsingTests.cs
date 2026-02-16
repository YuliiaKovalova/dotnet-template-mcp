// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.TemplateEngine.MCP.Tools;
using Xunit;

namespace Microsoft.TemplateEngine.MCP.Tests;

public class ParameterParsingTests
{
    [Fact]
    public void ParseParameters_ValidJson_ReturnsDictionary()
    {
        string json = """{"Framework": "net8.0", "EnableAot": "true"}""";

        var result = TemplateInstantiateTool.ParseParameters(json);

        Assert.Equal(2, result.Count);
        Assert.Equal("net8.0", result["Framework"]);
        Assert.Equal("true", result["EnableAot"]);
    }

    [Fact]
    public void ParseParameters_NullInput_ReturnsEmptyDictionary()
    {
        var result = TemplateInstantiateTool.ParseParameters(null);

        Assert.Empty(result);
    }

    [Fact]
    public void ParseParameters_EmptyString_ReturnsEmptyDictionary()
    {
        var result = TemplateInstantiateTool.ParseParameters(string.Empty);

        Assert.Empty(result);
    }

    [Fact]
    public void ParseParameters_InvalidJson_ReturnsEmptyDictionary()
    {
        var result = TemplateInstantiateTool.ParseParameters("not valid json");

        Assert.Empty(result);
    }

    [Fact]
    public void ParseParameters_NullValue_ReturnsNullEntry()
    {
        string json = """{"Framework": null}""";

        var result = TemplateInstantiateTool.ParseParameters(json);

        Assert.Single(result);
        Assert.Null(result["Framework"]);
    }

    [Fact]
    public void ParseParameters_BooleanValue_ConvertsToString()
    {
        string json = """{"EnableAot": true}""";

        var result = TemplateInstantiateTool.ParseParameters(json);

        Assert.Equal("True", result["EnableAot"]);
    }

    [Fact]
    public void ParseParameters_NumericValue_ConvertsToString()
    {
        string json = """{"Port": 8080}""";

        var result = TemplateInstantiateTool.ParseParameters(json);

        Assert.Equal("8080", result["Port"]);
    }
}
