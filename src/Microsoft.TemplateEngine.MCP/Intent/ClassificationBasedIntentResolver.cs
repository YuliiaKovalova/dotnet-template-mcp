// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.TemplateEngine.Abstractions;

namespace Microsoft.TemplateEngine.MCP.Intent;

/// <summary>
/// Rule-based intent resolver that matches natural-language descriptions against
/// template classifications, tags, short names, and parameter names.
/// No LLM required — works fully offline.
/// </summary>
internal sealed class ClassificationBasedIntentResolver : IIntentResolver
{
    private readonly TemplateEngineService _engineService;

    public ClassificationBasedIntentResolver(TemplateEngineService engineService)
    {
        _engineService = engineService;
    }

    public async Task<TemplateResolution> ResolveAsync(string intent, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(intent))
        {
            return new TemplateResolution
            {
                OriginalIntent = intent ?? string.Empty,
                Matches = Array.Empty<TemplateMatch>(),
            };
        }

        var keywords = IntentSynonymDictionary.ExtractKeywords(intent);
        var templates = await _engineService.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);

        // Detect language filter from intent
        string? languageFilter = DetectLanguage(keywords);

        // Collect candidate template short names from keyword matches
        var candidateShortNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var keyword in keywords)
        {
            if (IntentSynonymDictionary.TemplateKeywords.TryGetValue(keyword, out var shortNames))
            {
                foreach (var sn in shortNames)
                {
                    candidateShortNames.Add(sn);
                }
            }
        }

        // Collect candidate classifications
        var candidateClassifications = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var keyword in keywords)
        {
            if (IntentSynonymDictionary.ClassificationKeywords.TryGetValue(keyword, out var classifications))
            {
                foreach (var c in classifications)
                {
                    candidateClassifications.Add(c);
                }
            }
        }

        // Resolve parameters from keywords
        var resolvedParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var keyword in keywords)
        {
            if (IntentSynonymDictionary.ParameterKeywords.TryGetValue(keyword, out var paramSpec))
            {
                // Later keywords override earlier ones for the same parameter
                resolvedParams[paramSpec.ParameterName] = paramSpec.Value;
            }
        }

        // Score each template
        var scored = new List<(ITemplateInfo Template, double Score, List<string> Reasons)>();
        foreach (var template in templates)
        {
            // Apply language filter
            if (languageFilter != null)
            {
                if (!template.TagsCollection.TryGetValue("language", out var lang) ||
                    !lang.Equals(languageFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            var (score, reasons) = ScoreTemplate(template, intent, candidateShortNames, candidateClassifications, resolvedParams);
            if (score > 0)
            {
                scored.Add((template, score, reasons));
            }
        }

        // Sort by score descending, take top 5
        var matches = scored
            .OrderByDescending(s => s.Score)
            .Take(5)
            .Select(s =>
            {
                // Filter resolved params to only those the template actually supports
                var applicableParams = FilterParamsForTemplate(s.Template, resolvedParams);
                var unresolvedParams = resolvedParams.Keys
                    .Except(applicableParams.Keys, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new TemplateMatch
                {
                    Template = s.Template,
                    Confidence = Math.Min(s.Score, 1.0),
                    ResolvedParameters = applicableParams,
                    UnresolvedParameters = unresolvedParams,
                    MatchReasons = s.Reasons,
                };
            })
            .ToList();

        return new TemplateResolution
        {
            OriginalIntent = intent,
            Matches = matches,
            ExtractedKeywords = keywords,
        };
    }

    private static (double Score, List<string> Reasons) ScoreTemplate(
        ITemplateInfo template,
        string intent,
        IReadOnlySet<string> candidateShortNames,
        IReadOnlySet<string> candidateClassifications,
        IReadOnlyDictionary<string, string> resolvedParams)
    {
        double score = 0;
        var reasons = new List<string>();

        // 1. Short name match (highest weight: 0.5)
        foreach (var sn in template.ShortNameList)
        {
            if (candidateShortNames.Contains(sn))
            {
                score += 0.5;
                reasons.Add($"Short name '{sn}' matches intent keywords");
                break;
            }
        }

        // 2. Classification match (0.2 per match, max 0.4)
        double classScore = 0;
        foreach (var classification in template.Classifications)
        {
            if (candidateClassifications.Any(c =>
                classification.Contains(c, StringComparison.OrdinalIgnoreCase) ||
                c.Contains(classification, StringComparison.OrdinalIgnoreCase)))
            {
                classScore += 0.2;
                reasons.Add($"Classification '{classification}' matches");
            }
        }

        score += Math.Min(classScore, 0.4);

        // 3. Name/description direct match (0.15)
        var normalized = intent.ToLowerInvariant();
        if (template.Name.Contains(intent, StringComparison.OrdinalIgnoreCase) ||
            intent.Contains(template.Name, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.15;
            reasons.Add($"Name '{template.Name}' matches intent");
        }
        else if (template.Description?.Contains(intent, StringComparison.OrdinalIgnoreCase) == true)
        {
            score += 0.1;
            reasons.Add("Description contains intent text");
        }

        // 4. Parameter applicability (0.05 per matching param, max 0.2)
        double paramScore = 0;
        foreach (var (paramName, _) in resolvedParams)
        {
            if (template.ParameterDefinitions.Any(p =>
                p.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase)))
            {
                paramScore += 0.05;
            }
        }

        score += Math.Min(paramScore, 0.2);
        if (paramScore > 0)
        {
            reasons.Add($"Supports {(int)(paramScore / 0.05)} of the requested parameters");
        }

        // 5. Identity match fallback (0.3)
        if (template.Identity.Contains(normalized, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.3;
            reasons.Add($"Identity '{template.Identity}' contains intent text");
        }

        return (score, reasons);
    }

    /// <summary>
    /// Filter resolved parameters to only those that the template actually defines.
    /// Also validates choice values against allowed choices.
    /// </summary>
    private static Dictionary<string, string> FilterParamsForTemplate(
        ITemplateInfo template,
        IReadOnlyDictionary<string, string> resolvedParams)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (paramName, paramValue) in resolvedParams)
        {
            var paramDef = template.ParameterDefinitions.FirstOrDefault(p =>
                p.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase));

            if (paramDef == null)
            {
                continue;
            }

            // For choice parameters, verify the value is valid
            if (paramDef.DataType?.Equals("choice", StringComparison.OrdinalIgnoreCase) == true &&
                paramDef.Choices != null)
            {
                if (paramDef.Choices.Keys.Any(c => c.Equals(paramValue, StringComparison.OrdinalIgnoreCase)))
                {
                    result[paramName] = paramValue;
                }

                // If value doesn't match any choice, skip it (don't pre-fill invalid values)
            }
            else
            {
                result[paramName] = paramValue;
            }
        }

        return result;
    }

    private static string? DetectLanguage(IReadOnlyList<string> keywords)
    {
        foreach (var keyword in keywords)
        {
            if (IntentSynonymDictionary.LanguageAliases.TryGetValue(keyword, out var language))
            {
                return language;
            }
        }

        return null;
    }
}
