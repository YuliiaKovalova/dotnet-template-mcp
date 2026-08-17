// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using Microsoft.TemplateEngine.Abstractions;

namespace DotnetTemplateMcp.Intent;

/// <summary>
/// Result of resolving a natural-language intent to template + parameter selections.
/// </summary>
internal sealed class TemplateResolution
{
    /// <summary>Ranked template matches (highest confidence first).</summary>
    public required IReadOnlyList<TemplateMatch> Matches { get; init; }

    /// <summary>The original intent string that was resolved.</summary>
    public required string OriginalIntent { get; init; }

    /// <summary>Keywords extracted from the intent.</summary>
    public IReadOnlyList<string> ExtractedKeywords { get; init; } = Array.Empty<string>();

    /// <summary>Whether at least one match was found with non-zero confidence.</summary>
    public bool HasMatches => Matches.Count > 0;
}

/// <summary>
/// A single template match with confidence score and pre-filled parameters.
/// </summary>
internal sealed class TemplateMatch
{
    /// <summary>The matched template.</summary>
    public required ITemplateInfo Template { get; init; }

    /// <summary>Confidence score 0.0–1.0 indicating how well this template matches the intent.</summary>
    public required double Confidence { get; init; }

    /// <summary>Parameters resolved from the intent (name → value).</summary>
    public IReadOnlyDictionary<string, string> ResolvedParameters { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Parameter names that were mentioned but couldn't be resolved to a value.</summary>
    public IReadOnlyList<string> UnresolvedParameters { get; init; } = Array.Empty<string>();

    /// <summary>Human-readable explanation of why this template was matched.</summary>
    public IReadOnlyList<string> MatchReasons { get; init; } = Array.Empty<string>();
}
