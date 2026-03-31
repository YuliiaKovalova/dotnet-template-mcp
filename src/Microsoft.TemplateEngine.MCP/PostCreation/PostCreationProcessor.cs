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
    private readonly ILogger _logger;

    public PostCreationProcessor(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<PostCreationProcessor>();
    }

    /// <summary>
    /// Process all generated .csproj files in the output directory.
    /// Detects CPM, strips versions, updates Directory.Packages.props, and optionally resolves latest NuGet versions.
    /// </summary>
    public async Task<PostCreationResult> ProcessAsync(
        string outputDirectory,
        bool resolveLatestVersions = true,
        CancellationToken cancellationToken = default)
    {
        var result = new PostCreationResult();

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
                csprojPath, packagesPropsPath, cpmDetected, resolveLatestVersions, cancellationToken)
                .ConfigureAwait(false);
            result.ProcessedFiles.Add(csprojResult);
        }

        return result;
    }

    private async Task<CsprojProcessingResult> ProcessCsprojAsync(
        string csprojPath,
        string? packagesPropsPath,
        bool cpmDetected,
        bool resolveLatestVersions,
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

        // Step 1: Resolve latest stable versions if requested
        if (resolveLatestVersions)
        {
            foreach (var (element, name, currentVersion) in packages)
            {
                if (currentVersion == null)
                {
                    continue; // Already versionless (CPM project)
                }

                cancellationToken.ThrowIfCancellationRequested();

                var latestVersion = await NuGetVersionResolver.GetLatestStableVersionAsync(name, cancellationToken)
                    .ConfigureAwait(false);

                if (latestVersion != null && latestVersion != currentVersion)
                {
                    fileResult.VersionUpgrades.Add(new VersionUpgrade(name, currentVersion, latestVersion));
                    _logger.LogInformation("Upgrade available: {Package} {Old} → {New}", name, currentVersion, latestVersion);
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

                // Determine the version to use: latest if resolved, otherwise current
                var upgrade = fileResult.VersionUpgrades.FirstOrDefault(u => u.PackageName == name);
                var versionToUse = upgrade?.NewVersion ?? currentVersion;

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
                else if (resolveLatestVersions && upgrade != null)
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
        else if (resolveLatestVersions && fileResult.VersionUpgrades.Count > 0)
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

/// <summary>Overall result of post-creation processing.</summary>
internal class PostCreationResult
{
    public bool CpmDetected { get; set; }
    public string? DirectoryPackagesPropsPath { get; set; }
    public List<CsprojProcessingResult> ProcessedFiles { get; } = new();

    public bool HasChanges => ProcessedFiles.Any(f =>
        f.VersionUpgrades.Count > 0 || f.VersionsStripped.Count > 0 || f.AddedToDirectoryPackagesProps.Count > 0);
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
