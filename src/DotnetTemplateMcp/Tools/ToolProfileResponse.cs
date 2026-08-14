// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.Text.Json;

namespace DotnetTemplateMcp.Tools;

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
