# Microsoft.TemplateEngine.MCP

<!-- mcp-name: io.github.YuliiaKovalova/dotnet-template-mcp -->

An MCP server that lets AI agents work with `dotnet new` templates — search, inspect, preview, and create projects through natural conversation instead of memorizing CLI flags.

<a href="https://glama.ai/mcp/servers/@YuliiaKovalova/dotnet-template-mcp">
  <img width="380" height="200" src="https://glama.ai/mcp/servers/@YuliiaKovalova/dotnet-template-mcp/badge" alt="DotnetTemplateMCP MCP server" />
</a>

Instead of this:
```bash
dotnet new list --language C#
dotnet new webapi --help
dotnet new webapi --auth Individual --use-controllers --name MyApi --output ./MyApi
```

Your AI agent just says: *"I need a web API with authentication and controllers"* — and the MCP server figures out the rest.

## Template Validation for Authors

Building a custom `dotnet new` template? `template_validate` catches mistakes **before** you publish — no more guessing if your `template.json` is correct:

```
Agent calls: template_validate("./my-template")

← Returns:
{
  "valid": false,
  "summary": "2 error(s), 1 warning(s), 3 suggestion(s)",
  "errors": [
    "Missing required field 'shortName'.",
    "Parameter 'Framework': default value 'net7.0' is not in the choices list."
  ],
  "warnings": [
    "Missing 'sourceName'. Without it, the generated project name won't be customizable via --name."
  ],
  "suggestions": [
    "Consider adding a 'description' field to help users understand what this template creates.",
    "Consider adding 'language' tag (e.g., 'C#') for better discoverability.",
    "Consider adding 'type' tag (e.g., 'project', 'item') for filtering."
  ]
}
```

**What it catches:** missing required fields, invalid identity format, short name conflicts with CLI commands, parameter issues (missing defaults, empty choices, prefix collisions, type mismatches), broken computed symbols, constraint misconfiguration, and missing tags.

No existing tooling does this — most template authors discover issues only after `dotnet new install` fails or produces wrong output.

## Tools

| Tool | What it does |
|------|-------------|
| `template_search` | Search locally **and** on NuGet.org — one call, ranked results |
| `template_list` | List what's installed, filter by language/type/classification |
| `template_inspect` | Parameters, constraints, post-actions — all in one shot |
| `template_instantiate` | Create a project. Not installed? Auto-resolves from NuGet. Elicits missing params interactively |
| `template_dry_run` | Preview files without touching disk |
| `template_install` | Install a package (idempotent — skips if already there) |
| `template_uninstall` | Remove a template package |
| `templates_installed` | Inventory of everything installed |
| `template_from_intent` | *"web API with auth"* → webapi + `auth=Individual` — no LLM needed |
| `template_create_from_existing` | Analyze a .csproj → generate a reusable template matching repo conventions |
| `template_compose` | Execute a sequence of templates (project + items) in one workflow |
| `template_suggest_parameters` | Suggest parameter values with rationale based on cross-parameter relationships |
| `template_validate` | Validate a local template directory for authoring issues before publishing |
| `template_compare` | Compare 2+ templates side by side — parameters, features, frameworks |
| `solution_analyze` | Analyze a solution/workspace — project structure, frameworks, CPM status |

📖 [Full tool reference →](docs/tool-reference.md)

## Hosted deployment

