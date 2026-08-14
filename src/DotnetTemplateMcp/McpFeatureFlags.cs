// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

namespace DotnetTemplateMcp;

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
    /// Environment variable to select the tool profile.
    /// Values: "full" (default, all 15 tools), "lite" (5 core tools only).
    /// Lite mode reduces tool count to minimize agent confusion and context overhead.
    /// </summary>
    public const string ToolProfileEnvVar = "MCP_TEMPLATE_TOOL_PROFILE";

    /// <summary>
    /// Environment variable to enable/disable execution of template post-actions
    /// (restore, add-to-solution). Enabled by default. Set to "false" or "0" to disable.
    /// </summary>
    public const string PostActionsEnvVar = "MCP_TEMPLATE_POST_ACTIONS";

    /// <summary>
    /// Environment variable controlling whether package versions are rewritten to the latest
    /// stable release by default. Disabled by default — upgrades are reported, not applied.
    /// Set to "true" or "1" to restore the pre-1.5.0 apply-by-default behavior.
    /// </summary>
    public const string ResolveLatestVersionsEnvVar = "MCP_TEMPLATE_RESOLVE_LATEST_VERSIONS";

    /// <summary>
    /// Environment variable for the root directory that generated files must stay inside.
    /// Defaults to the process working directory.
    /// </summary>
    public const string WorkspaceRootEnvVar = "MCP_TEMPLATE_WORKSPACE_ROOT";

    /// <summary>
    /// Environment variable to enable/disable workspace confinement of write paths.
    /// Enabled by default. Set to "false" or "0" to allow writes to arbitrary paths.
    /// </summary>
    public const string WorkspaceEnforcementEnvVar = "MCP_TEMPLATE_WORKSPACE_ENFORCEMENT";

    /// <summary>
    /// Environment variable holding the bearer token required by the HTTP transport.
    /// </summary>
    public const string HttpAuthTokenEnvVar = "MCP_TEMPLATE_HTTP_TOKEN";

    /// <summary>
    /// Environment variable to explicitly allow unauthenticated HTTP access.
    /// Without it, the HTTP transport refuses to start unless a token is configured.
    /// </summary>
    public const string HttpAllowAnonymousEnvVar = "MCP_TEMPLATE_HTTP_ALLOW_ANONYMOUS";

    /// <summary>
    /// Environment variable for the per-client request budget per minute on the HTTP transport.
    /// Defaults to 120. Set to 0 to disable rate limiting.
    /// </summary>
    public const string HttpRateLimitEnvVar = "MCP_TEMPLATE_HTTP_RATE_LIMIT";

    /// <summary>Default number of HTTP requests allowed per client per minute.</summary>
    public const int DefaultHttpRateLimitPerMinute = 120;

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
    /// The active tool profile. Controls which tools are exposed to the MCP client.
    /// </summary>
    public ToolProfile Profile { get; init; } = ToolProfile.Full;

    /// <summary>
    /// Whether safe template post-actions (restore, add-to-solution) are executed after creation.
    /// </summary>
    public bool PostActionsEnabled { get; init; } = true;

    /// <summary>
    /// Default for the <c>resolveLatestVersions</c> tool parameter when the caller doesn't specify it.
    /// Defaults to false: rewriting every PackageReference to "latest stable" at creation time
    /// produces untested combinations and overrides the template author's deliberate pinning,
    /// so upgrades are reported instead of applied.
    /// </summary>
    public bool ResolveLatestVersionsByDefault { get; init; }

    /// <summary>
    /// Root directory that generated files must stay within when
    /// <see cref="WorkspaceEnforcementEnabled"/> is true.
    /// </summary>
    public string WorkspaceRoot { get; init; } = Environment.CurrentDirectory;

    /// <summary>
    /// Whether write paths are confined to <see cref="WorkspaceRoot"/>.
    /// </summary>
    public bool WorkspaceEnforcementEnabled { get; init; } = true;

    /// <summary>
    /// Bearer token required by the HTTP transport. Null when no token is configured.
    /// </summary>
    public string? HttpAuthToken { get; init; }

    /// <summary>
    /// Whether unauthenticated HTTP access was explicitly permitted by the operator.
    /// </summary>
    public bool HttpAllowAnonymous { get; init; }

    /// <summary>
    /// Per-client requests allowed per minute on the HTTP transport. Zero disables rate limiting.
    /// </summary>
    public int HttpRateLimitPerMinute { get; init; } = DefaultHttpRateLimitPerMinute;

    /// <summary>
    /// True when the HTTP transport should require a bearer token.
    /// </summary>
    public bool HttpAuthenticationRequired => !string.IsNullOrEmpty(HttpAuthToken);

    /// <summary>
    /// Returns true if the given tool is enabled in the current profile.
    /// </summary>
    public bool IsToolEnabled(string toolName) => Profile == ToolProfile.Full || IsLiteProfileTool(toolName);

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
            Profile = GetToolProfile(),
            PostActionsEnabled = IsEnabled(PostActionsEnvVar, defaultValue: true),
            ResolveLatestVersionsByDefault = IsEnabled(ResolveLatestVersionsEnvVar, defaultValue: false),
            WorkspaceRoot = GetWorkspaceRoot(),
            WorkspaceEnforcementEnabled = IsEnabled(WorkspaceEnforcementEnvVar, defaultValue: true),
            HttpAuthToken = NullIfEmpty(Environment.GetEnvironmentVariable(HttpAuthTokenEnvVar)),
            HttpAllowAnonymous = IsEnabled(HttpAllowAnonymousEnvVar, defaultValue: false),
            HttpRateLimitPerMinute = GetHttpRateLimit(),
        };
    }

    /// <summary>
    /// Load feature flags from environment variables only (backward-compatible overload).
    /// </summary>
    public static McpFeatureFlags FromEnvironment()
    {
        return FromEnvironment([]);
    }

    /// <summary>
    /// Lite profile tools: the 5 most essential tools for typical AI agent workflows.
    /// </summary>
    private static bool IsLiteProfileTool(string toolName)
        => toolName is "template_from_intent"
            or "template_instantiate"
            or "template_inspect"
            or "template_search"
            or "template_dry_run";

    private static ToolProfile GetToolProfile()
    {
        var value = Environment.GetEnvironmentVariable(ToolProfileEnvVar);
        if (!string.IsNullOrEmpty(value) &&
            value.Equals("lite", StringComparison.OrdinalIgnoreCase))
        {
            return ToolProfile.Lite;
        }

        return ToolProfile.Full;
    }

    private static string GetWorkspaceRoot()
    {
        var value = Environment.GetEnvironmentVariable(WorkspaceRootEnvVar);
        if (string.IsNullOrWhiteSpace(value))
        {
            return Environment.CurrentDirectory;
        }

        try
        {
            return Path.GetFullPath(value);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // An unusable root would silently widen or break confinement — fail safe to the cwd.
            return Environment.CurrentDirectory;
        }
    }

    private static int GetHttpRateLimit()
    {
        var value = Environment.GetEnvironmentVariable(HttpRateLimitEnvVar);
        if (!string.IsNullOrWhiteSpace(value) && int.TryParse(value, out var parsed) && parsed >= 0)
        {
            return parsed;
        }

        return DefaultHttpRateLimitPerMinute;
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

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

        return !value.Equals("false", StringComparison.OrdinalIgnoreCase)
            && !value.Equals("no", StringComparison.OrdinalIgnoreCase)
            && !value.Equals("off", StringComparison.OrdinalIgnoreCase)
            && value != "0";
    }
}

/// <summary>
/// Supported MCP transport modes.
/// </summary>
internal enum TransportMode
{
    /// <summary>Standard I/O transport (default, for CLI and local tool usage).</summary>
    Stdio,

    /// <summary>
    /// HTTP transport with streamable HTTP support, for remote or CI/CD deployment.
    /// Not multi-tenant: template install state and the workspace root are process-wide.
    /// </summary>
    Http,
}

/// <summary>
/// Tool profile modes controlling which tools are exposed to the MCP client.
/// </summary>
internal enum ToolProfile
{
    /// <summary>All 15 tools exposed (default).</summary>
    Full,

    /// <summary>5 core tools only: template_from_intent, template_instantiate, template_inspect, template_search, template_dry_run.</summary>
    Lite,
}
