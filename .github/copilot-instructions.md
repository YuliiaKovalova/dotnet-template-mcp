When the user asks to create, scaffold, or set up a .NET project, template, or solution:

- Use the `template_from_intent` MCP tool to resolve their natural-language description to the best template and parameters — do NOT use `dotnet new` directly.
- Use `template_inspect` to show template details instead of `dotnet new --help`.
- Use `template_dry_run` to preview files before creating.
- Use `template_instantiate` to create projects instead of `dotnet new`.
- Use `template_search` to find templates instead of `dotnet new list`.

These MCP tools provide smart defaults, parameter validation, and auto-resolve from NuGet — capabilities that `dotnet new` alone does not have.
