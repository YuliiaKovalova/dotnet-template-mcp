// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.TemplateEngine.MCP.Security;

using System.Security;

/// <summary>
/// Confines filesystem writes to a configured workspace root.
///
/// Before this existed, <c>outputPath</c> flowed from the MCP client straight into the template
/// engine, so any caller could write generated files to an arbitrary absolute path. Over stdio that
/// is roughly the same trust level as the shell the server was launched from, but over the HTTP
/// transport it is a remote arbitrary-file-write primitive.
///
/// Enforcement is on by default and rooted at <see cref="McpFeatureFlags.WorkspaceRoot"/>
/// (the process working directory unless <c>MCP_TEMPLATE_WORKSPACE_ROOT</c> is set).
/// Set <c>MCP_TEMPLATE_WORKSPACE_ENFORCEMENT=false</c> to restore the previous unconfined behavior.
/// </summary>
internal static class WorkspaceGuard
{
    /// <summary>
    /// Validates that <paramref name="candidatePath"/> resolves inside the workspace root.
    /// Returns null when the path is allowed, or a human-readable reason when it is rejected.
    /// </summary>
    /// <remarks>
    /// Callers must validate the path they are actually going to write to. Passing a raw, still-
    /// unresolved parameter is not sufficient when the effective path is composed from other
    /// untrusted inputs afterwards.
    /// </remarks>
    public static string? Validate(string? candidatePath, McpFeatureFlags featureFlags)
    {
        if (!featureFlags.WorkspaceEnforcementEnabled)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            // Null means "use the tool's default", which is itself derived from the workspace root.
            return null;
        }

        string root;
        string resolved;
        try
        {
            root = ResolveExisting(featureFlags.WorkspaceRoot);
            resolved = ResolveCandidate(candidatePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return $"Path '{candidatePath}' is not a valid filesystem path: {ex.Message}";
        }

        if (IsWithin(resolved, WithTrailingSeparator(root)))
        {
            return null;
        }

        return $"Path '{candidatePath}' resolves to '{resolved}', which is outside the permitted workspace root '{root}'.";
    }

    /// <summary>
    /// Validates a name segment that will be combined into a filesystem path. Template names and
    /// project names flow into <c>Path.Combine</c>, where a value like <c>../../etc</c> silently
    /// escapes the directory it was meant to be created under.
    /// </summary>
    public static string? ValidateNameSegment(string? name, string parameterName = "name")
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
        {
            return $"The '{parameterName}' value '{name}' contains a path separator. It must be a single name segment, not a path.";
        }

        if (name.Split('/', '\\').Any(segment => segment is ".." or "."))
        {
            return $"The '{parameterName}' value '{name}' contains a relative path segment.";
        }

        if (Path.IsPathRooted(name) || name.Contains(':'))
        {
            return $"The '{parameterName}' value '{name}' must be a relative single name segment, not a rooted path.";
        }

        return null;
    }

    /// <summary>
    /// Standard MCP error payload for a rejected path, including how to opt out.
    /// </summary>
    public static string PathRejectedError(string reason)
    {
        McpTelemetry.WorkspaceViolations.Add(1);

        return McpErrorResponse.Serialize(
            "path_outside_workspace",
            reason,
            "Pass a path inside the workspace root, or set MCP_TEMPLATE_WORKSPACE_ROOT to widen it. " +
            "Set MCP_TEMPLATE_WORKSPACE_ENFORCEMENT=false to disable confinement entirely (not recommended for the HTTP transport).",
            retryable: true);
    }

    /// <summary>
    /// Resolves a candidate path to a comparable absolute form, following symlinks so a link
    /// anywhere along the path cannot be used to redirect writes outside the workspace.
    /// </summary>
    private static string ResolveCandidate(string candidatePath)
    {
        var full = Path.GetFullPath(candidatePath);

        // Walk up to the nearest component that actually exists — the leaf is usually being created.
        var existing = full;
        while (!string.IsNullOrEmpty(existing) && !Directory.Exists(existing) && !File.Exists(existing))
        {
            var parent = Path.GetDirectoryName(existing);
            if (string.IsNullOrEmpty(parent) || parent == existing)
            {
                return full;
            }

            existing = parent;
        }

        if (string.IsNullOrEmpty(existing))
        {
            return full;
        }

        var resolvedExisting = ResolveExisting(existing);
        var remainder = full.Substring(existing.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.IsNullOrEmpty(remainder)
            ? resolvedExisting
            : Path.GetFullPath(Path.Combine(resolvedExisting, remainder));
    }

    /// <summary>
    /// Fully resolves an existing path, following links at <em>every</em> level.
    ///
    /// Resolving only the deepest existing component is not enough: if an ancestor is a junction,
    /// <see cref="Directory.Exists"/> succeeds straight through it, so the deepest component is not
    /// itself a link and the redirection goes unnoticed. The workspace root is resolved with this
    /// same function so a root that is itself a link (Dev Drive, redirected profile, /tmp on macOS)
    /// compares equal instead of rejecting every write.
    /// </summary>
    private static string ResolveExisting(string path)
    {
        var full = Path.GetFullPath(path);

        try
        {
            // Resolve the deepest component first; that collapses the common case in one call.
            var info = Directory.Exists(full) ? new DirectoryInfo(full) : (FileSystemInfo)new FileInfo(full);
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target != null)
            {
                full = Path.GetFullPath(target.FullName);
            }

            // Then walk the ancestors, since a junction higher up is transparent to Exists().
            var segments = new List<string>();
            var current = full;
            while (true)
            {
                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || parent == current)
                {
                    break;
                }

                segments.Add(Path.GetFileName(current));

                if (Directory.Exists(parent))
                {
                    var parentTarget = new DirectoryInfo(parent).ResolveLinkTarget(returnFinalTarget: true);
                    if (parentTarget != null)
                    {
                        segments.Reverse();
                        return Path.GetFullPath(Path.Combine(parentTarget.FullName, Path.Combine(segments.ToArray())));
                    }
                }

                current = parent;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Link resolution is best-effort; fall back to the lexical path.
        }

        return full;
    }

    private static string WithTrailingSeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    /// <summary>
    /// True when <paramref name="resolved"/> is the root itself or sits beneath it. The trailing
    /// separator on the root prevents "C:\work-other" from matching root "C:\work".
    /// </summary>
    private static bool IsWithin(string resolved, string rootWithSeparator)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var rootNoSeparator = rootWithSeparator.TrimEnd(Path.DirectorySeparatorChar);
        var resolvedNoSeparator = resolved.TrimEnd(Path.DirectorySeparatorChar);

        return resolvedNoSeparator.Equals(rootNoSeparator, comparison)
            || resolved.StartsWith(rootWithSeparator, comparison)
            || (resolvedNoSeparator + Path.DirectorySeparatorChar).StartsWith(rootWithSeparator, comparison);
    }
}
