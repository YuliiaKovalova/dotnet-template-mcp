# Microsoft.TemplateEngine.MCP

An MCP server that lets AI agents work with `dotnet new` templates — search, inspect, preview, and create projects through natural conversation instead of memorizing CLI flags.

Instead of this:
```bash
dotnet new list --language C#
dotnet new webapi --help
dotnet new webapi --auth Individual --use-controllers --name MyApi --output ./MyApi
```

Your AI agent just says: *"I need a web API with authentication and controllers"* — and the MCP server figures out the rest.

## Tools

| Tool | What it does |
|------|-------------|
| `template_search` | Search locally **and** on NuGet.org — one call, ranked results |
| `template_list` | List what's installed, filter by language/type/classification |
| `template_inspect` | Parameters, constraints, post-actions — all in one shot |
| `template_instantiate` | Create a project. Not installed? Auto-resolves from NuGet in one call |
| `template_dry_run` | Preview files without touching disk |
| `template_install` | Install a package (idempotent — skips if already there) |
| `template_uninstall` | Remove a template package |
| `templates_installed` | Inventory of everything installed |
| `template_from_intent` | *"web API with auth"* → webapi + `auth=Individual` — no LLM needed |
| `template_create_from_existing` | Analyze a .csproj → generate a reusable template matching repo conventions |
| `template_compose` | Execute a sequence of templates (project + items) in one workflow |
| `template_suggest_parameters` | Suggest parameter values with rationale based on cross-parameter relationships |

📖 [Full tool reference →](docs/tool-reference.md)

## Quick Start

### Zero-install with `dnx` (.NET 10+)

```bash
dnx -y DotnetTemplateMCP --version 0.1.0-preview.3
```

### Global tool

```bash
dotnet tool install --global DotnetTemplateMCP --version 0.1.0-preview.3
```

### VS Code / GitHub Copilot

Add to `mcp.json`:

```json
{
  "servers": {
    "dotnet-templates": {
      "type": "stdio",
      "command": "dnx",
      "args": ["-y", "DotnetTemplateMCP", "--version", "0.1.0-preview.3"]
    }
  }
}
```

📖 [Claude Desktop, Cursor, and more →](docs/configuration.md)

## How it works

```
You: "I need a web API with authentication, controllers, and Docker support"

→ template_from_intent extracts keywords: web api, authentication, controllers, docker
→ Matches: webapi (confidence: 0.85)
→ Resolves: auth=Individual, UseControllers=true, EnableDocker=true
→ template_instantiate creates the project
```

The server also does **smart defaults** (AOT → latest framework, auth → HTTPS stays on), **parameter validation** before writing files, **constraint checking** (OS, SDK, workload), and **auto-resolves** templates from NuGet if they're not installed.

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
dotnet test    # 170 tests — unit, integration, and E2E
```

CI runs on push/PR via [GitHub Actions](.github/workflows/ci.yml) (Ubuntu + Windows).

## License

MIT
