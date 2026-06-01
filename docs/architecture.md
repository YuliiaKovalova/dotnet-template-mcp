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

### Post-Creation Processing (Phase 3)
After template instantiation, the server automatically adapts the output to the target environment:

**CPM (Central Package Management):**
- Walks up the directory tree to find `Directory.Packages.props`
- Strips `Version` attributes from generated `.csproj` PackageReferences
- Adds missing `<PackageVersion>` entries to `Directory.Packages.props`
- Existing entries in the props file are preserved (not duplicated)

**Latest NuGet Versions:**
- Queries the NuGet V3 flat-container API for every package reference
- Replaces template-hardcoded versions with the latest stable release
- In CPM mode, latest versions go into `Directory.Packages.props`
- In standalone mode, versions are updated directly in `.csproj`
- Controlled via `resolveLatestVersions` parameter (default: `true`)

**Standalone Package Upgrades:**
- `packages_upgrade` scans an existing `.csproj`, `.sln`/`.slnx`, or directory (independent of template creation) for outdated NuGet versions
- CPM-aware: upgrades `Directory.Packages.props` `PackageVersion` entries for packages actually referenced by the scanned projects; otherwise rewrites inline `PackageReference` versions
- Skips floating (`1.*`), range, and MSBuild-property (`$(Foo)`) versions, and never downgrades
- Report-only by default; pass `apply=true` to write changes (whitespace-preserving XML edits)
- Backed by `PackageUpgradeService` (MCP-free, unit-tested) reusing the shared `NuGetVersionResolver`

**NuGet Version Cache:**
- `NuGetVersionResolver` caches lookups in two tiers: in-memory for the process, and a best-effort, bounded on-disk cache under `LocalApplicationData` that survives restarts
- Separate TTLs for successes (30 min) and failures (1 min); HTTP timeouts are treated as transient failures

**Multi-Template Composition:**
- `template_compose` executes a sequence of template operations in order
- First step creates the project; subsequent steps add items relative to it
- Each step supports auto-resolve, validation, and smart defaults independently

**Parameter Suggestions with Rationale:**
- `template_suggest_parameters` returns suggestions with human-readable explanations
- Example: `EnableAot=true` → "NativeAOT works best with the latest framework version"
- Covers AOT, authentication, Docker, controllers, and other cross-parameter relationships

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
│   ├── Host/                             # Template engine host + service + facade
│   ├── Intent/                           # Phase 2: intent resolution
│   ├── PostCreation/                     # Phase 3: CPM adaptation + NuGet version resolution + package upgrades
│   ├── Prompts/                          # MCP prompts
│   ├── Telemetry/                        # ActivitySource + Meter
│   ├── Tools/                            # MCP tools (15 tools)
│   ├── McpFeatureFlags.cs                # Feature toggles
│   └── Program.cs                        # Entry point
├── test/Microsoft.TemplateEngine.MCP.Tests/  # 207 tests
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
