# Microsoft.TemplateEngine.MCP

An MCP server that lets AI agents work with `dotnet new` templates — search, inspect, preview, and create projects through natural conversation instead of memorizing CLI flags.

## What is this?

Instead of this:
```bash
dotnet new list --language C#
dotnet new webapi --help
dotnet new webapi --auth Individual --use-controllers --name MyApi --output ./MyApi
```

Your AI agent just says: *"I need a web API with authentication and controllers"* — and the MCP server figures out the rest.

## What can it do?

### Core Tools (Phase 1)

The bread and butter — everything you need to discover, preview, and create .NET projects:

| Tool | What it does |
|------|-------------|
| `template_search` | Search locally **and** on NuGet.org — one call, ranked results |
| `template_list` | List what's installed, filter by language/type/classification |
| `template_inspect` | Get the full picture — parameters, constraints, post-actions, all in one shot |
| `template_instantiate` | Create a project. If the template isn't installed? It'll find it on NuGet, install it, and create — all in one call |
| `template_dry_run` | See what files would be created without touching disk |
| `template_install` | Install a template package (skips if already there, tells you about upgrades) |
| `template_uninstall` | Remove a template package |
| `templates_installed` | Quick inventory of everything installed |

### Intent Resolution (Phase 2) 🆕

Tell the server what you want in plain English — it figures out which template and parameters to use. No LLM needed, works fully offline.

| Tool | What it does |
|------|-------------|
| `template_from_intent` | *"web API with auth and controllers"* → webapi + `auth=Individual` + `UseControllers=true` |
| `create_from_description` | Guided prompt: describe what you want → match → preview → create |

The intent resolver knows 70+ keywords covering templates (`blazor`, `grpc`, `worker`, `maui`...), parameters (`authentication`, `native aot`, `docker`, `.NET 9`...), and languages (`C#`, `F#`, `VB`). It scores matches using a 5-factor algorithm and pre-fills parameters it can confidently resolve.

**Don't want it?** Set `MCP_TEMPLATE_INTENT_RESOLUTION=false` and it's off.

## Installation

### Zero-install with `dnx` (.NET 10+)

No installation needed — just run:
```bash
dnx -y Microsoft.TemplateEngine.MCP
```

This downloads and runs the tool on the fly, just like `npx` in Node.js. Perfect for trying it out or CI/CD.

### As a .NET global tool

```bash
dotnet tool install --global Microsoft.TemplateEngine.MCP
```

### As a local tool (recommended for project-level config)

```bash
dotnet new tool-manifest   # creates .config/dotnet-tools.json if not present
dotnet tool install Microsoft.TemplateEngine.MCP
```

Then run with:
```bash
dotnet tool run template-engine-mcp
```

This avoids PATH issues — the `dotnet` command is always available.

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

**Recommended** — use `dnx` for zero-install (.NET 10+):

```json
{
  "servers": {
    "dotnet-templates": {
      "type": "stdio",
      "command": "dnx",
      "args": ["-y", "Microsoft.TemplateEngine.MCP"]
    }
  }
}
```

**Alternative** — use `dotnet tool run` (requires local tool install):

```json
{
  "servers": {
    "dotnet-templates": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["tool", "run", "template-engine-mcp"]
    }
  }
}
```

