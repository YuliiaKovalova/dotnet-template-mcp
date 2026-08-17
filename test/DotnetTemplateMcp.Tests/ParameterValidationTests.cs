// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.Text.Json;
using FakeItEasy;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Constraints;
using Microsoft.TemplateEngine.Abstractions.Parameters;
using Xunit;

namespace DotnetTemplateMcp.Tests;

public class ParameterValidationTests
{
    [Fact]
    public void ValidateParameters_ValidChoiceValue_ReturnsNoErrors()
    {
        var template = CreateTemplateWithChoiceParam("Framework", new[] { "net8.0", "net9.0" });
        var parameters = new Dictionary<string, string?> { { "Framework", "net8.0" } };

        var errors = TemplateEngineService.ValidateParameters(template, parameters);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateParameters_InvalidChoiceValue_ReturnsError()
    {
        var template = CreateTemplateWithChoiceParam("Framework", new[] { "net8.0", "net9.0" });
        var parameters = new Dictionary<string, string?> { { "Framework", "net3.0" } };

        var errors = TemplateEngineService.ValidateParameters(template, parameters);

        Assert.Single(errors);
        Assert.Contains("Invalid value 'net3.0'", errors[0]);
        Assert.Contains("net8.0", errors[0]);
        Assert.Contains("net9.0", errors[0]);
    }

    [Fact]
    public void ValidateParameters_UnknownParameter_ReturnsError()
    {
        var template = CreateTemplateWithChoiceParam("Framework", new[] { "net8.0" });
        var parameters = new Dictionary<string, string?> { { "NonExistent", "value" } };

        var errors = TemplateEngineService.ValidateParameters(template, parameters);

        Assert.Single(errors);
        Assert.Contains("Unknown parameter 'NonExistent'", errors[0]);
    }

    [Fact]
    public void ValidateParameters_InvalidBoolValue_ReturnsError()
    {
        var template = CreateTemplateWithBoolParam("EnableAot");
        var parameters = new Dictionary<string, string?> { { "EnableAot", "yes" } };

        var errors = TemplateEngineService.ValidateParameters(template, parameters);

        Assert.Single(errors);
        Assert.Contains("boolean value", errors[0]);
    }

    [Fact]
    public void ValidateParameters_ValidBoolValue_ReturnsNoErrors()
    {
        var template = CreateTemplateWithBoolParam("EnableAot");
        var parameters = new Dictionary<string, string?> { { "EnableAot", "true" } };

        var errors = TemplateEngineService.ValidateParameters(template, parameters);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateParameters_InvalidIntegerValue_ReturnsError()
    {
        var template = CreateTemplateWithParam("Port", "int");
        var parameters = new Dictionary<string, string?> { { "Port", "abc" } };

        var errors = TemplateEngineService.ValidateParameters(template, parameters);

        Assert.Single(errors);
        Assert.Contains("integer value", errors[0]);
    }

    [Fact]
    public void ValidateParameters_NullValue_NoError()
    {
        var template = CreateTemplateWithBoolParam("EnableAot");
        var parameters = new Dictionary<string, string?> { { "EnableAot", null } };

        var errors = TemplateEngineService.ValidateParameters(template, parameters);

        Assert.Empty(errors);
    }

    [Fact]
    public void CheckConstraints_NoConstraints_ReturnsNoWarnings()
    {
        var template = A.Fake<ITemplateInfo>();
        A.CallTo(() => template.Constraints).Returns(Array.Empty<TemplateConstraintInfo>());

        var warnings = TemplateEngineService.CheckConstraints(template);

        Assert.Empty(warnings);
    }

    [Fact]
    public void CheckConstraints_WorkloadConstraint_ReturnsWarning()
    {
        var template = A.Fake<ITemplateInfo>();
        var constraint = new TemplateConstraintInfo("workload", "[\"maui\"]");
        A.CallTo(() => template.Constraints).Returns(new[] { constraint });

        var warnings = TemplateEngineService.CheckConstraints(template);

        Assert.Single(warnings);
        Assert.Contains("workload", warnings[0]);
    }

    [Fact]
    public void CheckConstraints_SdkVersionConstraint_ReturnsWarning()
    {
        var template = A.Fake<ITemplateInfo>();
        var constraint = new TemplateConstraintInfo("sdk-version", "[8.0,)");
        A.CallTo(() => template.Constraints).Returns(new[] { constraint });

        var warnings = TemplateEngineService.CheckConstraints(template);

        Assert.Single(warnings);
        Assert.Contains("SDK version", warnings[0]);
    }

    private static ITemplateInfo CreateTemplateWithChoiceParam(string paramName, string[] choices)
    {
        var template = A.Fake<ITemplateInfo>();

        var paramDef = A.Fake<ITemplateParameter>();
        A.CallTo(() => paramDef.Name).Returns(paramName);
        A.CallTo(() => paramDef.DataType).Returns("choice");

        var choiceDict = choices.ToDictionary(c => c, c => new ParameterChoice(c, c));
        A.CallTo(() => paramDef.Choices).Returns(choiceDict);

        var paramList = new List<ITemplateParameter> { paramDef };
        var paramDefs = A.Fake<IParameterDefinitionSet>();
        A.CallTo(() => paramDefs.GetEnumerator()).ReturnsLazily(() => paramList.GetEnumerator());

        A.CallTo(() => template.ParameterDefinitions).Returns(paramDefs);

        return template;
    }

    private static ITemplateInfo CreateTemplateWithBoolParam(string paramName)
    {
        return CreateTemplateWithParam(paramName, "bool");
    }

    private static ITemplateInfo CreateTemplateWithParam(string paramName, string dataType)
    {
        var template = A.Fake<ITemplateInfo>();

        var paramDef = A.Fake<ITemplateParameter>();
        A.CallTo(() => paramDef.Name).Returns(paramName);
        A.CallTo(() => paramDef.DataType).Returns(dataType);
        A.CallTo(() => paramDef.Choices).Returns((IReadOnlyDictionary<string, ParameterChoice>?)null);

        var paramList = new List<ITemplateParameter> { paramDef };
        var paramDefs = A.Fake<IParameterDefinitionSet>();
        A.CallTo(() => paramDefs.GetEnumerator()).ReturnsLazily(() => paramList.GetEnumerator());

        A.CallTo(() => template.ParameterDefinitions).Returns(paramDefs);

        return template;
    }
}
