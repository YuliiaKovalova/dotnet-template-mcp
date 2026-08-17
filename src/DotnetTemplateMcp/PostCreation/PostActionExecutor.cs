// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using ITemplateCreationResult = Microsoft.TemplateEngine.Edge.Template.ITemplateCreationResult;

namespace DotnetTemplateMcp.PostCreation;

/// <summary>
/// Executes the safe, well-known post-actions a template declares.
///
/// The template engine only *reports* post-actions; running them is the responsibility of the host.
/// <c>dotnet new</c> does this, which is why a project it creates is restored and registered in the
/// surrounding solution. Previously this server serialized the post-action metadata and stopped
/// there, so <c>template_instantiate</c> left an unrestored project that was not added to the
/// <c>.sln</c> — strictly less capable than the CLI it tells agents to prefer it over.
///
/// Only two actions are executed, both non-arbitrary:
/// restore (<c>dotnet restore</c>) and add-to-solution (<c>dotnet sln add</c>).
/// Template-supplied scripts and process-start actions are deliberately NOT run: they are arbitrary
/// code from a NuGet package, and auto-resolve means a template can be installed without the user
/// ever naming it. Those are reported with their manual instructions instead.
/// </summary>
internal sealed class PostActionExecutor
{
    /// <summary>Restore NuGet packages. Optional <c>args.files</c> selects specific projects.</summary>
    internal static readonly Guid RestoreNuGetPackagesActionId = new("210D431B-A78B-4D2F-B762-4ED3E3EA9025");

    /// <summary>Add project(s) to a solution file. <c>args.primaryOutputIndexes</c> selects which.</summary>
    internal static readonly Guid AddProjectToSolutionActionId = new("D396686C-DE0E-4DE6-906D-291CD29FC5DE");

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    private readonly ILogger _logger;
    private readonly Func<string, string, string, CancellationToken, Task<ProcessRunResult>> _runProcess;

    public PostActionExecutor(ILoggerFactory loggerFactory)
        : this(loggerFactory.CreateLogger<PostActionExecutor>(), null)
    {
    }

    /// <summary>Test seam: inject a fake process runner so tests never shell out.</summary>
    internal PostActionExecutor(
        ILogger logger,
        Func<string, string, string, CancellationToken, Task<ProcessRunResult>>? runProcess)
    {
        _logger = logger;
        _runProcess = runProcess ?? RunProcessAsync;
    }

    /// <summary>
    /// Runs the supported post-actions declared by the template.
    /// Never throws: failures are captured in the returned report so the caller can still report
    /// the (already created) files.
    /// </summary>
    public async Task<PostActionExecutionReport> ExecuteAsync(
        ITemplateCreationResult creationResult,
        string outputDirectory,
        string? boundaryDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var report = new PostActionExecutionReport();

        var postActions = creationResult.CreationResult?.PostActions;
        if (postActions == null || postActions.Count == 0)
        {
            return report;
        }

        var primaryOutputs = creationResult.CreationResult?.PrimaryOutputs
            ?.Select(p => p.Path)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList() ?? new List<string>();

        foreach (var postAction in postActions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (postAction.ActionId == RestoreNuGetPackagesActionId)
                {
                    report.Executed.Add(await RunRestoreAsync(
                        postAction, primaryOutputs, outputDirectory, cancellationToken).ConfigureAwait(false));
                }
                else if (postAction.ActionId == AddProjectToSolutionActionId)
                {
                    report.Executed.Add(await RunAddToSolutionAsync(
                        postAction, primaryOutputs, outputDirectory, boundaryDirectory, cancellationToken).ConfigureAwait(false));
                }
                else
                {
                    report.Skipped.Add(new SkippedPostAction(
                        postAction.ActionId.ToString(),
                        postAction.Description,
                        postAction.ManualInstructions,
                        "Not a built-in safe action. Template-supplied scripts are never executed automatically."));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Post-action {ActionId} failed.", postAction.ActionId);
                report.Executed.Add(new ExecutedPostAction(
                    postAction.ActionId.ToString(),
                    postAction.Description,
                    Command: null,
                    Success: false,
                    ContinueOnError: postAction.ContinueOnError,
                    Output: null,
                    Error: ex.Message,
                    ManualInstructions: postAction.ManualInstructions));
            }
        }

        return report;
    }

