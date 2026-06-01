# Tool Reference

## `template_search`

Search for templates by name, tags, language, or type. Searches locally and on NuGet.org.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `query` | string | Yes | Match against names, short names, tags, descriptions |
| `language` | string | No | e.g., `C#`, `F#`, `VB` |
| `type` | string | No | e.g., `project`, `item`, `solution` |

*Example: "Search for web templates in C#"*

---

## `template_list`

List installed templates with optional filtering.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `language` | string | No | Language filter |
| `type` | string | No | Type filter |
| `classification` | string | No | e.g., `Web`, `Console`, `Library` |

---

## `template_inspect`

Full metadata in one call: parameters (names, types, defaults, choices), constraints, post-actions, classifications. Can also preview templates on NuGet that aren't installed yet.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `templateName` | string | Yes | Template identity or short name |

---

## `template_instantiate`

Create a project or item from a template. **Writes files to disk.** Auto-resolves from NuGet if not installed, validates parameters, checks constraints, applies smart defaults.

**Post-creation intelligence:**
- **CPM (Central Package Management)** — detects `Directory.Packages.props` in the directory tree. If found, strips `Version` attributes from generated `.csproj` PackageReferences and adds `<PackageVersion>` entries to `Directory.Packages.props`.
- **Latest NuGet versions** — queries the NuGet V3 API for the latest stable version of every package reference, replacing the template's hardcoded (often stale) versions. Set `resolveLatestVersions=false` to keep original versions.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `templateName` | string | Yes | Template identity or short name |
| `name` | string | No | Project/item name |
| `outputPath` | string | No | Output directory |
| `parametersJson` | string | No | JSON object of parameter values |
| `resolveLatestVersions` | bool | No | Resolve latest stable NuGet versions (default: true) |

Example call:
```json
{
  "templateName": "webapi",
  "name": "MyApi",
  "parametersJson": "{\"Framework\": \"net9.0\", \"auth\": \"Individual\"}"
}
```

**Common parameter combinations:**

| Template | Parameters | Example |
|----------|-----------|---------|
| `webapi` | `--auth` (None, Individual, SingleOrg, Windows), `--aot` (native AOT) | `dotnet new webapi -n MyApi --auth Individual --aot` |
| `webapi` | `--use-controllers` (use controllers vs minimal APIs) | `dotnet new webapi -n MyApi --use-controllers` |
| `blazor` | `--interactivity` (None, Server, WebAssembly, Auto), `--auth` | `dotnet new blazor -n MyApp --interactivity Server` |
| `grpc` | `--aot` (native AOT) | `dotnet new grpc -n MyService --aot` |
| `worker` | `--aot` (native AOT) | `dotnet new worker -n MyWorker --aot` |

Use `template_inspect` to see all available parameters for any template.

Example response (CPM solution):
```json
{
  "Status": "Success",
  "PostCreation": {
    "CpmDetected": true,
    "DirectoryPackagesPropsPath": "C:\\myrepo\\Directory.Packages.props",
    "VersionUpgrades": [
      { "PackageName": "Swashbuckle.AspNetCore", "OldVersion": "6.6.2", "NewVersion": "7.2.0" }
    ],
    "VersionsStrippedFromCsproj": ["Microsoft.AspNetCore.OpenApi", "Swashbuckle.AspNetCore"],
    "AddedToDirectoryPackagesProps": [
      { "PackageName": "Swashbuckle.AspNetCore", "Version": "7.2.0" }
    ]
  }
}
```

---

## `template_dry_run`

Preview what files would be created **without writing to disk**. Same parameters as `template_instantiate`.

---

## `template_install`

Install a template package from NuGet or local path. Idempotent — skips if same version, reports upgrades.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `packageId` | string | Yes | NuGet package ID or local path |
| `version` | string | No | Package version (latest if omitted) |

---

## `template_uninstall`

Remove an installed template package.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `packageId` | string | Yes | Package identifier to uninstall |

---

## `templates_installed`

Structured listing of all installed templates. No parameters.

---

## `template_from_intent`

Resolve a natural-language description to ranked template matches with pre-filled parameters. Works offline, no LLM needed.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `intent` | string | Yes | Plain English description (e.g., "web API with auth and controllers") |
| `maxResults` | int | No | Max matches to return (default: 5) |

---

## `template_create_from_existing`

