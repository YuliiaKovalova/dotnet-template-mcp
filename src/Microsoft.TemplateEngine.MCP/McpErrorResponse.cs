// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;

namespace Microsoft.TemplateEngine.MCP;

/// <summary>
/// Standardized error response format for all MCP tools.
/// Provides machine-readable error codes and actionable suggestions for AI agents.
/// </summary>
internal static class McpErrorResponse
{
    /// <summary>
    /// Create a structured error response JSON string.
    /// </summary>
    /// <param name="errorCode">Machine-readable error code (e.g., "not_found", "validation_failed").</param>
    /// <param name="message">Human-readable error message.</param>
    /// <param name="suggestion">Actionable suggestion for recovery.</param>
    /// <param name="retryable">Whether the operation can be retried with different input.</param>
    /// <param name="details">Optional additional details (validation errors, etc.).</param>
    public static string Serialize(
        string errorCode,
        string message,
        string? suggestion = null,
        bool retryable = true,
        object? details = null)
    {
        var response = new
        {
            error = message,
            errorCode,
            suggestion,
            retryable,
            details,
        };

        return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
    }
}
