// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using DotnetTemplateMcp.Intent;
using Xunit;

namespace DotnetTemplateMcp.Tests;

public class IntentSynonymDictionaryTests
{
    [Theory]
    [InlineData("web api", "web api")]
    [InlineData("I want a web API project", "web api")]
    [InlineData("console app with .NET 9", "console app")]
    [InlineData("blazor server application", "blazor server")]
    [InlineData("create a grpc service", "grpc")]
    public void ExtractKeywords_FindsTemplateKeywords(string intent, string expectedKeyword)
    {
        var keywords = IntentSynonymDictionary.ExtractKeywords(intent);
        Assert.Contains(expectedKeyword, keywords, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("web API with authentication", "authentication")]
    [InlineData("console app with native aot", "native aot")]
    [InlineData("project with controllers", "controllers")]
    [InlineData("app with docker support", "docker")]
    public void ExtractKeywords_FindsParameterKeywords(string intent, string expectedKeyword)
    {
        var keywords = IntentSynonymDictionary.ExtractKeywords(intent);
        Assert.Contains(expectedKeyword, keywords, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("C# web api", "C#")]
    [InlineData("fsharp console app", "fsharp")]
    [InlineData("visual basic library", "visual basic")]
    public void ExtractKeywords_FindsLanguageKeywords(string intent, string expectedKeyword)
    {
        var keywords = IntentSynonymDictionary.ExtractKeywords(intent);
        Assert.Contains(expectedKeyword, keywords, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractKeywords_PrefersLongerMatch()
    {
        // "web api" should be preferred over "web" and "api" individually
        var keywords = IntentSynonymDictionary.ExtractKeywords("web api project");

        Assert.Contains("web api", keywords, StringComparer.OrdinalIgnoreCase);
        // "web" and "api" individually should NOT appear since "web api" covers them
        Assert.DoesNotContain("web", keywords.Where(k =>
            k.Equals("web", StringComparison.OrdinalIgnoreCase)).ToList());
    }

    [Fact]
    public void ExtractKeywords_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(IntentSynonymDictionary.ExtractKeywords(""));
        Assert.Empty(IntentSynonymDictionary.ExtractKeywords("   "));
    }

    [Fact]
    public void ExtractKeywords_NoMatch_ReturnsEmpty()
    {
        var keywords = IntentSynonymDictionary.ExtractKeywords("xyzzy foobar baz");
        Assert.Empty(keywords);
    }

    [Fact]
    public void ExtractKeywords_MultipleKeywords_AllFound()
    {
        var keywords = IntentSynonymDictionary.ExtractKeywords(
            "web api with authentication and controllers and docker");

        Assert.Contains("web api", keywords, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("authentication", keywords, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("controllers", keywords, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("docker", keywords, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TemplateKeywords_WebApiSynonyms_AllMapToWebapi()
    {
        var synonyms = new[] { "web api", "web service", "rest api", "restful" };
        foreach (var syn in synonyms)
        {
            Assert.True(IntentSynonymDictionary.TemplateKeywords.TryGetValue(syn, out var templates),
                $"Synonym '{syn}' not found");
            Assert.Contains("webapi", templates);
        }
    }

    [Fact]
    public void ParameterKeywords_AuthSynonyms_MapToAuthParam()
    {
        var authSynonyms = new[] { "authentication", "auth", "individual auth" };
        foreach (var syn in authSynonyms)
        {
            Assert.True(IntentSynonymDictionary.ParameterKeywords.TryGetValue(syn, out var param),
                $"Synonym '{syn}' not found");
            Assert.Equal("auth", param.ParameterName);
        }
    }

    [Fact]
    public void ParameterKeywords_FrameworkVersions_MapCorrectly()
    {
        Assert.Equal("net8.0", IntentSynonymDictionary.ParameterKeywords[".net 8"].Value);
        Assert.Equal("net9.0", IntentSynonymDictionary.ParameterKeywords[".net 9"].Value);
        Assert.Equal("net10.0", IntentSynonymDictionary.ParameterKeywords[".net 10"].Value);
    }

    [Fact]
    public void LanguageAliases_CoverCommonVariants()
    {
        Assert.Equal("C#", IntentSynonymDictionary.LanguageAliases["c#"]);
        Assert.Equal("C#", IntentSynonymDictionary.LanguageAliases["csharp"]);
        Assert.Equal("F#", IntentSynonymDictionary.LanguageAliases["f#"]);
        Assert.Equal("VB", IntentSynonymDictionary.LanguageAliases["vb"]);
        Assert.Equal("VB", IntentSynonymDictionary.LanguageAliases["visual basic"]);
    }
}
