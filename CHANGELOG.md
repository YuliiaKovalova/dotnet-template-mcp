# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-03-02

### Added

#### Core Tools (12 total)
- **`template_search`** — Search locally and on NuGet.org with ranked results
- **`template_list`** — List installed templates with language/type/classification filters
- **`template_inspect`** — Full metadata including parameters, constraints, and post-actions
- **`template_instantiate`** — Create projects with auto-resolve from NuGet, smart defaults, parameter validation, constraint checking, and interactive elicitation of missing required parameters
- **`template_dry_run`** — Preview files without writing to disk
- **`template_install`** — Idempotent package installation
- **`template_uninstall`** — Remove template packages
- **`templates_installed`** — Inventory of all installed templates
- **`template_from_intent`** — Natural language to template resolution (offline, rule-based, 70+ keyword mappings)
- **`template_create_from_existing`** — Reverse-engineer a reusable template from an existing .csproj
- **`template_compose`** — Execute multi-template workflows (project + items) in one call
- **`template_suggest_parameters`** — Parameter recommendations with cross-parameter rationale
- **`solution_analyze`** — Analyze workspace structure, target frameworks, and CPM status

#### Transport Modes
- **Stdio transport** (default) — for local CLI and IDE integration
- **Streamable HTTP transport** — for remote, cloud, team-shared, and CI/CD deployment
  - `/mcp` endpoint for MCP protocol
  - `/health` endpoint for monitoring
  - Configurable via `--transport http` or `MCP_TEMPLATE_TRANSPORT` env var
  - Custom listen URL via `MCP_TEMPLATE_HTTP_URL`

#### Smart Behaviors
- **Smart defaults** — cross-parameter intelligence (AOT → latest framework, auth → HTTPS stays on, controllers → minimal APIs off)
- **Parameter validation** — validates before writing any files
- **Constraint checking** — OS, SDK version, and workload requirements
- **Auto-resolve from NuGet** — template not installed? Searches, installs, and creates in one call
- **Interactive elicitation** — asks user for missing required parameters via MCP elicitation protocol

#### Post-Creation Processing
- **CPM detection** — walks directory tree for `Directory.Packages.props`
- **CPM adaptation** — strips `Version` from .csproj PackageReferences, adds to props
- **NuGet version resolution** — replaces stale template versions with latest stable from NuGet

#### Intent Resolution
- `ClassificationBasedIntentResolver` with 5-factor scoring
- `IntentSynonymDictionary` with 70+ keyword mappings
- Feature flag: `MCP_TEMPLATE_INTENT_RESOLUTION`

#### Infrastructure
- `dotnet tool` packaging as `DotnetTemplateMCP` NuGet package
- `dnx` zero-install support (.NET 10+)
- CI pipeline (GitHub Actions, Ubuntu + Windows)
- OpenTelemetry-ready telemetry (ActivitySource + Meter)
- Structured error responses with error codes and suggestions

#### Documentation
- Architecture guide (`docs/architecture.md`)
- Tool reference (`docs/tool-reference.md`)
- Configuration guide (`docs/configuration.md`)
- MCP vs Skills comparison (`docs/mcp-vs-skills.md`)
- Plain LLM vs MCP comparison (`docs/plain-llm-vs-mcp.md`)

## [0.1.0-preview.3] - 2026-02-20

### Added
- CPM adaptation and latest NuGet version resolution
- Template composition (`template_compose`)
- Parameter suggestions (`template_suggest_parameters`)

## [0.1.0-preview.2] - 2026-02-19

### Added
- Intent resolution (`template_from_intent`)
- Template creation from existing projects (`template_create_from_existing`)
- .NET 10 target framework

## [0.1.0-preview.1] - 2026-02-18

### Added
- Initial release with core tools: search, list, inspect, instantiate, dry-run, install, uninstall
- Smart defaults and parameter validation
- Stdio transport
