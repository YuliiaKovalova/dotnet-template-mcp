// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace Microsoft.TemplateEngine.MCP.Prompts;

[McpServerPromptType]
internal sealed class CreateFromDescriptionPrompt
{
    [McpServerPrompt(Name = "create_from_description")]
    [Description("Create a .NET project from a plain English description. Uses intent resolution to find the best template and pre-fill parameters, then walks through preview and confirmation.")]
    public static IEnumerable<ChatMessage> CreateFromDescription(
        [Description("Plain English description of what you want (e.g., 'a web API with authentication, controllers, and .NET 9')")] string description,
        [Description("Optional output directory path for the project")] string? outputPath = null)
    {
        var outputNote = string.IsNullOrWhiteSpace(outputPath)
            ? string.Empty
            : $"\nThe project should be created at: {outputPath}";

        return
        [
            new ChatMessage(
                ChatRole.User,
                $$"""
                I want to create a .NET project. Here's what I need: {{description}}{{outputNote}}

                Please follow this workflow:
                1. Use `template_from_intent` with my description to find matching templates and auto-resolve parameters
                2. Review the top match — show me the template name, resolved parameters, and confidence score
                3. Use `template_inspect` on the best match to verify all parameters and show any I might want to set
                4. Use `template_dry_run` with the resolved parameters to preview what files would be created
                5. After I confirm, use `template_instantiate` to create the project

                Start by resolving my intent to find the best template match.
                """),
        ];
    }
}
