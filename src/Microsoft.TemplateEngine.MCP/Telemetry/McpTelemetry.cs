// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Microsoft.TemplateEngine.MCP;

/// <summary>
/// Centralized telemetry for the MCP server.
/// Exposes an <see cref="ActivitySource"/> for distributed tracing and a <see cref="Meter"/>
/// for metrics. Consumers can wire these to any OpenTelemetry-compatible backend
/// (OTLP, Prometheus, Azure Monitor, etc.) via standard .NET configuration.
/// </summary>
internal static class McpTelemetry
{
    public const string ServiceName = "Microsoft.TemplateEngine.MCP";

    /// <summary>
    /// ActivitySource for distributed tracing spans.
    /// Each MCP tool invocation creates a child activity.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ServiceName, "0.1.0");

    /// <summary>
    /// Meter for numeric measurements (counters, histograms).
    /// </summary>
    public static readonly Meter Meter = new(ServiceName, "0.1.0");

    // ── Counters ──

    /// <summary>Total MCP tool invocations (tagged by tool name and status).</summary>
    public static readonly Counter<long> ToolInvocations =
        Meter.CreateCounter<long>(
            "mcp.tool.invocations",
            unit: "{invocations}",
            description: "Number of MCP tool invocations");

    /// <summary>Tool invocations that returned an error result.</summary>
    public static readonly Counter<long> ToolErrors =
        Meter.CreateCounter<long>(
            "mcp.tool.errors",
            unit: "{errors}",
            description: "Number of MCP tool invocations that resulted in an error");

    /// <summary>Templates created via template_instantiate.</summary>
    public static readonly Counter<long> TemplatesCreated =
        Meter.CreateCounter<long>(
            "mcp.templates.created",
            unit: "{templates}",
            description: "Number of templates successfully instantiated");

    /// <summary>Template packages installed (including auto-resolve).</summary>
    public static readonly Counter<long> PackagesInstalled =
        Meter.CreateCounter<long>(
            "mcp.packages.installed",
            unit: "{packages}",
            description: "Number of template packages installed");

    /// <summary>Auto-resolve events (template not found locally → NuGet search → install).</summary>
    public static readonly Counter<long> AutoResolves =
        Meter.CreateCounter<long>(
            "mcp.templates.auto_resolved",
            unit: "{resolves}",
            description: "Number of templates auto-resolved from NuGet");

    /// <summary>Parameter validation failures caught before creation.</summary>
    public static readonly Counter<long> ValidationFailures =
        Meter.CreateCounter<long>(
            "mcp.templates.validation_failures",
            unit: "{failures}",
            description: "Number of parameter validation failures caught before file creation");

    /// <summary>Smart defaults applied to template parameters.</summary>
    public static readonly Counter<long> SmartDefaultsApplied =
        Meter.CreateCounter<long>(
            "mcp.templates.smart_defaults_applied",
            unit: "{defaults}",
            description: "Number of smart parameter defaults applied");

    /// <summary>Intent resolution attempts via template_from_intent.</summary>
    public static readonly Counter<long> IntentResolutions =
        Meter.CreateCounter<long>(
            "mcp.intent.resolutions",
            unit: "{resolutions}",
            description: "Number of intent resolution attempts");

    /// <summary>Templates generated from existing projects via template_create_from_existing.</summary>
    public static readonly Counter<long> TemplatesGeneratedFromExisting =
        Meter.CreateCounter<long>(
            "mcp.templates.generated_from_existing",
            unit: "{templates}",
            description: "Number of templates generated from existing project analysis");

    /// <summary>Template composition operations via template_compose.</summary>
    public static readonly Counter<long> TemplateCompositions =
        Meter.CreateCounter<long>(
            "mcp.templates.compositions",
            unit: "{compositions}",
            description: "Number of multi-template composition operations");

    /// <summary>Parameters collected via elicitation.</summary>
    public static readonly Counter<long> ElicitedParameters =
        Meter.CreateCounter<long>(
            "mcp.templates.elicited_parameters",
            unit: "{parameters}",
            description: "Number of parameters collected via interactive elicitation");

    /// <summary>Template post-actions executed after creation (restore, add-to-solution).</summary>
    public static readonly Counter<long> PostActionsExecuted =
        Meter.CreateCounter<long>(
            "mcp.templates.post_actions_executed",
            unit: "{actions}",
            description: "Number of template post-actions executed after creation");

    /// <summary>Write attempts rejected for resolving outside the workspace root.</summary>
    public static readonly Counter<long> WorkspaceViolations =
        Meter.CreateCounter<long>(
            "mcp.security.workspace_violations",
            unit: "{violations}",
            description: "Number of tool calls rejected for targeting a path outside the workspace root");

    /// <summary>HTTP requests rejected by the bearer-token check.</summary>
    public static readonly Counter<long> UnauthorizedRequests =
        Meter.CreateCounter<long>(
            "mcp.security.unauthorized_requests",
            unit: "{requests}",
            description: "Number of HTTP requests rejected for missing or invalid credentials");

    // ── Histograms ──

    /// <summary>Duration of MCP tool invocations in milliseconds.</summary>
    public static readonly Histogram<double> ToolDuration =
        Meter.CreateHistogram<double>(
            "mcp.tool.duration",
            unit: "ms",
            description: "Duration of MCP tool invocations in milliseconds");

    // ── Helpers ──

    /// <summary>
    /// Start a traced activity for an MCP tool invocation.
    /// Records tool name, increments the invocation counter, and
    /// measures duration on dispose.
    /// </summary>
    public static Activity? StartToolActivity(string toolName)
    {
        ToolInvocations.Add(1, new KeyValuePair<string, object?>("tool", toolName));

        var activity = ActivitySource.StartActivity(
            $"mcp.tool.{toolName}",
            ActivityKind.Server);

        activity?.SetTag("mcp.tool.name", toolName);
        return activity;
    }

    /// <summary>
    /// Record a tool error on the current activity.
    /// </summary>
    public static void RecordError(Activity? activity, string toolName, string errorMessage)
    {
        ToolErrors.Add(1, new KeyValuePair<string, object?>("tool", toolName));
        activity?.SetTag("error", true);
        activity?.SetTag("error.message", errorMessage);
        activity?.SetStatus(ActivityStatusCode.Error, errorMessage);
    }

    /// <summary>
    /// Record the tool duration on completion.
    /// </summary>
    public static void RecordDuration(string toolName, double elapsedMs)
    {
        ToolDuration.Record(elapsedMs, new KeyValuePair<string, object?>("tool", toolName));
    }
}
