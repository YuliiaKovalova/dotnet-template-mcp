// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

namespace Microsoft.TemplateEngine.MCP.Intent;

/// <summary>
/// Maps common natural-language terms to template classifications, short names,
/// and parameter values. Works offline — no LLM required.
/// </summary>
internal static class IntentSynonymDictionary
{
    /// <summary>
    /// Maps user-facing keywords/phrases → template short names or identities.
    /// Multiple synonyms can point to the same template.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> TemplateKeywords =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            // Web
            ["web api"] = ["webapi"],
            ["web service"] = ["webapi"],
            ["rest api"] = ["webapi"],
            ["restful"] = ["webapi"],
            ["api"] = ["webapi"],
            ["web app"] = ["webapp", "blazorserver"],
            ["web application"] = ["webapp", "blazorserver"],
            ["mvc"] = ["mvc"],
            ["razor"] = ["webapp", "razor"],
            ["razor pages"] = ["webapp"],
            ["blazor"] = ["blazorserver", "blazorwasm", "blazor"],
            ["blazor server"] = ["blazorserver"],
            ["blazor wasm"] = ["blazorwasm"],
            ["blazor webassembly"] = ["blazorwasm"],
            ["grpc"] = ["grpc"],
            ["signalr"] = ["webapi", "webapp"],
            ["minimal api"] = ["webapi"],
            ["web"] = ["webapp", "webapi"],

            // Console & Worker
            ["console"] = ["console"],
            ["console app"] = ["console"],
            ["command line"] = ["console"],
            ["cli"] = ["console"],
            ["worker"] = ["worker"],
            ["background service"] = ["worker"],
            ["daemon"] = ["worker"],
            ["windows service"] = ["worker"],

            // Libraries
            ["class library"] = ["classlib"],
            ["library"] = ["classlib"],
            ["lib"] = ["classlib"],
            ["nuget package"] = ["classlib"],

            // Mobile & Desktop
            ["maui"] = ["maui"],
            ["mobile"] = ["maui"],
            ["cross-platform app"] = ["maui"],
            ["ios"] = ["maui"],
            ["android"] = ["maui"],
            ["desktop"] = ["maui", "wpf", "winforms"],
            ["wpf"] = ["wpf"],
            ["winforms"] = ["winforms"],
            ["windows forms"] = ["winforms"],

            // Testing
            ["test"] = ["xunit", "nunit", "mstest"],
            ["unit test"] = ["xunit", "nunit", "mstest"],
            ["xunit"] = ["xunit"],
            ["nunit"] = ["nunit"],
            ["mstest"] = ["mstest"],

            // Other
            ["solution"] = ["sln"],
            ["gitignore"] = ["gitignore"],
            ["editorconfig"] = ["editorconfig"],
            ["nuget config"] = ["nugetconfig"],
            ["global json"] = ["globaljson"],

