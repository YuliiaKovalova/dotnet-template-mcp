// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Text.Json;

namespace Microsoft.TemplateEngine.MCP.PostCreation;

/// <summary>
/// Resolves the latest stable version of a NuGet package using the NuGet V3 API.
/// Uses the registration endpoint for efficient version lookups.
/// Results are cached in-memory with a configurable TTL to avoid redundant API calls.
/// </summary>
internal static class NuGetVersionResolver
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    private const string NuGetRegistrationBaseUrl = "https://api.nuget.org/v3-flatcontainer/";

    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Get the latest stable version of a NuGet package.
    /// Returns null if the package is not found or on error.
    /// Results are cached for 30 minutes.
    /// </summary>
    public static async Task<string?> GetLatestStableVersionAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        // Check cache first
        if (Cache.TryGetValue(packageId, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
        {
            return cached.Version;
        }

        try
        {
            var url = $"{NuGetRegistrationBaseUrl}{packageId.ToLowerInvariant()}/index.json";
            var response = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var doc = JsonDocument.Parse(content);

            if (!doc.RootElement.TryGetProperty("versions", out var versionsElement))
            {
                return null;
            }

            // versions array is sorted ascending — find last stable version
            string? latestStable = null;
            foreach (var version in versionsElement.EnumerateArray())
            {
                var versionStr = version.GetString();
                if (versionStr != null && IsStableVersion(versionStr))
                {
                    latestStable = versionStr;
                }
            }

            // Cache the result (even null, to avoid repeated failed lookups)
            Cache[packageId] = new CacheEntry(latestStable, DateTime.UtcNow + CacheTtl);

            return latestStable;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// A stable version has no prerelease suffix (no dash after the version numbers).
    /// </summary>
    internal static bool IsStableVersion(string version)
    {
        return !version.Contains('-');
    }

    /// <summary>
    /// Clear the version cache. Useful for testing.
    /// </summary>
    internal static void ClearCache() => Cache.Clear();

    private sealed record CacheEntry(string? Version, DateTime ExpiresAt);
}
