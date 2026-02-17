# Microsoft.TemplateEngine.MCP

An MCP (Model Context Protocol) server that wraps the .NET Template Engine, enabling AI agents to discover, inspect, and instantiate `dotnet new` templates via structured tool calls.

## Overview

This server exposes the .NET Template Engine's capabilities as MCP tools, solving key problems for AI-driven development:

- **Single-call parameter discovery** — `template_inspect` returns all parameters, constraints, and post-actions in one call (vs. multiple `dotnet new` CLI commands)
- **Dry-run preview** — `template_dry_run` shows what files would be created without writing to disk
- **SDK template auto-discovery** — automatically detects and installs SDK-bundled templates (`webapi`, `console`, `blazor`, etc.) on first access
- **AI-friendly metadata** — Uses `HostIdentifier = "ai"` to auto-discover `ai.host.json` files for enhanced template descriptions
- **Standalone** — consumes template engine via NuGet packages, no engine modifications needed

## MCP Tools

| Tool | Description |
|------|-------------|
| `template_search` | Search templates locally **and** on NuGet.org in a unified ranked list |
| `template_list` | List installed templates with filtering |
| `template_inspect` | Full metadata inspection (parameters, constraints, post-actions) in a single call |
| `template_instantiate` | Create a project/item — **auto-resolves** from NuGet if not installed, validates parameters, checks constraints |
| `template_dry_run` | Preview creation effects without writing to disk — same smart behaviors as instantiate |
| `template_install` | Install a template package and **return full metadata** for all installed templates |
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

