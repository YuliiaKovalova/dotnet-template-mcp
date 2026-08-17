// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.Text.Json;
using Microsoft.TemplateEngine.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Microsoft.TemplateEngine.MCP.Host;

/// <summary>
/// Builds MCP elicitation schemas from template parameter definitions,
/// enabling interactive parameter collection from users when required parameters are missing.
/// </summary>
internal static class ElicitationHelper
{
    /// <summary>
    /// Checks whether the MCP client supports elicitation.
    /// </summary>
    public static bool IsElicitationSupported(McpServer server)
    {
        return server.ClientCapabilities?.Elicitation is not null;
    }

    /// <summary>
    /// Elicits missing required parameters from the user interactively.
    /// Returns the collected parameter values, or null if the user declined/cancelled.
    /// </summary>
    public static async Task<Dictionary<string, string?>?> ElicitMissingParametersAsync(
        McpServer server,
        ITemplateInfo template,
        Dictionary<string, string?> existingParameters,
        CancellationToken cancellationToken)
    {
        var missingParams = GetMissingRequiredParameters(template, existingParameters);
        if (missingParams.Count == 0)
        {
            return null;
        }

        var schema = BuildSchemaFromParameters(template, missingParams);
        if (schema.Properties.Count == 0)
        {
            return null;
        }

        var result = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = $"Template '{template.Name}' needs additional information to create the project:",
            RequestedSchema = schema,
        }, cancellationToken).ConfigureAwait(false);

        if (result.Action != "accept" || result.Content is null)
        {
            return null;
        }

        var collected = new Dictionary<string, string?>();
        foreach (var kvp in result.Content)
        {
            string? value = kvp.Value.ValueKind switch
            {
                JsonValueKind.String => kvp.Value.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => kvp.Value.GetRawText(),
                JsonValueKind.Null => null,
                _ => kvp.Value.GetRawText(),
            };

            if (!string.IsNullOrEmpty(value))
            {
                collected[kvp.Key] = value;
            }
        }

        return collected;
    }

    /// <summary>
    /// Identifies required template parameters that are not yet provided.
    /// </summary>
    internal static List<ITemplateParameter> GetMissingRequiredParameters(
        ITemplateInfo template,
        IReadOnlyDictionary<string, string?> existingParameters)
    {
        return template.ParameterDefinitions
            .Where(p => p.Precedence.PrecedenceDefinition == PrecedenceDefinition.Required
                && !IsInternalParameter(p)
                && (!existingParameters.TryGetValue(p.Name, out var existingValue) || string.IsNullOrEmpty(existingValue)))
            .ToList();
    }

    /// <summary>
    /// Builds an MCP elicitation schema from template parameters.
    /// Maps template parameter types to MCP schema types.
    /// </summary>
    internal static ElicitRequestParams.RequestSchema BuildSchemaFromParameters(
        ITemplateInfo template,
        IReadOnlyList<ITemplateParameter> parameters)
    {
        var properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>();

        foreach (var param in parameters)
        {
            var schemaDef = CreateSchemaDefinition(param);
            if (schemaDef is not null)
            {
                properties[param.Name] = schemaDef;
            }
        }

        return new ElicitRequestParams.RequestSchema
        {
            Properties = properties,
        };
    }

    private static ElicitRequestParams.PrimitiveSchemaDefinition? CreateSchemaDefinition(ITemplateParameter param)
    {
        // Parameters with explicit choices → enum schema
        if (param.Choices is { Count: > 0 })
        {
            return new ElicitRequestParams.UntitledSingleSelectEnumSchema
            {
                Description = GetParameterDescription(param),
                Enum = param.Choices.Keys.ToList(),
                Default = param.DefaultValue,
            };
        }

        // Map by data type
        string dataType = param.DataType?.ToLowerInvariant() ?? "string";
        return dataType switch
        {
            "bool" or "boolean" => new ElicitRequestParams.BooleanSchema
            {
                Description = GetParameterDescription(param),
                Default = param.DefaultValue is not null
                    ? bool.TryParse(param.DefaultValue, out bool b) && b
                    : null,
            },
            "int" or "integer" or "float" or "number" => new ElicitRequestParams.NumberSchema
            {
                Description = GetParameterDescription(param),
                Default = param.DefaultValue is not null && double.TryParse(param.DefaultValue, out double n)
                    ? n
                    : null,
            },
            _ => new ElicitRequestParams.StringSchema
            {
                Description = GetParameterDescription(param),
                Default = param.DefaultValue,
            },
        };
    }

    private static string GetParameterDescription(ITemplateParameter param)
    {
        var description = param.Description ?? param.DisplayName ?? param.Name;
        if (!string.IsNullOrEmpty(param.DefaultValue))
        {
            description += $" (default: {param.DefaultValue})";
        }

        return description;
    }

    private static bool IsInternalParameter(ITemplateParameter param)
    {
        // Skip parameters that are internal/implicit and not user-facing
        return param.Name.StartsWith("_") ||
               param.Name.Equals("name", StringComparison.OrdinalIgnoreCase) ||
               param.Name.Equals("copyrightYear", StringComparison.OrdinalIgnoreCase);
    }
}
