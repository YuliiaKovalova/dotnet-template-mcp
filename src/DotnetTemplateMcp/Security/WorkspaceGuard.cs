// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

namespace DotnetTemplateMcp.Security;

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
            root = NormalizeDirectory(featureFlags.WorkspaceRoot);
            resolved = ResolveCandidate(candidatePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return $"Path '{candidatePath}' is not a valid filesystem path: {ex.Message}";
        }

        if (IsWithin(resolved, root))
        {
            return null;
        }

        return $"Path '{candidatePath}' resolves to '{resolved}', which is outside the permitted workspace root '{root.TrimEnd(Path.DirectorySeparatorChar)}'.";
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
    /// Resolves a candidate path to a comparable absolute form, following symlinks where possible so
    /// a link inside the workspace cannot be used to redirect writes outside it.
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

        try
        {
            var info = Directory.Exists(existing) ? new DirectoryInfo(existing) : (FileSystemInfo)new FileInfo(existing);
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target != null)
            {
                // Re-attach the not-yet-created remainder to the real target.
                var remainder = full.Substring(existing.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.IsNullOrEmpty(remainder)
                    ? Path.GetFullPath(target.FullName)
                    : Path.GetFullPath(Path.Combine(target.FullName, remainder));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Link resolution is best-effort; fall back to the lexical path.
        }

        return full;
    }

    private static string NormalizeDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        return full.EndsWith(Path.DirectorySeparatorChar) ? full : full + Path.DirectorySeparatorChar;
    }

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