            // Modern .NET scenarios
            ["aspire"] = ["aspire-starter", "aspire"],
            [".net aspire"] = ["aspire-starter", "aspire"],
            ["azure functions"] = ["func"],
            ["function app"] = ["func"],
            ["serverless"] = ["func"],
            ["orleans"] = ["orleans"],
            ["winui"] = ["winui3", "winui"],
            ["winui3"] = ["winui3"],
            ["blazor web"] = ["blazor"],
            ["blazor web app"] = ["blazor"],
            ["razor component"] = ["razorcomponent"],
            ["razor class library"] = ["razorclasslib"],
            ["web component"] = ["razorcomponent"],
        };

    /// <summary>
    /// Maps user-facing keywords/phrases → template parameter name + value.
    /// When a keyword appears in the intent, the corresponding parameter is pre-filled.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string ParameterName, string Value)> ParameterKeywords =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            // Authentication
            ["authentication"] = ("auth", "Individual"),
            ["auth"] = ("auth", "Individual"),
            ["individual auth"] = ("auth", "Individual"),
            ["individual accounts"] = ("auth", "Individual"),
            ["windows auth"] = ("auth", "SingleOrg"),
            ["azure ad"] = ("auth", "SingleOrg"),
            ["entra"] = ("auth", "SingleOrg"),
            ["no auth"] = ("auth", "None"),
            ["no authentication"] = ("auth", "None"),

            // Framework
            ["net8"] = ("Framework", "net8.0"),
            [".net 8"] = ("Framework", "net8.0"),
            ["dotnet 8"] = ("Framework", "net8.0"),
            ["net9"] = ("Framework", "net9.0"),
            [".net 9"] = ("Framework", "net9.0"),
            ["dotnet 9"] = ("Framework", "net9.0"),
            ["net10"] = ("Framework", "net10.0"),
            [".net 10"] = ("Framework", "net10.0"),
            ["dotnet 10"] = ("Framework", "net10.0"),

            // Architecture
            ["controllers"] = ("UseControllers", "true"),
            ["controller-based"] = ("UseControllers", "true"),
            ["minimal api"] = ("UseControllers", "false"),
            ["minimal apis"] = ("UseControllers", "false"),
            ["top-level"] = ("UseProgramMain", "false"),
            ["program.main"] = ("UseProgramMain", "true"),
            ["program main"] = ("UseProgramMain", "true"),

            // Features
            ["aot"] = ("PublishAot", "true"),
            ["native aot"] = ("PublishAot", "true"),
            ["nativeaot"] = ("PublishAot", "true"),
            ["trimming"] = ("PublishAot", "true"),
            ["https"] = ("NoHttps", "false"),
            ["no https"] = ("NoHttps", "true"),
            ["docker"] = ("EnableDocker", "true"),
            ["dockerfile"] = ("EnableDocker", "true"),
            ["container"] = ("EnableDocker", "true"),
            ["openapi"] = ("UseOpenApi", "true"),
            ["swagger"] = ("UseOpenApi", "true"),

            // Interactivity (Blazor)
            ["server rendering"] = ("interactivity", "Server"),
            ["server-side rendering"] = ("interactivity", "Server"),
            ["webassembly rendering"] = ("interactivity", "WebAssembly"),
            ["wasm rendering"] = ("interactivity", "WebAssembly"),
            ["auto rendering"] = ("interactivity", "Auto"),
        };

    /// <summary>
    /// Maps user-facing keywords → template classifications (Web, Console, Library, etc.).
    /// Used for secondary matching when template short names don't match directly.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> ClassificationKeywords =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["web"] = ["Web", "Web/WebAPI", "Web/MVC", "Web/Razor Pages"],
            ["api"] = ["Web/WebAPI", "Web/API"],
            ["console"] = ["Console", "Common/Console"],
            ["library"] = ["Library", "Common/Library"],
            ["test"] = ["Test", "Test/xUnit", "Test/NUnit", "Test/MSTest"],
            ["mobile"] = ["MAUI", "Mobile"],
            ["desktop"] = ["Desktop", "WPF", "WinForms"],
            ["cloud"] = ["Cloud", "Azure", "AWS"],
            ["worker"] = ["Worker", "Background", "Service"],
            ["blazor"] = ["Web/Blazor"],
            ["aspire"] = ["Aspire"],
            ["function"] = ["Azure Functions", "Serverless"],
        };

    /// <summary>
    /// Language aliases mapping user-friendly names to template language tags.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> LanguageAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["c#"] = "C#",
            ["csharp"] = "C#",
            ["f#"] = "F#",
            ["fsharp"] = "F#",
            ["vb"] = "VB",
            ["visual basic"] = "VB",
            ["vb.net"] = "VB",
        };

    /// <summary>
    /// Extract all keywords that appear in the intent text.
    /// Returns them ordered by length descending (longest match first to avoid partial matches).
    /// </summary>
    public static IReadOnlyList<string> ExtractKeywords(string intent)
    {
        var normalized = intent.ToLowerInvariant();
        var found = new List<(string Keyword, int Index)>();

        // Check all dictionaries for matches
        foreach (var key in TemplateKeywords.Keys)
        {
            var idx = normalized.IndexOf(key.ToLowerInvariant(), StringComparison.Ordinal);
            if (idx >= 0)
            {
                found.Add((key, idx));
            }
        }

        foreach (var key in ParameterKeywords.Keys)
        {
            var idx = normalized.IndexOf(key.ToLowerInvariant(), StringComparison.Ordinal);
            if (idx >= 0)
            {
                found.Add((key, idx));
            }
        }

        foreach (var key in ClassificationKeywords.Keys)
        {
            var idx = normalized.IndexOf(key.ToLowerInvariant(), StringComparison.Ordinal);
            if (idx >= 0)
            {
                found.Add((key, idx));
            }
        }

        foreach (var key in LanguageAliases.Keys)
        {
            var idx = normalized.IndexOf(key.ToLowerInvariant(), StringComparison.Ordinal);
            if (idx >= 0)
            {
                found.Add((key, idx));
            }
        }

        // Deduplicate: if "web api" and "web" and "api" all match, prefer longest
        var result = new List<string>();
        var ordered = found.OrderByDescending(f => f.Keyword.Length).ToList();
        var coveredRanges = new List<(int Start, int End)>();

        foreach (var (keyword, index) in ordered)
        {
            var end = index + keyword.Length;
            if (!coveredRanges.Any(r => index >= r.Start && index < r.End))
            {
                result.Add(keyword);
                coveredRanges.Add((index, end));
            }
        }

        return result;
    }
}
