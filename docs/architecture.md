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

**NuGet Version Resolution:**
- Resolves feeds from the `NuGet.config` chain that applies to the target directory (`Settings.LoadDefaultSettings`), so private feeds, disabled sources, `packageSourceMapping`, credential providers and proxies are all honored
- Queries each applicable source via `FindPackageByIdResource`, taking the highest stable version
- In CPM mode, versions go into `Directory.Packages.props`; in standalone mode, into the `.csproj`
- Controlled by `resolveLatestVersions`. The default is **report-only**: upgrades are surfaced to the caller and nothing is written. Applying by default produced untested version combinations and overrode the template author's pinning

**Post-Action Execution:**
- `PostActionExecutor` runs the two built-in, non-arbitrary post-actions: restore (`210D431B-…`) and add-to-solution (`D396686C-…`)
- Honors `args.files` for restore and `args.primaryOutputIndexes` for add-to-solution
- Every other action — notably "run script" and "start process" — is reported as skipped with its manual instructions. Those are arbitrary code from a NuGet package, and auto-resolve means a template can be installed without the user ever naming it
- Failures never throw: they are captured in the report so already-created files are still returned. `continueOnError` actions are non-blocking

**Path Confinement:**
- `WorkspaceGuard` validates every caller-supplied write path against `MCP_TEMPLATE_WORKSPACE_ROOT`
- Resolves symlinks (`ResolveLinkTarget(returnFinalTarget: true)`) so a link inside the workspace cannot redirect writes outside it, and uses a trailing-separator root so `C:\work-other` does not match root `C:\work`

**Standalone Package Upgrades:**
- `packages_upgrade` scans an existing `.csproj`, `.sln`/`.slnx`, or directory (independent of template creation) for outdated NuGet versions
- CPM-aware: upgrades `Directory.Packages.props` `PackageVersion` entries for packages actually referenced by the scanned projects; otherwise rewrites inline `PackageReference` versions
- Skips floating (`1.*`), range, and MSBuild-property (`$(Foo)`) versions, and never downgrades
- Report-only by default; pass `apply=true` to write changes (whitespace-preserving XML edits)
- Backed by `PackageUpgradeService` (MCP-free, unit-tested) reusing the shared `NuGetVersionResolver`

**NuGet Version Cache:**
- `NuGetVersionResolver` caches lookups in two tiers: in-memory for the process, and a best-effort, bounded on-disk cache under `LocalApplicationData` that survives restarts
- Cache keys are prefixed with a hash of the resolved feed scope, so the same package id resolving differently under different `NuGet.config` files never leaks across repositories
- Separate TTLs for successes (30 min) and failures (1 min); timeouts are treated as transient failures

**Template List Cache:**
- `GetTemplatesAsync` memoizes the template inventory for the process lifetime behind a double-checked lock, since nearly every tool calls it and the first call sits behind the SDK nupkg scan + install
- Explicitly invalidated by `InvalidateTemplateCache()` on every install and uninstall

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
| `MCP_TEMPLATE_POST_ACTIONS` | `true` | Safe post-action execution (restore, add-to-solution) |
| `MCP_TEMPLATE_RESOLVE_LATEST_VERSIONS` | `false` | Apply version upgrades at creation instead of reporting them |
| `MCP_TEMPLATE_WORKSPACE_ROOT` | working directory | Root that all file writes must stay inside |
| `MCP_TEMPLATE_WORKSPACE_ENFORCEMENT` | `true` | Path confinement |
| `MCP_TEMPLATE_HTTP_TOKEN` | _(unset)_ | Bearer token for the HTTP transport |
| `MCP_TEMPLATE_HTTP_ALLOW_ANONYMOUS` | `false` | Explicit opt-in to unauthenticated HTTP |
| `MCP_TEMPLATE_HTTP_RATE_LIMIT` | `120` | Per-client requests/minute on `/mcp` |

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
