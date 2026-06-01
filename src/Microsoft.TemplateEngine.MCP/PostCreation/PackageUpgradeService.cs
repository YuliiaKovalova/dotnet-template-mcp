// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.TemplateEngine.MCP.PostCreation;

/// <summary>
/// Scans an existing project, solution, or directory for outdated NuGet package versions and
/// reports (or applies) upgrades to the latest stable version. CPM-aware: it reads/writes
/// <c>PackageVersion</c> entries in <c>Directory.Packages.props</c> as well as inline
/// <c>PackageReference</c> versions in <c>.csproj</c> files.
/// </summary>
internal sealed class PackageUpgradeService
{
    private readonly ILogger _logger;
    private readonly Func<string, CancellationToken, Task<string?>> _resolveLatest;

    public PackageUpgradeService(ILoggerFactory loggerFactory)
        : this(NuGetVersionResolver.GetLatestStableVersionAsync, loggerFactory.CreateLogger<PackageUpgradeService>())
    {
    }

    /// <summary>Test seam: inject a deterministic version resolver to avoid network access.</summary>
    internal PackageUpgradeService(
        Func<string, CancellationToken, Task<string?>> resolveLatest,
        ILogger? logger = null)
    {
        _resolveLatest = resolveLatest;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Analyze the packages referenced under <paramref name="path"/> and report available upgrades.
    /// When <paramref name="apply"/> is true, the newer versions are written back to disk.
    /// </summary>
    public async Task<PackageUpgradeReport> AnalyzeAsync(
        string path,
        bool apply,
        CancellationToken cancellationToken = default)
    {
        var report = new PackageUpgradeReport { Applied = apply };

        var (csprojFiles, scanRoot) = ResolveCsprojFiles(path);
        report.ProjectsScanned = csprojFiles.Count;
        if (csprojFiles.Count == 0)
        {
            return report;
        }

        var propsPath = PostCreationProcessor.FindDirectoryPackagesProps(scanRoot);
        report.CpmDetected = propsPath != null;
        report.DirectoryPackagesPropsPath = propsPath;

        // Collect every (file, element, package, current version) occurrence that carries a
        // parseable version. In CPM mode the authoritative versions live in
        // Directory.Packages.props, so we only consider PackageVersion entries that are actually
        // referenced by the scanned projects (avoids touching unrelated packages). In non-CPM mode
        // versions live inline on each PackageReference.
        var occurrences = new List<PackageOccurrence>();
        var loadedDocs = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);

        if (propsPath != null)
        {
            var referenced = CollectReferencedPackageNames(csprojFiles);
            CollectFromProps(propsPath, referenced, loadedDocs, occurrences);
        }
        else
        {
            foreach (var csproj in csprojFiles)
            {
                CollectFromCsproj(csproj, loadedDocs, occurrences);
            }
        }

        if (occurrences.Count == 0)
        {
            return report;
        }

        // Resolve the latest stable version once per distinct package name.
        var distinctNames = occurrences
            .Select(o => o.PackageName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var latestByName = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in distinctNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            latestByName[name] = await _resolveLatest(name, cancellationToken).ConfigureAwait(false);
        }

        var modifiedDocs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var occ in occurrences)
        {
            var latest = latestByName.TryGetValue(occ.PackageName, out var v) ? v : null;
            if (latest == null)
            {
                if (!report.UnresolvedPackages.Contains(occ.PackageName, StringComparer.OrdinalIgnoreCase))
                {
                    report.UnresolvedPackages.Add(occ.PackageName);
                }

                continue;
            }

            if (!PostCreationProcessor.IsNewerVersion(latest, occ.CurrentVersion))
            {
                report.UpToDateCount++;
                continue;
            }

            report.Upgrades.Add(new PackageUpgradeItem(
                occ.PackageName, occ.CurrentVersion, latest, occ.FilePath, occ.Location));

            if (apply)
            {
                occ.VersionAttribute.Value = latest;
                modifiedDocs.Add(occ.FilePath);
            }

            _logger.LogInformation("Upgrade available: {Package} {Old} → {New} ({File})",
                occ.PackageName, occ.CurrentVersion, latest, occ.FilePath);
        }

        if (apply)
        {
            foreach (var file in modifiedDocs)
            {
                loadedDocs[file].Save(file);
            }
        }

