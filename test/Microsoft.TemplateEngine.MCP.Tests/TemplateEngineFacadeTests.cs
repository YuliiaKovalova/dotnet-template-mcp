// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using FakeItEasy;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Constraints;
using Microsoft.TemplateEngine.Abstractions.Parameters;
using Microsoft.TemplateEngine.MCP.Host;
using Xunit;

namespace Microsoft.TemplateEngine.MCP.Tests;

public class TemplateEngineFacadeTests
{
    // ── Parameter Suggestions ──

    [Fact]
    public void GetParameterSuggestions_AotEnabled_SuggestsFrameworkWithRationale()
    {
        var template = CreateTemplateWithParams(
            ("EnableAot", "bool", null),
            ("Framework", "choice", new[] { "net8.0", "net9.0" }));
        var userParams = new Dictionary<string, string?> { { "EnableAot", "true" } };

        var suggestions = TemplateEngineFacade.GetParameterSuggestions(template, userParams);

        var frameworkSuggestion = Assert.Single(suggestions, s => s.ParameterName == "Framework");
        Assert.Equal("net9.0", frameworkSuggestion.SuggestedValue);
        Assert.Contains("NativeAOT", frameworkSuggestion.Rationale);
    }

    [Fact]
    public void GetParameterSuggestions_AotEnabled_FrameworkAlreadySet_NoSuggestion()
    {
        var template = CreateTemplateWithParams(
            ("EnableAot", "bool", null),
            ("Framework", "choice", new[] { "net8.0", "net9.0" }));
        var userParams = new Dictionary<string, string?> { { "EnableAot", "true" }, { "Framework", "net8.0" } };

        var suggestions = TemplateEngineFacade.GetParameterSuggestions(template, userParams);

        Assert.DoesNotContain(suggestions, s => s.ParameterName == "Framework");
    }

    [Fact]
    public void GetParameterSuggestions_AuthSet_PicksNet10OverNet9()
    {
        // Regression: the auth-path framework suggestion previously sorted lexicographically,
        // wrongly picking net9.0 over net10.0.
        var template = CreateTemplateWithParams(
            ("auth", "choice", new[] { "None", "Individual" }),
            ("Framework", "choice", new[] { "net8.0", "net9.0", "net10.0" }));
        var userParams = new Dictionary<string, string?> { { "auth", "Individual" } };

        var suggestions = TemplateEngineFacade.GetParameterSuggestions(template, userParams);

        var frameworkSuggestion = Assert.Single(suggestions, s => s.ParameterName == "Framework");
        Assert.Equal("net10.0", frameworkSuggestion.SuggestedValue);
    }

    [Fact]
    public void GetParameterSuggestions_AuthSet_SuggestsNoHttpsFalseWithRationale()
    {
        var template = CreateTemplateWithParams(
            ("auth", "choice", new[] { "None", "Individual", "SingleOrg" }),
            ("NoHttps", "bool", null));
        var userParams = new Dictionary<string, string?> { { "auth", "Individual" } };

        var suggestions = TemplateEngineFacade.GetParameterSuggestions(template, userParams);

        var httpsSuggestion = Assert.Single(suggestions, s => s.ParameterName == "NoHttps");
        Assert.Equal("false", httpsSuggestion.SuggestedValue);
        Assert.Contains("Authentication", httpsSuggestion.Rationale);
    }

    [Fact]
    public void GetParameterSuggestions_UseControllers_SuggestsMinimalApisFalse()
    {
        var template = CreateTemplateWithParams(
            ("UseControllers", "bool", null),
            ("UseMinimalAPIs", "bool", null));
        var userParams = new Dictionary<string, string?> { { "UseControllers", "true" } };

        var suggestions = TemplateEngineFacade.GetParameterSuggestions(template, userParams);

        var minimalSuggestion = Assert.Single(suggestions, s => s.ParameterName == "UseMinimalAPIs");
        Assert.Equal("false", minimalSuggestion.SuggestedValue);
        Assert.Contains("mutually exclusive", minimalSuggestion.Rationale);
    }

