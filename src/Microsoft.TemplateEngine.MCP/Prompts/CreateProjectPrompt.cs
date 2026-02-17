// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace Microsoft.TemplateEngine.MCP.Prompts;

[McpServerPromptType]
internal sealed class CreateProjectPrompt
{
    [McpServerPrompt(Name = "create_project")]
    [Description("Guided workflow to create a .NET project: search for templates, inspect parameters, preview with dry-run, then create.")]
    public static IEnumerable<ChatMessage> CreateProject(
        [Description("What kind of project do you want to create? (e.g., 'web API', 'console app', 'MAUI app', 'class library')")] string description)
    {
        return
        [
            new ChatMessage(
                ChatRole.User,
                $$"""
                I want to create a .NET project. Here's what I need: {{description}}

                Please help me through these steps:
                1. Use `template_search` to find matching templates for my description
                2. Use `template_inspect` on the best match to see all parameters, constraints, and post-actions
                3. Based on the inspection, suggest parameter values that match my requirements
                4. Use `template_dry_run` to preview what files would be created
                5. After I confirm, use `template_instantiate` to create the project

                Start by searching for templates that match my description.
                """),
        ];
    }
}
