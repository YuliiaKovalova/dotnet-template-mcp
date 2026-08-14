// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Microsoft.TemplateEngine.MCP.Security;
using Xunit;

namespace Microsoft.TemplateEngine.MCP.Tests;

/// <summary>
/// Covers gap 1.5: outputPath previously flowed unvalidated into the template engine, giving any
/// caller an arbitrary filesystem write. Over the HTTP transport that is a remote primitive.
/// </summary>
public class WorkspaceGuardTests : IDisposable
{
    private readonly string _root;

    public WorkspaceGuardTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"mcp-ws-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch { }

        GC.SuppressFinalize(this);
    }

    private McpFeatureFlags Flags(bool enforcement = true)
        => new() { WorkspaceRoot = _root, WorkspaceEnforcementEnabled = enforcement };

    [Fact]
    public void Validate_PathInsideRoot_IsAllowed()
    {
        Assert.Null(WorkspaceGuard.Validate(Path.Combine(_root, "MyApp"), Flags()));
    }

    [Fact]
    public void Validate_RootItself_IsAllowed()
    {
        Assert.Null(WorkspaceGuard.Validate(_root, Flags()));
    }

    [Fact]
    public void Validate_NullPath_IsAllowed()
    {
        // Null means "use the tool default", which is itself derived from the workspace root.
        Assert.Null(WorkspaceGuard.Validate(null, Flags()));
    }

    [Fact]
    public void Validate_PathOutsideRoot_IsRejected()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"mcp-outside-{Guid.NewGuid():N}");

        var reason = WorkspaceGuard.Validate(outside, Flags());

        Assert.NotNull(reason);
        Assert.Contains("outside the permitted workspace root", reason);
    }

    [Fact]
    public void Validate_DotDotEscape_IsRejected()
    {
        var escape = Path.Combine(_root, "..", "..", "escaped");

        Assert.NotNull(WorkspaceGuard.Validate(escape, Flags()));
    }

    [Fact]
    public void Validate_SiblingWithSharedPrefix_IsRejected()
    {
        // "C:\work-other" must not be treated as inside "C:\work".
        var sibling = _root + "-other";

        Assert.NotNull(WorkspaceGuard.Validate(sibling, Flags()));
    }

    [Fact]
    public void Validate_EnforcementDisabled_AllowsAnyPath()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"mcp-outside-{Guid.NewGuid():N}");

        Assert.Null(WorkspaceGuard.Validate(outside, Flags(enforcement: false)));
    }

    [Fact]
    public void PathRejectedError_IsStructuredJsonWithOptOutHint()
    {
        var json = WorkspaceGuard.PathRejectedError("nope");

        var root = JsonDocument.Parse(json).RootElement;
        Assert.True(root.TryGetProperty("error", out _));
        Assert.Equal("path_outside_workspace", root.GetProperty("errorCode").GetString());
        Assert.Contains("MCP_TEMPLATE_WORKSPACE_ROOT", root.GetProperty("suggestion").GetString());
    }
}
