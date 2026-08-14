// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using Microsoft.TemplateEngine.MCP.PostCreation;
using Xunit;

namespace Microsoft.TemplateEngine.MCP.Tests;

/// <summary>
/// Covers gap 1.1: post-actions were previously serialized as metadata and never executed, so
/// template_instantiate left projects unrestored and outside the solution — strictly less capable
/// than the `dotnet new` it told agents to prefer it over.
/// All tests inject a fake process runner, so nothing here shells out.
/// </summary>
public class PostActionExecutorTests : IDisposable
{
    private readonly List<(string FileName, string Arguments, string WorkingDirectory)> _invocations = new();
    private readonly string _tempDir;

    public PostActionExecutorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mcp-postaction-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }

        GC.SuppressFinalize(this);
    }

    /// <summary>Creates a real file so the executor's "does this path exist" guard is satisfied.</summary>
    private string WriteProject(string relativePath)
    {
        var full = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        return relativePath.Replace('\\', '/');
    }

    private PostActionExecutor CreateExecutor(int exitCode = 0, string stdout = "", string stderr = "")
        => new(
            NullLogger.Instance,
            (fileName, arguments, workingDirectory, _) =>
            {
                _invocations.Add((fileName, arguments, workingDirectory));
                return Task.FromResult(new ProcessRunResult(exitCode, stdout, stderr));
            });

    private static IPostAction FakePostAction(
        Guid actionId,
        IReadOnlyDictionary<string, string>? args = null,
        bool continueOnError = false,
        string? manualInstructions = null)
    {
        var action = A.Fake<IPostAction>();
        A.CallTo(() => action.ActionId).Returns(actionId);
        A.CallTo(() => action.Args).Returns(args ?? new Dictionary<string, string>());
        A.CallTo(() => action.ContinueOnError).Returns(continueOnError);
        A.CallTo(() => action.Description).Returns("fake action");
        A.CallTo(() => action.ManualInstructions).Returns(manualInstructions);
        return action;
    }

    private static ITemplateCreationResult FakeCreationResult(
        IReadOnlyList<IPostAction> postActions,
        IReadOnlyList<string>? primaryOutputs = null)
    {
        var paths = (primaryOutputs ?? Array.Empty<string>())
            .Select(p =>
            {
                var path = A.Fake<ICreationPath>();
                A.CallTo(() => path.Path).Returns(p);
                return path;
            })
            .ToList();

        var creationResult = A.Fake<ICreationResult>();
        A.CallTo(() => creationResult.PostActions).Returns(postActions);
        A.CallTo(() => creationResult.PrimaryOutputs).Returns(paths);

        var result = A.Fake<ITemplateCreationResult>();
        A.CallTo(() => result.CreationResult).Returns(creationResult);
        return result;
    }

    [Fact]
    public async Task Execute_NoPostActions_ReturnsEmptyReport()
    {
        var report = await CreateExecutor().ExecuteAsync(
            FakeCreationResult(Array.Empty<IPostAction>()), _tempDir);

        Assert.False(report.HasAnything);
        Assert.Empty(_invocations);
    }

    [Fact]
    public async Task Execute_RestoreAction_RunsDotnetRestoreOnPrimaryOutput()
    {
        var project = WriteProject(Path.Combine("App", "App.csproj"));
        var creation = FakeCreationResult(
            new[] { FakePostAction(PostActionExecutor.RestoreNuGetPackagesActionId) },
            new[] { project });

        var report = await CreateExecutor().ExecuteAsync(creation, _tempDir);

        var executed = Assert.Single(report.Executed);
        Assert.True(executed.Success);

        var invocation = Assert.Single(_invocations);
        Assert.Equal("dotnet", invocation.FileName);
        Assert.StartsWith("restore", invocation.Arguments);
        Assert.Contains("App.csproj", invocation.Arguments);
        Assert.Equal(_tempDir, invocation.WorkingDirectory);
    }

    [Fact]
    public async Task Execute_RestoreAction_NonZeroExit_ReportsFailureWithoutThrowing()
    {
        var project = WriteProject(Path.Combine("App", "App.csproj"));
        var creation = FakeCreationResult(
            new[] { FakePostAction(PostActionExecutor.RestoreNuGetPackagesActionId) },
            new[] { project });

        var report = await CreateExecutor(exitCode: 1, stderr: "NU1101: Unable to find package")
            .ExecuteAsync(creation, _tempDir);

        var executed = Assert.Single(report.Executed);
        Assert.False(executed.Success);
        Assert.Contains("NU1101", executed.Error);
        Assert.True(report.HasBlockingFailure);
    }

    [Fact]
    public async Task Execute_RestoreAction_ContinueOnError_IsNotBlocking()
    {
        var project = WriteProject(Path.Combine("App", "App.csproj"));
        var creation = FakeCreationResult(
            new[] { FakePostAction(PostActionExecutor.RestoreNuGetPackagesActionId, continueOnError: true) },
            new[] { project });

        var report = await CreateExecutor(exitCode: 1, stderr: "offline").ExecuteAsync(creation, _tempDir);

        Assert.False(report.HasBlockingFailure);
    }

    [Fact]
    public async Task Execute_RestoreAction_HonorsFilesArg()
    {
        var chosen = WriteProject(Path.Combine("Chosen", "Chosen.csproj"));
        var ignored = WriteProject(Path.Combine("Ignored", "Ignored.csproj"));

        var creation = FakeCreationResult(
            new[]
            {
                FakePostAction(
                    PostActionExecutor.RestoreNuGetPackagesActionId,
                    new Dictionary<string, string> { ["files"] = chosen }),
            },
            new[] { ignored });

        await CreateExecutor().ExecuteAsync(creation, _tempDir);

        var invocation = Assert.Single(_invocations);
        Assert.Contains("Chosen.csproj", invocation.Arguments);
        Assert.DoesNotContain("Ignored.csproj", invocation.Arguments);
    }

    [Fact]
    public async Task Execute_UnsafeAction_IsSkippedWithManualInstructions()
    {
        // A "run script" post-action is arbitrary code shipped in a NuGet package, and auto-resolve
        // means a template can be installed without the user ever naming it. Never auto-execute.
        var scriptAction = FakePostAction(
            new Guid("3A7C4B45-1F5D-4A30-959A-51B88E82B5D2"),
            manualInstructions: "Run build.sh manually.");

        var report = await CreateExecutor().ExecuteAsync(
            FakeCreationResult(new[] { scriptAction }), _tempDir);

        Assert.Empty(report.Executed);
        var skipped = Assert.Single(report.Skipped);
        Assert.Equal("Run build.sh manually.", skipped.ManualInstructions);
        Assert.Contains("never executed automatically", skipped.Reason);
        Assert.Empty(_invocations);
    }

    [Fact]
    public async Task Execute_AddToSolution_UsesPrimaryOutputIndexes()
    {
        File.WriteAllText(Path.Combine(_tempDir, "My.sln"), string.Empty);
        var first = WriteProject(Path.Combine("First", "First.csproj"));
        var second = WriteProject(Path.Combine("Second", "Second.csproj"));

        var creation = FakeCreationResult(
            new[]
            {
                FakePostAction(
                    PostActionExecutor.AddProjectToSolutionActionId,
                    new Dictionary<string, string> { ["primaryOutputIndexes"] = "1" }),
            },
            new[] { first, second });

        var report = await CreateExecutor().ExecuteAsync(creation, _tempDir);

        var executed = Assert.Single(report.Executed);
        Assert.True(executed.Success);

        var invocation = Assert.Single(_invocations);
        Assert.StartsWith("sln", invocation.Arguments);
        Assert.Contains("My.sln", invocation.Arguments);
        Assert.Contains("Second.csproj", invocation.Arguments);
        Assert.DoesNotContain("First.csproj", invocation.Arguments);
    }

    [Fact]
    public async Task Execute_AddToSolution_NoIndexes_AddsAllPrimaryOutputs()
    {
        File.WriteAllText(Path.Combine(_tempDir, "My.sln"), string.Empty);
        var first = WriteProject(Path.Combine("First", "First.csproj"));
        var second = WriteProject(Path.Combine("Second", "Second.csproj"));

        var creation = FakeCreationResult(
            new[] { FakePostAction(PostActionExecutor.AddProjectToSolutionActionId) },
            new[] { first, second });

        await CreateExecutor().ExecuteAsync(creation, _tempDir);

        var invocation = Assert.Single(_invocations);
        Assert.Contains("First.csproj", invocation.Arguments);
        Assert.Contains("Second.csproj", invocation.Arguments);
    }

    [Fact]
    public async Task Execute_AddToSolution_NoSolutionFound_ReportsNonBlockingFailure()
    {
        var project = WriteProject(Path.Combine("First", "First.csproj"));

        var creation = FakeCreationResult(
            new[] { FakePostAction(PostActionExecutor.AddProjectToSolutionActionId) },
            new[] { project });

        var report = await CreateExecutor().ExecuteAsync(creation, _tempDir);

        Assert.Empty(_invocations);
        var executed = Assert.Single(report.Executed);
        Assert.False(executed.Success);
        Assert.Contains("No .sln", executed.Error);

        // A missing solution is normal for a standalone project — it must not fail the creation.
        Assert.False(report.HasBlockingFailure);
    }

    [Fact]
    public async Task Execute_MixedActions_RunsSafeOnesAndSkipsTheRest()
    {
        File.WriteAllText(Path.Combine(_tempDir, "My.sln"), string.Empty);
        var project = WriteProject(Path.Combine("App", "App.csproj"));

        var creation = FakeCreationResult(
            new[]
            {
                FakePostAction(PostActionExecutor.RestoreNuGetPackagesActionId),
                FakePostAction(PostActionExecutor.AddProjectToSolutionActionId),
                FakePostAction(Guid.NewGuid(), manualInstructions: "Do it yourself."),
            },
            new[] { project });

        var report = await CreateExecutor().ExecuteAsync(creation, _tempDir);

        Assert.Equal(2, report.Executed.Count);
        Assert.Single(report.Skipped);
        Assert.Equal(2, _invocations.Count);
    }

    [Fact]
    public async Task Execute_RestoreAction_IgnoresPathsThatDoNotExist()
    {
        var real = WriteProject(Path.Combine("Real", "Real.csproj"));

        var creation = FakeCreationResult(
            new[] { FakePostAction(PostActionExecutor.RestoreNuGetPackagesActionId) },
            new[] { real, "Ghost/Ghost.csproj" });

        await CreateExecutor().ExecuteAsync(creation, _tempDir);

        var invocation = Assert.Single(_invocations);
        Assert.Contains("Real.csproj", invocation.Arguments);
        Assert.DoesNotContain("Ghost.csproj", invocation.Arguments);
    }
}