    private async Task<ExecutedPostAction> RunRestoreAsync(
        IPostAction postAction,
        IReadOnlyList<string> primaryOutputs,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        // args.files selects specific projects; when absent, restore the template's primary outputs.
        var targets = ResolveTargets(GetArg(postAction, "files"), primaryOutputs, outputDirectory)
            .Where(IsRestorableProject)
            .ToList();

        // Nothing identifiable — fall back to restoring the output directory itself, which is what
        // a user running `dotnet restore` in the new folder would get.
        var arguments = targets.Count > 0
            ? string.Join(" ", targets.Select(t => Quote(t)))
            : string.Empty;

        var command = $"dotnet restore {arguments}".TrimEnd();
        var run = await _runProcess("dotnet", $"restore {arguments}".TrimEnd(), outputDirectory, cancellationToken)
            .ConfigureAwait(false);

        LogResult(postAction, command, run);

        return new ExecutedPostAction(
            postAction.ActionId.ToString(),
            postAction.Description ?? "Restore NuGet packages",
            command,
            run.ExitCode == 0,
            postAction.ContinueOnError,
            Truncate(run.StandardOutput),
            run.ExitCode == 0 ? null : Truncate(run.StandardError),
            run.ExitCode == 0 ? null : postAction.ManualInstructions);
    }

    private async Task<ExecutedPostAction> RunAddToSolutionAsync(
        IPostAction postAction,
        IReadOnlyList<string> primaryOutputs,
        string outputDirectory,
        string? boundaryDirectory,
        CancellationToken cancellationToken)
    {
        var solutionPath = FindSolution(outputDirectory, boundaryDirectory);
        if (solutionPath == null)
        {
            return new ExecutedPostAction(
                postAction.ActionId.ToString(),
                postAction.Description ?? "Add project to solution",
                Command: null,
                Success: false,
                ContinueOnError: true,
                Output: null,
                Error: boundaryDirectory == null
                    ? "No .sln or .slnx file found in the output directory or any parent directory."
                    : $"No .sln or .slnx file found between the output directory and the workspace root '{boundaryDirectory}'.",
                ManualInstructions: postAction.ManualInstructions);
        }

        var projects = SelectProjectsByIndex(postAction, primaryOutputs, outputDirectory);
        if (projects.Count == 0)
        {
            return new ExecutedPostAction(
                postAction.ActionId.ToString(),
                postAction.Description ?? "Add project to solution",
                Command: null,
                Success: false,
                ContinueOnError: true,
                Output: null,
                Error: "The template did not produce any project file to add to the solution.",
                ManualInstructions: postAction.ManualInstructions);
        }

        var projectArgs = string.Join(" ", projects.Select(Quote));
        var arguments = $"sln {Quote(solutionPath)} add {projectArgs}";
        var command = $"dotnet {arguments}";

        var run = await _runProcess("dotnet", arguments, outputDirectory, cancellationToken).ConfigureAwait(false);

        LogResult(postAction, command, run);

        return new ExecutedPostAction(
            postAction.ActionId.ToString(),
            postAction.Description ?? "Add project to solution",
            command,
            run.ExitCode == 0,
            postAction.ContinueOnError,
            Truncate(run.StandardOutput),
            run.ExitCode == 0 ? null : Truncate(run.StandardError),
            run.ExitCode == 0 ? null : postAction.ManualInstructions);
    }

    /// <summary>
    /// Resolves a post-action file argument (semicolon-delimited relative paths) to absolute paths,
    /// falling back to the template's primary outputs when the argument is absent.
    /// </summary>
    internal static List<string> ResolveTargets(
        string? filesArg,
        IReadOnlyList<string> primaryOutputs,
        string outputDirectory)
    {
        var relative = string.IsNullOrWhiteSpace(filesArg)
            ? primaryOutputs
            : filesArg.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var result = new List<string>();
        foreach (var item in relative)
        {
            var full = Path.IsPathRooted(item) ? item : Path.GetFullPath(Path.Combine(outputDirectory, item));
            if (File.Exists(full))
            {
                result.Add(full);
            }
        }

        return result;
    }

    /// <summary>
    /// Applies the <c>primaryOutputIndexes</c> argument, a semicolon-delimited list of indexes into
    /// the template's primary outputs. When absent, every project-like primary output is used.
    /// </summary>
    internal static List<string> SelectProjectsByIndex(
        IPostAction postAction,
        IReadOnlyList<string> primaryOutputs,
        string outputDirectory)
    {
        var indexesArg = GetArg(postAction, "primaryOutputIndexes");
        IEnumerable<string> selected;

        if (string.IsNullOrWhiteSpace(indexesArg))
        {
            selected = primaryOutputs;
        }
        else
        {
            var picked = new List<string>();
            foreach (var token in indexesArg.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(token, out var index) && index >= 0 && index < primaryOutputs.Count)
                {
                    picked.Add(primaryOutputs[index]);
                }
            }

            selected = picked;
        }

