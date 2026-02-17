// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FakeItEasy;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Parameters;
using Xunit;

namespace Microsoft.TemplateEngine.MCP.Tests;

public class SmartDefaultsTests
{
    [Fact]
    public void SuggestSmartDefaults_AotEnabled_SuggestsLatestFramework()
    {
        var template = CreateTemplateWithParams(
            ("EnableAot", "bool", null),
            ("Framework", "choice", new[] { "net8.0", "net9.0" }));
        var userParams = new Dictionary<string, string?> { { "EnableAot", "true" } };

        var suggestions = TemplateEngineService.SuggestSmartDefaults(template, userParams);

        Assert.Contains("Framework", suggestions.Keys);
        Assert.Equal("net9.0", suggestions["Framework"]);
    }

    [Fact]
    public void SuggestSmartDefaults_AotEnabled_FrameworkAlreadySet_DoesNotOverride()
    {
        var template = CreateTemplateWithParams(
            ("EnableAot", "bool", null),
            ("Framework", "choice", new[] { "net8.0", "net9.0" }));
        var userParams = new Dictionary<string, string?> { { "EnableAot", "true" }, { "Framework", "net8.0" } };

        var suggestions = TemplateEngineService.SuggestSmartDefaults(template, userParams);

        Assert.DoesNotContain("Framework", suggestions.Keys);
    }

    [Fact]
    public void SuggestSmartDefaults_AotDisabled_NoFrameworkSuggestion()
    {
        var template = CreateTemplateWithParams(
            ("EnableAot", "bool", null),
            ("Framework", "choice", new[] { "net8.0", "net9.0" }));
        var userParams = new Dictionary<string, string?> { { "EnableAot", "false" } };

        var suggestions = TemplateEngineService.SuggestSmartDefaults(template, userParams);

        Assert.DoesNotContain("Framework", suggestions.Keys);
    }

    [Fact]
    public void SuggestSmartDefaults_AuthSet_SetsNoHttpsFalse()
    {
        var template = CreateTemplateWithParams(
            ("auth", "choice", new[] { "None", "IndividualB2C", "SingleOrg" }),
            ("NoHttps", "bool", null));
        var userParams = new Dictionary<string, string?> { { "auth", "SingleOrg" } };

        var suggestions = TemplateEngineService.SuggestSmartDefaults(template, userParams);

        Assert.Contains("NoHttps", suggestions.Keys);
        Assert.Equal("false", suggestions["NoHttps"]);
    }

    [Fact]
    public void SuggestSmartDefaults_AuthNone_NoHttpsSuggestion()
    {
        var template = CreateTemplateWithParams(
            ("auth", "choice", new[] { "None", "SingleOrg" }),
            ("NoHttps", "bool", null));
        var userParams = new Dictionary<string, string?> { { "auth", "None" } };

        var suggestions = TemplateEngineService.SuggestSmartDefaults(template, userParams);

        Assert.DoesNotContain("NoHttps", suggestions.Keys);
    }

    [Fact]
    public void SuggestSmartDefaults_UseControllers_SetsMinimalApisFalse()
    {
        var template = CreateTemplateWithParams(
            ("UseControllers", "bool", null),
            ("UseMinimalAPIs", "bool", null));
        var userParams = new Dictionary<string, string?> { { "UseControllers", "true" } };

        var suggestions = TemplateEngineService.SuggestSmartDefaults(template, userParams);

        Assert.Contains("UseMinimalAPIs", suggestions.Keys);
        Assert.Equal("false", suggestions["UseMinimalAPIs"]);
    }

    [Fact]
    public void SuggestSmartDefaults_UseControllersFalse_NoMinimalApisSuggestion()
    {
        var template = CreateTemplateWithParams(
            ("UseControllers", "bool", null),
            ("UseMinimalAPIs", "bool", null));
        var userParams = new Dictionary<string, string?> { { "UseControllers", "false" } };

        var suggestions = TemplateEngineService.SuggestSmartDefaults(template, userParams);

        Assert.DoesNotContain("UseMinimalAPIs", suggestions.Keys);
    }

    [Fact]
    public void SuggestSmartDefaults_NoRelevantParams_ReturnsEmpty()
    {
        var template = CreateTemplateWithParams(
            ("SomeParam", "string", null));
        var userParams = new Dictionary<string, string?> { { "SomeParam", "value" } };

        var suggestions = TemplateEngineService.SuggestSmartDefaults(template, userParams);

        Assert.Empty(suggestions);
    }

    [Fact]
    public void SuggestSmartDefaults_PublishAotAlias_AlsoTriggers()
    {
        var template = CreateTemplateWithParams(
            ("PublishAot", "bool", null),
            ("Framework", "choice", new[] { "net8.0", "net9.0" }));
        var userParams = new Dictionary<string, string?> { { "PublishAot", "true" } };

        var suggestions = TemplateEngineService.SuggestSmartDefaults(template, userParams);

        Assert.Contains("Framework", suggestions.Keys);
        Assert.Equal("net9.0", suggestions["Framework"]);
    }

    private static ITemplateInfo CreateTemplateWithParams(params (string Name, string DataType, string[]? Choices)[] paramDefs)
    {
        var template = A.Fake<ITemplateInfo>();
        var paramList = new List<ITemplateParameter>();

        foreach (var (name, dataType, choices) in paramDefs)
        {
            var paramDef = A.Fake<ITemplateParameter>();
            A.CallTo(() => paramDef.Name).Returns(name);
            A.CallTo(() => paramDef.DataType).Returns(dataType);

            if (choices != null)
            {
                var choiceDict = choices.ToDictionary(c => c, c => new ParameterChoice(c, c));
                A.CallTo(() => paramDef.Choices).Returns(choiceDict);
            }
            else
            {
                A.CallTo(() => paramDef.Choices).Returns((IReadOnlyDictionary<string, ParameterChoice>?)null);
            }

            paramList.Add(paramDef);
        }

        var paramDefSet = A.Fake<IParameterDefinitionSet>();
        A.CallTo(() => paramDefSet.GetEnumerator()).ReturnsLazily(() => paramList.GetEnumerator());

        A.CallTo(() => template.ParameterDefinitions).Returns(paramDefSet);
        return template;
    }
}
