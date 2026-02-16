// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using ITemplateCreationResult = Microsoft.TemplateEngine.Edge.Template.ITemplateCreationResult;

namespace Microsoft.TemplateEngine.MCP.Tools;

[McpServerToolType]
internal sealed class TemplateInstantiateTool
{
    [McpServerTool(Name = "template_instantiate")]
    [Description("Create a project or item from a template with provided parameter values. Writes files to disk at the specified output path. Use template_dry_run first to preview what will be created.")]
    public static async Task<string> InstantiateTemplateAsync(
        TemplateEngineService engineService,
        [Description("Template identity or short name")] string templateName,
        [Description("Name for the created project/item")] string? name = null,
        [Description("Output directory path where files will be created")] string? outputPath = null,
        [Description("JSON object of parameter name-value pairs (e.g., {\"Framework\": \"net8.0\", \"EnableAot\": \"true\"})")] string? parametersJson = null,
        CancellationToken cancellationToken = default)
    {
        var templates = await engineService.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);

        var template = templates.FirstOrDefault(t =>
            t.Identity.Equals(templateName, StringComparison.OrdinalIgnoreCase) ||
            t.ShortNameList.Any(sn => sn.Equals(templateName, StringComparison.OrdinalIgnoreCase)));

        if (template == null)
        {
            return JsonSerializer.Serialize(new { error = $"Template '{templateName}' not found." });
        }

        var parameters = ParseParameters(parametersJson);
        string resolvedOutputPath = outputPath ?? Path.Combine(Environment.CurrentDirectory, name ?? template.DefaultName ?? "NewProject");

        var result = await engineService.CreateAsync(template, name, resolvedOutputPath, parameters, cancellationToken).ConfigureAwait(false);

        return SerializeCreationResult(result);
    }

    internal static Dictionary<string, string?> ParseParameters(string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson))
        {
            return new Dictionary<string, string?>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(parametersJson);
            if (parsed == null)
            {
                return new Dictionary<string, string?>();
            }

            return parsed.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ValueKind == JsonValueKind.Null ? null : kvp.Value.ToString());
        }
        catch (JsonException)
        {
            return new Dictionary<string, string?>();
        }
    }

    internal static string SerializeCreationResult(ITemplateCreationResult result)
    {
        var postActions = result.CreationResult?.PostActions?.Select(pa => new
        {
            pa.Description,
            ActionId = pa.ActionId.ToString(),
            pa.ManualInstructions,
            pa.ContinueOnError,
            pa.Args,
        }).ToList();

        var primaryOutputs = result.CreationResult?.PrimaryOutputs?.Select(po => po.Path).ToList();

        var fileChanges = result.CreationEffects?.FileChanges?.Select(fc => new
        {
            fc.TargetRelativePath,
            ChangeKind = fc.ChangeKind.ToString(),
        }).ToList();

        var response = new
        {
            Status = result.Status.ToString(),
            result.ErrorMessage,
            result.OutputBaseDirectory,
            result.TemplateFullName,
            PrimaryOutputs = primaryOutputs,
            PostActions = postActions,
            FileChanges = fileChanges,
        };

        return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
    }
}
