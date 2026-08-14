// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Microsoft.TemplateEngine.MCP.PostCreation;

/// <summary>
/// Post-creation processor that adapts template output to the target environment.
/// Handles CPM (Central Package Management) compatibility and NuGet version upgrades.
/// </summary>
internal sealed class PostCreationProcessor
{
    /// <summary>
    /// Report, not Apply. Rewriting every PackageReference to "latest stable" at creation time
    /// produces untested version combinations and silently overrides the template author's pinning,
    /// so upgrades are surfaced to the caller instead of being applied.
    /// </summary>
    public const PackageVersionPolicy DefaultVersionPolicy = PackageVersionPolicy.Report;

    /// <summary>
    /// Maps a tool's optional <c>resolveLatestVersions</c> argument to a policy.
    ///
    /// Single-sourced deliberately: the tool and the <c>bool</c> overload previously disagreed about
    /// what <c>false</c> meant (one reported — still querying feeds — while the other skipped), which
    /// left callers with no way to opt out of network access at all.
    /// </summary>
    /// <param name="resolveLatestVersions">
    /// <c>true</c> to apply upgrades, <c>false</c> to make no feed calls, <c>null</c> to use the
    /// configured default.
    /// </param>
    public static PackageVersionPolicy ResolvePolicy(bool? resolveLatestVersions, McpFeatureFlags featureFlags)
        => resolveLatestVersions switch
        {
            true => PackageVersionPolicy.Apply,
            false => PackageVersionPolicy.Skip,
            null when featureFlags.OfflineMode => PackageVersionPolicy.Skip,
            null when featureFlags.ResolveLatestVersionsByDefault => PackageVersionPolicy.Apply,
            null => DefaultVersionPolicy,
        };

    private readonly ILogger _logger;

    public PostCreationProcessor(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<PostCreationProcessor>();
    }

    /// <summary>
    /// Process all generated .csproj files in the output directory.
    /// Detects CPM, strips versions, updates Directory.Packages.props, and reports (or applies)
    /// newer NuGet versions according to <paramref name="versionPolicy"/>.
    /// </summary>
    public async Task<PostCreationResult> ProcessAsync(
        string outputDirectory,
        PackageVersionPolicy versionPolicy = DefaultVersionPolicy,
        CancellationToken cancellationToken = default)
    {
        var result = new PostCreationResult { VersionPolicy = versionPolicy };

        var csprojFiles = Directory.GetFiles(outputDirectory, "*.csproj", SearchOption.AllDirectories);
        if (csprojFiles.Length == 0)
        {
            return result;
        }

        // Detect CPM: walk up from output directory to find Directory.Packages.props
        var packagesPropsPath = FindDirectoryPackagesProps(outputDirectory);
        bool cpmDetected = packagesPropsPath != null;

        if (cpmDetected)
        {
            result.CpmDetected = true;
            result.DirectoryPackagesPropsPath = packagesPropsPath;
            _logger.LogInformation("Detected CPM: {Path}", packagesPropsPath);
        }

        foreach (var csprojPath in csprojFiles)
        {
            var csprojResult = await ProcessCsprojAsync(
                csprojPath, outputDirectory, packagesPropsPath, cpmDetected, versionPolicy, cancellationToken)
                .ConfigureAwait(false);
            result.ProcessedFiles.Add(csprojResult);
        }

        return result;
    }

    /// <summary>
    /// Backward-compatible overload: <c>true</c> applies upgrades, <c>false</c> skips the lookup entirely.
    /// </summary>
    public Task<PostCreationResult> ProcessAsync(
        string outputDirectory,
        bool resolveLatestVersions,
        CancellationToken cancellationToken = default)
    {
        return ProcessAsync(
            outputDirectory,
            resolveLatestVersions ? PackageVersionPolicy.Apply : PackageVersionPolicy.Skip,
            cancellationToken);
    }

