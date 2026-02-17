# Microsoft.TemplateEngine.MCP

An MCP (Model Context Protocol) server that wraps the .NET Template Engine, enabling AI agents to discover, inspect, and instantiate `dotnet new` templates via structured tool calls.

## Overview

This server exposes the .NET Template Engine's capabilities as MCP tools, solving key problems for AI-driven development:

- **Single-call parameter discovery** — `template_inspect` returns all parameters, constraints, and post-actions in one call (vs. multiple `dotnet new` CLI commands)
- **Dry-run preview** — `template_dry_run` shows what files would be created without writing to disk
- **AI-friendly metadata** — Uses `HostIdentifier = "ai"` to auto-discover `ai.host.json` files for enhanced template descriptions
- **Standalone** — consumes template engine via NuGet packages, no engine modifications needed

## MCP Tools

| Tool | Description |
|------|-------------|
| `template_search` | Search templates by name, tags, language, or type |
| `template_list` | List installed templates with filtering |
| `template_inspect` | Full metadata inspection (parameters, constraints, post-actions) in a single call |
| `template_instantiate` | Create a project/item from a template |
| `template_dry_run` | Preview creation effects without writing to disk |
| `template_install` | Install a template package from NuGet or local path |
| `template_uninstall` | Remove an installed template package |
| `templates_installed` | Structured listing of all installed templates |

## Installation

### As a .NET global tool

```bash
dotnet tool install --global Microsoft.TemplateEngine.MCP
```

### From source

```bash
git clone https://github.com/YuliiaKovalova/dotnet-template-mcp.git
cd dotnet-template-mcp
dotnet build
dotnet pack -o ./nupkg
dotnet tool install --global --add-source ./nupkg Microsoft.TemplateEngine.MCP
```

## Configuration

### Claude Desktop

Add to your Claude Desktop configuration (`%APPDATA%\Claude\claude_desktop_config.json` on Windows, `~/Library/Application Support/Claude/claude_desktop_config.json` on macOS):

```json
{
  "mcpServers": {
    "dotnet-templates": {
      "command": "template-engine-mcp"
    }
  }
}
```

### VS Code / GitHub Copilot

Add to `.vscode/mcp.json` in your workspace:

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

### Cursor

Add to Cursor settings → MCP Servers:

```json
{
  "mcpServers": {
    "dotnet-templates": {
      "command": "template-engine-mcp"
    }
  }
}
```

### Any MCP client (stdio)

```bash
template-engine-mcp
```

The server communicates over stdin/stdout using the MCP JSON-RPC protocol.

## Tool Reference

### `template_search`

Search for templates by name, tags, language, or type.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `query` | string | Yes | Search string matched against names, short names, tags, descriptions |
| `language` | string | No | Language filter (e.g., `C#`, `F#`, `VB`) |
| `type` | string | No | Type filter (e.g., `project`, `item`, `solution`) |

**Example prompt:** *"Search for web templates in C#"*

---

### `template_list`

List all installed templates with optional filtering.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `language` | string | No | Language filter |
| `type` | string | No | Type filter |
| `classification` | string | No | Classification filter (e.g., `Web`, `Console`, `Library`) |

**Example prompt:** *"List all installed project templates"*

---

### `template_inspect`

Inspect a template's full metadata in a single call. Returns parameters (with names, types, defaults, choices, descriptions), constraints, post-actions, baselines, and classifications.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `templateName` | string | Yes | Template identity or short name (e.g., `console`, `webapp`) |

**Example prompt:** *"Inspect the webapp template and show me all its parameters"*

**Returns:** JSON with `Identity`, `Name`, `Parameters` (each with `Name`, `DataType`, `DefaultValue`, `Choices`, `Precedence`), `Constraints`, `PostActionIds`, `Baselines`, etc.

---

### `template_instantiate`

