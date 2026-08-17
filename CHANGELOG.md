# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [2.0.0] - 2026-08-17

Major version because several defaults changed in ways that alter existing behavior. The
NuGet package id (`DotnetTemplateMCP`) and the tool command (`template-engine-mcp`) are
**unchanged**, so installing and launching the server works exactly as before.

### Breaking

- **The HTTP transport refuses to start without a bearer token.** Set `MCP_TEMPLATE_HTTP_TOKEN`,
  or pass `MCP_TEMPLATE_HTTP_ALLOW_ANONYMOUS=true` to explicitly accept an unauthenticated
  endpoint. Previously `MapMcp()` was exposed with no auth at all, which — combined with
  unvalidated output paths and NuGet template install — was a remote arbitrary-write surface.
  **stdio is unaffected**, which is how most users run this.
- **`template_instantiate` now executes the template's restore and add-to-solution post-actions.**
  Creating a project now shells out to `dotnet restore`, which takes longer and touches the
  network. This is what `dotnet new` already did. Opt out per call with `runPostActions=false`
  or globally with `MCP_TEMPLATE_POST_ACTIONS=false`.
- **`resolveLatestVersions` defaults to report-only** (was: apply). Upgrades that would previously
  have been written to disk are now returned as `AvailableVersionUpgrades` instead of
  `VersionUpgrades`. **Consumers parsing that response field must update.** Restore the old
  behavior with `resolveLatestVersions=true` or `MCP_TEMPLATE_RESOLVE_LATEST_VERSIONS=true`.
- **Write paths are confined to a workspace root.** Absolute `outputPath` values outside the
  process working directory are now rejected with `errorCode: path_outside_workspace`. Widen with
  `MCP_TEMPLATE_WORKSPACE_ROOT`, or disable with `MCP_TEMPLATE_WORKSPACE_ENFORCEMENT=false`.
- **The assembly and root namespace are now `DotnetTemplateMcp`** (was `Microsoft.TemplateEngine.MCP`).
  Source-level only — it affects code referencing the namespace or building the project by path,
  not anyone installing or invoking the tool.

### Added
- **Post-action execution** — `template_instantiate` now runs the template's built-in restore (`dotnet restore`) and add-to-solution (`dotnet sln add`) post-actions, so created projects are restored and added to the solution instead of merely being described. Previously these were serialized as metadata and never executed, which made the tool strictly less capable than the `dotnet new` it told agents to prefer it over. Template-supplied script and process-start actions are **never** auto-executed — they are reported as skipped with their manual instructions. Controlled by `runPostActions` and `MCP_TEMPLATE_POST_ACTIONS`.
- **`NuGet.config` support** — version resolution and `packages_upgrade` now resolve feeds through the `NuGet.config` chain that applies to the target directory, honoring private feeds, disabled sources, `packageSourceMapping`, credential providers and proxies. Previously `https://api.nuget.org/v3-flatcontainer/` was hardcoded, which silently wrote public nuget.org versions into repositories whose policy is an internal feed.
- **Path confinement** — all caller-supplied write paths are validated against a workspace root (`MCP_TEMPLATE_WORKSPACE_ROOT`, default: the process working directory), with symlink resolution. Rejections return `errorCode: path_outside_workspace`. Disable with `MCP_TEMPLATE_WORKSPACE_ENFORCEMENT=false`.
- **HTTP authentication and rate limiting** — the `/mcp` endpoint now requires a bearer token (`MCP_TEMPLATE_HTTP_TOKEN`) compared in constant time, and is rate limited per client (`MCP_TEMPLATE_HTTP_RATE_LIMIT`, default 120/min). The server **refuses to start** the HTTP transport unless a token is set or `MCP_TEMPLATE_HTTP_ALLOW_ANONYMOUS=true` is passed explicitly. `/health` remains anonymous.
- **Template list caching** — `GetTemplatesAsync` is memoized for the process lifetime behind a double-checked lock and invalidated on install/uninstall. Nearly every tool calls it, and the first call sits behind the SDK nupkg scan.