A hosted deployment is available on [Fronteir AI](https://fronteir.ai/mcp/yuliiakovalova-dotnet-template-mcp).

## Quick Start

### Global tool (.NET 8+)

```bash
dotnet tool install --global DotnetTemplateMCP --version 1.3.0
```

### Zero-install with `dnx` (.NET 10+)

```bash
dnx -y DotnetTemplateMCP --version 1.3.0
```

### VS Code / GitHub Copilot

Add to `mcp.json`:

```json
{
  "servers": {
    "dotnet-templates": {
      "type": "stdio",
      "command": "dnx",
      "args": ["-y", "DotnetTemplateMCP", "--version", "1.3.0"]
    }
  }
}
```

📖 [Claude Desktop, Cursor, and more →](docs/configuration.md)

## Transport Modes

### Stdio (default)

Standard I/O transport for local CLI and tool usage:

```bash
template-engine-mcp                     # stdio is the default
template-engine-mcp --transport stdio   # explicit
```

### HTTP (remote / cloud / team-shared)

Streamable HTTP transport for remote, multi-tenant, or CI/CD deployment:

```bash
template-engine-mcp --transport http
# or via environment variable:
MCP_TEMPLATE_TRANSPORT=http template-engine-mcp
```

The HTTP server exposes:
- **`/mcp`** — MCP streamable HTTP endpoint
- **`/health`** — Health check endpoint

Configure the listen URL:
```bash
MCP_TEMPLATE_HTTP_URL=http://0.0.0.0:8080 template-engine-mcp --transport http
```

Connect your MCP client:
```json
{
  "servers": {
    "dotnet-templates": {
      "type": "http",
      "url": "http://localhost:5005/mcp"
    }
  }
}
```

### Interactive Elicitation

When a template has required parameters that weren't provided, the server **asks the user interactively** via MCP elicitation — instead of failing. Template parameter types are mapped to form fields:

| Template Parameter | Elicitation Field |
|---|---|
| `string` | Text input |
| `bool` / `boolean` | Checkbox |
| `int` / `number` | Number input |
| Choice parameter | Single-select dropdown |

Disable with `MCP_TEMPLATE_ELICITATION=false`.

## How it works

```
You: "I need a web API with authentication, controllers, and Docker support"

→ template_from_intent extracts keywords: web api, authentication, controllers, docker
→ Matches: webapi (confidence: 0.85)
→ Resolves: auth=Individual, UseControllers=true, EnableDocker=true
→ template_instantiate creates the project
```

The server also does **smart defaults** (AOT → latest framework, auth → HTTPS stays on), **parameter validation** before writing files, **constraint checking** (OS, SDK, workload), **interactive elicitation** of missing required parameters, and **auto-resolves** templates from NuGet if they're not installed.

### CPM & Latest Package Versions

When creating a project inside a solution that uses [Central Package Management](https://learn.microsoft.com/nuget/consume-packages/central-package-management), the server automatically:

1. **Detects** `Directory.Packages.props` by walking up the directory tree
2. **Strips** `Version` attributes from generated `.csproj` PackageReferences
3. **Adds** missing `<PackageVersion>` entries to `Directory.Packages.props`
4. **Resolves** latest stable NuGet versions — no more stale hardcoded versions from templates

```
Before (what dotnet new generates):
  <PackageReference Include="Serilog" Version="3.1.0" />    ← stale, breaks CPM

After (what template_instantiate produces):
  .csproj:                    <PackageReference Include="Serilog" />
  Directory.Packages.props:   <PackageVersion Include="Serilog" Version="4.2.0" />
```

Works for standalone projects too — versions are updated directly in the `.csproj`.

### Multi-Template Composition

Chain multiple templates in one call with `template_compose`:

```json
[
  {"templateName": "webapi", "name": "MyApi", "parametersJson": "{\"auth\": \"Individual\"}"},
  {"templateName": "gitignore", "target": "."}
]
```

📖 [Architecture & smart behaviors →](docs/architecture.md)

### Tool Profiles (Lite vs Full)

By default, all 14 tools are available. If your agent works better with fewer tools, set the `MCP_TEMPLATE_TOOL_PROFILE` environment variable:

| Profile | Tools | When to use |
|---------|-------|-------------|
| `full` (default) | All 14 tools | Full control — advanced workflows, composition, custom templates |
| `lite` | 5 core tools | Simpler agents that just need to find and create projects |

**Lite profile tools:** `template_from_intent`, `template_instantiate`, `template_inspect`, `template_search`, `template_dry_run`

```json
{
  "servers": {
    "dotnet-template-mcp": {
      "command": "dotnet-template-mcp",
      "env": {
        "MCP_TEMPLATE_TOOL_PROFILE": "lite"
      }
    }
  }
}
```

Non-lite tools will return a helpful message explaining they're disabled and how to enable them.

## Documentation

| Doc | What's in it |
|-----|-------------|
| [Configuration](docs/configuration.md) | VS Code, Claude Desktop, Cursor setup + troubleshooting |
| [Tool Reference](docs/tool-reference.md) | Every tool's parameters, types, and examples |
| [Architecture](docs/architecture.md) | Template cache, smart behaviors, telemetry, project structure |
| [MCP vs Skills](docs/mcp-vs-skills.md) | Why MCP over Copilot Skills — benefits and downsides |
| [Plain LLM vs MCP](docs/plain-llm-vs-mcp.md) | Side-by-side: what a plain LLM does vs. the MCP tool (4 scenarios) |
| [Skills Equivalent](docs/skills-equivalent.md) | What it'd take to cover this with Skills instead |

## Building & Testing

```bash
dotnet build
dotnet test    # 185+ tests — unit, integration, and E2E
```

CI runs on push/PR via [GitHub Actions](.github/workflows/ci.yml) (Ubuntu + Windows).

## Contributing

Contributions are welcome! Please open an issue to discuss proposed changes before submitting a PR.

```bash
# Setup
dotnet restore
dotnet build

# Run tests
dotnet test

# Pack locally
dotnet pack src/Microsoft.TemplateEngine.MCP -o nupkg/
```

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for release history.

## License

[MIT](LICENSE)