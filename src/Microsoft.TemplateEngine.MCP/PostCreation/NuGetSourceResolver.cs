// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NuGet.Configuration;
using NuGet.Credentials;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using INuGetLogger = NuGet.Common.ILogger;
using NuGetLogLevel = NuGet.Common.LogLevel;
using NuGetNullLogger = NuGet.Common.NullLogger;

namespace Microsoft.TemplateEngine.MCP.PostCreation;

/// <summary>
/// Resolves package versions through the caller's real NuGet configuration instead of a hardcoded
/// endpoint.
///
/// The previous implementation issued a raw HTTP GET against
/// <c>https://api.nuget.org/v3-flatcontainer/</c>. That ignored <c>NuGet.config</c> entirely, so
/// private feeds, credentials, package source mapping and proxies were all invisible — and, worse,
/// it would happily write public nuget.org versions into a repository whose policy is an internal
/// feed. This type discovers the <c>NuGet.config</c> chain for a given directory and queries the
/// configured, enabled sources using the standard NuGet client stack, which brings credential
/// providers (including Azure Artifacts), proxy settings, and local folder feeds along for free.
/// </summary>
internal static class NuGetSourceResolver
{
    private static readonly ConcurrentDictionary<string, SourceContext> ContextCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, SourceRepository> RepositoryCache = new(StringComparer.OrdinalIgnoreCase);
    private static int _credentialServiceInitialized;

    /// <summary>
    /// Returns the highest stable version of <paramref name="packageId"/> visible from the NuGet
    /// configuration that applies to <paramref name="rootDirectory"/>, or null when the package is
    /// not found on any configured source (including when the machine is offline).
    /// </summary>
    public static async Task<string?> GetLatestStableVersionAsync(
        string packageId,
        string rootDirectory,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var context = GetSourceContext(rootDirectory, logger);
        var sources = context.GetSourcesForPackage(packageId);
        if (sources.Count == 0)
        {
            logger?.LogDebug(
                "No enabled NuGet source applies to package {Package} (root: {Root}).", packageId, rootDirectory);
            return null;
        }

        EnsureCredentialService();

        NuGetVersion? best = null;
        using var cacheContext = new SourceCacheContext { NoCache = false };

        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var repository = RepositoryCache.GetOrAdd(source.Source, _ => Repository.Factory.GetCoreV3(source));
                var finder = await repository
                    .GetResourceAsync<FindPackageByIdResource>(cancellationToken)
                    .ConfigureAwait(false);

                if (finder == null)
                {
                    continue;
                }

                var versions = await finder
                    .GetAllVersionsAsync(packageId, cacheContext, NuGetNullLogger.Instance, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var version in versions)
                {
                    if (!version.IsPrerelease && (best == null || version > best))
                    {
                        best = version;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A single unreachable or unauthorized feed must not fail the whole lookup — other
                // configured sources may still answer.
                logger?.LogDebug(
                    ex, "NuGet source {Source} could not be queried for {Package}.", source.Name, packageId);
            }
        }

        return best?.ToNormalizedString();
    }

    /// <summary>
    /// Describes the sources that apply to a directory, for diagnostics surfaced to agents.
    /// </summary>
    public static SourceDescription DescribeSources(string rootDirectory, ILogger? logger)
    {
        var context = GetSourceContext(rootDirectory, logger);
        return new SourceDescription(
            context.ConfigFilePaths,
            context.EnabledSources.Select(s => s.Name).ToList(),
            context.HasPackageSourceMapping);
    }

    /// <summary>
    /// Identity of the feed set that applies to <paramref name="rootDirectory"/> for a given
    /// package, used to partition caches.
    ///
    /// Keyed on source <em>URLs</em>, not names: two organizations commonly both call their feed
    /// "internal" (or override "nuget.org" to point at a proxy), and keying on the display name
    /// would serve one repository's resolved versions to another with entirely different sources —
    /// the exact cross-feed leak this scoping exists to prevent. The per-package allowed set is
    /// included so a packageSourceMapping change invalidates the entry.
    /// </summary>
    public static string GetSourceScopeIdentity(string packageId, string rootDirectory, ILogger? logger)
    {
        var context = GetSourceContext(rootDirectory, logger);

        var allSources = context.EnabledSources
            .Select(s => s.Source ?? s.Name)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);