### Changed
- **`resolveLatestVersions` now defaults to report-only** (was: apply). Rewriting every `PackageReference` to "latest stable" at creation produced untested version combinations and overrode the template author's deliberate pinning. Available upgrades are returned as `AvailableVersionUpgrades`; pass `resolveLatestVersions=true` or set `MCP_TEMPLATE_RESOLVE_LATEST_VERSIONS=true` to apply them.
- **Removed the "multi-tenant" claim** from the README and HTTP transport docs. `TemplateEngineService` is a process-wide singleton with `virtualizeSettings: false` and the workspace root is process-wide, so concurrent tenants would share template install state and a working directory. Documented as one instance per trusted team.
- **NuGet version cache keys** are now prefixed with a hash of the resolved feed scope, so a package id that resolves differently under different `NuGet.config` files cannot leak across repositories.
- **Renamed off the `Microsoft.*` namespace.** The assembly, root namespace, project directories and solution file were `Microsoft.TemplateEngine.MCP`, and every source file carried a `Licensed to the .NET Foundation` header — false provenance for a personal-account project, and a plausible trademark problem. These are now `DotnetTemplateMcp` with a plain MIT header. **The NuGet package id (`DotnetTemplateMCP`) and the tool command (`template-engine-mcp`) are unchanged**, so installs, `dnx` invocations and MCP client configurations continue to work untouched. Only code that referenced the `Microsoft.TemplateEngine.MCP` namespace or built the project by path is affected.
- **Retracted the "no existing tooling does this" claim** about `template_validate`. Microsoft ships `Microsoft.TemplateEngine.Authoring.CLI` (`dotnet template-authoring validate`), `Authoring.Tasks` for build-time validation and `TemplateVerifier` for snapshot testing. The docs now name them and position `template_validate` on what it actually adds: agent-consumable structured output, not more thorough validation.

### Fixed
- **`dotnet sln add` could target a solution outside the workspace root.** The add-to-solution post-action walked from the output directory to the filesystem root, so creating a project inside the workspace could modify a solution belonging to an unrelated repository above it. The walk is now bounded by the workspace root when enforcement is enabled.
- **The version reported to MCP clients was hardcoded** and had gone stale, so clients were told a version the server was not. It is now read from the assembly and cannot drift.

### Infrastructure
- **The release workflow now publishes to the MCP Registry.** The registry entry was stuck at v1.0.1 while the repo shipped 1.4.0, so anyone discovering the server through the official registry installed a five-release-old build. Releases now sync `server.json`, wait for nuget.org to index the package (registry ownership validation reads the published package README), and publish via `mcp-publisher` with GitHub OIDC. Also runnable standalone via `workflow_dispatch`.

## [1.4.0] - 2026-06-01

### Added
- **`packages_upgrade` tool** — scan a `.csproj`, `.sln`/`.slnx`, or directory for outdated NuGet packages and report (default) or apply (`apply=true`) upgrades to the latest stable version. CPM-aware: updates `Directory.Packages.props` `PackageVersion` entries for packages actually referenced by the scanned projects, otherwise rewrites inline `PackageReference` versions. Skips floating/range/MSBuild-property versions and never downgrades. Brings the tool count to 15.
- **Persistent NuGet version cache** — `NuGetVersionResolver` now keeps a best-effort, bounded on-disk cache (one JSON file per package under `LocalApplicationData`, atomic temp+move writes, pruned to 1000 entries) that survives restarts, plus a `User-Agent` header and separate success (30 min) vs failure (1 min) cache TTLs. HTTP timeouts are treated as transient failures rather than caller cancellation.

### Fixed
- **Framework version sorting (auth path)** — `TemplateEngineFacade` parameter suggestions sorted frameworks lexicographically, picking `net9.0` over `net10.0`. Now uses numeric version parsing.
- **Choice symbol generation** — `template_create_from_existing` now emits a `choices` array for choice parameters and de-duplicates `replaces` values and property names.
- **NuGet latest-version selection** — `NuGetVersionResolver` now picks the highest stable version via SemVer ordering instead of trusting array order, and never registers a downgrade.
- **SDK install caching** — only caches success when all installs succeed; disposes the bootstrap semaphore; null-guards `lang`/`templateType` tags during NuGet search.
- **Tool robustness** — added null guards before `.Contains` on tag values, SemVer-based version equality in `template_install`, error telemetry on `template_dry_run`/`template_validate`/`template_create_from_existing` failure paths, a crash guard in `template_validate`, and XML-based (not substring) CPM detection in `solution_analyze`.

### Changed
- **Intent scoring** — `ClassificationBasedIntentResolver` now matches template name/identity/description against extracted keywords instead of the full intent sentence, improving ranking precision.

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
