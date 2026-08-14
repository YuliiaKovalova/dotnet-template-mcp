// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Microsoft.TemplateEngine.MCP.PostCreation;

/// <summary>
/// Resolves the latest stable version of a NuGet package using the caller's configured feeds.
///
/// Lookups are delegated to <see cref="NuGetSourceResolver"/>, which honours the <c>NuGet.config</c>
/// chain (private feeds, credentials, package source mapping, proxies). Results are cached in two
/// tiers: an in-memory tier for the process lifetime and a best-effort on-disk tier that survives
/// restarts (important for short-lived stdio servers). Successful lookups are cached for 30 minutes;
/// failed lookups for a short window so a transient network blip doesn't suppress a package for long.
///
/// Cache entries are scoped to the feed set that produced them — the same package id can legitimately
/// resolve to different versions in two repositories that point at different feeds, so a
/// package-id-only key would leak versions across repository boundaries.
/// </summary>
internal static class NuGetVersionResolver
{
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.Ordinal);

    private static readonly TimeSpan SuccessTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromMinutes(1);

    /// <summary>Maximum number of entries kept in the on-disk cache before pruning.</summary>
    private const int MaxDiskEntries = 1000;

    private static readonly string? DiskCacheDir = ResolveDiskCacheDir();

    /// <summary>
    /// Get the latest stable version of a NuGet package as seen from <paramref name="rootDirectory"/>.
    /// Returns null if the package is not found on any configured source, or on error.
    /// </summary>
    /// <param name="packageId">The package id to look up.</param>
    /// <param name="rootDirectory">
    /// Directory used to discover the applicable <c>NuGet.config</c>. Defaults to the current
    /// working directory when null.
    /// </param>
    /// <param name="logger">Optional logger for feed diagnostics.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<string?> GetLatestStableVersionAsync(
        string packageId,
        string? rootDirectory = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var root = string.IsNullOrWhiteSpace(rootDirectory) ? Environment.CurrentDirectory : rootDirectory;
        var scope = GetScopeKey(root, logger);
        var cacheKey = scope + "|" + packageId.ToLowerInvariant();

        // L1: in-memory cache
        if (Cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
        {
            return cached.Version;
        }

        // L2: on-disk cache
        if (TryReadDiskCache(cacheKey, out var diskEntry) && diskEntry.ExpiresAt > DateTime.UtcNow)
        {
            Cache[cacheKey] = diskEntry;
            return diskEntry.Version;
        }

        try
        {
            var latest = await NuGetSourceResolver
                .GetLatestStableVersionAsync(packageId, root, logger, cancellationToken)
                .ConfigureAwait(false);

            Store(cacheKey, latest);
            return latest;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Genuine caller cancellation — propagate.
            throw;
        }
        catch (Exception)
        {
            // Offline, unreachable feed, or an auth failure. Cache the miss briefly so we don't
            // retry every package in a large solution against a feed that is currently down.
            Store(cacheKey, null);
            return null;
        }
    }

    /// <summary>
    /// Builds a short, stable key describing which feeds apply to a directory, so cached versions
    /// are never reused across repositories configured against different sources.
    /// </summary>
    private static string GetScopeKey(string rootDirectory, ILogger? logger)
    {
        try
        {
            var description = NuGetSourceResolver.DescribeSources(rootDirectory, logger);
            var raw = string.Join(
                "\n",
                description.EnabledSources.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
            raw += "\nmapping=" + description.PackageSourceMappingEnabled;

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
        }
        catch
        {
            return "default";
        }
    }

    private static void Store(string cacheKey, string? version)
    {
        var ttl = version == null ? FailureTtl : SuccessTtl;
        var entry = new CacheEntry(version, DateTime.UtcNow + ttl);
        Cache[cacheKey] = entry;
        WriteDiskCache(cacheKey, entry);
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
        NuGetSourceResolver.ClearCache();
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

    // ── On-disk cache (best-effort, one file per package + feed scope) ──

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

    private static string? CacheFilePath(string cacheKey)
    {
        if (DiskCacheDir == null)
        {
            return null;
        }

        // Sanitize the cache key into a safe file name.
        var safe = string.Concat(cacheKey.ToLowerInvariant().Select(c =>
            char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));

        // Long package ids plus the scope prefix can exceed path limits — bound the file name.
        if (safe.Length > 120)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey)), 0, 8).ToLowerInvariant();
            safe = safe.Substring(0, 100) + "_" + hash;
        }

        return Path.Combine(DiskCacheDir, safe + ".json");
    }

    private static bool TryReadDiskCache(string cacheKey, out CacheEntry entry)
    {
        entry = default!;
        var path = CacheFilePath(cacheKey);
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

    private static void WriteDiskCache(string cacheKey, CacheEntry entry)
    {
        var path = CacheFilePath(cacheKey);
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
