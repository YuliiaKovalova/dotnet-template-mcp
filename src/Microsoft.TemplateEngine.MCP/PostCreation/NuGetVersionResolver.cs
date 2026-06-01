// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace Microsoft.TemplateEngine.MCP.PostCreation;

/// <summary>
/// Resolves the latest stable version of a NuGet package using the NuGet V3 API.
/// Uses the flat-container endpoint for efficient version lookups. Results are cached in two tiers:
/// an in-memory tier for the process lifetime and a best-effort on-disk tier that survives restarts
/// (important for short-lived stdio servers). Successful lookups are cached for 30 minutes; failed
/// lookups for a short window so a transient network blip doesn't suppress a package for long.
/// </summary>
internal static class NuGetVersionResolver
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private const string NuGetRegistrationBaseUrl = "https://api.nuget.org/v3-flatcontainer/";

    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly TimeSpan SuccessTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromMinutes(1);

    /// <summary>Maximum number of entries kept in the on-disk cache before pruning.</summary>
    private const int MaxDiskEntries = 1000;

    private static readonly string? DiskCacheDir = ResolveDiskCacheDir();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Microsoft.TemplateEngine.MCP/{version}");
        return client;
    }

    /// <summary>
    /// Get the latest stable version of a NuGet package.
    /// Returns null if the package is not found or on error.
    /// </summary>
    public static async Task<string?> GetLatestStableVersionAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        // L1: in-memory cache
        if (Cache.TryGetValue(packageId, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
        {
            return cached.Version;
        }

        // L2: on-disk cache
        if (TryReadDiskCache(packageId, out var diskEntry) && diskEntry.ExpiresAt > DateTime.UtcNow)
        {
            Cache[packageId] = diskEntry;
            return diskEntry.Version;
        }

        try
        {
            var url = $"{NuGetRegistrationBaseUrl}{packageId.ToLowerInvariant()}/index.json";
            var response = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Store(packageId, null);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);

            if (!doc.RootElement.TryGetProperty("versions", out var versionsElement))
            {
                Store(packageId, null);
                return null;
            }

            // Pick the highest stable version using proper SemVer ordering
            // (don't rely on the array being pre-sorted).
            NuGet.Versioning.NuGetVersion? latestStable = null;
            foreach (var version in versionsElement.EnumerateArray())
            {
                var versionStr = version.GetString();
                if (versionStr != null &&
                    NuGet.Versioning.NuGetVersion.TryParse(versionStr, out var parsed) &&
                    !parsed.IsPrerelease &&
                    (latestStable == null || parsed > latestStable))
                {
                    latestStable = parsed;
                }
            }

            var latestStableStr = latestStable?.ToNormalizedString();
            Store(packageId, latestStableStr);
            return latestStableStr;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Genuine caller cancellation — propagate.
            throw;
        }
        catch (Exception)
        {
            // Includes the HttpClient timeout (surfaced as a TaskCanceledException with no caller
            // cancellation). Cache the failure briefly so we don't hammer the API on repeated misses.
            Store(packageId, null);
            return null;
        }
    }

    private static void Store(string packageId, string? version)
    {
        var ttl = version == null ? FailureTtl : SuccessTtl;
        var entry = new CacheEntry(version, DateTime.UtcNow + ttl);
        Cache[packageId] = entry;
        WriteDiskCache(packageId, entry);
    }

    /// <summary>
    /// A stable version has no prerelease suffix (no dash after the version numbers).
    /// </summary>
    internal static bool IsStableVersion(string version)
    {
        return !version.Contains('-');
    }

    /// <summary>
    /// Clear both cache tiers. Useful for testing.
    /// </summary>
    internal static void ClearCache()
    {
        Cache.Clear();
        try
        {
            if (DiskCacheDir != null && Directory.Exists(DiskCacheDir))
            {
                foreach (var file in Directory.EnumerateFiles(DiskCacheDir, "*.json"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }

    // ── On-disk cache (best-effort, one file per package) ──

    private static string? ResolveDiskCacheDir()
    {
        try
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDir))
            {
                baseDir = Path.GetTempPath();
            }

            var dir = Path.Combine(baseDir, "dotnet-template-mcp", "nuget-version-cache");
            Directory.CreateDirectory(dir);
            return dir;
        }
        catch
        {
            return null; // Disk cache is optional — fall back to memory-only.
        }
    }

    private static string? CacheFilePath(string packageId)
    {
        if (DiskCacheDir == null)
        {
            return null;
        }

        // Sanitize the package id into a safe file name.
        var safe = string.Concat(packageId.ToLowerInvariant().Select(c =>
            char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
        return Path.Combine(DiskCacheDir, safe + ".json");
    }

    private static bool TryReadDiskCache(string packageId, out CacheEntry entry)
    {
        entry = default!;
        var path = CacheFilePath(packageId);
        if (path == null || !File.Exists(path))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<DiskEntry>(json);
            if (dto == null)
            {
                return false;
            }

            entry = new CacheEntry(dto.Version, dto.ExpiresAt);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteDiskCache(string packageId, CacheEntry entry)
    {
        var path = CacheFilePath(packageId);
        if (path == null)
        {
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(new DiskEntry(entry.Version, entry.ExpiresAt));
            // Atomic-ish write: write to a temp file then move into place.
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tmp, json);
                File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                // Clean up the orphaned temp file if the move failed.
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }

            PruneDiskCacheIfNeeded();
        }
        catch
        {
            // Best-effort; ignore disk failures.
        }
    }

    private static void PruneDiskCacheIfNeeded()
    {
        try
        {
            if (DiskCacheDir == null)
            {
                return;
            }

            var files = Directory.GetFiles(DiskCacheDir, "*.json");
            if (files.Length <= MaxDiskEntries)
            {
                return;
            }

            // Evict the oldest files first.
            foreach (var file in files
                .OrderBy(f => File.GetLastWriteTimeUtc(f))
                .Take(files.Length - MaxDiskEntries))
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch { }
    }

    private sealed record CacheEntry(string? Version, DateTime ExpiresAt);

    private sealed record DiskEntry(string? Version, DateTime ExpiresAt);
}
