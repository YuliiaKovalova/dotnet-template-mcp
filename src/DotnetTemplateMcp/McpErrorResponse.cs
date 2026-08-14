// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.Text.Json;

namespace DotnetTemplateMcp;

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
