# Configuration Guide

## Finding the executable

After installing as a global tool, the executable is at `~/.dotnet/tools/` (`%USERPROFILE%\.dotnet\tools\` on Windows). If it's not on your `PATH`, MCP clients may fail with `ENOENT` — use the full path or `dnx` approach instead.

```bash
# PowerShell
(Get-Command template-engine-mcp).Source

# bash/zsh
which template-engine-mcp
```

## VS Code / GitHub Copilot

Add to `%APPDATA%\Code\User\mcp.json` (Windows) or `.vscode/mcp.json` in your workspace.

**Recommended** — zero-install with `dnx` (.NET 10+):

```json
{
  "servers": {
    "dotnet-templates": {
      "type": "stdio",
      "command": "dnx",
      "args": ["-y", "DotnetTemplateMCP", "--version", "1.4.0"]
    }
  }
}
```

**Alternative** — `dotnet tool run`(requires local tool install):

```json
{
  "servers": {
    "dotnet-templates": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["tool", "run", "template-engine-mcp"]
    }
  }
}
```

**Alternative** — full path to the executable:

```json
{
  "servers": {
    "dotnet-templates": {
      "type": "stdio",
      "command": "C:\\Users\\<username>\\.dotnet\\tools\\template-engine-mcp.exe"
    }
  }
}
```

On macOS/Linux:

```json
{
  "servers": {
    "dotnet-templates": {
      "type": "stdio",
      "command": "/Users/<username>/.dotnet/tools/template-engine-mcp"
    }
  }
}
```

> **Note:** The `"type": "stdio"` field is required. After updating the config, reload VS Code (`Ctrl+Shift+P` → "Developer: Reload Window") and check the **Output** panel → **MCP: dotnet-templates**.

## Claude Desktop

Add to `%APPDATA%\Claude\claude_desktop_config.json` (Windows) or `~/Library/Application Support/Claude/claude_desktop_config.json` (macOS):

```json
{
  "mcpServers": {
    "dotnet-templates": {
      "command": "C:\\Users\\<username>\\.dotnet\\tools\\template-engine-mcp.exe"
    }
  }
}
```

## Cursor

```json
{
  "mcpServers": {
    "dotnet-templates": {
      "command": "dnx",
      "args": ["-y", "DotnetTemplateMCP", "--version", "1.4.0"]
    }
  }
}
```

## Any MCP client (stdio)

```bash
template-engine-mcp
```

The server communicates over stdin/stdout using the MCP JSON-RPC protocol.

## HTTP Transport

For remote, team-shared, or CI/CD deployment, run with HTTP transport:

```bash
template-engine-mcp --transport http
```

Or via environment variable:

```bash
MCP_TEMPLATE_TRANSPORT=http template-engine-mcp
```

The server exposes:
- **`/mcp`** — MCP streamable HTTP endpoint
- **`/health`** — Health check endpoint

Configure the listen URL (default: `http://localhost:5005`):

```bash
MCP_TEMPLATE_HTTP_URL=http://0.0.0.0:8080 template-engine-mcp --transport http
```

Connect your MCP client to the HTTP endpoint:

```json
{
  "servers": {
    "dotnet-templates": {
      "type": "http",
      "url": "http://localhost:5005/mcp"
    }
  }
}
```

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `MCP_TEMPLATE_TRANSPORT` | `stdio` | Transport mode: `stdio` or `http` |
| `MCP_TEMPLATE_HTTP_URL` | `http://localhost:5005` | Listen URL for HTTP transport |
| `MCP_TEMPLATE_HTTP_TOKEN` | _(unset)_ | Bearer token required on `/mcp`. Without it the HTTP transport refuses to start |
| `MCP_TEMPLATE_HTTP_ALLOW_ANONYMOUS` | `false` | Explicitly permit unauthenticated HTTP. Only for a trusted, isolated network. Must be a recognized boolean (`true`/`false`/`1`/`0`/`yes`/`no`/`on`/`off`) — anything else is a startup error rather than a silent opt-out |
| `MCP_TEMPLATE_HTTP_RATE_LIMIT` | `120` | Requests per minute per remote address. `0` disables limiting. `/health` is never throttled |
| `MCP_TEMPLATE_WORKSPACE_ROOT` | process working directory | Root that all file reads and writes must stay inside |
| `MCP_TEMPLATE_WORKSPACE_ENFORCEMENT` | `true` | Set `false` to allow writes to any path (not recommended over HTTP) |
| `MCP_TEMPLATE_POST_ACTIONS` | `true` | Run safe post-actions (restore, add-to-solution) after instantiation |
| `MCP_TEMPLATE_POST_ACTION_TIMEOUT_SECONDS` | `300` | Wall-clock budget for a single post-action process. Raise for large solutions on a cold NuGet cache |
| `MCP_TEMPLATE_RESOLVE_LATEST_VERSIONS` | `false` | Apply latest stable NuGet versions at creation instead of only reporting them |
| `MCP_TEMPLATE_OFFLINE` | `false` | Contact no NuGet feed for version resolution. Package versions are left exactly as the template authored them |
| `MCP_TEMPLATE_CACHE_TTL_SECONDS` | `300` | How long the template list stays memoized. `0` disables caching, so out-of-band `dotnet new install` is picked up immediately |
| `MCP_TEMPLATE_INTENT_RESOLUTION` | `true` | Enable/disable `template_from_intent` tool |
| `MCP_TEMPLATE_ELICITATION` | `true` | Enable/disable interactive parameter elicitation |

## NuGet feeds

Version lookups and package upgrades resolve through the `NuGet.config` chain that applies to the
directory being operated on — the same one `dotnet restore` would use. Private feeds, disabled
sources, `packageSourceMapping`, credential providers and proxies are all honored, and nothing is
hardcoded to `nuget.org`. If every configured source is unreachable, version resolution returns no
result rather than failing the operation.

## Making Copilot prefer MCP tools over `dotnet new`

By default, Copilot might use `dotnet new` directly. To make it use the MCP tools automatically, add a `.github/copilot-instructions.md` to your workspace:

```markdown
When the user asks to create, scaffold, or set up a .NET project, template, or solution:

- Use the `template_from_intent` MCP tool to resolve their description — do NOT use `dotnet new` directly.
- Use `template_inspect` instead of `dotnet new --help`.
- Use `template_dry_run` to preview files before creating.
- Use `template_instantiate` to create projects instead of `dotnet new`.
- Use `template_search` to find templates instead of `dotnet new list`.
```

To apply globally, add the same text to VS Code settings under `github.copilot.chat.codeGeneration.instructions`.

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| `ENOENT` or "command not found" | `~/.dotnet/tools` not on PATH | Use `dnx -y DotnetTemplateMCP --version 1.4.0` or full path |
| `spawn template-engine-mcp ENOENT` in VS Code | Same | Switch to `dnx` config shown above |
| `template_search` returns empty | MCP server has its own cache | SDK templates auto-install on first access; use `template_install` for others |
| Copilot uses `dotnet new` instead of MCP tools | No instructions file | Add `.github/copilot-instructions.md` (see above) |
