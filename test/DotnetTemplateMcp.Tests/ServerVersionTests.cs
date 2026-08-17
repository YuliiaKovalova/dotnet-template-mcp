// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

using System.Reflection;
using Xunit;

namespace DotnetTemplateMcp.Tests;

/// <summary>
/// The version advertised to MCP clients used to be a hardcoded literal in
/// <c>ConfigureMcpServer</c>, and it silently went stale across releases — clients were told a
/// version the server was not. It is now derived from the assembly; these tests keep it that way.
/// </summary>
public class ServerVersionTests
{
    [Fact]
    public void ServerVersion_MatchesTheAssemblyVersion()
    {
        var informational = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        var expected = informational.Split('+')[0];

        Assert.Equal(expected, Program.ServerVersion);
    }

    [Fact]
    public void ServerVersion_StripsSourceControlMetadata()
    {
        // The SDK appends "+<commit sha>" to InformationalVersion. Clients want "2.0.0", not
        // "2.0.0+348241c...", so the '+' suffix must never reach the wire.
        Assert.DoesNotContain('+', Program.ServerVersion);
    }

    [Fact]
    public void ServerVersion_IsANonPlaceholderVersion()
    {
        Assert.False(string.IsNullOrWhiteSpace(Program.ServerVersion));

        // "0.0.0" is the fallback when no version metadata exists at all — if that shows up, the
        // csproj <Version> is not flowing into the assembly.
        Assert.NotEqual("0.0.0", Program.ServerVersion);
        Assert.StartsWith(Program.ServerVersion.Split('.')[0], Program.ServerVersion);
    }
}
