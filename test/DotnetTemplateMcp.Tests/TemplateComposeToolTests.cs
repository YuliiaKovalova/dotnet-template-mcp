// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.Text.Json;
using Xunit;

namespace DotnetTemplateMcp.Tests;

public class TemplateComposeToolTests
{
    [Fact]
    public void ComposeStep_DeserializesFromJson()
    {
        var json = """
        [
            {"templateName": "console", "name": "MyApp"},
            {"templateName": "gitignore", "target": ".", "name": null}
        ]
        """;

        var steps = JsonSerializer.Deserialize<List<Host.ComposeStep>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        Assert.NotNull(steps);
        Assert.Equal(2, steps!.Count);
        Assert.Equal("console", steps[0].TemplateName);
        Assert.Equal("MyApp", steps[0].Name);
        Assert.Equal("gitignore", steps[1].TemplateName);
        Assert.Equal(".", steps[1].Target);
    }

    [Fact]
    public void ComposeStep_EmptyArray_Deserializes()
    {
        var json = "[]";

        var steps = JsonSerializer.Deserialize<List<Host.ComposeStep>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        Assert.NotNull(steps);
        Assert.Empty(steps!);
    }

    [Fact]
    public void ComposeStep_WithParameters_Deserializes()
    {
        var json = """
        [
            {
                "templateName": "webapi",
                "name": "MyApi",
                "outputPath": "C:\\projects\\MyApi",
                "parametersJson": "{\"Framework\": \"net9.0\", \"EnableAot\": \"true\"}"
            }
        ]
        """;

        var steps = JsonSerializer.Deserialize<List<Host.ComposeStep>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        Assert.NotNull(steps);
        Assert.Single(steps!);
        Assert.Equal("webapi", steps![0].TemplateName);
        Assert.Equal("MyApi", steps[0].Name);
        Assert.NotNull(steps[0].ParametersJson);
    }
}
