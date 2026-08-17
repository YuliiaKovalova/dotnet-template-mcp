// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using Xunit;

namespace Microsoft.TemplateEngine.MCP.Tests;

/// <summary>
/// Covers the environment-variable escape hatches added alongside the new safe defaults, so an
/// operator can restore the previous behavior without forking.
/// </summary>
[Collection("EnvironmentVariables")]
public class SafetyFeatureFlagTests : IDisposable
{
    private readonly Dictionary<string, string?> _saved = new();

    private void SetEnv(string name, string? value)
    {
        if (!_saved.ContainsKey(name))
        {
            _saved[name] = Environment.GetEnvironmentVariable(name);
        }

        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose()
    {
        foreach (var (name, value) in _saved)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Defaults_AreSafe()
    {
        var flags = new McpFeatureFlags();

        Assert.True(flags.PostActionsEnabled);
        Assert.False(flags.ResolveLatestVersionsByDefault);
        Assert.True(flags.WorkspaceEnforcementEnabled);
        Assert.Null(flags.HttpAuthToken);
        Assert.False(flags.HttpAllowAnonymous);
        Assert.False(flags.HttpAuthenticationRequired);
        Assert.Equal(120, flags.HttpRateLimitPerMinute);
    }

    [Fact]
    public void WorkspaceRoot_DefaultsToCurrentDirectory()
    {
        SetEnv(McpFeatureFlags.WorkspaceRootEnvVar, null);

        Assert.Equal(Environment.CurrentDirectory, McpFeatureFlags.FromEnvironment([]).WorkspaceRoot);
    }

    [Fact]
    public void WorkspaceRoot_ReadsEnvironmentVariable()
    {
        SetEnv(McpFeatureFlags.WorkspaceRootEnvVar, Path.GetTempPath());

        Assert.Equal(Path.GetTempPath(), McpFeatureFlags.FromEnvironment([]).WorkspaceRoot);
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    [InlineData("0", false)]
    [InlineData("true", true)]
    [InlineData(null, true)]
    public void PostActions_CanBeDisabled(string? value, bool expected)
    {
        SetEnv(McpFeatureFlags.PostActionsEnvVar, value);

        Assert.Equal(expected, McpFeatureFlags.FromEnvironment([]).PostActionsEnabled);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    public void ResolveLatestVersions_OptInOnly(string? value, bool expected)
    {
        SetEnv(McpFeatureFlags.ResolveLatestVersionsEnvVar, value);

        Assert.Equal(expected, McpFeatureFlags.FromEnvironment([]).ResolveLatestVersionsByDefault);
    }

    [Fact]
    public void WorkspaceEnforcement_CanBeDisabled()
    {
        SetEnv(McpFeatureFlags.WorkspaceEnforcementEnvVar, "false");

        Assert.False(McpFeatureFlags.FromEnvironment([]).WorkspaceEnforcementEnabled);
    }

    [Fact]
    public void HttpAuthentication_RequiredWhenTokenSet()
    {
        SetEnv(McpFeatureFlags.HttpAuthTokenEnvVar, "abc");

        var flags = McpFeatureFlags.FromEnvironment([]);

        Assert.Equal("abc", flags.HttpAuthToken);
        Assert.True(flags.HttpAuthenticationRequired);
    }

    [Fact]
    public void HttpAuthentication_WhitespaceTokenIsTreatedAsUnset()
    {
        SetEnv(McpFeatureFlags.HttpAuthTokenEnvVar, "   ");

        var flags = McpFeatureFlags.FromEnvironment([]);

        Assert.Null(flags.HttpAuthToken);
        Assert.False(flags.HttpAuthenticationRequired);
    }

    [Fact]
    public void HttpRateLimit_ZeroDisablesLimiting()
    {
        SetEnv(McpFeatureFlags.HttpRateLimitEnvVar, "0");

        Assert.Equal(0, McpFeatureFlags.FromEnvironment([]).HttpRateLimitPerMinute);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("-5")]
    public void HttpRateLimit_InvalidValueFallsBackToDefault(string value)
    {
        SetEnv(McpFeatureFlags.HttpRateLimitEnvVar, value);

        Assert.Equal(120, McpFeatureFlags.FromEnvironment([]).HttpRateLimitPerMinute);
    }
}
