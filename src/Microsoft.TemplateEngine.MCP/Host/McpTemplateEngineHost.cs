// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Edge;

namespace Microsoft.TemplateEngine.MCP.Host;

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
