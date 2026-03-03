// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;

namespace Microsoft.TemplateEngine.MCP.Tools;

/// <summary>
/// Generates consistent responses for tools disabled by the active tool profile.
/// </summary>
internal static class ToolProfileResponse
{
    public static string DisabledMessage(string toolName, string alternative)
    {
        return JsonSerializer.Serialize(new
        {
            error = $"Tool '{toolName}' is disabled in lite profile mode. Set MCP_TEMPLATE_TOOL_PROFILE=full to enable all tools.",
            hint = alternative,
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
