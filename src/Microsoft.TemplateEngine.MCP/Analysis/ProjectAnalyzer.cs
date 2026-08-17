// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.Xml.Linq;

namespace Microsoft.TemplateEngine.MCP.Analysis;

/// <summary>
/// Parses a .csproj file and extracts its configuration pattern into a <see cref="ProjectAnalysis"/>.
/// Handles SDK-style projects, CPM, analyzer metadata, shared compiles, and content items.
/// </summary>
internal static class ProjectAnalyzer
{
    /// <summary>
    /// Analyze a .csproj file and extract its full configuration pattern.
    /// </summary>
    public static ProjectAnalysis Analyze(string csprojPath)
    {
        if (!File.Exists(csprojPath))
        {
            throw new FileNotFoundException($"Project file not found: {csprojPath}");
        }

        var doc = XDocument.Load(csprojPath);
        var root = doc.Root ?? throw new InvalidOperationException($"Invalid project file: {csprojPath}");
        var ns = root.GetDefaultNamespace();

        var sdk = root.Attribute("Sdk")?.Value ?? "Microsoft.NET.Sdk";

        var properties = ExtractProperties(root, ns);
        var packageRefs = ExtractPackageReferences(root, ns);
        var projectRefs = ExtractProjectReferences(root, ns);
        var sharedCompiles = ExtractSharedCompiles(root, ns);
        var contentItems = ExtractContentItems(root, ns);
        var imports = ExtractImports(root, ns);

        // Detect CPM: if any PackageReference lacks a Version attribute and there's no Version child element
        bool usesCpm = packageRefs.Count > 0 && packageRefs.All(p => p.Version == null);

        return new ProjectAnalysis
        {
            SourceProjectPath = Path.GetFullPath(csprojPath),
            Sdk = sdk,
            Properties = properties,
            PackageReferences = packageRefs,
            ProjectReferences = projectRefs,
            SharedCompiles = sharedCompiles,
            ContentItems = contentItems,
            Imports = imports,
            UsesCentralPackageManagement = usesCpm,
        };
    }

    private static IReadOnlyList<ProjectProperty> ExtractProperties(XElement root, XNamespace ns)
    {
        var properties = new List<ProjectProperty>();

        foreach (var pg in root.Elements(ns + "PropertyGroup"))
        {
            foreach (var prop in pg.Elements())
            {
                properties.Add(new ProjectProperty
                {
                    Name = prop.Name.LocalName,
                    Value = prop.Value,
                    Condition = prop.Attribute("Condition")?.Value,
                });
            }
        }

        return properties;
    }

    private static IReadOnlyList<PackageReferenceInfo> ExtractPackageReferences(XElement root, XNamespace ns)
    {
        var refs = new List<PackageReferenceInfo>();

        foreach (var ig in root.Elements(ns + "ItemGroup"))
        {
            foreach (var pr in ig.Elements(ns + "PackageReference"))
            {
                var include = pr.Attribute("Include")?.Value;
                if (string.IsNullOrEmpty(include))
                {
                    continue;
                }

                refs.Add(new PackageReferenceInfo
                {
                    Include = include,
                    Version = pr.Attribute("Version")?.Value ?? pr.Element(ns + "Version")?.Value,
                    PrivateAssets = pr.Attribute("PrivateAssets")?.Value ?? pr.Element(ns + "PrivateAssets")?.Value,
                    IncludeAssets = pr.Attribute("IncludeAssets")?.Value ?? pr.Element(ns + "IncludeAssets")?.Value,
                    ExcludeAssets = pr.Attribute("ExcludeAssets")?.Value ?? pr.Element(ns + "ExcludeAssets")?.Value,
                });
            }
        }

        return refs;
    }

    private static IReadOnlyList<string> ExtractProjectReferences(XElement root, XNamespace ns)
    {
        var refs = new List<string>();

        foreach (var ig in root.Elements(ns + "ItemGroup"))
        {
            foreach (var pr in ig.Elements(ns + "ProjectReference"))
            {
                var include = pr.Attribute("Include")?.Value;
                if (!string.IsNullOrEmpty(include))
                {
                    refs.Add(include);
                }
            }
        }

        return refs;
    }

    private static IReadOnlyList<SharedCompileInfo> ExtractSharedCompiles(XElement root, XNamespace ns)
    {
        var compiles = new List<SharedCompileInfo>();

        foreach (var ig in root.Elements(ns + "ItemGroup"))
        {
            foreach (var c in ig.Elements(ns + "Compile"))
            {
                var include = c.Attribute("Include")?.Value;
                // Only treat as "shared compile" if it references outside the project dir
                if (include != null && (include.Contains("..") || include.Contains("**")))
                {
                    compiles.Add(new SharedCompileInfo
                    {
                        Include = include,
                        Link = c.Attribute("Link")?.Value,
                    });
                }
            }
        }

        return compiles;
    }

    private static IReadOnlyList<ContentItemInfo> ExtractContentItems(XElement root, XNamespace ns)
    {
        var items = new List<ContentItemInfo>();
        var itemTypes = new[] { "Content", "None", "EmbeddedResource", "Compile" };

        foreach (var ig in root.Elements(ns + "ItemGroup"))
        {
            foreach (var itemType in itemTypes)
            {
                foreach (var item in ig.Elements(ns + itemType))
                {
                    var include = item.Attribute("Include")?.Value;
                    var remove = item.Attribute("Remove")?.Value;

                    // Skip regular source files — only capture items with special metadata
                    if (itemType == "Compile" && remove == null)
                    {
                        continue;
                    }

                    if (include != null || remove != null)
                    {
                        items.Add(new ContentItemInfo
                        {
                            ItemType = itemType,
                            Include = include,
                            Remove = remove,
                            CopyToOutputDirectory = item.Attribute("CopyToOutputDirectory")?.Value
                                ?? item.Element(ns + "CopyToOutputDirectory")?.Value,
                        });
                    }
                }
            }
        }

        return items;
    }

    private static IReadOnlyList<string> ExtractImports(XElement root, XNamespace ns)
    {
        var imports = new List<string>();

        foreach (var import in root.Elements(ns + "Import"))
        {
            var project = import.Attribute("Project")?.Value;
            if (!string.IsNullOrEmpty(project))
            {
                imports.Add(project);
            }
        }

        return imports;
    }
}
