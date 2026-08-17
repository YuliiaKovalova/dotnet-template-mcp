// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using DotnetTemplateMcp.Security;
using Xunit;

namespace DotnetTemplateMcp.Tests;

/// <summary>
/// Covers gap 1.4: the HTTP transport called MapMcp() with no auth, authz or rate limiting while
/// being marketed as remote/team-shared. Every tool writes files or installs packages.
/// </summary>
public class BearerTokenMiddlewareTests
{
    private const string Token = "s3cret-token-value";

    private static async Task<HttpContext> InvokeAsync(string? authorizationHeader, McpFeatureFlags flags)
    {
        var nextCalled = false;
        var middleware = new BearerTokenMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            flags);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        if (authorizationHeader != null)
        {
            context.Request.Headers.Authorization = authorizationHeader;
        }

        await middleware.InvokeAsync(context);

        context.Items["nextCalled"] = nextCalled;
        return context;
    }

    private static McpFeatureFlags FlagsWithToken() => new() { HttpAuthToken = Token };

    [Fact]
    public async Task ValidToken_PassesThrough()
    {
        var context = await InvokeAsync($"Bearer {Token}", FlagsWithToken());

        Assert.True((bool)context.Items["nextCalled"]!);
        Assert.NotEqual((int)HttpStatusCode.Unauthorized, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer ")]
    [InlineData("Bearer wrong-token")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("s3cret-token-value")] // missing the Bearer scheme
    public async Task MissingOrInvalidToken_Returns401AndBlocksPipeline(string? header)
    {
        var context = await InvokeAsync(header, FlagsWithToken());

        Assert.False((bool)context.Items["nextCalled"]!);
        Assert.Equal((int)HttpStatusCode.Unauthorized, context.Response.StatusCode);
        Assert.Equal("Bearer", context.Response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public async Task TokenPrefixOfExpected_IsRejected()
    {
        var context = await InvokeAsync($"Bearer {Token.Substring(0, 5)}", FlagsWithToken());

        Assert.Equal((int)HttpStatusCode.Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task EmptyConfiguredToken_RejectsEverything()
    {
        // Fail closed: an unset token must never mean "allow all".
        var context = await InvokeAsync("Bearer anything", new McpFeatureFlags());

        Assert.False((bool)context.Items["nextCalled"]!);
        Assert.Equal((int)HttpStatusCode.Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task Response_DoesNotEchoTheExpectedToken()
    {
        var context = await InvokeAsync("Bearer wrong", FlagsWithToken());

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();

        Assert.DoesNotContain(Token, body);
    }
}