Analyze an existing .csproj file and generate a reusable dotnet template that preserves its exact conventions. Solves the 6 gaps between `dotnet new` generic templates and real repo projects: SDK type, analyzer metadata, OutputType, CPM, custom build props, and repo conventions.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `projectPath` | string | Yes | Full path to the .csproj to analyze |
| `templateName` | string | Yes | Human-readable name (e.g., "Repo Unit Test Project") |
| `shortName` | string | No | Short name for `dotnet new <shortname>` |
| `outputPath` | string | No | Where to generate the template (defaults to `../templates/`) |
| `install` | bool | No | If true, installs the template immediately |

Returns: project analysis (SDK, properties, packages with metadata, CPM detection), gaps report (what `dotnet new` would get wrong), generated template path, and next steps.

---

## `template_compose`

Execute a sequence of template operations (project + item templates) in order. For example, create a MAUI app then add specific pages/views. If a template is not installed, it will be auto-resolved from NuGet.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `stepsJson` | string | Yes | JSON array of steps |

Each step object:
| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `templateName` | string | Yes | Template identity or short name |
| `name` | string | No | Project/item name |
| `outputPath` | string | No | Output directory |
| `target` | string | No | Relative path within first step's output |
| `parametersJson` | string | No | JSON object of parameter values |

Example:
```json
[
  {"templateName": "console", "name": "MyApp"},
  {"templateName": "gitignore", "target": "."}
]
```

---

## `template_validate`

Validate a local template directory for authoring issues before publishing. Catches mistakes that would otherwise only surface after `dotnet new install` or during project creation. **No existing tooling provides this level of template.json validation.**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `path` | string | Yes | Path to the template directory (containing `.template.config/template.json`), or direct path to `template.json` |

**Validation checks:**
- Required fields (`identity`, `name`, `shortName`)
- Identity format and namespace conventions
- Short name conflicts with dotnet CLI commands (`build`, `run`, `test`, etc.)
- Parameter issues: missing datatypes, empty choices, invalid defaults, prefix collisions
- Computed/generated symbol completeness (references to undefined symbols)
- Post-action and constraint configuration
- Tag recommendations (language, type)

**Example response:**
```json
{
  "valid": false,
  "templatePath": "/templates/.template.config/template.json",
  "identity": "MyCompany.WebApi",
  "summary": "2 error(s), 1 warning(s), 3 suggestion(s)",
  "errors": [
    "Missing required field 'shortName'.",
    "Parameter 'Framework': default value 'net7.0' is not in the choices list. Valid: net8.0, net9.0, net10.0"
  ],
  "warnings": [
    "Missing 'sourceName'. Without it, --name won't customize the generated project name."
  ],
  "suggestions": [
    "Consider adding a 'description' field to help users understand what this template creates.",
    "Consider adding 'language' tag (e.g., 'C#') for better discoverability.",
    "Consider adding 'type' tag (e.g., 'project', 'item') for filtering."
  ]
}
```

**When to use:** Before running `dotnet new install` on a template you're building, or as part of a CI pipeline for template packages.

---

## `template_compare`

Compare 2 or more templates side by side — parameters, auth support, AOT, framework options, and classifications. Useful when deciding between templates (e.g., `webapi` vs `webapp`, `blazorserver` vs `blazorwasm`).

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `templateNames` | string | Yes | Comma-separated template identities or short names (e.g., `"webapi,webapp"`) |

Returns for each template: identity, parameters with types/defaults/choices, feature support flags (auth, AOT, Docker, controllers, interactivity), available frameworks, and classifications.

---

## `template_suggest_parameters`

Given a template and partial parameter values, suggest reasonable defaults with rationale. Example: `EnableAot=true` → suggests `Framework=net9.0` with explanation.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `templateName` | string | Yes | Template identity or short name |
| `parametersJson` | string | No | JSON object of parameters already chosen |

---

## `packages_upgrade`

Scan a `.csproj`, `.sln`/`.slnx`, or directory for outdated NuGet packages and report (or apply) upgrades to the latest stable version. CPM-aware: when a `Directory.Packages.props` is found, it reads and updates the `PackageVersion` entries there; otherwise it updates inline `PackageReference` versions. Floating versions (`1.*`), version ranges, and MSBuild-property versions (`$(Foo)`) are left untouched, and it never downgrades.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `path` | string | No | Path to a `.csproj`, `.sln`/`.slnx`, or directory. Defaults to the current directory |
| `apply` | bool | No | When `true`, writes upgrades to disk. Defaults to `false` (report only) |

---

## MCP Prompts

### `create_project`

Guided workflow: search → inspect → suggest params → dry-run → create.

### `create_from_description`

Intent-based workflow: describe what you want → auto-match template + params → preview → create.