    private async Task<CsprojProcessingResult> ProcessCsprojAsync(
        string csprojPath,
        string rootDirectory,
        string? packagesPropsPath,
        bool cpmDetected,
        PackageVersionPolicy versionPolicy,
        CancellationToken cancellationToken)
    {
        var fileResult = new CsprojProcessingResult { FilePath = csprojPath };

        var doc = XDocument.Load(csprojPath, LoadOptions.PreserveWhitespace);
        var root = doc.Root;
        if (root == null)
        {
            return fileResult;
        }

        var ns = root.GetDefaultNamespace();
        var packageRefs = root.Descendants(ns + "PackageReference").ToList();
        if (packageRefs.Count == 0)
        {
            return fileResult;
        }

        // Collect all packages with their current versions
        var packages = new List<(XElement Element, string Name, string? Version)>();
        foreach (var pr in packageRefs)
        {
            var name = pr.Attribute("Include")?.Value;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var version = pr.Attribute("Version")?.Value ?? pr.Element(ns + "Version")?.Value;
            packages.Add((pr, name, version));
        }

        // Step 1: Look up newer versions unless explicitly skipped. Under Report (the default) the
        // findings are surfaced to the caller but never written — rewriting every reference to
        // "latest stable" at creation time produces untested combinations and discards the template
        // author's deliberate pinning.
        bool applyUpgrades = versionPolicy == PackageVersionPolicy.Apply;
        if (versionPolicy != PackageVersionPolicy.Skip)
        {
            foreach (var (element, name, currentVersion) in packages)
            {
                if (currentVersion == null)
                {
                    continue; // Already versionless (CPM project)
                }

                cancellationToken.ThrowIfCancellationRequested();

                var latestVersion = await NuGetVersionResolver
                    .GetLatestStableVersionAsync(name, rootDirectory, _logger, cancellationToken)
                    .ConfigureAwait(false);

                if (latestVersion != null && IsNewerVersion(latestVersion, currentVersion))
                {
                    fileResult.VersionUpgrades.Add(new VersionUpgrade(name, currentVersion, latestVersion));
                    _logger.LogInformation(
                        applyUpgrades ? "Upgrading: {Package} {Old} → {New}" : "Upgrade available: {Package} {Old} → {New}",
                        name, currentVersion, latestVersion);
                }
            }
        }

        // Step 2: Handle CPM
        if (cpmDetected && packagesPropsPath != null)
        {
            var propsDoc = XDocument.Load(packagesPropsPath, LoadOptions.PreserveWhitespace);
            var propsRoot = propsDoc.Root;
            if (propsRoot == null)
            {
                return fileResult;
            }

            var propsNs = propsRoot.GetDefaultNamespace();

            // Get existing PackageVersion entries
            var existingVersions = propsRoot.Descendants(propsNs + "PackageVersion")
                .Select(pv => pv.Attribute("Include")?.Value)
                .Where(v => v != null)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Find or create the ItemGroup for PackageVersion entries
            var packageVersionItemGroup = propsRoot.Descendants(propsNs + "PackageVersion")
                .FirstOrDefault()?.Parent;

            bool propsModified = false;

            foreach (var (element, name, currentVersion) in packages)
            {
                if (currentVersion == null)
                {
                    continue; // Already versionless
                }

                // Determine the version to use: latest only when we're applying upgrades,
                // otherwise preserve the version the template author chose.
                var upgrade = fileResult.VersionUpgrades.FirstOrDefault(u => u.PackageName == name);
                var versionToUse = (applyUpgrades ? upgrade?.NewVersion : null) ?? currentVersion;

                // Add to Directory.Packages.props if not already there, or update if stale
                if (!existingVersions.Contains(name))
                {
                    if (packageVersionItemGroup == null)
                    {
                        packageVersionItemGroup = new XElement(propsNs + "ItemGroup");
                        propsRoot.Add(packageVersionItemGroup);
                    }

                    var newEntry = new XElement(propsNs + "PackageVersion",
                        new XAttribute("Include", name),
                        new XAttribute("Version", versionToUse));
                    packageVersionItemGroup.Add(newEntry);

                    fileResult.AddedToDirectoryPackagesProps.Add(
                        new PackageVersionEntry(name, versionToUse));
                    propsModified = true;

                    _logger.LogInformation("Added to Directory.Packages.props: {Package} {Version}", name, versionToUse);
                }
                else if (applyUpgrades && upgrade != null)
                {
                    // Package exists in props but version is stale — update it
                    var existingElement = propsRoot.Descendants(propsNs + "PackageVersion")
                        .FirstOrDefault(pv => pv.Attribute("Include")?.Value
                            ?.Equals(name, StringComparison.OrdinalIgnoreCase) == true);
                    if (existingElement != null)
                    {
                        var versionAttrInProps = existingElement.Attribute("Version");
                        if (versionAttrInProps != null && !versionAttrInProps.Value.Equals(upgrade.NewVersion, StringComparison.Ordinal))
                        {
                            versionAttrInProps.Value = upgrade.NewVersion;
                            propsModified = true;
                            _logger.LogInformation("Updated in Directory.Packages.props: {Package} {Old} → {New}",
                                name, upgrade.OldVersion, upgrade.NewVersion);
                        }
                    }
                }

                // Strip Version from .csproj PackageReference
                var versionAttr = element.Attribute("Version");
                if (versionAttr != null)
                {
                    versionAttr.Remove();
                    fileResult.VersionsStripped.Add(name);
                }

                var versionElement = element.Element(ns + "Version");
                if (versionElement != null)
                {
                    versionElement.Remove();
                    if (!fileResult.VersionsStripped.Contains(name))
                    {
                        fileResult.VersionsStripped.Add(name);
                    }
                }
            }

            if (propsModified)
            {
                propsDoc.Save(packagesPropsPath);
            }

            if (fileResult.VersionsStripped.Count > 0)
            {
                doc.Save(csprojPath);
            }
        }
        else if (applyUpgrades && fileResult.VersionUpgrades.Count > 0)
        {
            // No CPM — update versions directly in .csproj
            foreach (var upgrade in fileResult.VersionUpgrades)
            {
                var element = packages.FirstOrDefault(p => p.Name == upgrade.PackageName).Element;
                if (element == null)
                {
                    continue;
                }

                var versionAttr = element.Attribute("Version");
                if (versionAttr != null)
                {
                    versionAttr.Value = upgrade.NewVersion;
                }
                else
                {
                    var versionElement = element.Element(ns + "Version");
                    if (versionElement != null)
                    {
                        versionElement.Value = upgrade.NewVersion;
                    }
                }
            }

            doc.Save(csprojPath);
        }

        return fileResult;
    }

