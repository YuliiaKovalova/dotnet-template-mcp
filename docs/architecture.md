# Architecture

## How it works

The MCP server is a **host** for the template engine — the same way Visual Studio and the `dotnet` CLI are hosts. It uses `HostIdentifier = "ai"`, which means the engine automatically discovers `ai.host.json` metadata files that template authors can ship for AI-enhanced descriptions.

## Template Cache & Package Sharing

Installed packages live in a **global** `packages.json` (`~/.templateengine/packages.json`) shared across all hosts:

- Templates installed via `dotnet new install` are automatically visible to the MCP server
- Templates installed via `template_install` are visible to `dotnet new`
- SDK workload templates (MAUI, Android, etc.) are shared

What's **per-host**:
- `templatecache.json` — cached metadata (auto-rebuilt on first access)
- `nugetTemplateSearchInfo.json` — NuGet search cache
- `*.host.json` resolution — `ai.host.json` vs `dotnetcli.host.json`

The server uses `fallbackHostTemplateConfigNames: ["dotnetcli.host.json"]` so templates without an `ai.host.json` still work.

## For template authors

Ship `.template.config/ai.host.json` alongside `template.json` to provide AI-enhanced descriptions, parameter hints, and skill mappings.

## NuGet dependencies

No source modifications needed — everything is consumed via public NuGet packages:

- `Microsoft.TemplateEngine.IDE` — Bootstrapper API
- `Microsoft.TemplateEngine.Abstractions` — `ITemplateInfo`, `IPostAction`, etc.
- `Microsoft.TemplateEngine.Edge` — `DefaultTemplateEngineHost`, `TemplateCreator`
- `Microsoft.TemplateSearch.Common` — NuGet search
- `ModelContextProtocol` — C# MCP SDK

## Smart Behaviors

### Auto-Resolve
Template not installed? The server searches NuGet, installs the best match, and creates — all in one call. Ambiguous? Returns *"did you mean...?"* candidates.

### Parameter Validation
Catches mistakes before files are written: invalid choice values, bad booleans, unknown parameters.

### Smart Defaults
Cross-parameter intelligence:
- `EnableAot=true` → suggests latest framework
- `auth=Individual` → keeps HTTPS enabled
- `UseControllers=true` → sets `UseMinimalAPIs=false`

Applied defaults are returned in `AppliedSmartDefaults`.

### Constraint Checking
Warns about OS mismatches, missing SDK versions, or missing workloads before creation.

### SDK Template Auto-Discovery
On first use, scans `{dotnet_root}/templates/{version}/*.nupkg` and installs SDK-bundled templates automatically.

### Intent Resolution (Phase 2)
70+ keyword mappings resolve natural-language descriptions to template + parameter selections. 5-factor scoring algorithm ranks matches by short name, classification, name/description, parameter applicability, and identity.

## Telemetry

Instrumented via `System.Diagnostics` (OpenTelemetry-compatible):

- **ActivitySource** `Microsoft.TemplateEngine.MCP` — spans for every tool call
- **Meter** — counters for invocations, errors, templates created, packages installed, auto-resolves, validation failures, smart defaults, intent resolutions
- **Histogram** — tool duration in milliseconds

```bash
dotnet-counters monitor --process-id <PID> Microsoft.TemplateEngine.MCP
```

## Feature Flags

| Environment Variable | Default | Controls |
|---------------------|---------|----------|
| `MCP_TEMPLATE_INTENT_RESOLUTION` | `true` | Intent resolution tools |

Set to `false`, `0`, `no`, or `off` to disable.

## Project Structure

```
dotnet-template-mcp/
├── src/Microsoft.TemplateEngine.MCP/
│   ├── Analysis/                          # Project analyzer + template generator
│   ├── Host/                             # Template engine host + service
│   ├── Intent/                           # Phase 2: intent resolution
│   ├── Prompts/                          # MCP prompts
│   ├── Telemetry/                        # ActivitySource + Meter
│   ├── Tools/                            # MCP tools (10 tools)
│   ├── McpFeatureFlags.cs                # Feature toggles
│   └── Program.cs                        # Entry point
├── test/Microsoft.TemplateEngine.MCP.Tests/  # 143 tests
├── docs/
│   ├── architecture.md                   # This file
│   ├── configuration.md                  # MCP client setup + troubleshooting
│   ├── tool-reference.md                 # Full tool parameter reference
│   ├── mcp-vs-skills.md                  # MCP vs Copilot Skills comparison
│   └── skills-equivalent.md              # Skills-based equivalent analysis
├── .github/
│   ├── workflows/ci.yml                  # CI pipeline
│   └── copilot-instructions.md           # Copilot tool routing
└── Microsoft.TemplateEngine.MCP.sln
```
