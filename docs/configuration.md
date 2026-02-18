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
      "args": ["-y", "DotnetTemplateMCP"]
    }
  }
}
```

**Alternative** — `dotnet tool run` (requires local tool install):

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
      "args": ["-y", "DotnetTemplateMCP"]
    }
  }
}
```

## Any MCP client(stdio)

```bash
template-engine-mcp
```

The server communicates over stdin/stdout using the MCP JSON-RPC protocol.

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
| `ENOENT` or "command not found" | `~/.dotnet/tools` not on PATH | Use `dnx -y DotnetTemplateMCP` or full path |
| `spawn template-engine-mcp ENOENT` in VS Code | Same | Switch to `dnx` config shown above |
| `template_search` returns empty | MCP server has its own cache | SDK templates auto-install on first access; use `template_install` for others |
| Copilot uses `dotnet new` instead of MCP tools | No instructions file | Add `.github/copilot-instructions.md` (see above) |