        var applicable = context.GetSourcesForPackage(packageId)
            .Select(s => s.Source ?? s.Name)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);

        return string.Join("\n", allSources)
            + "\nmapping=" + context.HasPackageSourceMapping
            + "\napplicable=" + string.Join(",", applicable);
    }

    /// <summary>Drop cached settings/repositories. Used by tests and after config changes.</summary>
    internal static void ClearCache()
    {
        ContextCache.Clear();
        RepositoryCache.Clear();
    }

    private static SourceContext GetSourceContext(string rootDirectory, ILogger? logger)
    {
        var key = NormalizeRoot(rootDirectory);
        return ContextCache.GetOrAdd(key, k => SourceContext.Load(k, logger));
    }

    private static string NormalizeRoot(string rootDirectory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                return Environment.CurrentDirectory;
            }

            var full = Path.GetFullPath(rootDirectory);

            // Settings are discovered from a directory; if a file was passed, start at its parent.
            if (File.Exists(full))
            {
                return Path.GetDirectoryName(full) ?? Environment.CurrentDirectory;
            }

            return full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Environment.CurrentDirectory;
        }
    }

    /// <summary>
    /// Installs NuGet's default credential service once per process so authenticated feeds
    /// (Azure Artifacts, GitHub Packages, on-prem Artifactory) work. Non-interactive: an MCP server
    /// has no console to prompt on, so it relies on credential providers and stored credentials.
    /// </summary>
    private static void EnsureCredentialService()
    {
        if (Interlocked.Exchange(ref _credentialServiceInitialized, 1) != 0)
        {
            return;
        }

        try
        {
            DefaultCredentialServiceUtility.SetupDefaultCredentialService(
                NuGetNullLogger.Instance, nonInteractive: true);
        }
        catch
        {
            // Credential provider setup is best-effort; anonymous sources still work without it.
        }
    }

    /// <summary>Resolved NuGet configuration for one directory.</summary>
    private sealed class SourceContext
    {
        private readonly PackageSourceMapping _sourceMapping;

        private SourceContext(
            IReadOnlyList<PackageSource> enabledSources,
            PackageSourceMapping sourceMapping,
            IReadOnlyList<string> configFilePaths)
        {
            EnabledSources = enabledSources;
            _sourceMapping = sourceMapping;
            ConfigFilePaths = configFilePaths;
        }

        public IReadOnlyList<PackageSource> EnabledSources { get; }

        public IReadOnlyList<string> ConfigFilePaths { get; }

        public bool HasPackageSourceMapping => _sourceMapping.IsEnabled;

        public static SourceContext Load(string rootDirectory, ILogger? logger)
        {
            try
            {
                var settings = Settings.LoadDefaultSettings(rootDirectory);
                var provider = new PackageSourceProvider(settings);
                var enabled = provider.LoadPackageSources().Where(s => s.IsEnabled).ToList();
                var mapping = PackageSourceMapping.GetPackageSourceMapping(settings);
                var configPaths = settings.GetConfigFilePaths()?.ToList() ?? new List<string>();

                logger?.LogDebug(
                    "Loaded {Count} enabled NuGet source(s) for {Root} from {ConfigCount} config file(s).",
                    enabled.Count, rootDirectory, configPaths.Count);

                return new SourceContext(enabled, mapping, configPaths);
            }
            catch (Exception ex)
            {
                // A malformed NuGet.config must degrade to "no sources" rather than crash a tool call.
                logger?.LogWarning(ex, "Failed to load NuGet configuration for {Root}.", rootDirectory);
                return new SourceContext(
                    Array.Empty<PackageSource>(),
                    PackageSourceMapping.GetPackageSourceMapping(NullSettings.Instance),
                    Array.Empty<string>());
            }
        }

        /// <summary>
        /// Applies package source mapping. When mapping is configured, only the sources it allows
        /// for this package id may be queried — otherwise the server would leak internal package
        /// names to public feeds and could resolve versions the repository policy forbids.
        /// </summary>
        public IReadOnlyList<PackageSource> GetSourcesForPackage(string packageId)
        {
            if (!_sourceMapping.IsEnabled)
            {
                return EnabledSources;
            }

            var allowedNames = _sourceMapping.GetConfiguredPackageSources(packageId);
            if (allowedNames == null || allowedNames.Count == 0)
            {
                return Array.Empty<PackageSource>();
            }

            var allowed = new HashSet<string>(allowedNames, StringComparer.OrdinalIgnoreCase);
            return EnabledSources.Where(s => allowed.Contains(s.Name)).ToList();
        }
    }

    /// <summary>Diagnostic view of the NuGet configuration in effect.</summary>
    internal sealed record SourceDescription(
        IReadOnlyList<string> ConfigFiles,
        IReadOnlyList<string> EnabledSources,
        bool PackageSourceMappingEnabled);

    /// <summary>Adapts Microsoft.Extensions.Logging to NuGet's logger abstraction.</summary>
    internal sealed class NuGetLoggerAdapter : INuGetLogger
    {
        private readonly ILogger _logger;

        public NuGetLoggerAdapter(ILogger logger) => _logger = logger;

        public void LogDebug(string data) => _logger.LogDebug("{Message}", data);

        public void LogVerbose(string data) => _logger.LogDebug("{Message}", data);

        public void LogInformation(string data) => _logger.LogInformation("{Message}", data);

        public void LogMinimal(string data) => _logger.LogInformation("{Message}", data);

        public void LogWarning(string data) => _logger.LogWarning("{Message}", data);

        public void LogError(string data) => _logger.LogError("{Message}", data);

        public void LogInformationSummary(string data) => _logger.LogInformation("{Message}", data);

        public void Log(NuGetLogLevel level, string data) => _logger.Log(Map(level), "{Message}", data);

        public Task LogAsync(NuGetLogLevel level, string data)
        {
            Log(level, data);
            return Task.CompletedTask;
        }

        public void Log(NuGet.Common.ILogMessage message) => Log(message.Level, message.Message);

        public Task LogAsync(NuGet.Common.ILogMessage message)
        {
            Log(message);
            return Task.CompletedTask;
        }

        private static LogLevel Map(NuGetLogLevel level) => level switch
        {
            NuGetLogLevel.Debug or NuGetLogLevel.Verbose => LogLevel.Debug,
            NuGetLogLevel.Information or NuGetLogLevel.Minimal => LogLevel.Information,
            NuGetLogLevel.Warning => LogLevel.Warning,
            NuGetLogLevel.Error => LogLevel.Error,
            _ => LogLevel.Information,
        };
    }
}