> **Important:** After installing as a global tool, the executable is placed in `~/.dotnet/tools/` (i.e., `%USERPROFILE%\.dotnet\tools\` on Windows, `~/.dotnet/tools/` on macOS/Linux). If this directory is not on your system `PATH`, MCP clients may fail with `ENOENT`. In that case, use the **full path** to the executable as shown below, or add the directory to your `PATH`.

To find the full path to the tool:
```bash
# PowerShell
(Get-Command template-engine-mcp).Source

# bash/zsh
which template-engine-mcp
```

### Claude Desktop

Add to your Claude Desktop configuration (`%APPDATA%\Claude\claude_desktop_config.json` on Windows, `~/Library/Application Support/Claude/claude_desktop_config.json` on macOS):

**Recommended** — use the full path:

```json
{
  "mcpServers": {
    "dotnet-templates": {
      "command": "C:\\Users\\<username>\\.dotnet\\tools\\template-engine-mcp.exe"
    }
  }
}
```

If `~/.dotnet/tools` is on your `PATH`, you can use the short form:

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

Add to your user-level MCP config (`%APPDATA%\Code\User\mcp.json` on Windows) or `.vscode/mcp.json` in your workspace.

**Recommended** — use the full path to the executable (most reliable):

```json
{
  "servers": {
    "dotnet-templates": {
      "type": "stdio",
      "command": "C:\\Users\\<username>\\.dotnet\\tools\\template-engine-mcp.exe"
    }
  }
}
```

On macOS/Linux:

```json
{
  "servers": {
    "dotnet-templates": {
      "type": "stdio",
      "command": "/Users/<username>/.dotnet/tools/template-engine-mcp"
    }
  }
}
```

If `~/.dotnet/tools` is on your `PATH`, you can use the short form:

```json
{
  "servers": {
    "dotnet-templates": {
      "type": "stdio",
      "command": "template-engine-mcp"
    }
  }
}
```

> **Note:** The `"type": "stdio"` field is required. After updating the config, reload VS Code (`Ctrl+Shift+P` → "Developer: Reload Window") and check the **Output** panel → **MCP: dotnet-templates** for `Connection state: Running`.

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

If the command is not found, use the full path or `dotnet tool run` approach as shown above.

### Any MCP client (stdio)

```bash
template-engine-mcp
```

The server communicates over stdin/stdout using the MCP JSON-RPC protocol.

### Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| `ENOENT` or "command not found" | `~/.dotnet/tools` not on PATH | Use full path to the `.exe`, or add `~/.dotnet/tools` to your system PATH |
| `spawn template-engine-mcp ENOENT` in VS Code | Same as above | Use full path or `dotnet tool run` approach |
| `template_search` returns empty | MCP server uses its own template cache (`HostIdentifier = "ai"`), separate from `dotnet new` | SDK templates auto-install on first access; use `template_install` for additional packages |

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

### Simple (template already installed)
```
template_instantiate("console", name: "MyApp")  → project created (1 call)
```

### With discovery
```
1. template_search("web API")          → find matching templates (local + NuGet)
2. template_inspect("webapi")          → discover all parameters in one call
3. template_dry_run("webapi", ...)     → preview files without writing
4. template_instantiate("webapi", ...) → create the project
```

### Auto-resolve (template NOT installed)
```
template_instantiate("maui-blazor", name: "MyApp")
  → auto-searches NuGet → installs package → creates project (1 call)
```

This replaces 4+ `dotnet new` CLI commands with structured, AI-friendly tool calls.

## Smart Behaviors

### Auto-Resolve
If you call `template_instantiate` or `template_dry_run` with a template that's not installed, the server automatically:
1. Searches NuGet.org for the template
2. Installs the matching package
3. Proceeds with instantiation or dry-run

If the match is ambiguous, it returns a list of candidates with a "did you mean...?" suggestion.

### Parameter Validation
Before writing any files, the server validates parameters against the template's definition:
- **Choice parameters** — checks the value is in the allowed set (e.g., `Framework: "net3.0"` → error with valid choices)
- **Boolean parameters** — checks the value is `true` or `false`
- **Integer parameters** — checks the value is a valid number
- **Unknown parameters** — reports which parameters are available

### Constraint Checking
Before creation, the server checks template constraints and returns warnings:
- **OS constraints** — e.g., "This template requires Windows but you are on Linux"
- **SDK version constraints** — e.g., "Requires .NET 9.0 SDK"
- **Workload constraints** — e.g., "Requires the MAUI workload"

### SDK Template Auto-Discovery
On first template operation, the server automatically scans the .NET SDK directory for bundled template packages (e.g., `console`, `webapi`, `classlib`, `blazor`, `worker`, etc.) and installs them into the MCP host's template cache. This means SDK templates are available immediately — no manual `template_install` required.

The discovery process:
1. Locates the SDK root (`DOTNET_ROOT` or default install path)
2. Scans `{dotnet_root}/templates/{latest_version}/*.nupkg`
3. Deduplicates packages by base name (keeps highest version)
4. Installs only packages not already present

### Unified Search
`template_search` returns results from both local installed templates AND NuGet.org in a single ranked list. Local templates appear first (ready to use), NuGet results include package ID and version for installation.

### Smart Install
`template_install` returns install status **and** full metadata for all templates in the package, so the AI can immediately proceed to instantiation without a second call.

## Architecture

The MCP server is a **host** for the template engine — the same way Visual Studio and the `dotnet` CLI are hosts. It uses `HostIdentifier = "ai"`, which means the engine automatically discovers `ai.host.json` metadata files that template authors can ship alongside their templates for AI-enhanced descriptions and parameter hints.

### Template Cache & Package Sharing

The template engine stores installed packages in a **global** `packages.json` file (`~/.templateengine/packages.json`) that is **shared across all hosts**. This means:

- Templates installed via `dotnet new install` are **automatically visible** to the MCP server
- Templates installed via the MCP server's `template_install` are visible to `dotnet new`
- SDK workload templates (MAUI, Android, etc.) are shared across all hosts

What's **per-host** is only:
- `templatecache.json` — cached template metadata (auto-rebuilt on first access)
- `nugetTemplateSearchInfo.json` — the NuGet search cache
- `*.host.json` resolution — which host config to load (`ai.host.json` vs `dotnetcli.host.json`)

The MCP server uses `fallbackHostTemplateConfigNames: ["dotnetcli.host.json"]` so templates without an `ai.host.json` still load the CLI's metadata.

**Template authors** can optionally ship `.template.config/ai.host.json` alongside their `template.json` to provide AI-enhanced descriptions, parameter hints, and skill mappings that are automatically discovered when the MCP server loads the template.

**NuGet dependencies (no source modifications needed):**
- `Microsoft.TemplateEngine.IDE` — `Bootstrapper` API for template operations
- `Microsoft.TemplateEngine.Abstractions` — `ITemplateInfo`, `IPostAction`, etc.
- `Microsoft.TemplateEngine.Edge` — `DefaultTemplateEngineHost`, `TemplateCreator`
- `Microsoft.TemplateSearch.Common` — `TemplateSearchCoordinator` for NuGet search
- `ModelContextProtocol` — C# MCP SDK for tool registration and stdio transport

## MCP Prompt

### `create_project`

A guided prompt that walks the AI through the full project creation workflow:

1. Search for templates matching your description
2. Inspect the best match for parameters and constraints
3. Suggest parameter values
4. Preview with dry-run
5. Create the project after confirmation

**Usage in AI chat:** *"I want to create a new web API with authentication"*

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
│   │   └── TemplateEngineService.cs      # Bootstrapper wrapper + NuGet search + validation
│   ├── Prompts/
│   │   └── CreateProjectPrompt.cs        # create_project guided workflow
│   ├── Tools/
│   │   ├── TemplateSearchTool.cs         # template_search (local + NuGet)
│   │   ├── TemplateListTool.cs           # template_list
│   │   ├── TemplateInspectTool.cs        # template_inspect
│   │   ├── TemplateInstantiateTool.cs    # template_instantiate (auto-resolve + validation)
│   │   ├── TemplateDryRunTool.cs         # template_dry_run (auto-resolve + validation)
│   │   ├── TemplateInstallTool.cs        # template_install (with metadata return)
│   │   ├── TemplateUninstallTool.cs      # template_uninstall
│   │   └── TemplateInstalledResourceTool.cs  # templates_installed
│   ├── Program.cs                        # MCP server entry point
│   └── Microsoft.TemplateEngine.MCP.csproj
├── test/Microsoft.TemplateEngine.MCP.Tests/
│   ├── TemplateSearchToolTests.cs
│   ├── TemplateListToolTests.cs
│   ├── TemplateInspectToolTests.cs
│   ├── ParameterParsingTests.cs
│   └── ParameterValidationTests.cs
└── Microsoft.TemplateEngine.MCP.sln
```

## License

MIT
