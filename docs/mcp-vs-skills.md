# MCP Tools vs Copilot Skills — Why MCP for the Template Engine

## TL;DR

MCP tools and Copilot Skills solve different problems. For the template engine, MCP is the right choice because templates need **live engine access** — searching NuGet, reading parameters, validating constraints, writing files. Skills are great for teaching Copilot *how to think*, but they can't *do things* on their own.

---

## What's the difference?

| | MCP Tools | Copilot Skills |
|---|---|---|
| **What they are** | Server-side tools with typed inputs/outputs | Markdown instructions + scripts |
| **Where they live** | Standalone server process (stdio/SSE) | `.github/skills/` or `~/.copilot/skills/` |
| **What they do** | Execute code, call APIs, read/write files | Teach Copilot patterns and workflows |
| **Runtime** | The server runs your code directly | Copilot interprets instructions and generates code |
| **State** | Server holds state (template cache, installed packages) | Stateless — loaded into context per-task |
| **Discovery** | MCP protocol — `tools/list` returns schemas | Copilot loads relevant SKILL.md files |
| **Cross-client** | Any MCP client (VS Code, Claude, Cursor, etc.) | VS Code + Copilot CLI + coding agent only |

## Why MCP wins for the template engine

### 1. Templates need a runtime, not instructions

A skill could say *"to create a web API, run `dotnet new webapi --auth Individual`"* — but it's just text. The model still has to shell out, parse output, handle errors.

An MCP tool **is** the runtime. `template_instantiate` calls the template engine API directly, validates parameters, checks constraints, applies smart defaults, and returns structured JSON. No shell, no parsing, no guessing.

### 2. Parameter validation happens before files are written

Skills can't validate. If a skill tells Copilot to run `dotnet new webapi --framework net3.0`, it'll fail at the shell level with a cryptic error.

MCP tools validate **before** creation:
- Invalid choice? → *"'net3.0' is not valid for Framework. Options: net8.0, net9.0"*
- Bad type? → *"Expected true/false for EnableAot, got 'yes'"*
- Unknown param? → *"Available: Framework, auth, UseControllers..."*

### 3. Auto-resolve from NuGet — one call

Ask a skill-based Copilot to use a template that isn't installed? It'll fail, then suggest `dotnet new install`, then retry. Multiple steps, multiple opportunities for error.

MCP: `template_instantiate("maui-blazor")` → not installed → searches NuGet → installs best match → creates. One call, done.

### 4. Smart defaults require cross-parameter logic

Skills can document rules like *"if you enable AOT, use the latest framework"* — but the model might forget, or apply them inconsistently.

MCP tools enforce them deterministically:
- `EnableAot=true` → auto-suggests latest framework
- `auth=Individual` → keeps HTTPS enabled
- `UseControllers=true` → sets `UseMinimalAPIs=false`

These are code, not suggestions.

### 5. Structured output vs free-text

Skills produce whatever the model generates. MCP tools return typed JSON:

```json
{
  "status": "Success",
  "templateName": "webapi",
  "filesCreated": ["Program.cs", "Controllers/WeatherController.cs", ...],
  "appliedSmartDefaults": { "UseHttps": "true" },
  "validationWarnings": [],
  "constraintResults": []
}
```

The model can reliably extract status, file lists, warnings — no regex on shell output.

### 6. Works with any MCP client, not just Copilot

Skills only work in GitHub Copilot (VS Code, CLI, coding agent). MCP tools work with:
- VS Code / GitHub Copilot
- Claude Desktop
- Cursor
- Any custom MCP client

Build once, works everywhere.

### 7. Template cache and state

The MCP server maintains a **persistent template cache** — installed packages, NuGet search index, template metadata. This means:
- First call to `template_search` is slow (builds cache), subsequent calls are instant
- Installed templates persist across sessions
- SDK templates auto-discover on first use

Skills are stateless — every session starts from scratch.

### 8. Intent resolution without an LLM

The MCP server has a built-in intent resolver with 70+ keyword mappings. *"web API with auth and controllers"* → `webapi` + `auth=Individual` + `UseControllers=true`. This runs locally, offline, deterministically.

A skill would have to rely on the LLM to figure out the mapping every time, and might get it wrong.

---

## Where Skills still make sense

Skills aren't wrong — they complement MCP:

| Use case | Better fit |
|---|---|
| *"Always use xUnit for tests in this repo"* | Skill |
| *"Create a web API with auth"* | MCP tool |
| *"Follow our team's project structure conventions"* | Skill |
| *"Search NuGet for Blazor templates"* | MCP tool |
| *"When creating APIs, always add health checks"* | Skill |
| *"Validate template parameters before creation"* | MCP tool |

