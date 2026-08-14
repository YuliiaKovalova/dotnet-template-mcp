// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Edge;

namespace DotnetTemplateMcp.Host;

/// <summary>
/// ITemplateEngineHost adapter for the MCP server context.
/// Uses HostIdentifier "ai" so the engine auto-discovers ai.host.json files.
/// </summary>
internal sealed class McpTemplateEngineHost : DefaultTemplateEngineHost
{
    public McpTemplateEngineHost(ILoggerFactory loggerFactory)
        : base(
            hostIdentifier: "ai",
            version: "1.0.0",
            fallbackHostTemplateConfigNames: new[] { "dotnetcli.host.json" },
            loggerFactory: loggerFactory)
    {
    }
}