    /// <summary>
    /// Returns true when <paramref name="candidate"/> is a strictly newer version than
    /// <paramref name="current"/>. Uses SemVer ordering, falling back to ordinal inequality
    /// when either value isn't a valid NuGet version (never downgrades on parse failure).
    /// </summary>
    internal static bool IsNewerVersion(string candidate, string current)
    {
        if (NuGet.Versioning.NuGetVersion.TryParse(candidate, out var c) &&
            NuGet.Versioning.NuGetVersion.TryParse(current, out var cur))
        {
            return c > cur;
        }

        return !candidate.Equals(current, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Walk up the directory tree to find Directory.Packages.props.
    /// </summary>
    internal static string? FindDirectoryPackagesProps(string startDirectory)
    {
        var dir = startDirectory;
        while (dir != null)
        {
            var propsPath = Path.Combine(dir, "Directory.Packages.props");
            if (File.Exists(propsPath))
            {
                return propsPath;
            }

            var parent = Directory.GetParent(dir);
            if (parent == null)
            {
                break;
            }

            dir = parent.FullName;
        }

        return null;
    }
}

/// <summary>
/// Controls what happens when a newer stable version of a referenced package exists.
/// </summary>
internal enum PackageVersionPolicy
{
    /// <summary>Don't query feeds at all — fastest, and fully offline.</summary>
    Skip,

    /// <summary>Query feeds and report available upgrades without modifying any file. Default.</summary>
    Report,

    /// <summary>Query feeds and rewrite package references to the latest stable version.</summary>
    Apply,
}

/// <summary>Overall result of post-creation processing.</summary>
internal class PostCreationResult
{
    public bool CpmDetected { get; set; }
    public string? DirectoryPackagesPropsPath { get; set; }
    public List<CsprojProcessingResult> ProcessedFiles { get; } = new();

    /// <summary>The policy that governed version handling for this run.</summary>
    public PackageVersionPolicy VersionPolicy { get; init; } = PackageVersionPolicy.Report;

    /// <summary>True when the discovered upgrades were written to disk rather than just reported.</summary>
    public bool VersionUpgradesApplied => VersionPolicy == PackageVersionPolicy.Apply;

    /// <summary>
    /// True when this run actually modified files. Reported-but-not-applied upgrades are
    /// deliberately excluded — nothing was written for them.
    /// </summary>
    public bool HasChanges => ProcessedFiles.Any(f =>
        (VersionUpgradesApplied && f.VersionUpgrades.Count > 0)
        || f.VersionsStripped.Count > 0
        || f.AddedToDirectoryPackagesProps.Count > 0);

    /// <summary>True when there is anything worth reporting back to the caller.</summary>
    public bool HasFindings => HasChanges || ProcessedFiles.Any(f => f.VersionUpgrades.Count > 0);
}

/// <summary>Result of processing a single .csproj file.</summary>
internal class CsprojProcessingResult
{
    public required string FilePath { get; init; }
    public List<VersionUpgrade> VersionUpgrades { get; } = new();
    public List<string> VersionsStripped { get; } = new();
    public List<PackageVersionEntry> AddedToDirectoryPackagesProps { get; } = new();
}

/// <summary>A package version upgrade (old → new).</summary>
internal record VersionUpgrade(string PackageName, string OldVersion, string NewVersion);

/// <summary>A PackageVersion entry added to Directory.Packages.props.</summary>
internal record PackageVersionEntry(string PackageName, string Version);
