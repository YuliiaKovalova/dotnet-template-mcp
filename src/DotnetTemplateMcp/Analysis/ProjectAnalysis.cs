// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

namespace DotnetTemplateMcp.Analysis;

/// <summary>
/// Structured representation of a .csproj file's configuration pattern.
/// Captures everything needed to generate a matching template.
/// </summary>
internal sealed class ProjectAnalysis
{
    /// <summary>Full path to the analyzed .csproj file.</summary>
    public required string SourceProjectPath { get; init; }

    /// <summary>Project SDK (e.g., "Microsoft.NET.Sdk", "MSTest.Sdk").</summary>
    public required string Sdk { get; init; }

    /// <summary>MSBuild properties from all PropertyGroups.</summary>
    public required IReadOnlyList<ProjectProperty> Properties { get; init; }

    /// <summary>PackageReference items with metadata.</summary>
    public required IReadOnlyList<PackageReferenceInfo> PackageReferences { get; init; }

    /// <summary>ProjectReference items (relative paths).</summary>
    public required IReadOnlyList<string> ProjectReferences { get; init; }

    /// <summary>Shared Compile includes (e.g., ..\Shared\**\*.cs).</summary>
    public required IReadOnlyList<SharedCompileInfo> SharedCompiles { get; init; }

    /// <summary>Content/None items with copy behavior.</summary>
    public required IReadOnlyList<ContentItemInfo> ContentItems { get; init; }

    /// <summary>Import statements (if any).</summary>
    public required IReadOnlyList<string> Imports { get; init; }

    /// <summary>Whether the project uses Central Package Management (no Version on PackageReferences).</summary>
    public bool UsesCentralPackageManagement { get; init; }
}

internal sealed class ProjectProperty
{
    public required string Name { get; init; }
    public required string Value { get; init; }

    /// <summary>Condition attribute on the property, if any.</summary>
    public string? Condition { get; init; }
}

internal sealed class PackageReferenceInfo
{
    public required string Include { get; init; }
    public string? Version { get; init; }

    /// <summary>e.g., "all"</summary>
    public string? PrivateAssets { get; init; }

    /// <summary>e.g., "runtime; build; native; contentfiles; analyzers; buildtransitive"</summary>
    public string? IncludeAssets { get; init; }

    /// <summary>e.g., "all"</summary>
    public string? ExcludeAssets { get; init; }
}

internal sealed class SharedCompileInfo
{
    public required string Include { get; init; }
    public string? Link { get; init; }
}

internal sealed class ContentItemInfo
{
    /// <summary>"Content", "None", "Compile" (for Remove), "EmbeddedResource", etc.</summary>
    public required string ItemType { get; init; }

    public string? Include { get; init; }

    /// <summary>Remove pattern, if this is a Remove item.</summary>
    public string? Remove { get; init; }

    public string? CopyToOutputDirectory { get; init; }
}
