# AGENTS.md — AI Agent Instructions for dotnet-template-mcp

## Project Overview

MCP server wrapping the .NET Template Engine for AI-driven template discovery, inspection, and instantiation. Ships as a dotnet tool (`DotnetTemplateMCP`) on NuGet.

## Build & Test

```bash
dotnet build
dotnet test
```

- Targets **net8.0** and **net10.0** (see `global.json` for SDK version).
- Uses **Central Package Management** — all versions in `Directory.Packages.props`.
- CI runs on **ubuntu-latest** and **windows-latest** (see `.github/workflows/ci.yml`).
- When the MCP server is running locally (e.g., as a tool provider), `dotnet build` may fail with a file lock on `bin/Debug/net10.0/Microsoft.TemplateEngine.MCP.exe`. Use `-o <tempdir>` to build to an alternate output path.

## Architecture

### Tool Registration Pattern

Each MCP tool is a `static async` method in a sealed class under `src/Microsoft.TemplateEngine.MCP/Tools/`:

```csharp
[McpServerToolType]
internal sealed class MyTool
{
    [McpServerTool(Name = "tool_name")]
    [Description("Description shown to AI agents — lead with the pain point, not the feature name.")]
    public static async Task<string> MyMethodAsync(
        TemplateEngineService engineService,    // DI-injected service
        McpFeatureFlags featureFlags,           // DI-injected feature flags
        [Description("...")] string param1,     // User-facing params with [Description]
        CancellationToken cancellationToken = default)
    {
        // 1. Telemetry
        using var activity = McpTelemetry.StartToolActivity("tool_name");
        var sw = Stopwatch.StartNew();
        try
        {
            // 2. Profile check (for non-lite tools)
            if (!featureFlags.IsToolEnabled("tool_name"))
            {
                return ToolProfileResponse.DisabledMessage("tool_name", "Hint for the user.");
            }

            // 3. Tool logic
            // ...
        }
        finally
        {
            McpTelemetry.RecordDuration("tool_name", sw.Elapsed.TotalMilliseconds);
        }
    }
}
```

### Key Rules

1. **DI parameters come first** — `TemplateEngineService`, `McpFeatureFlags`, `McpServer` (if elicitation is needed) are injected by the MCP framework. User-facing parameters follow, each with a `[Description]` attribute.

2. **When you add or change a DI parameter on a tool method, you MUST update all test call sites.** Tests in `test/Microsoft.TemplateEngine.MCP.Tests/` call tool methods directly (not through DI), so they must pass all parameters explicitly. Example: `new McpFeatureFlags()` for the default (Full profile).

3. **Tool profiles** — Tools are either "lite" (5 core tools always available) or "full" (all tools). Non-lite tools must include a `featureFlags.IsToolEnabled()` check at the start. The lite tools are: `template_from_intent`, `template_instantiate`, `template_inspect`, `template_search`, `template_dry_run`.

4. **Telemetry** — Every tool must call `McpTelemetry.StartToolActivity()` and `McpTelemetry.RecordDuration()`. Use `McpTelemetry.RecordError()` for failures.

5. **Return format** — Tools return JSON strings via `JsonSerializer.Serialize(new { ... }, new JsonSerializerOptions { WriteIndented = true })`. Errors use `{ error, hint }` shape.

6. **File header** — Every `.cs` file starts with:
   ```csharp
   // Licensed to the .NET Foundation under one or more agreements.
   // The .NET Foundation licenses this file to you under the MIT license.
   ```

## PR Workflow

1. **Always create PRs on a feature branch**, not directly to `main`.
2. **After pushing, monitor the CI run** — check GitHub Actions status. If build or tests fail, fix and push again before considering the PR ready.
3. **Version bumps** require updating three files: `Microsoft.TemplateEngine.MCP.csproj` (`<Version>`), `server.json` (both `version` fields), and `README.md` (install commands).

## Testing

- Unit tests use **xUnit** + **FakeItEasy** for mocking.
- `TemplateEngineService` is mocked via `A.Fake<TemplateEngineService>()` in unit tests.
- Integration tests (in `IntegrationTests.cs`, `EndToEndTests.cs`) use a real template engine instance.
- Test naming: `MethodName_Scenario_ExpectedBehavior`.

## Key Files

| File | Purpose |
|------|---------|
| `src/.../McpFeatureFlags.cs` | Environment-based feature flags (transport, profiles, elicitation) |
| `src/.../Tools/ToolProfileResponse.cs` | Consistent "tool disabled" JSON responses |
| `src/.../Host/TemplateEngineService.cs` | Core service wrapping the template engine |
| `src/.../Telemetry/McpTelemetry.cs` | ActivitySource + Meter for observability |
| `server.json` | MCP Registry manifest |
| `.github/copilot-instructions.md` | Instructions for AI agents *using* this tool (not developing it) |