    [Fact]
    public void GetParameterSuggestions_DockerEnabled_SuggestsNoHttpsTrue()
    {
        var template = CreateTemplateWithParams(
            ("EnableDocker", "bool", null),
            ("NoHttps", "bool", null));
        var userParams = new Dictionary<string, string?> { { "EnableDocker", "true" } };

        var suggestions = TemplateEngineFacade.GetParameterSuggestions(template, userParams);

        var httpsSuggestion = Assert.Single(suggestions, s => s.ParameterName == "NoHttps");
        Assert.Equal("true", httpsSuggestion.SuggestedValue);
        Assert.Contains("reverse proxy", httpsSuggestion.Rationale);
    }

    [Fact]
    public void GetParameterSuggestions_NoRelevantParams_ReturnsEmpty()
    {
        var template = CreateTemplateWithParams(
            ("SomeParam", "string", null));
        var userParams = new Dictionary<string, string?> { { "SomeParam", "value" } };

        var suggestions = TemplateEngineFacade.GetParameterSuggestions(template, userParams);

        Assert.Empty(suggestions);
    }

    // ── Creation Effects Analysis ──

    [Fact]
    public void AnalyzeCreationEffects_ProducesAISummary()
    {
        var template = CreateTemplateWithTags("C#", "project");
        var result = CreateMockCreationResult(
            new[] { "Program.cs", "MyApp.csproj", "Properties/launchSettings.json" },
            new[] { ("Restore NuGet packages", "210D431B-A57B-423A-B3EB-B77A786CFA17") });

        var analysis = TemplateEngineFacade.AnalyzeCreationEffects(result, template);

        Assert.Contains("C#", analysis.Summary);
        Assert.Contains("project", analysis.Summary);
        Assert.Equal(3, analysis.TotalFiles);
        Assert.Equal(2, analysis.FilesByDirectory.Count); // (root) + Properties
        Assert.Contains(".cs", analysis.FileExtensions.Keys);
        Assert.Contains(".csproj", analysis.FileExtensions.Keys);
        Assert.Single(analysis.PostActions);
        Assert.Equal("Restore NuGet packages", analysis.PostActions[0].Description);
    }

    [Fact]
    public void AnalyzeCreationEffects_EmptyResult_HandlesGracefully()
    {
        var template = CreateTemplateWithTags("C#", "project");
        var result = CreateMockCreationResult(Array.Empty<string>(), Array.Empty<(string, string)>());

        var analysis = TemplateEngineFacade.AnalyzeCreationEffects(result, template);

        Assert.Equal(0, analysis.TotalFiles);
        Assert.Empty(analysis.PostActions);
        Assert.Contains("0 file(s)", analysis.Summary);
    }

    // ── Parameter Preparation ──

