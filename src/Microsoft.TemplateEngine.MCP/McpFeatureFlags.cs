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
    /// Environment variable to select the transport mode.
    /// Values: "stdio" (default), "http".
    /// </summary>
    public const string TransportEnvVar = "MCP_TEMPLATE_TRANSPORT";

    /// <summary>
    /// Environment variable for the HTTP listen URL when using HTTP transport.
    /// Default: "http://localhost:5005".
    /// </summary>
    public const string HttpUrlEnvVar = "MCP_TEMPLATE_HTTP_URL";

    /// <summary>
    /// Environment variable to enable/disable elicitation for interactive parameter collection.
    /// Enabled by default. Set to "false" or "0" to disable.
    /// </summary>
    public const string ElicitationEnvVar = "MCP_TEMPLATE_ELICITATION";

    /// <summary>
    /// Whether intent resolution tools (template_from_intent, create_from_description) are enabled.
    /// </summary>
    public bool IntentResolutionEnabled { get; init; } = true;

    /// <summary>
    /// The transport mode to use for the MCP server.
    /// </summary>
    public TransportMode Transport { get; init; } = TransportMode.Stdio;

    /// <summary>
    /// The URL to listen on when using HTTP transport.
    /// </summary>
    public string HttpUrl { get; init; } = "http://localhost:5005";

    /// <summary>
    /// Whether elicitation is enabled for interactive parameter collection.
    /// </summary>
    public bool ElicitationEnabled { get; init; } = true;

    /// <summary>
    /// Load feature flags from environment variables and command-line arguments.
    /// </summary>
    public static McpFeatureFlags FromEnvironment(string[] args)
    {
        return new McpFeatureFlags
        {
            IntentResolutionEnabled = IsEnabled(IntentResolutionEnvVar, defaultValue: true),
            Transport = GetTransportMode(args),
            HttpUrl = Environment.GetEnvironmentVariable(HttpUrlEnvVar) ?? "http://localhost:5005",
            ElicitationEnabled = IsEnabled(ElicitationEnvVar, defaultValue: true),
        };
    }

    /// <summary>
    /// Load feature flags from environment variables only (backward-compatible overload).
    /// </summary>
    public static McpFeatureFlags FromEnvironment()
    {
        return FromEnvironment([]);
    }

    private static TransportMode GetTransportMode(string[] args)
    {
        // Check command-line: --transport http
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--transport", StringComparison.OrdinalIgnoreCase) &&
                args[i + 1].Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                return TransportMode.Http;
            }
        }

        // Check environment variable
        var envValue = Environment.GetEnvironmentVariable(TransportEnvVar);
        if (!string.IsNullOrEmpty(envValue) &&
            envValue.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            return TransportMode.Http;
        }

        return TransportMode.Stdio;
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

/// <summary>
/// Supported MCP transport modes.
/// </summary>
internal enum TransportMode
{
    /// <summary>Standard I/O transport (default, for CLI and local tool usage).</summary>
    Stdio,

    /// <summary>HTTP transport with streamable HTTP support (for remote, cloud, and multi-tenant deployment).</summary>
    Http,
}