**The sweet spot**: Use MCP tools for the *doing* (search, validate, create) and skills for the *policy* (team conventions, post-creation setup, project-specific rules).

---

## Side-by-side: Creating a project

### With a Skill

```
SKILL.md says: "To create a web API, run dotnet new webapi with these flags..."
→ Model reads instructions
→ Model generates shell command
→ Shell runs dotnet new
→ Model parses stdout/stderr (free text)
→ If error → model guesses what went wrong
→ If template missing → model suggests install → retries
```

### With MCP

```
Model calls template_from_intent("web API with auth and controllers")
→ Server returns: webapi, confidence 0.85, params: {auth: Individual, UseControllers: true}
→ Model calls template_instantiate with those params
→ Server validates → applies smart defaults → checks constraints → creates
→ Returns structured JSON: status, files, warnings
```

**Fewer steps, deterministic validation, structured output, no shell parsing.**

---

## Where MCP falls short

### 1. You have to build and ship a server

Skills are a markdown file. MCP tools are a full .NET project — build, pack, publish, version, maintain. That's a real cost. If all you need is *"remind Copilot to use these flags"*, a skill is 10 minutes; an MCP server is weeks.

### 2. Deployment and distribution complexity

Users need to install the tool (`dotnet tool install`, `dnx`, or build from source). Skills just exist in the repo — clone and go. MCP requires NuGet publishing, version management, and users might hit PATH issues, SDK version mismatches, or `ENOENT` errors.

### 3. Server process overhead

MCP runs a separate process that stays alive. It consumes memory (template cache, NuGet search index), takes a few seconds to cold-start, and can crash independently. Skills add zero runtime overhead — they're just text loaded into context.

### 4. Debugging is harder

When a skill misbehaves, you read the SKILL.md and fix the instructions. When an MCP tool misbehaves, you're debugging a .NET server — logs, telemetry, stepping through `ClassificationBasedIntentResolver` scoring logic. The feedback loop is longer.

### 5. Client support is uneven

MCP is well-supported in VS Code, Claude Desktop, and Cursor — but not everywhere. Some clients have quirks (VS Code lazy-connects, Claude needs full paths). Skills work consistently wherever Copilot runs, including the coding agent and GitHub.com.

### 6. Tool selection is probabilistic

Even with good descriptions and copilot-instructions, the model *might* ignore MCP tools and shell out to `dotnet new` anyway. Skills influence the model's reasoning directly (they're in-context instructions). MCP tools are external capabilities the model has to *choose* to use.

### 7. No post-creation orchestration

MCP tools create the project and return. They don't know about your team's conventions — *"always add a health check endpoint"*, *"wire up our internal NuGet feed"*, *"add the standard CI pipeline"*. Skills can encode all of that. MCP would need a separate tool for each post-creation step.

### 8. Versioning and compatibility

The MCP server depends on specific `Microsoft.TemplateEngine.*` NuGet packages. When the template engine ships breaking changes (new parameter types, changed APIs), the MCP server needs an update. Skills reference `dotnet new` CLI, which is always in sync with the SDK.

### 9. Testing surface area

108 tests and counting. Template engine behavior changes across SDK versions, NuGet availability varies, E2E tests need real SDK installs. Skills need no tests — they're instructions, not code.

### 10. Overkill for simple scenarios

If your team just needs *"create a console app with our standard setup"*, a 3-line skill beats a 9-tool MCP server. MCP shines when you need validation, NuGet resolution, cross-parameter logic — but not every team does.

---

## Summary

| Capability | MCP | Skills |
|---|---|---|
| Execute template engine APIs | ✅ Direct | ❌ Via shell |
| Parameter validation | ✅ Pre-creation | ❌ Post-failure |
| Auto-resolve from NuGet | ✅ One call | ❌ Multi-step |
| Smart defaults | ✅ Deterministic | ⚠️ Model-dependent |
| Structured output | ✅ Typed JSON | ❌ Free text |
| Cross-client support | ✅ Any MCP client | ❌ Copilot only |
| Stateful cache | ✅ Persistent | ❌ Stateless |
| Intent resolution | ✅ Offline/local | ❌ LLM-dependent |
| Setup effort | ❌ Build + publish + install | ✅ Drop a markdown file |
| Runtime overhead | ❌ Separate process | ✅ Zero |
| Debugging | ❌ Server logs + telemetry | ✅ Read the markdown |
| Post-creation conventions | ❌ Not its job | ✅ Perfect fit |
| Team policies | ❌ Not its job | ✅ Perfect fit |
| Maintenance burden | ❌ Versioning, NuGet deps, tests | ✅ Minimal |
| Works on GitHub.com | ❌ Needs local server | ✅ Coding agent loads skills |
