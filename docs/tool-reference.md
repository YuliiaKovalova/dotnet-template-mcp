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

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `templateName` | string | Yes | Template identity or short name |
| `name` | string | No | Project/item name |
| `outputPath` | string | No | Output directory |
| `parametersJson` | string | No | JSON object of parameter values |

Example call:
```json
{
  "templateName": "console",
  "name": "MyApp",
  "parametersJson": "{\"Framework\": \"net8.0\"}"
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

## MCP Prompts

### `create_project`

Guided workflow: search → inspect → suggest params → dry-run → create.

### `create_from_description`

Intent-based workflow: describe what you want → auto-match template + params → preview → create.
