// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace Microsoft.TemplateEngine.MCP.Tests;

public class FeatureFlagsTests
{
    [Fact]
    public void FromEnvironment_DefaultsToEnabled()
    {
        // Clear the env var to test default
        var original = Environment.GetEnvironmentVariable(McpFeatureFlags.IntentResolutionEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(McpFeatureFlags.IntentResolutionEnvVar, null);
            var flags = McpFeatureFlags.FromEnvironment();
            Assert.True(flags.IntentResolutionEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable(McpFeatureFlags.IntentResolutionEnvVar, original);
        }
    }

    [Theory]
    [InlineData("false")]
    [InlineData("False")]
    [InlineData("FALSE")]
    [InlineData("0")]
    [InlineData("no")]
    [InlineData("off")]
    [InlineData("OFF")]
    public void FromEnvironment_DisablesWhenSetToFalsy(string value)
    {
        var original = Environment.GetEnvironmentVariable(McpFeatureFlags.IntentResolutionEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(McpFeatureFlags.IntentResolutionEnvVar, value);
            var flags = McpFeatureFlags.FromEnvironment();
            Assert.False(flags.IntentResolutionEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable(McpFeatureFlags.IntentResolutionEnvVar, original);
        }
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("on")]
    public void FromEnvironment_EnablesWhenSetToTruthy(string value)
    {
        var original = Environment.GetEnvironmentVariable(McpFeatureFlags.IntentResolutionEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(McpFeatureFlags.IntentResolutionEnvVar, value);
            var flags = McpFeatureFlags.FromEnvironment();
            Assert.True(flags.IntentResolutionEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable(McpFeatureFlags.IntentResolutionEnvVar, original);
        }
    }

    [Fact]
    public void EnvVarName_IsCorrect()
    {
        Assert.Equal("MCP_TEMPLATE_INTENT_RESOLUTION", McpFeatureFlags.IntentResolutionEnvVar);
    }
}
