# Plain LLM vs MCP Tool — Side-by-Side

What happens when you ask an AI agent to create a .NET project **without** the MCP server vs. **with** it.

---

## Scenario 1: Create a Web API with Authentication

### ❌ Plain LLM (no MCP)

**User**: *"Create a web API with authentication and controllers"*

```
LLM thinks:
  "I know dotnet new webapi exists. Auth flag is... --auth? Let me try."

LLM runs:
  dotnet new webapi --name MyApi --auth Individual --use-controllers

Problems:
  1. LLM might hallucinate flags (--use-controllers vs --controllers vs --UseControllers)
  2. No validation until the command fails
  3. If the SDK version is too old, cryptic error: "No templates found matching: 'webapi'"
  4. LLM has to parse stdout/stderr free text to understand what happened
  5. Smart defaults not applied — e.g., HTTPS not forced with auth=Individual
  6. LLM doesn't know about mutual exclusions (UseControllers vs UseMinimalAPIs)
```

**Typical failure loop:**
```
→ dotnet new webapi --auth Individual --use-controllers
  ✗ "Invalid option '--use-controllers'"
→ dotnet new webapi --help                           # LLM reads help text
→ dotnet new webapi --auth Individual --use-controllers true
  ✗ "'true' is not valid for --use-controllers"
→ dotnet new webapi --auth Individual --UseControllers
  ✓ Finally works (3 attempts, 2 failures)
```

### ✅ With MCP

**User**: *"Create a web API with authentication and controllers"*

```
Agent calls: template_from_intent("web API with auth and controllers")
← Returns: { template: "webapi", confidence: 0.85,
              params: { auth: "Individual", UseControllers: "true" } }

Agent calls: template_instantiate(template: "webapi", params: { auth: "Individual", UseControllers: "true" })
← Returns: {
    "status": "Success",
    "filesCreated": ["Program.cs", "Controllers/WeatherController.cs", ...],
    "appliedSmartDefaults": { "UseHttps": "true" },
    "validationWarnings": []
  }
```

**One call to resolve intent, one call to create. Zero failures.**

---

## Scenario 2: Template Not Installed Locally

### ❌ Plain LLM (no MCP)

**User**: *"Create a MAUI Blazor app"*

```
LLM runs:
  dotnet new maui-blazor --name MyApp
  ✗ "No templates found matching: 'maui-blazor'"

LLM thinks:
  "Template isn't installed. I need to find and install it."

LLM runs:
  dotnet new search maui-blazor
  → Shows results (free text, multiple matches)

LLM parses output, picks one:
  dotnet new install Microsoft.Maui.Templates
  → Installs (slow, verbose output)

LLM retries:
  dotnet new maui-blazor --name MyApp
  → Finally works

Total: 3 commands, 1 failure, manual NuGet search parsing.
```

**Edge cases the LLM can't handle:**
- Multiple NuGet packages match — which one is right?
- Package name differs from template short name
- Template requires a specific SDK workload that's missing

### ✅ With MCP

**User**: *"Create a MAUI Blazor app"*

```
Agent calls: template_instantiate(template: "maui-blazor", name: "MyApp")

Server internally:
  1. Template not found locally
  2. Searches NuGet → finds Microsoft.Maui.Templates
  3. Installs the best match
  4. Checks workload constraints → warns if MAUI workload missing
  5. Creates the project

← Returns: {
    "status": "Success",
    "autoResolved": {
      "source": "NuGet",
      "package": "Microsoft.Maui.Templates",
      "version": "9.0.0"
    },
    "constraintResults": [
      { "workload": "maui", "status": "Installed" }
    ]
  }
```

**One call. Auto-resolve, auto-install, constraint check — all handled.**

---

## Scenario 3: CPM-Enabled Solution

### ❌ Plain LLM (no MCP)

**User**: *"Add a class library to my solution"* (solution uses Central Package Management)

```
LLM runs:
  dotnet new classlib --name MyLib --output src/MyLib
  → Creates .csproj with hardcoded versions:

  <PackageReference Include="Newtonsoft.Json" Version="13.0.1" />
  <PackageReference Include="Serilog" Version="2.12.0" />

Problems:
  1. LLM doesn't know Directory.Packages.props exists in the solution
  2. Build fails: "NU1008: Projects that use CPM should not define the version
     on the PackageReference items"
  3. LLM has to:
     a. Understand the error
     b. Find Directory.Packages.props
     c. Remove Version from .csproj
     d. Add <PackageVersion> entries to props file
     e. Hope it doesn't duplicate existing entries
  4. Even if LLM gets it right, versions are stale (from template)
```