    [Fact]
    public void PrepareParameters_Valid_ReturnsSuccess()
    {
        var engineService = A.Fake<TemplateEngineService>();
        var facade = new TemplateEngineFacade(engineService);
        var template = CreateTemplateWithParams(
            ("Framework", "choice", new[] { "net8.0", "net9.0" }));

        var result = facade.PrepareParameters(template, "{\"Framework\": \"net8.0\"}");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Parameters);
        Assert.Equal("net8.0", result.Parameters!["Framework"]);
    }

    [Fact]
    public void PrepareParameters_InvalidParam_ReturnsFailed()
    {
        var engineService = A.Fake<TemplateEngineService>();
        var facade = new TemplateEngineFacade(engineService);
        var template = CreateTemplateWithParams(
            ("Framework", "choice", new[] { "net8.0", "net9.0" }));

        var result = facade.PrepareParameters(template, "{\"Framework\": \"net3.0\"}");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ValidationErrors);
        Assert.NotEmpty(result.ValidationErrors!);
    }

    [Fact]
    public void PrepareParameters_AppliesSmartDefaults()
    {
        var engineService = A.Fake<TemplateEngineService>();
        var facade = new TemplateEngineFacade(engineService);
        var template = CreateTemplateWithParams(
            ("EnableAot", "bool", null),
            ("Framework", "choice", new[] { "net8.0", "net9.0" }));

        var result = facade.PrepareParameters(template, "{\"EnableAot\": \"true\"}");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.SmartDefaults);
        Assert.Contains("Framework", result.SmartDefaults!.Keys);
        Assert.Equal("net9.0", result.SmartDefaults!["Framework"]);
        // Smart default should be merged into parameters
        Assert.Equal("net9.0", result.Parameters!["Framework"]);
    }

    [Fact]
    public void PrepareParameters_NullJson_ReturnsEmptyParams()
    {
        var engineService = A.Fake<TemplateEngineService>();
        var facade = new TemplateEngineFacade(engineService);
        var template = CreateTemplateWithParams(
            ("SomeParam", "string", null));

        var result = facade.PrepareParameters(template, null);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Parameters);
        Assert.Empty(result.Parameters!);
    }

    // ── Helpers ──

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
        A.CallTo(() => template.Constraints).Returns(Array.Empty<TemplateConstraintInfo>());
        A.CallTo(() => template.TagsCollection).Returns(new Dictionary<string, string>());

        return template;
    }

    private static ITemplateInfo CreateTemplateWithTags(string language, string type)
    {
        var template = A.Fake<ITemplateInfo>();
        var tags = new Dictionary<string, string>
        {
            { "language", language },
            { "type", type },
        };
        A.CallTo(() => template.TagsCollection).Returns(tags);
        return template;
    }

    private static Microsoft.TemplateEngine.Edge.Template.ITemplateCreationResult CreateMockCreationResult(
        string[] filePaths,
        (string Description, string ActionId)[] postActions)
    {
        var result = A.Fake<Microsoft.TemplateEngine.Edge.Template.ITemplateCreationResult>();

        // File changes
        var fileChanges = filePaths.Select(path =>
        {
            var fc = A.Fake<Microsoft.TemplateEngine.Abstractions.IFileChange2>();
            A.CallTo(() => fc.TargetRelativePath).Returns(path);
            A.CallTo(() => fc.ChangeKind).Returns(Microsoft.TemplateEngine.Abstractions.ChangeKind.Create);
            return (Microsoft.TemplateEngine.Abstractions.IFileChange)fc;
        }).ToList();

        var creationEffects = A.Fake<Microsoft.TemplateEngine.Abstractions.ICreationEffects>();
        A.CallTo(() => creationEffects.FileChanges).Returns(fileChanges);
        A.CallTo(() => result.CreationEffects).Returns(creationEffects);

        // Post-actions
        var postActionList = postActions.Select(pa =>
        {
            var action = A.Fake<Microsoft.TemplateEngine.Abstractions.IPostAction>();
            A.CallTo(() => action.Description).Returns(pa.Description);
            A.CallTo(() => action.ActionId).Returns(Guid.Parse(pa.ActionId));
            A.CallTo(() => action.ManualInstructions).Returns(string.Empty);
            A.CallTo(() => action.ContinueOnError).Returns(true);
            A.CallTo(() => action.Args).Returns(new Dictionary<string, string>());
            return action;
        }).ToList();

        var creationResult = A.Fake<Microsoft.TemplateEngine.Abstractions.ICreationResult>();
        A.CallTo(() => creationResult.PostActions).Returns(postActionList);
        A.CallTo(() => creationResult.PrimaryOutputs).Returns(new List<Microsoft.TemplateEngine.Abstractions.ICreationPath>());
        A.CallTo(() => result.CreationResult).Returns(creationResult);

        return result;
    }
}
