// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

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

    [Fact]
    public void DefaultTransport_IsStdio()
    {
        var original = Environment.GetEnvironmentVariable(McpFeatureFlags.TransportEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(McpFeatureFlags.TransportEnvVar, null);
            var flags = McpFeatureFlags.FromEnvironment([]);
            Assert.Equal(TransportMode.Stdio, flags.Transport);
        }
        finally
        {
            Environment.SetEnvironmentVariable(McpFeatureFlags.TransportEnvVar, original);
        }
    }

    [Fact]
    public void Transport_Http_ViaEnvironmentVariable()
    {
        var original = Environment.GetEnvironmentVariable(McpFeatureFlags.TransportEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(McpFeatureFlags.TransportEnvVar, "http");
            var flags = McpFeatureFlags.FromEnvironment([]);
            Assert.Equal(TransportMode.Http, flags.Transport);
        }
        finally
        {
            Environment.SetEnvironmentVariable(McpFeatureFlags.TransportEnvVar, original);
        }
    }

    [Fact]
    public void Transport_Http_ViaCommandLineArg()
    {
        var original = Environment.GetEnvironmentVariable(McpFeatureFlags.TransportEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(McpFeatureFlags.TransportEnvVar, null);
            var flags = McpFeatureFlags.FromEnvironment(["--transport", "http"]);
            Assert.Equal(TransportMode.Http, flags.Transport);
        }
        finally
        {
            Environment.SetEnvironmentVariable(McpFeatureFlags.TransportEnvVar, original);
        }
    }

    [Fact]
    public void Elicitation_EnabledByDefault()
    {
        var original = Environment.GetEnvironmentVariable(McpFeatureFlags.ElicitationEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(McpFeatureFlags.ElicitationEnvVar, null);
            var flags = McpFeatureFlags.FromEnvironment();
            Assert.True(flags.ElicitationEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable(McpFeatureFlags.ElicitationEnvVar, original);
        }
    }

    [Fact]
    public void Elicitation_DisabledWhenSetToFalse()
    {
        var original = Environment.GetEnvironmentVariable(McpFeatureFlags.ElicitationEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(McpFeatureFlags.ElicitationEnvVar, "false");
            var flags = McpFeatureFlags.FromEnvironment();
            Assert.False(flags.ElicitationEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable(McpFeatureFlags.ElicitationEnvVar, original);
        }
    }

    [Fact]
    public void HttpUrl_DefaultValue()
    {
        var original = Environment.GetEnvironmentVariable(McpFeatureFlags.HttpUrlEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(McpFeatureFlags.HttpUrlEnvVar, null);
            var flags = McpFeatureFlags.FromEnvironment();
            Assert.Equal("http://localhost:5005", flags.HttpUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable(McpFeatureFlags.HttpUrlEnvVar, original);
        }
    }

    [Fact]
    public void HttpUrl_FromEnvironmentVariable()
    {
        var original = Environment.GetEnvironmentVariable(McpFeatureFlags.HttpUrlEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(McpFeatureFlags.HttpUrlEnvVar, "http://0.0.0.0:8080");
            var flags = McpFeatureFlags.FromEnvironment();
            Assert.Equal("http://0.0.0.0:8080", flags.HttpUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable(McpFeatureFlags.HttpUrlEnvVar, original);
        }
    }
}
