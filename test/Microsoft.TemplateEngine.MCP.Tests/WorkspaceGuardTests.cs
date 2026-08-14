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

    // --- Name segments -------------------------------------------------------------------------
    // A project name is combined into the output path, so a name carrying path syntax escapes the
    // directory the caller believes it is writing to.

    [Theory]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("../../../Users/me/.ssh")]
    [InlineData("sub/dir")]
    [InlineData("sub\\dir")]
    public void ValidateNameSegment_PathSyntax_IsRejected(string name)
    {
        Assert.NotNull(WorkspaceGuard.ValidateNameSegment(name));
    }

    [Theory]
    [InlineData("MyApp")]
    [InlineData("My.App")]
    [InlineData("my-app_1")]
    [InlineData(null)]
    [InlineData("")]
    public void ValidateNameSegment_PlainName_IsAllowed(string? name)
    {
        Assert.Null(WorkspaceGuard.ValidateNameSegment(name));
    }

    [Fact]
    public void ValidateNameSegment_RootedPath_IsRejected()
    {
        Assert.NotNull(WorkspaceGuard.ValidateNameSegment(Path.Combine(Path.GetTempPath(), "elsewhere")));
    }

    [Fact]
    public void ValidateNameSegment_MessageNamesTheParameter()
    {
        var message = WorkspaceGuard.ValidateNameSegment("../x", "projectName");

        Assert.NotNull(message);
        Assert.Contains("projectName", message);
    }

    // --- Links ---------------------------------------------------------------------------------

    [Fact]
    public void Validate_SymlinkedRoot_StillAllowsPathsInsideIt()
    {
        // A workspace root that is itself a link is normal (Dev Drive, redirected profile, /tmp on
        // macOS). If the root is compared lexically while candidates are link-resolved, every write
        // is falsely rejected and the server is unusable.
        var realRoot = Path.Combine(Path.GetTempPath(), $"mcp-real-{Guid.NewGuid():N}");
        var linkRoot = Path.Combine(Path.GetTempPath(), $"mcp-link-{Guid.NewGuid():N}");
        Directory.CreateDirectory(realRoot);

        if (!TryCreateDirectoryLink(linkRoot, realRoot))
        {
            return; // Unprivileged Windows agent: symlink creation is not permitted.
        }

        try
        {
            var flags = new McpFeatureFlags { WorkspaceRoot = linkRoot, WorkspaceEnforcementEnabled = true };

            Assert.Null(WorkspaceGuard.Validate(Path.Combine(linkRoot, "MyApp"), flags));
            Assert.Null(WorkspaceGuard.Validate(Path.Combine(realRoot, "MyApp"), flags));
        }
        finally
        {
            TryDelete(linkRoot);
            TryDelete(realRoot);
        }
    }

    [Fact]
    public void Validate_LinkInsideRootPointingOutside_IsRejected()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"mcp-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        var link = Path.Combine(_root, "escape");

        if (!TryCreateDirectoryLink(link, outside))
        {
            return;
        }

        try
        {
            // Both the link itself and a not-yet-created path *under* it must be rejected: resolving
            // only the deepest existing component would miss the second case entirely.
            Assert.NotNull(WorkspaceGuard.Validate(link, Flags()));
            Assert.NotNull(WorkspaceGuard.Validate(Path.Combine(link, "MyApp"), Flags()));
            Assert.NotNull(WorkspaceGuard.Validate(Path.Combine(link, "a", "b", "c"), Flags()));
        }
        finally
        {
            TryDelete(link);
            TryDelete(outside);
        }
    }

    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch { }
    }
}
