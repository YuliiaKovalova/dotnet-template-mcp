// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace DotnetTemplateMcp.Tools;

[McpServerToolType]
internal sealed class TemplateValidateTool
{
    [McpServerTool(Name = "template_validate")]
    [Description("Catch template.json mistakes before publishing. Validates a local template directory for schema compliance, parameter issues (missing defaults, choice conflicts, prefix collisions), and configuration completeness. Returns errors, warnings, and suggestions.")]
    public static async Task<string> ValidateTemplateAsync(
        McpFeatureFlags featureFlags,
        [Description("Path to the template directory (must contain .template.config/template.json), or direct path to template.json")] string path,
        CancellationToken cancellationToken = default)
    {
        using var activity = McpTelemetry.StartToolActivity("template_validate");
        var sw = Stopwatch.StartNew();
        try
        {
            if (!featureFlags.IsToolEnabled("template_validate"))
            {
                return ToolProfileResponse.DisabledMessage("template_validate", "Set MCP_TEMPLATE_TOOL_PROFILE=full to validate templates.");
            }

            string templateJsonPath = ResolveTemplatePath(path);
            if (!File.Exists(templateJsonPath))
            {
                McpTelemetry.RecordError(activity, "template_validate", "template.json not found");
                return JsonSerializer.Serialize(new
                {
                    error = $"template.json not found. Searched: {templateJsonPath}",
                    hint = "Provide a path to a directory containing .template.config/template.json, or a direct path to template.json.",
                }, SerializerOptions);
            }

            string json;
            try
            {
                json = await File.ReadAllTextAsync(templateJsonPath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                McpTelemetry.RecordError(activity, "template_validate", $"Failed to read template.json: {ex.Message}");
                return JsonSerializer.Serialize(new { error = $"Failed to read template.json: {ex.Message}" }, SerializerOptions);
            }

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(json);
            }
            catch (JsonException ex)
            {
                McpTelemetry.RecordError(activity, "template_validate", "Invalid JSON in template.json");
                return JsonSerializer.Serialize(new
                {
                    error = "Invalid JSON in template.json",
                    details = ex.Message,
                    line = ex.LineNumber,
                }, SerializerOptions);
            }

            if (root is not JsonObject obj)
            {
                McpTelemetry.RecordError(activity, "template_validate", "template.json root must be a JSON object");
                return JsonSerializer.Serialize(new { error = "template.json root must be a JSON object." }, SerializerOptions);
            }

            var result = new ValidationResult();
            ValidateRequiredFields(obj, result);
            ValidateIdentity(obj, result);
            ValidateShortName(obj, result);
            ValidateSymbols(obj, result);
            ValidateSources(obj, result);
            ValidatePostActions(obj, result);
            ValidateConstraints(obj, result);
            ValidateTags(obj, result);

            activity?.SetTag("mcp.validation.errors", result.Errors.Count);
            activity?.SetTag("mcp.validation.warnings", result.Warnings.Count);

            return JsonSerializer.Serialize(new
            {
                valid = result.Errors.Count == 0,
                templatePath = templateJsonPath,
                identity = (obj["identity"] as JsonValue)?.GetValueKind() == System.Text.Json.JsonValueKind.String
                    ? obj["identity"]!.GetValue<string>()
                    : null,
                summary = $"{result.Errors.Count} error(s), {result.Warnings.Count} warning(s), {result.Suggestions.Count} suggestion(s)",
                errors = result.Errors,
                warnings = result.Warnings,
                suggestions = result.Suggestions,
            }, SerializerOptions);
        }
        catch (Exception ex)
        {
            // A validator must never crash on malformed input — surface it as a friendly error.
            McpTelemetry.RecordError(activity, "template_validate", ex.Message);
            return JsonSerializer.Serialize(new
            {
                error = "Failed to validate template.json.",
                details = ex.Message,
                hint = "Ensure template.json is well-formed and that fields like 'identity', 'shortName', and symbol values use the expected types.",
            }, SerializerOptions);
        }
        finally
        {
            McpTelemetry.RecordDuration("template_validate", sw.Elapsed.TotalMilliseconds);
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private static string ResolveTemplatePath(string path)
    {
        // Direct path to template.json
        if (path.EndsWith("template.json", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            return path;
        }

        // Directory with .template.config subfolder
        var configDir = Path.Combine(path, ".template.config", "template.json");
        if (File.Exists(configDir))
        {
            return configDir;
        }

        // Maybe .template.config was passed directly
        var direct = Path.Combine(path, "template.json");
        if (File.Exists(direct))
        {
            return direct;
        }

        return configDir; // Return expected path for error message
    }

    private static void ValidateRequiredFields(JsonObject obj, ValidationResult result)
    {
        string[] requiredFields = ["identity", "name", "shortName"];
        foreach (var field in requiredFields)
        {
            if (obj[field] == null)
            {
                result.Errors.Add($"Missing required field '{field}'.");
            }
            else if (obj[field] is JsonValue val && string.IsNullOrWhiteSpace(val.GetValue<string>()))
            {
                result.Errors.Add($"Required field '{field}' is empty.");
            }
        }

        if (obj["sourceName"] == null)
        {
            result.Warnings.Add("Missing 'sourceName'. Without it, the generated project name won't be customizable via --name.");
        }

        if (obj["author"] == null)
        {
            result.Warnings.Add("Missing 'author' field. Consider adding it for template discoverability.");
        }

        if (obj["description"] == null)
        {
            result.Suggestions.Add("Consider adding a 'description' field to help users understand what this template creates.");
        }

        if (obj["classifications"] == null)
        {
            result.Suggestions.Add("Consider adding 'classifications' (e.g. [\"Web\", \"API\"]) for better search and categorization.");
        }

        if (obj["defaultName"] == null)
        {
            result.Suggestions.Add("Consider adding 'defaultName' to provide a fallback project name when --name is not specified.");
        }
    }

    private static void ValidateIdentity(JsonObject obj, ValidationResult result)
    {
        var identity = obj["identity"]?.GetValue<string>();
        if (identity == null)
        {
            return;
        }

        if (identity.Contains(' '))
        {
            result.Errors.Add($"Identity '{identity}' contains spaces. Use dots or dashes instead (e.g. 'MyCompany.MyTemplate').");
        }

        if (!identity.Contains('.') && !identity.Contains('-'))
        {
            result.Warnings.Add($"Identity '{identity}' has no namespace separator. Consider using reverse-DNS format (e.g. 'MyCompany.WebApi.CSharp').");
        }
    }

    private static void ValidateShortName(JsonObject obj, ValidationResult result)
    {
        var shortName = obj["shortName"];
        if (shortName == null)
        {
            return;
        }

        // shortName can be string or array
        var names = new List<string>();
        if (shortName is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item?.GetValue<string>() is string s)
                {
                    names.Add(s);
                }
            }
        }
        else if (shortName is JsonValue val)
        {
            names.Add(val.GetValue<string>());
        }

        // Check for reserved names that conflict with dotnet CLI commands
        string[] reservedNames = ["new", "build", "run", "test", "publish", "restore", "clean", "pack", "add", "remove", "list", "nuget", "tool", "sln", "help"];
        foreach (var name in names)
        {
            if (reservedNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                result.Errors.Add($"Short name '{name}' conflicts with a dotnet CLI command. Choose a different name.");
            }

            if (name.Length < 2)
            {
                result.Warnings.Add($"Short name '{name}' is very short. Consider a more descriptive name for better discoverability.");
            }
        }
    }

    private static void ValidateSymbols(JsonObject obj, ValidationResult result)
    {
        var symbols = obj["symbols"];
        if (symbols is not JsonObject symbolsObj)
        {
            return;
        }

        var parameterNames = new List<string>();

        foreach (var (symbolName, symbolNode) in symbolsObj)
        {
            if (symbolNode is not JsonObject symbol)
            {
                continue;
            }

            var type = symbol["type"]?.GetValue<string>();
            if (type == null)
            {
                result.Errors.Add($"Symbol '{symbolName}' is missing required 'type' field.");
                continue;
            }

            if (type.Equals("parameter", StringComparison.OrdinalIgnoreCase))
            {
                parameterNames.Add(symbolName);
                ValidateParameter(symbolName, symbol, result);
            }
            else if (type.Equals("computed", StringComparison.OrdinalIgnoreCase))
            {
                if (symbol["value"] == null)
                {
                    result.Errors.Add($"Computed symbol '{symbolName}' is missing required 'value' expression.");
                }
            }
            else if (type.Equals("generated", StringComparison.OrdinalIgnoreCase))
            {
                if (symbol["generator"] == null)
                {
                    result.Errors.Add($"Generated symbol '{symbolName}' is missing required 'generator' field.");
                }
            }
        }

        // Check for parameter name prefix collisions (issue dotnet/templating#2623)
        for (int i = 0; i < parameterNames.Count; i++)
        {
            for (int j = i + 1; j < parameterNames.Count; j++)
            {
                if (parameterNames[j].StartsWith(parameterNames[i], StringComparison.OrdinalIgnoreCase) ||
                    parameterNames[i].StartsWith(parameterNames[j], StringComparison.OrdinalIgnoreCase))
                {
                    result.Warnings.Add($"Parameter names '{parameterNames[i]}' and '{parameterNames[j]}' share a prefix. This can create ambiguous parsing in some expression contexts.");
                }
            }
        }
    }

    private static void ValidateParameter(string name, JsonObject param, ValidationResult result)
    {
        var dataType = param["datatype"]?.GetValue<string>();

        if (dataType == null)
        {
            result.Warnings.Add($"Parameter '{name}' has no 'datatype'. Defaults to 'string'. Consider being explicit.");
        }

        // Choice parameter validation
        if (dataType?.Equals("choice", StringComparison.OrdinalIgnoreCase) == true)
        {
            var choices = param["choices"];
            if (choices == null)
            {
                result.Errors.Add($"Choice parameter '{name}' has no 'choices' defined.");
            }
            else if (choices is JsonArray choicesArr)
            {
                if (choicesArr.Count == 0)
                {
                    result.Errors.Add($"Choice parameter '{name}' has an empty 'choices' array.");
                }

                // Validate each choice has the 'choice' field
                foreach (var choiceItem in choicesArr)
                {
                    if (choiceItem is JsonObject choiceObj && choiceObj["choice"] == null)
                    {
                        result.Errors.Add($"Choice parameter '{name}' has a choice entry without a 'choice' key.");
                    }
                }

                // Validate defaultValue is in choices
                var defaultValue = param["defaultValue"]?.GetValue<string>();
                if (defaultValue != null)
                {
                    var allowMultiple = param["allowMultipleValues"]?.GetValue<bool>() == true;
                    var values = allowMultiple ? defaultValue.Split('|') : [defaultValue];
                    foreach (var val in values)
                    {
                        bool found = false;
                        foreach (var choiceItem in choicesArr)
                        {
                            string? choiceValue = null;
                            if (choiceItem is JsonObject choiceObj)
                            {
                                choiceValue = choiceObj["choice"]?.GetValue<string>();
                            }
                            else if (choiceItem is JsonValue choiceVal)
                            {
                                choiceValue = choiceVal.GetValue<string>();
                            }

                            if (choiceValue != null && choiceValue.Equals(val.Trim(), StringComparison.OrdinalIgnoreCase))
                            {
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            result.Errors.Add($"Default value '{val.Trim()}' for choice parameter '{name}' is not in the choices list.");
                        }
                    }
                }
            }
            else if (choices is JsonObject choicesObj)
            {
                if (choicesObj.Count == 0)
                {
                    result.Errors.Add($"Choice parameter '{name}' has an empty 'choices' object.");
                }

                // Validate defaultValue is in choices (object form)
                var defaultValue = param["defaultValue"]?.GetValue<string>();
                if (defaultValue != null)
                {
                    var allowMultiple = param["allowMultipleValues"]?.GetValue<bool>() == true;
                    var values = allowMultiple ? defaultValue.Split('|') : [defaultValue];
                    foreach (var val in values)
                    {
                        if (!choicesObj.ContainsKey(val.Trim()))
                        {
                            bool caseInsensitiveMatch = choicesObj.Any(c => c.Key.Equals(val.Trim(), StringComparison.OrdinalIgnoreCase));
                            if (!caseInsensitiveMatch)
                            {
                                result.Errors.Add($"Default value '{val.Trim()}' for choice parameter '{name}' is not in the choices list.");
                            }
                        }
                    }
                }
            }
        }

        // Bool parameter validation
        if (dataType?.Equals("bool", StringComparison.OrdinalIgnoreCase) == true)
        {
            var defaultValue = param["defaultValue"]?.GetValue<string>();
            if (defaultValue != null && !bool.TryParse(defaultValue, out _))
            {
                result.Errors.Add($"Bool parameter '{name}' has non-boolean defaultValue '{defaultValue}'.");
            }
        }

        // Integer parameter validation
        if (dataType?.Equals("int", StringComparison.OrdinalIgnoreCase) == true)
        {
            var defaultValue = param["defaultValue"]?.GetValue<string>();
            if (defaultValue != null && !long.TryParse(defaultValue, out _))
            {
                result.Errors.Add($"Integer parameter '{name}' has non-integer defaultValue '{defaultValue}'.");
            }
        }

        // Invalid datatype validation
        string[] validDatatypes = ["string", "bool", "choice", "int", "float", "hex", "text"];
        if (dataType != null && !validDatatypes.Contains(dataType, StringComparer.OrdinalIgnoreCase))
        {
            result.Errors.Add($"Parameter '{name}' has invalid datatype '{dataType}'. Valid datatypes: {string.Join(", ", validDatatypes)}.");
        }

        // Optional parameter without default (issue dotnet/templating#2623)
        bool isRequired = param["isRequired"]?.GetValue<bool>() == true;
        if (!isRequired && param["defaultValue"] == null && dataType?.Equals("choice", StringComparison.OrdinalIgnoreCase) == true)
        {
            result.Warnings.Add($"Optional choice parameter '{name}' has no defaultValue. Users may get unexpected behavior if they don't provide a value.");
        }

        // Description check
        if (param["description"] == null)
        {
            result.Suggestions.Add($"Parameter '{name}' has no 'description'. Adding one improves the --help output and AI tool usage.");
        }
    }

    private static void ValidateSources(JsonObject obj, ValidationResult result)
    {
        var sources = obj["sources"];
        if (sources is not JsonArray sourcesArr)
        {
            return;
        }

        foreach (var sourceNode in sourcesArr)
        {
            if (sourceNode is not JsonObject source)
            {
                continue;
            }

            var modifiers = source["modifiers"];
            if (modifiers is JsonArray modArr)
            {
                foreach (var mod in modArr)
                {
                    if (mod is not JsonObject modObj)
                    {
                        continue;
                    }

                    var condition = modObj["condition"]?.GetValue<string>();
                    if (condition != null && !condition.Contains('(') && !condition.Contains(')'))
                    {
                        result.Warnings.Add($"Source modifier condition '{condition}' may be missing parentheses around the symbol name. Expected format: '(symbolName)'.");
                    }
                }
            }
        }
    }

    private static void ValidatePostActions(JsonObject obj, ValidationResult result)
    {
        var postActions = obj["postActions"];
        if (postActions is not JsonArray postActionsArr)
        {
            return;
        }

        foreach (var paNode in postActionsArr)
        {
            if (paNode is not JsonObject pa)
            {
                continue;
            }

            if (pa["actionId"] == null)
            {
                result.Errors.Add("Post-action is missing required 'actionId' field.");
            }

            if (pa["description"] == null)
            {
                result.Warnings.Add("Post-action is missing 'description'. This text is shown to users when the action requires manual steps.");
            }

            if (pa["manualInstructions"] == null)
            {
                result.Suggestions.Add("Post-action has no 'manualInstructions'. These are shown when the action can't run automatically (e.g., in an IDE).");
            }
        }
    }

    private static void ValidateConstraints(JsonObject obj, ValidationResult result)
    {
        var constraints = obj["constraints"];
        if (constraints is not JsonObject constraintsObj)
        {
            return;
        }

        foreach (var (constraintName, constraintNode) in constraintsObj)
        {
            if (constraintNode is not JsonObject constraint)
            {
                continue;
            }

            if (constraint["type"] == null)
            {
                result.Errors.Add($"Constraint '{constraintName}' is missing required 'type' field.");
            }

            if (constraint["args"] == null)
            {
                result.Warnings.Add($"Constraint '{constraintName}' has no 'args'. Most constraint types require arguments.");
            }
        }
    }

    private static void ValidateTags(JsonObject obj, ValidationResult result)
    {
        var tags = obj["tags"];
        if (tags is not JsonObject tagsObj)
        {
            if (tags == null)
            {
                result.Suggestions.Add("No 'language' tag. Adding tags.language (e.g. \"C#\") improves filtering in 'dotnet new list --language'.");
                result.Suggestions.Add("No 'type' tag. Adding tags.type (e.g. \"project\" or \"item\") improves categorization.");
            }

            return;
        }

        if (!tagsObj.ContainsKey("language"))
        {
            result.Suggestions.Add("No 'language' tag. Adding tags.language (e.g. \"C#\") improves filtering in 'dotnet new list --language'.");
        }

        if (!tagsObj.ContainsKey("type"))
        {
            result.Suggestions.Add("No 'type' tag. Adding tags.type (e.g. \"project\" or \"item\") improves categorization.");
        }
    }

    private sealed class ValidationResult
    {
        public List<string> Errors { get; } = [];
        public List<string> Warnings { get; } = [];
        public List<string> Suggestions { get; } = [];
    }
}
