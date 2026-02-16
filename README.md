# Microsoft.TemplateEngine.MCP

An MCP (Model Context Protocol) server that wraps the .NET Template Engine, enabling AI agents to discover, inspect, and instantiate `dotnet new` templates via structured tool calls.

## Overview

This server exposes the .NET Template Engine's capabilities as MCP tools, solving key problems for AI-driven development:

- **Single-call parameter discovery** — `template_inspect` returns all parameters, constraints, and post-actions in one call
- **Dry-run preview** — `template_dry_run` shows what files would be created without writing to disk
- **AI-friendly metadata** — Uses `HostIdentifier = "ai"` to auto-discover `ai.host.json` files for enhanced template descriptions

## MCP Tools

| Tool | Description |
|------|-------------|
| `template_search` | Search templates by name, tags, language, or type |
| `template_list` | List installed templates with filtering |
| `template_inspect` | Full metadata inspection (parameters, constraints, post-actions) |
| `template_instantiate` | Create a project/item from a template |
| `template_dry_run` | Preview creation effects without writing to disk |
| `template_install` | Install a template package from NuGet or local path |
| `template_uninstall` | Remove an installed template package |
| `templates_installed` | Get structured listing of all installed templates |

## Installation

```bash
dotnet tool install --global Microsoft.TemplateEngine.MCP
```

## Usage

### With Claude Desktop

Add to your `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "dotnet-templates": {
      "command": "template-engine-mcp"
    }
  }
}
```

### With VS Code Copilot

Add to your `.vscode/mcp.json`:

```json
{
  "servers": {
    "dotnet-templates": {
      "command": "template-engine-mcp",
      "type": "stdio"
    }
  }
}
```

### Direct stdio

```bash
template-engine-mcp
```

## Building

```bash
dotnet build
dotnet test
```

## Architecture

The MCP server is a **host** for the template engine, the same way Visual Studio and the `dotnet` CLI are hosts. It consumes public NuGet packages:

- `Microsoft.TemplateEngine.IDE` — `Bootstrapper` API for template operations
- `Microsoft.TemplateEngine.Abstractions` — `ITemplateInfo`, `IPostAction`, etc.
- `Microsoft.TemplateEngine.Edge` — `DefaultTemplateEngineHost`, `TemplateCreator`
- `ModelContextProtocol` — C# MCP SDK for tool registration and stdio transport

No modifications to the template engine are required.

## License

MIT
