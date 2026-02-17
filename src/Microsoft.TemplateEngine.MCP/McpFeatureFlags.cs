// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.TemplateEngine.MCP;

/// <summary>
/// Feature flags for enabling/disabling MCP server capabilities.
/// Controlled via environment variables or configuration.
/// </summary>
internal sealed class McpFeatureFlags
{
    /// <summary>
    /// Environment variable name to enable/disable intent resolution.
    /// Set to "false" or "0" to disable. Enabled by default.
    /// </summary>
    public const string IntentResolutionEnvVar = "MCP_TEMPLATE_INTENT_RESOLUTION";

    /// <summary>
    /// Whether intent resolution tools (template_from_intent, create_from_description) are enabled.
    /// </summary>
    public bool IntentResolutionEnabled { get; init; } = true;

    /// <summary>
    /// Load feature flags from environment variables.
    /// </summary>
    public static McpFeatureFlags FromEnvironment()
    {
        return new McpFeatureFlags
        {
            IntentResolutionEnabled = IsEnabled(IntentResolutionEnvVar, defaultValue: true),
        };
    }

    private static bool IsEnabled(string envVar, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrEmpty(value))
        {
            return defaultValue;
        }

        return value switch
        {
            "0" or "false" or "False" or "FALSE" or "no" or "No" or "NO" or "off" or "Off" or "OFF" => false,
            _ => true,
        };
    }
}