        return report;
    }

    private static (List<string> Files, string ScanRoot) ResolveCsprojFiles(string path)
    {
        if (File.Exists(path))
        {
            if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                return (new List<string> { Path.GetFullPath(path) }, Path.GetDirectoryName(Path.GetFullPath(path))!);
            }

            // .sln/.slnx or anything else: scan the file's directory.
            var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            return (EnumerateCsproj(dir), dir);
        }

        if (Directory.Exists(path))
        {
            var full = Path.GetFullPath(path);
            return (EnumerateCsproj(full), full);
        }

        return (new List<string>(), path);
    }

    private static List<string> EnumerateCsproj(string directory)
        => Directory.GetFiles(directory, "*.csproj", SearchOption.AllDirectories).ToList();

    /// <summary>
    /// Collect the set of package ids referenced (by Include/Update) across the scanned projects.
    /// Used in CPM mode to scope Directory.Packages.props upgrades to packages actually in use.
    /// </summary>
    private static HashSet<string> CollectReferencedPackageNames(IEnumerable<string> csprojFiles)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var csproj in csprojFiles)
        {
            XDocument doc;
            try
            {
                doc = XDocument.Load(csproj);
            }
            catch
            {
                continue;
            }

            var root = doc.Root;
            if (root == null)
            {
                continue;
            }

            var ns = root.GetDefaultNamespace();
            foreach (var pr in root.Descendants(ns + "PackageReference"))
            {
                var name = pr.Attribute("Include")?.Value ?? pr.Attribute("Update")?.Value;
                if (!string.IsNullOrEmpty(name))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    private static void CollectFromProps(
        string propsPath,
        HashSet<string> referencedPackages,
        Dictionary<string, XDocument> loadedDocs,
        List<PackageOccurrence> occurrences)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(propsPath, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            return;
        }

        var root = doc.Root;
        if (root == null)
        {
            return;
        }

        loadedDocs[propsPath] = doc;
        var ns = root.GetDefaultNamespace();

        foreach (var pv in root.Descendants(ns + "PackageVersion"))
        {
            var name = pv.Attribute("Include")?.Value;
            var versionAttr = pv.Attribute("Version");
            if (string.IsNullOrEmpty(name) || versionAttr == null)
            {
                continue;
            }

            // Only upgrade versions for packages actually referenced by the scanned projects.
            if (!referencedPackages.Contains(name))
            {
                continue;
            }

            if (!IsUpgradeableVersion(versionAttr.Value))
            {
                continue;
            }

            occurrences.Add(new PackageOccurrence(propsPath, name, versionAttr.Value, versionAttr, "Directory.Packages.props"));
        }
    }

    private static void CollectFromCsproj(
        string csprojPath,
        Dictionary<string, XDocument> loadedDocs,
        List<PackageOccurrence> occurrences)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(csprojPath, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            return;
        }

        var root = doc.Root;
        if (root == null)
        {
            return;
        }

        var ns = root.GetDefaultNamespace();
        bool hasAny = false;

        foreach (var pr in root.Descendants(ns + "PackageReference"))
        {
            var name = pr.Attribute("Include")?.Value ?? pr.Attribute("Update")?.Value;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            // Only the attribute form is rewritable in place; the element form is rare.
            var versionAttr = pr.Attribute("Version");
            if (versionAttr == null)
            {
                continue; // Versionless reference (CPM) or element-style version — handled via props.
            }

            if (!IsUpgradeableVersion(versionAttr.Value))
            {
                continue;
            }

            hasAny = true;
            occurrences.Add(new PackageOccurrence(csprojPath, name, versionAttr.Value, versionAttr, "csproj"));
        }

        if (hasAny)
        {
            loadedDocs[csprojPath] = doc;
        }
    }

    /// <summary>
    /// Only consider concrete, parseable versions for upgrade. Floating ranges ("1.*"),
    /// MSBuild properties ("$(Foo)"), and version ranges ("[1.0,2.0)") are left untouched.
    /// </summary>
    private static bool IsUpgradeableVersion(string version)
        => !string.IsNullOrWhiteSpace(version) && NuGet.Versioning.NuGetVersion.TryParse(version, out _);

    private sealed record PackageOccurrence(
        string FilePath,
        string PackageName,
        string CurrentVersion,
        XAttribute VersionAttribute,
        string Location);
}

/// <summary>Result of a package-upgrade analysis.</summary>
internal sealed class PackageUpgradeReport
{
    public bool Applied { get; set; }
    public bool CpmDetected { get; set; }
    public string? DirectoryPackagesPropsPath { get; set; }
    public int ProjectsScanned { get; set; }
    public int UpToDateCount { get; set; }
    public List<PackageUpgradeItem> Upgrades { get; } = new();
    public List<string> UnresolvedPackages { get; } = new();
}

/// <summary>A single available package upgrade.</summary>
internal sealed record PackageUpgradeItem(
    string PackageName,
    string CurrentVersion,
    string LatestVersion,
    string File,
    string Location);