        var result = new List<string>();
        foreach (var item in selected)
        {
            var full = Path.IsPathRooted(item) ? item : Path.GetFullPath(Path.Combine(outputDirectory, item));
            if (IsProjectFile(full) && File.Exists(full))
            {
                result.Add(full);
            }
        }

        return result;
    }

    /// <summary>
    /// Walks up from the output directory looking for a solution file, stopping at
    /// <paramref name="boundaryDirectory"/> (inclusive) when one is supplied.
    ///
    /// The boundary matters: without it the walk continues to the filesystem root, so creating a
    /// project inside the workspace could run <c>dotnet sln add</c> against a solution belonging to
    /// an unrelated repository above it — a write outside the confinement the workspace root is
    /// supposed to provide.
    /// </summary>
    internal static string? FindSolution(string startDirectory, string? boundaryDirectory = null)
    {
        try
        {
            var dir = Path.GetFullPath(startDirectory);
            string? boundary = string.IsNullOrWhiteSpace(boundaryDirectory)
                ? null
                : Path.GetFullPath(boundaryDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            // A start directory outside the boundary means the caller already opted out of
            // confinement for this path; searching above it would be worse, so search nothing.
            if (boundary != null && !IsAtOrBelow(dir, boundary, comparison))
            {
                return null;
            }

            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(dir))
                {
                    var solution = Directory.EnumerateFiles(dir, "*.sln")
                        .Concat(Directory.EnumerateFiles(dir, "*.slnx"))
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();

                    if (solution != null)
                    {
                        return solution;
                    }
                }

                if (boundary != null
                    && dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Equals(boundary, comparison))
                {
                    break;
                }

                var parent = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(parent) || parent == dir)
                {
                    break;
                }

                dir = parent;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Treated as "no solution found".
        }

        return null;
    }

    private static bool IsAtOrBelow(string candidate, string boundaryNoSeparator, StringComparison comparison)
    {
        var trimmed = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Equals(boundaryNoSeparator, comparison)
            || trimmed.StartsWith(boundaryNoSeparator + Path.DirectorySeparatorChar, comparison);
    }

    private static bool IsProjectFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".fsproj", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".vbproj", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRestorableProject(string path)
        => IsProjectFile(path)
            || Path.GetExtension(path).Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).Equals(".slnx", StringComparison.OrdinalIgnoreCase);

    private static string? GetArg(IPostAction postAction, string key)
    {
        if (postAction.Args == null)
        {
            return null;
        }

        foreach (var kvp in postAction.Args)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }

        return null;
    }

    private void LogResult(IPostAction postAction, string command, ProcessRunResult run)
    {
        if (run.ExitCode == 0)
        {
            _logger.LogInformation("Post-action succeeded: {Command}", command);
        }
        else
        {
            _logger.LogWarning(
                "Post-action failed (exit {ExitCode}, continueOnError={ContinueOnError}): {Command}",
                run.ExitCode, postAction.ContinueOnError, command);
        }
    }

    private static string Quote(string path) => path.Contains(' ') ? $"\"{path}\"" : path;

    private static string? Truncate(string? value, int max = 4000)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed.Substring(0, max) + "… (truncated)";
    }

    private static async Task<ProcessRunResult> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Keep child tooling non-interactive: a prompt would hang the MCP server indefinitely.
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        using var process = new Process { StartInfo = startInfo };

        if (!process.Start())
        {
            return new ProcessRunResult(-1, string.Empty, $"Failed to start '{fileName}'.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DefaultTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new ProcessRunResult(-1, string.Empty, $"'{fileName} {arguments}' timed out after {DefaultTimeout.TotalMinutes:0} minutes.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        return new ProcessRunResult(process.ExitCode, stdout, stderr);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}

/// <summary>Raw result of a child process invocation.</summary>
internal sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>What the executor did, for reporting back to the agent.</summary>
internal sealed class PostActionExecutionReport
{
    public List<ExecutedPostAction> Executed { get; } = new();

    public List<SkippedPostAction> Skipped { get; } = new();

    public bool HasAnything => Executed.Count > 0 || Skipped.Count > 0;

    /// <summary>True when a post-action that was not marked <c>continueOnError</c> failed.</summary>
    public bool HasBlockingFailure => Executed.Any(e => !e.Success && !e.ContinueOnError);
}

/// <summary>A post-action the server actually ran.</summary>
internal sealed record ExecutedPostAction(
    string ActionId,
    string? Description,
    string? Command,
    bool Success,
    bool ContinueOnError,
    string? Output,
    string? Error,
    string? ManualInstructions);

/// <summary>A post-action the server deliberately did not run.</summary>
internal sealed record SkippedPostAction(
    string ActionId,
    string? Description,
    string? ManualInstructions,
    string Reason);
