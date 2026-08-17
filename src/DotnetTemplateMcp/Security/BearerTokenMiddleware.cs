// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace DotnetTemplateMcp.Security;

/// <summary>
/// Bearer-token gate for the HTTP transport.
///
/// <c>MapMcp()</c> previously sat on an open port with no authentication, authorization or rate
/// limiting while the README advertised the transport as "remote / cloud / team-shared". Every MCP
/// tool can write files and install NuGet packages, so an unauthenticated endpoint is a remote code
/// execution surface rather than a convenience.
///
/// The server now refuses to start the HTTP transport unless the operator either configures
/// <c>MCP_TEMPLATE_HTTP_TOKEN</c> or explicitly opts in to anonymous access with
/// <c>MCP_TEMPLATE_HTTP_ALLOW_ANONYMOUS=true</c>.
/// </summary>
internal sealed class BearerTokenMiddleware
{
    private readonly RequestDelegate _next;
    private readonly byte[] _expectedToken;

    public BearerTokenMiddleware(RequestDelegate next, McpFeatureFlags featureFlags)
    {
        _next = next;
        _expectedToken = Encoding.UTF8.GetBytes(featureFlags.HttpAuthToken ?? string.Empty);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!TryGetPresentedToken(context, out var presented) ||
            !FixedTimeEquals(presented, _expectedToken))
        {
            McpTelemetry.UnauthorizedRequests.Add(1);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Unauthorized",
                detail = "Provide the server token via 'Authorization: Bearer <token>'.",
            }).ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);
    }

    private static bool TryGetPresentedToken(HttpContext context, out byte[] token)
    {
        token = Array.Empty<byte>();

        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header))
        {
            return false;
        }

        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = header.Substring(prefix.Length).Trim();
        if (value.Length == 0)
        {
            return false;
        }

        token = Encoding.UTF8.GetBytes(value);
        return true;
    }

    /// <summary>Constant-time comparison so the endpoint doesn't leak the token byte by byte.</summary>
    private static bool FixedTimeEquals(byte[] presented, byte[] expected)
    {
        if (expected.Length == 0)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(presented),
            SHA256.HashData(expected));
    }
}
