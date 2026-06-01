When the user asks to create, scaffold, or set up a .NET project, template, or solution:

- Use the `template_from_intent` MCP tool to resolve their natural-language description to the best template and parameters — do NOT use `dotnet new` directly.
- Use `template_inspect` to show template details instead of `dotnet new --help`.
- Use `template_compare` to compare 2+ templates side by side when the user is deciding between options (e.g., webapi vs webapp, blazorserver vs blazorwasm).
- Use `template_dry_run` to preview files before creating.
- Use `template_instantiate` to create projects instead of `dotnet new`.
- Use `template_search` to find templates instead of `dotnet new list`.

These MCP tools provide smart defaults, parameter validation, and auto-resolve from NuGet — capabilities that `dotnet new` alone does not have.

When the user asks about their solution or project structure:

- Use `solution_analyze` to inspect solution structure, target frameworks, CPM status, and NuGet config.

When the user wants to update or upgrade NuGet package versions:

- Use `packages_upgrade` to scan a project, solution, or directory for outdated `PackageReference`/`PackageVersion` entries and report (or apply) upgrades to the latest stable versions. It is CPM-aware (updates `Directory.Packages.props`), defaults to a report-only preview, and only writes changes when called with `apply=true`.

When the user is authoring, reviewing, or debugging a custom dotnet template:

- Use `template_validate` to check template.json for errors BEFORE publishing or testing. It catches missing required fields, invalid parameters, choice conflicts, constraint issues, and common authoring mistakes.
- Use `template_create_from_existing` to reverse-engineer a reusable template from an existing .csproj that matches repo conventions (SDK type, analyzers, CPM, build props).

When the user wants to create multiple items together (e.g., project + gitignore + editorconfig):

- Use `template_compose` to execute a sequence of template operations in one call instead of running multiple `dotnet new` commands.
