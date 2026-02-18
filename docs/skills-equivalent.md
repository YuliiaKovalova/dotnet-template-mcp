# Copilot Skills Equivalent for Template Engine MCP

What would it take to cover the same functionality with Copilot Skills instead of MCP tools? Here's a skill-by-skill breakdown — what works, what's awkward, and what's simply not possible.

---

## Skill 1: Template Search

**SKILL.md** — Tell Copilot how to search for templates.

```markdown
# template-search

When the user asks to find or search for .NET templates:

1. Run `dotnet new search <query>` to search NuGet
2. Run `dotnet new list <query>` to search locally
3. Combine results, showing local matches first
4. For each result, show: name, short name, language, type, author
```

**What you lose:**
- No unified ranking — two separate commands, model merges results ad-hoc
- No structured output — model parses CLI table output (fragile)
- No language/type filtering in one call

**Feasibility:** ⚠️ Works but clunky

---

## Skill 2: Template List

**SKILL.md:**

```markdown
# template-list

When the user asks to list installed templates:

1. Run `dotnet new list` with optional `--language`, `--type` flags
2. Parse the table output and present results
```

**What you lose:**
- CLI output is a text table — model has to parse columns
- No classification filter in `dotnet new list`

**Feasibility:** ✅ Mostly works

---

## Skill 3: Template Inspect

**SKILL.md:**

```markdown
# template-inspect

When the user asks about a template's parameters or details:

1. Run `dotnet new <shortname> --help` to get parameter list
2. Parse the output to extract parameter names, types, defaults, and choices
3. Present in a structured format
```

