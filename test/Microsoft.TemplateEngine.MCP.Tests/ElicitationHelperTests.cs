// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FakeItEasy;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Parameters;
using Microsoft.TemplateEngine.MCP.Host;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Microsoft.TemplateEngine.MCP.Tests;

public class ElicitationHelperTests
{
    [Fact]
    public void GetMissingRequiredParameters_ReturnsOnlyMissing()
    {
        // Arrange
        var reqParam = CreateParameter("Framework", "string", PrecedenceDefinition.Required);
        var optParam = CreateParameter("EnableDocker", "bool", PrecedenceDefinition.Optional);
        var reqProvided = CreateParameter("Auth", "string", PrecedenceDefinition.Required);

        var template = A.Fake<ITemplateInfo>();
        A.CallTo(() => template.ParameterDefinitions).Returns(
            new ParameterDefinitionSet([reqParam, optParam, reqProvided]));

        var existing = new Dictionary<string, string?> { ["Auth"] = "Individual" };

        // Act
        var missing = ElicitationHelper.GetMissingRequiredParameters(template, existing);

        // Assert
        Assert.Single(missing);
        Assert.Equal("Framework", missing[0].Name);
    }

    [Fact]
    public void GetMissingRequiredParameters_SkipsInternalParameters()
    {
        // Arrange — parameters starting with _ or named "name" are internal
        var internalParam = CreateParameter("_skipme", "string", PrecedenceDefinition.Required);
        var nameParam = CreateParameter("name", "string", PrecedenceDefinition.Required);
        var userParam = CreateParameter("Framework", "string", PrecedenceDefinition.Required);

        var template = A.Fake<ITemplateInfo>();
        A.CallTo(() => template.ParameterDefinitions).Returns(
            new ParameterDefinitionSet([internalParam, nameParam, userParam]));

        // Act
        var missing = ElicitationHelper.GetMissingRequiredParameters(template, new Dictionary<string, string?>());

        // Assert
        Assert.Single(missing);
        Assert.Equal("Framework", missing[0].Name);
    }

    [Fact]
    public void GetMissingRequiredParameters_EmptyWhenAllProvided()
    {
        // Arrange
        var reqParam = CreateParameter("Framework", "string", PrecedenceDefinition.Required);
        var template = A.Fake<ITemplateInfo>();
        A.CallTo(() => template.ParameterDefinitions).Returns(
            new ParameterDefinitionSet([reqParam]));

        var existing = new Dictionary<string, string?> { ["Framework"] = "net8.0" };

        // Act
        var missing = ElicitationHelper.GetMissingRequiredParameters(template, existing);

        // Assert
        Assert.Empty(missing);
    }

    [Fact]
    public void BuildSchemaFromParameters_MapsStringParam()
    {
        var param = CreateParameter("ProjectName", "string", PrecedenceDefinition.Required, defaultValue: "MyApp");
        var schema = ElicitationHelper.BuildSchemaFromParameters(
            A.Fake<ITemplateInfo>(), [param]);

        Assert.Single(schema.Properties);
        Assert.True(schema.Properties.ContainsKey("ProjectName"));
        Assert.IsType<ElicitRequestParams.StringSchema>(schema.Properties["ProjectName"]);

        var strSchema = (ElicitRequestParams.StringSchema)schema.Properties["ProjectName"];
        Assert.Equal("MyApp", strSchema.Default);
    }

    [Fact]
    public void BuildSchemaFromParameters_MapsBoolParam()
    {
        var param = CreateParameter("EnableAot", "bool", PrecedenceDefinition.Required, defaultValue: "true");
        var schema = ElicitationHelper.BuildSchemaFromParameters(
            A.Fake<ITemplateInfo>(), [param]);

        Assert.Single(schema.Properties);
        Assert.IsType<ElicitRequestParams.BooleanSchema>(schema.Properties["EnableAot"]);

        var boolSchema = (ElicitRequestParams.BooleanSchema)schema.Properties["EnableAot"];
        Assert.Equal(true, boolSchema.Default);
    }

    [Fact]
    public void BuildSchemaFromParameters_MapsChoiceParam()
    {
        var param = CreateParameter("Framework", "string", PrecedenceDefinition.Required,
            choices: new Dictionary<string, ParameterChoice>
            {
                ["net8.0"] = new ParameterChoice("net8.0", ".NET 8"),
                ["net9.0"] = new ParameterChoice("net9.0", ".NET 9"),
            });

        var schema = ElicitationHelper.BuildSchemaFromParameters(
            A.Fake<ITemplateInfo>(), [param]);

        Assert.Single(schema.Properties);
        Assert.IsType<ElicitRequestParams.UntitledSingleSelectEnumSchema>(schema.Properties["Framework"]);

        var enumSchema = (ElicitRequestParams.UntitledSingleSelectEnumSchema)schema.Properties["Framework"];
        Assert.Contains("net8.0", enumSchema.Enum);
        Assert.Contains("net9.0", enumSchema.Enum);
    }

    [Fact]
    public void BuildSchemaFromParameters_MapsNumberParam()
    {
        var param = CreateParameter("Port", "integer", PrecedenceDefinition.Required, defaultValue: "5000");
        var schema = ElicitationHelper.BuildSchemaFromParameters(
            A.Fake<ITemplateInfo>(), [param]);

        Assert.Single(schema.Properties);
        Assert.IsType<ElicitRequestParams.NumberSchema>(schema.Properties["Port"]);

        var numSchema = (ElicitRequestParams.NumberSchema)schema.Properties["Port"];
        Assert.Equal(5000.0, numSchema.Default);
    }

    [Fact]
    public void BuildSchemaFromParameters_MultipleParams()
    {
        var templateParams = new List<ITemplateParameter>
        {
            CreateParameter("ProjectName", "string", PrecedenceDefinition.Required),
            CreateParameter("EnableDocker", "bool", PrecedenceDefinition.Required),
            CreateParameter("Port", "integer", PrecedenceDefinition.Required),
        };

        var schema = ElicitationHelper.BuildSchemaFromParameters(
            A.Fake<ITemplateInfo>(), templateParams);

        Assert.Equal(3, schema.Properties.Count);
    }

    private static ITemplateParameter CreateParameter(
        string name,
        string dataType,
        PrecedenceDefinition precedence,
        string? defaultValue = null,
        IDictionary<string, ParameterChoice>? choices = null)
    {
        var param = A.Fake<ITemplateParameter>();
        A.CallTo(() => param.Name).Returns(name);
        A.CallTo(() => param.DataType).Returns(dataType);
        A.CallTo(() => param.DefaultValue).Returns(defaultValue);
        A.CallTo(() => param.Description).Returns($"The {name} parameter");
        A.CallTo(() => param.DisplayName).Returns(name);
        A.CallTo(() => param.Precedence).Returns(
            new TemplateParameterPrecedence(precedence));
        A.CallTo(() => param.Choices).Returns(
            choices as IReadOnlyDictionary<string, ParameterChoice>
            ?? (choices != null ? new Dictionary<string, ParameterChoice>(choices) : null));

        return param;
    }

    /// <summary>
    /// Simple wrapper for IParameterDefinitionSet for testing.
    /// </summary>
    private class TestParameterDefinitionSet : ParameterDefinitionSet
    {
        public TestParameterDefinitionSet(IEnumerable<ITemplateParameter> parameters) : base(parameters)
        {
        }
    }
}