**Alternative** — use the full path to the executable:

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
      "command": "dnx",
      "args": ["-y", "Microsoft.TemplateEngine.MCP"]
    }
  }
}
```

Or using `dotnet tool run` (requires local install):

```json
{
  "mcpServers": {
    "dotnet-templates": {
      "command": "dotnet",
      "args": ["tool", "run", "template-engine-mcp"]
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
| `ENOENT` or "command not found" | `~/.dotnet/tools` not on PATH | Use `dotnet tool run template-engine-mcp` (recommended), use full path to `.exe`, or add `~/.dotnet/tools` to your system PATH |
| `spawn template-engine-mcp ENOENT` in VS Code | Same as above | Switch to `"command": "dotnet", "args": ["tool", "run", "template-engine-mcp"]` in your MCP config |
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

## How AI agents actually use this

### The easy way (Phase 2 intent resolution)
```
You: "I need a web API with authentication, controllers, and Docker support"
→ template_from_intent resolves to: webapi + auth=Individual + UseControllers=true + EnableDocker=true
→ template_instantiate creates the project
```
One natural sentence, done.

### The precise way (Phase 1 tools)
```
1. template_search("web API")          → find matching templates
2. template_inspect("webapi")          → see all 25 parameters
3. template_dry_run("webapi", ...)     → preview without writing
4. template_instantiate("webapi", ...) → create the project
```

### The lazy way
```
template_instantiate("maui-blazor", name: "MyApp")
  → not installed? auto-searches NuGet → installs → creates (1 call)
```

## Smart Behaviors

The server isn't just a thin wrapper — it actually thinks about what you're doing.

### Auto-Resolve
Ask for a template that's not installed? The server searches NuGet, installs the best match, and creates the project — all in one call. If it's not sure, it asks *"did you mean...?"* with candidates.

### Parameter Validation
Catches mistakes before they hit disk:
- Wrong choice value → *"Invalid 'net3.0' for Framework. Valid: net8.0, net9.0"*
- Bad boolean → *"Expected true/false, got 'yes'"*
- Unknown param → *"Available parameters: Framework, auth, UseControllers..."*

### Smart Defaults
Cross-parameter intelligence so you don't have to think about every flag:
- `EnableAot=true` → auto-suggests the latest framework
- `auth=Individual` → makes sure HTTPS stays enabled
- `UseControllers=true` → sets `UseMinimalAPIs=false` (they're mutually exclusive)

Shows what it changed in `AppliedSmartDefaults` so nothing is a surprise.

### Constraint Checking
Warns you before creation if something's going to be a problem — wrong OS, missing SDK version, missing workload.

### SDK Template Auto-Discovery
First time you use the server, it finds all SDK-bundled templates (`console`, `webapi`, `blazor`, etc.) and installs them automatically. No `template_install` needed for the basics.

### Idempotent Install
`template_install` won't reinstall something that's already there. If there's a newer version, it tells you.

### NuGet Preview
`template_inspect` on a template that isn't installed? It queries NuGet.org and shows you the package metadata so you can decide whether to install.

## Feature Flags

| Environment Variable | Default | What it controls |
|---------------------|---------|-----------------|
| `MCP_TEMPLATE_INTENT_RESOLUTION` | `true` | Intent resolution tools (`template_from_intent`, `create_from_description`) |

Set to `false`, `0`, `no`, or `off` to disable. The core tools always work regardless.

## Telemetry & Observability

Everything is instrumented via `System.Diagnostics` — plug in any OpenTelemetry-compatible backend and you're good:

- **Tracing**: `ActivitySource` named `Microsoft.TemplateEngine.MCP` — every tool call gets a span
- **Metrics**: `Meter` with counters for invocations, errors, templates created, packages installed, auto-resolves, validation failures, smart defaults applied, and intent resolutions

Quick way to see what's happening:
```bash
dotnet-counters monitor --process-id <PID> Microsoft.TemplateEngine.MCP
```

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

## MCP Prompts

### `create_project`

Step-by-step guided workflow: search → inspect → suggest params → dry-run → create.

*"I want to create a new web API with authentication"*

### `create_from_description` 🆕

Same idea but starts with intent resolution — describe what you want in plain English and the server figures out the template and parameters for you.

*"I need a Blazor app with individual accounts auth and Docker support"*

## Building & Testing

```bash
dotnet build
dotnet test    # 108 tests — unit, integration, and E2E
```

CI runs automatically on push/PR via GitHub Actions (build + test on Ubuntu and Windows).

## Project Structure

```
dotnet-template-mcp/
├── src/Microsoft.TemplateEngine.MCP/
│   ├── Host/
│   │   ├── McpTemplateEngineHost.cs      # ITemplateEngineHost with HostIdentifier="ai"
│   │   └── TemplateEngineService.cs      # Bootstrapper wrapper + NuGet search + validation + smart defaults
│   ├── Intent/                           # 🆕 Phase 2
│   │   ├── IIntentResolver.cs            # Intent resolution interface
│   │   ├── TemplateResolution.cs         # Resolution models (matches, confidence, params)
│   │   ├── IntentSynonymDictionary.cs    # 70+ keyword → template/param mappings
│   │   └── ClassificationBasedIntentResolver.cs  # 5-factor scoring resolver
│   ├── Prompts/
│   │   ├── CreateProjectPrompt.cs        # create_project guided workflow
│   │   └── CreateFromDescriptionPrompt.cs # 🆕 create_from_description (intent-based)
│   ├── Telemetry/
│   │   └── McpTelemetry.cs               # ActivitySource + Meter (OpenTelemetry-compatible)
│   ├── Tools/
│   │   ├── TemplateSearchTool.cs         # template_search (local + NuGet)
│   │   ├── TemplateListTool.cs           # template_list
│   │   ├── TemplateInspectTool.cs        # template_inspect (+ NuGet preview)
│   │   ├── TemplateInstantiateTool.cs    # template_instantiate (auto-resolve + validation + smart defaults)
│   │   ├── TemplateDryRunTool.cs         # template_dry_run
│   │   ├── TemplateInstallTool.cs        # template_install (idempotent)
│   │   ├── TemplateUninstallTool.cs      # template_uninstall
│   │   ├── TemplateInstalledResourceTool.cs  # templates_installed
│   │   └── TemplateFromIntentTool.cs     # 🆕 template_from_intent
│   ├── McpFeatureFlags.cs                # 🆕 Feature toggles (env var based)
│   ├── Program.cs
│   └── Microsoft.TemplateEngine.MCP.csproj
├── test/Microsoft.TemplateEngine.MCP.Tests/
│   ├── IntentSynonymDictionaryTests.cs   # 🆕 Keyword extraction tests
│   ├── IntentResolverTests.cs            # 🆕 Resolver integration tests
│   ├── FeatureFlagsTests.cs              # 🆕 Toggle tests
│   ├── TemplateFromIntentToolTests.cs    # 🆕 Intent tool tests
│   ├── EndToEndTests.cs                  # Full workflow E2E
│   ├── IntegrationTests.cs              # Real engine integration
│   ├── SmartDefaultsTests.cs            # Smart defaults logic
│   ├── TemplateInstallToolTests.cs      # Idempotent install
│   ├── TemplateInspectNuGetPreviewTests.cs
│   ├── TemplateSearchToolTests.cs
│   ├── TemplateListToolTests.cs
│   ├── TemplateInspectToolTests.cs
│   ├── ParameterParsingTests.cs
│   └── ParameterValidationTests.cs
├── .github/workflows/ci.yml             # 🆕 GitHub Actions CI
└── Microsoft.TemplateEngine.MCP.sln
```

## License

MIT