**What you lose:**
- No constraint details (OS, SDK version, workload)
- No post-action listing
- No NuGet preview (can't inspect uninstalled templates)
- `--help` output format varies between template types
- No baseline or classification metadata

**Feasibility:** ⚠️ Partial — misses constraints, post-actions, NuGet preview

---

## Skill 4: Template Instantiate

**SKILL.md:**

```markdown
# template-create

When the user asks to create a .NET project:

1. Determine the template short name
2. Run `dotnet new <shortname> --name <name> --output <path> [params...]`
3. If the template is not found, run `dotnet new install <package>` first, then retry
4. Report created files
```

**What you lose:**
- ❌ No parameter validation before creation — errors come from `dotnet new` stderr
- ❌ No smart defaults — model doesn't know AOT→framework or auth→HTTPS rules
- ❌ No auto-resolve — model has to guess the NuGet package name
- ❌ No constraint checking — fails at creation time instead of warning beforehand
- ❌ No structured output — parses "The template was created successfully" text
- ❌ No idempotent install check

**Feasibility:** ❌ Major gaps — this is where MCP provides the most value

---

## Skill 5: Template Dry Run

**SKILL.md:**

```markdown
# template-preview

When the user asks to preview a template before creating:

1. Run `dotnet new <shortname> --dry-run --name <name> [params...]`
2. Parse the output to show file list
```

**What you lose:**
- No parameter validation before dry-run
- No smart defaults applied
- No constraint checking
- Output format is inconsistent across SDK versions

**Feasibility:** ⚠️ Basic version works, no validation layer

---

## Skill 6: Template Install

**SKILL.md:**

```markdown
# template-install

When the user asks to install a template package:

1. Check if already installed: `dotnet new list` and look for the package
2. If not installed, run `dotnet new install <packageId> [--version <ver>]`
3. Report installed templates
```

**What you lose:**
- ❌ No idempotent version checking (already installed at same version → skip)
- ❌ No upgrade detection (older version installed → suggest upgrade)
- Model has to parse `dotnet new list` output to check existing installs

**Feasibility:** ⚠️ Works but not idempotent

---

## Skill 7: Template Uninstall

**SKILL.md:**

```markdown
# template-uninstall

When the user asks to remove a template package:

1. Run `dotnet new uninstall <packageId>`
2. If package not found, show list of installed packages
```

**What you lose:** Not much — this one maps cleanly.

**Feasibility:** ✅ Works fine

---

## Skill 8: Templates Installed

**SKILL.md:**

```markdown
# templates-inventory

When the user asks what templates are installed:

1. Run `dotnet new list`
2. Parse the table and present as a structured list with counts
```

**What you lose:**
- No parameter/constraint/post-action counts per template
- Text table parsing

**Feasibility:** ✅ Works, less metadata

---

## Skill 9: Intent Resolution

**SKILL.md:**

```markdown
# template-from-description

When the user describes a project in natural language:

1. Map their description to a template:
   - "web API" / "API" / "REST" → webapi
   - "console" / "command line" / "CLI" → console
   - "Blazor" / "Blazor app" → blazor / blazorserver / blazorwasm
   - "class library" / "library" / "lib" → classlib
   - "MAUI" / "cross-platform" / "mobile" → maui
   - "WPF" / "Windows desktop" → wpf
   - "Worker" / "background service" → worker
   - "gRPC" → grpc
   - "MVC" / "web app with views" → mvc
   - "Razor Pages" / "web pages" → webapp

2. Map keywords to parameters:
   - "authentication" / "auth" / "identity" → --auth Individual
   - "controllers" / "with controllers" → --use-controllers
   - "minimal API" → (default, no --use-controllers)
   - "AOT" / "native AOT" → --aot
   - "Docker" / "container" → --enable-docker
   - ".NET 8" / "net8" → --framework net8.0
   - ".NET 9" / "net9" → --framework net9.0

3. Run the appropriate `dotnet new` command with mapped parameters
```

**What you lose:**
- ❌ No confidence scoring — model picks one, no ranked alternatives
- ❌ No "did you mean...?" with candidates
- ❌ No pre-filled parameter validation against actual template choices
- ❌ Keyword list is static in markdown — can't adapt to newly installed templates
- ❌ Model might ignore the mapping and use its own knowledge
- ❌ No telemetry on intent resolution quality

**Feasibility:** ❌ Superficially works, but brittle and non-adaptive

---

## Skill 10: Smart Defaults

**SKILL.md:**

```markdown
# template-smart-defaults

When creating .NET projects, apply these rules:

- If `--aot` or `--publish-aot` is used, also set `--framework` to the latest AOT-compatible version
- If `--auth` is set to anything other than None, make sure `--no-https` is NOT used
- If `--use-controllers` is set, do NOT also pass `--use-minimal-apis`
- Do not apply a rule if the user explicitly set a conflicting value
```

**What you lose:**
- ❌ Model might forget or misapply rules
- ❌ No tracking of which defaults were applied
- ❌ Rules can't reference actual template parameter definitions
- ❌ New cross-parameter rules require updating the markdown

**Feasibility:** ❌ Unreliable — rules in text are suggestions, not enforcement

---

## Skill 11: Parameter Validation

Not possible as a skill. Validation requires:
- Knowing every template's parameter schema (types, choices, defaults)
- Checking user input against those schemas before creation
- Returning specific error messages with valid options

A skill can say *"validate parameters before creating"* but the model would have to run `dotnet new <template> --help`, parse the output, cross-reference each parameter — and it probably won't do this reliably.

**Feasibility:** ❌ Not practical

---

## Skill 12: Constraint Checking

Not possible as a skill. Constraints are embedded in `template.json` metadata — OS requirements, SDK version minimums, workload dependencies. The CLI doesn't expose these in a parseable way before creation.

**Feasibility:** ❌ Not possible

---

## Skill 13: SDK Template Auto-Discovery

Not needed as a skill — `dotnet new` already knows about SDK templates. But MCP's auto-discovery scans `{dotnet_root}/templates/` and pre-installs them into the MCP host's cache, which is a different host from the CLI.

**Feasibility:** N/A — not applicable to skills

---

## Scorecard

| Capability | Skill feasibility | What's lost |
|---|---|---|
| Template search | ⚠️ Clunky | No unified ranking, text parsing |
| Template list | ✅ Works | No classification filter |
| Template inspect | ⚠️ Partial | No constraints, post-actions, NuGet preview |
| Template instantiate | ❌ Major gaps | No validation, smart defaults, auto-resolve |
| Template dry-run | ⚠️ Basic | No validation layer |
| Template install | ⚠️ Works-ish | Not idempotent |
| Template uninstall | ✅ Works | — |
| Templates inventory | ✅ Works | Less metadata |
| Intent resolution | ❌ Brittle | No scoring, no adaptation, no candidates |
| Smart defaults | ❌ Unreliable | Text rules ≠ enforcement |
| Parameter validation | ❌ Not practical | Can't read template schemas |
| Constraint checking | ❌ Not possible | Metadata not exposed by CLI |

## Bottom line

**5 out of 12 capabilities** work reasonably as skills. The rest either degrade significantly or aren't possible. The gap is widest where the MCP server uses the template engine API directly — validation, smart defaults, constraints, and intent resolution all require programmatic access to template metadata that `dotnet new` CLI doesn't expose in a structured way.

Skills are great for the *"how we do things here"* layer on top — team conventions, post-creation setup, coding standards. But they can't replace the engine-level intelligence that MCP provides.