Create a project or item from a template. **Writes files to disk.**

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `templateName` | string | Yes | Template identity or short name |
| `name` | string | No | Project/item name |
| `outputPath` | string | No | Output directory |
| `parametersJson` | string | No | JSON object of parameter values |

**Example prompt:** *"Create a new console app called MyApp with .NET 8"*

The AI agent would call:
```json
{
  "templateName": "console",
  "name": "MyApp",
  "parametersJson": "{\"Framework\": \"net8.0\"}"
}
```

---

### `template_dry_run`

Preview what files and post-actions a template would create **without writing anything to disk**. Use this before `template_instantiate` to review changes.

**Parameters:** Same as `template_instantiate`.

**Example prompt:** *"Show me what files the webapp template would create with authentication"*

---

### `template_install`

Install a template package from NuGet or a local path.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `packageId` | string | Yes | NuGet package ID or local path to `.nupkg`/folder |
| `version` | string | No | Package version (latest if omitted) |

**Example prompt:** *"Install the MAUI templates"*

---

### `template_uninstall`

Remove an installed template package.

**Parameters:**
| Name | Type | Required | Description |
|------|------|----------|-------------|
| `packageId` | string | Yes | Package identifier to uninstall |

---

### `templates_installed`

Get a structured listing of all installed templates with metadata counts.

**Parameters:** None.

**Returns:** JSON with `totalCount` and array of templates, each including `ParameterCount`, `ConstraintCount`, `PostActionCount`.

## Typical AI Agent Workflow

```
1. template_search("web API")          → find matching templates
2. template_inspect("webapi")          → discover all parameters in one call
3. template_dry_run("webapi", ...)     → preview files without writing
4. template_instantiate("webapi", ...) → create the project
```

This replaces 4+ `dotnet new` CLI commands with structured, AI-friendly tool calls.

## Architecture

The MCP server is a **host** for the template engine — the same way Visual Studio and the `dotnet` CLI are hosts. It uses `HostIdentifier = "ai"`, which means the engine automatically discovers `ai.host.json` metadata files that template authors can ship alongside their templates for AI-enhanced descriptions and parameter hints.

**NuGet dependencies (no source modifications needed):**
- `Microsoft.TemplateEngine.IDE` — `Bootstrapper` API for template operations
- `Microsoft.TemplateEngine.Abstractions` — `ITemplateInfo`, `IPostAction`, etc.
- `Microsoft.TemplateEngine.Edge` — `DefaultTemplateEngineHost`, `TemplateCreator`
- `ModelContextProtocol` — C# MCP SDK for tool registration and stdio transport

## Building & Testing

```bash
dotnet build
dotnet test
```

## Project Structure

```
dotnet-template-mcp/
├── src/Microsoft.TemplateEngine.MCP/
│   ├── Host/
│   │   ├── McpTemplateEngineHost.cs      # ITemplateEngineHost with HostIdentifier="ai"
│   │   └── TemplateEngineService.cs      # Bootstrapper wrapper for DI
│   ├── Tools/
│   │   ├── TemplateSearchTool.cs         # template_search
│   │   ├── TemplateListTool.cs           # template_list
│   │   ├── TemplateInspectTool.cs        # template_inspect
│   │   ├── TemplateInstantiateTool.cs    # template_instantiate
│   │   ├── TemplateDryRunTool.cs         # template_dry_run
│   │   ├── TemplateInstallTool.cs        # template_install
│   │   ├── TemplateUninstallTool.cs      # template_uninstall
│   │   └── TemplateInstalledResourceTool.cs  # templates_installed
│   ├── Program.cs                        # MCP server entry point
│   └── Microsoft.TemplateEngine.MCP.csproj
├── test/Microsoft.TemplateEngine.MCP.Tests/
│   ├── TemplateSearchToolTests.cs
│   ├── TemplateListToolTests.cs
│   ├── TemplateInspectToolTests.cs
│   └── ParameterParsingTests.cs
└── Microsoft.TemplateEngine.MCP.sln
```

## License

MIT