**What the LLM typically does:**
```
→ dotnet build
  ✗ NU1008 error
→ LLM reads error, edits .csproj to remove versions
→ dotnet build
  ✗ NU1008 — versions still needed in Directory.Packages.props
→ LLM edits Directory.Packages.props, adds entries
→ dotnet build
  ✓ Works (3 build attempts, 2 manual edits)
```

### ✅ With MCP

**User**: *"Add a class library to my solution"*

```
Agent calls: template_instantiate(template: "classlib", name: "MyLib", output: "src/MyLib")

Server internally:
  1. Creates the project (template engine)
  2. Detects Directory.Packages.props in parent directories
  3. Strips Version attributes from .csproj PackageReferences
  4. Adds <PackageVersion> entries to Directory.Packages.props
  5. Skips entries that already exist in the props file

← Returns: {
    "status": "Success",
    "postCreation": {
      "cpmDetected": true,
      "directoryPackagesPropsPath": "C:\\MySolution\\Directory.Packages.props",
      "processedFiles": [
        {
          "file": "MyLib.csproj",
          "versionsStripped": 2
        }
      ],
      "packagesAdded": [
        { "id": "Newtonsoft.Json", "version": "13.0.4" },
        { "id": "Serilog", "version": "4.3.0" }
      ]
    }
  }
```

**Zero build failures. CPM adapted automatically. Versions upgraded to latest stable.**

---

## Scenario 4: Stale Package Versions

### ❌ Plain LLM (no MCP)

**User**: *"Create a console app with Serilog logging"*

```
LLM generates a .csproj (from training data, possibly months old):

  <PackageReference Include="Serilog" Version="2.12.0" />
  <PackageReference Include="Serilog.Sinks.Console" Version="4.1.0" />

Problems:
  1. LLM's training data is frozen — it doesn't know current versions
  2. Serilog 2.12.0 might be years old (latest is 4.x)
  3. Version combinations might be incompatible
  4. User has to manually check NuGet and update
  5. If user asks "use latest versions", LLM still guesses from training data
```

**What the LLM might suggest vs reality:**

| Package | LLM suggests | Actual latest |
|---------|-------------|--------------|
| Serilog | 2.12.0 | 4.3.0 |
| Serilog.Sinks.Console | 4.1.0 | 6.0.0 |
| Newtonsoft.Json | 13.0.1 | 13.0.4 |
| xunit | 2.4.2 | 2.9.3 |

### ✅ With MCP

**User**: *"Create a console app with Serilog logging"*

```
Agent calls: template_instantiate(
  template: "console",
  name: "MyApp",
  resolveLatestVersions: true    ← default
)

Server internally:
  1. Creates the project
  2. Finds all PackageReference elements
  3. Queries NuGet V3 API for each package
  4. Replaces stale versions with latest stable

← Returns: {
    "postCreation": {
      "versionUpgrades": [
        {
          "package": "Serilog",
          "templateVersion": "2.12.0",
          "latestVersion": "4.3.0"
        },
        {
          "package": "Serilog.Sinks.Console",
          "templateVersion": "4.1.0",
          "latestVersion": "6.0.0"
        }
      ]
    }
  }
```

**Always current. No guessing. Queries live NuGet API at creation time.**

---

## Summary

| Scenario | Plain LLM | With MCP |
|----------|-----------|----------|
| **Create project** | 1-3 attempts, flag guessing, no validation | 1 call, validated, smart defaults |
| **Template missing** | 3 commands, manual NuGet search | 1 call, auto-resolve + install |
| **CPM solution** | Build fails, 2-3 manual edits | Automatic CPM adaptation |
| **Package versions** | Stale (training data) | Live NuGet API, always current |
| **Error handling** | Parse shell stderr | Structured JSON with clear messages |
| **Smart defaults** | Model might forget cross-param rules | Deterministic, code-enforced |
| **Total reliability** | Depends on LLM reasoning quality | Deterministic engine behavior |

---

## The Key Insight

A plain LLM treats `dotnet new` as a **black box** — it guesses flags, runs commands, and reacts to errors. The MCP server **is** the template engine — it validates before acting, adapts to the environment, and returns structured results. The LLM goes from "trial and error" to "ask the expert".
