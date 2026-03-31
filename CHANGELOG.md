# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.0] - 2026-03-31

### Added
- **`template_compare` tool** — compare 2+ templates side by side showing parameters, feature support flags (auth, AOT, Docker, controllers, interactivity), available frameworks, and classifications. Useful when deciding between templates like `webapi` vs `webapp`.
- **Search relevance scoring** — `template_search` now ranks results by relevance (exact short name match → name → classification → description → identity) instead of just concatenating local + NuGet results.
- **Intent synonym expansion** — added 11 new template keywords (.NET Aspire, Azure Functions, Orleans, WinUI, Blazor Web App, Razor components) and 5 new parameter keywords (Blazor interactivity modes). Total: 60 template keywords, 41 parameter keywords, 11 classification keywords.
- **Invalid datatype validation** — `template_validate` now flags unknown datatypes (e.g., `"datatype": "invalid-type"`) against the valid list: `string`, `bool`, `choice`, `int`, `float`, `hex`, `text`.
- **Common parameter combinations** — `docs/tool-reference.md` now includes a quick-reference table of common template + parameter combinations (webapi auth, Blazor interactivity, AOT).
- **Template validation skill documentation** — `docs/skills-equivalent.md` now covers the new `template-validation` Copilot skill from dotnet/skills PR #480 with feasibility analysis.

### Fixed
- **Integer datatype validation** — `template_validate` and `ValidateParameters` used `"integer"` instead of `"int"` for datatype checks, meaning integer parameters were never validated. Fixed in both `TemplateValidateTool.cs` and `TemplateEngineService.cs`.
- **Framework version sorting** — smart defaults picked `net9.0` over `net10.0` due to lexicographic string sort. Now uses numeric version parsing via `ParseFrameworkVersion()`. Fixed in both `TemplateEngineService` and `TemplateEngineFacade`.
- **DataType null safety** — `ValidateParameters` could throw `NullReferenceException` when a template parameter had no `DataType`. Added null checks before all `.Equals()` calls.
- **Tags validation when absent** — `template_validate` skipped tag suggestions when the `tags` field was entirely absent. Now suggests adding language/type tags regardless.
- **ParseParameters silent JSON failure** — `ParseParameters` silently returned an empty dictionary on malformed JSON. Now reports structured parse errors to all callers (instantiate, dry-run, suggest-parameters, facade).
- **Elicitation null-value bypass** — `GetMissingRequiredParameters` treated `{"Framework": null}` as "provided", skipping elicitation. Now checks for null/empty values.
- **Post-processing crash after creation** — `template_instantiate` post-processing (CPM + NuGet versions) was not wrapped in try/catch. Failures after project creation now return structured responses instead of unhandled exceptions.
- **Template install package matching** — `TemplateInstallTool` used `Contains` for package matching, which could match wrong packages. Changed to exact `Equals`. Removed fallback that returned all templates when matching failed.
- **SDK bootstrap race condition** — replaced plain `bool` flag with `SemaphoreSlim` double-check locking. Failures now allow retry on next call instead of permanently suppressing SDK template discovery.
- **CPM stale version updates** — post-processing now updates existing packages in `Directory.Packages.props` to latest version instead of skipping them when they already exist with a stale version.
- **Short name sanitization** — `ToShortName()` now strips filesystem-unsafe and dotnet-new-invalid characters (`/ \ : * ? " < > |` etc.), collapses repeated hyphens, and falls back to `"template"` for empty results.

## [1.2.0] - 2026-03-06

### Added
- **Multi-target net8.0 and net10.0** — broadens adoption to .NET 8 LTS users. The tool package includes both framework builds; the correct one is selected at runtime. Template engine packages use `VersionOverride` (8.0.406 for net8.0, 10.0.103 for net10.0).
- **NuGet version lookup cache** — `NuGetVersionResolver` now caches results in a `ConcurrentDictionary` with 30-minute TTL, eliminating redundant API calls for repeated package lookups within a session.
- **GitHub Releases workflow** — push a `v*` tag to auto-create a GitHub Release with install instructions, auto-generated release notes, and `.nupkg` artifact attached.
- **Dependabot configuration** — automated weekly dependency updates for NuGet packages and GitHub Actions.
- **Enhanced Copilot instructions** — `.github/copilot-instructions.md` now routes `template_validate`, `template_create_from_existing`, `solution_analyze`, and `template_compose` to the appropriate MCP tools.
- **`template_validate` as hero feature** — prominent section in README with example output; enriched tool-reference.md with full example response and usage guidance.

### Fixed
- **`McpFeatureFlags.IsEnabled()` case sensitivity** — replaced explicit 9-casing switch with `StringComparison.OrdinalIgnoreCase`, correctly handling all case variants of `false`, `no`, `off`.
- **`Program.cs` ServerInfo version** — was hardcoded to `1.0.0`, now matches package version.

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
