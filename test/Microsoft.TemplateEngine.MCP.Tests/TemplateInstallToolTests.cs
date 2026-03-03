// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using FakeItEasy;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Abstractions.Parameters;
using Microsoft.TemplateEngine.MCP.Tools;
using Xunit;

namespace Microsoft.TemplateEngine.MCP.Tests;

public class TemplateInstallToolTests
{
    [Fact]
    public async Task InstallTemplate_AlreadyInstalledSameVersion_SkipsInstall()
    {
        var engineService = A.Fake<TemplateEngineService>();

        var existingPackage = A.Fake<IManagedTemplatePackage>();
        A.CallTo(() => existingPackage.Identifier).Returns("MyPackage");
        A.CallTo(() => existingPackage.Version).Returns("1.0.0");
        A.CallTo(() => engineService.GetManagedTemplatePackagesAsync(A<CancellationToken>._))
            .Returns(new[] { existingPackage });

        var template = CreateFakeTemplate("MyTemplate", "mytempl");
        A.CallTo(() => engineService.GetTemplatesAsync(A<CancellationToken>._))
            .Returns(new[] { template });

        var result = await TemplateInstallTool.InstallTemplateAsync(
            engineService, new McpFeatureFlags(), "MyPackage", "1.0.0");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.True(parsed.GetProperty("AlreadyInstalled").GetBoolean());
        Assert.True(parsed.GetProperty("Success").GetBoolean());
        Assert.Contains("already installed", parsed.GetProperty("Message").GetString());

        // Verify InstallTemplatePackagesAsync was NOT called
        A.CallTo(() => engineService.InstallTemplatePackagesAsync(
            A<IEnumerable<InstallRequest>>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task InstallTemplate_AlreadyInstalledNoVersionSpecified_SkipsInstall()
    {
        var engineService = A.Fake<TemplateEngineService>();

        var existingPackage = A.Fake<IManagedTemplatePackage>();
        A.CallTo(() => existingPackage.Identifier).Returns("MyPackage");
        A.CallTo(() => existingPackage.Version).Returns("2.0.0");
        A.CallTo(() => engineService.GetManagedTemplatePackagesAsync(A<CancellationToken>._))
            .Returns(new[] { existingPackage });

        var template = CreateFakeTemplate("MyTemplate", "mytempl");
        A.CallTo(() => engineService.GetTemplatesAsync(A<CancellationToken>._))
            .Returns(new[] { template });

        var result = await TemplateInstallTool.InstallTemplateAsync(
            engineService, new McpFeatureFlags(), "MyPackage");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.True(parsed.GetProperty("AlreadyInstalled").GetBoolean());

        A.CallTo(() => engineService.InstallTemplatePackagesAsync(
            A<IEnumerable<InstallRequest>>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task InstallTemplate_AlreadyInstalledDifferentVersion_ReturnsUpgradeInfo()
    {
        var engineService = A.Fake<TemplateEngineService>();

        var existingPackage = A.Fake<IManagedTemplatePackage>();
        A.CallTo(() => existingPackage.Identifier).Returns("MyPackage");
        A.CallTo(() => existingPackage.Version).Returns("1.0.0");
        A.CallTo(() => engineService.GetManagedTemplatePackagesAsync(A<CancellationToken>._))
            .Returns(new[] { existingPackage });

        var template = CreateFakeTemplate("MyTemplate", "mytempl");
        A.CallTo(() => engineService.GetTemplatesAsync(A<CancellationToken>._))
            .Returns(new[] { template });

        var result = await TemplateInstallTool.InstallTemplateAsync(
            engineService, new McpFeatureFlags(), "MyPackage", "2.0.0");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.True(parsed.GetProperty("UpgradeAvailable").GetBoolean());
        Assert.Equal("1.0.0", parsed.GetProperty("CurrentVersion").GetString());
        Assert.Equal("2.0.0", parsed.GetProperty("RequestedVersion").GetString());

        A.CallTo(() => engineService.InstallTemplatePackagesAsync(
            A<IEnumerable<InstallRequest>>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task InstallTemplate_NotInstalled_CallsInstall()
    {
        var engineService = A.Fake<TemplateEngineService>();

        // No existing packages
        A.CallTo(() => engineService.GetManagedTemplatePackagesAsync(A<CancellationToken>._))
            .Returns(Array.Empty<IManagedTemplatePackage>());

        // InstallResult is sealed with private constructors — verify the call happens
        // by returning an empty list (simulating no result)
        A.CallTo(() => engineService.InstallTemplatePackagesAsync(
            A<IEnumerable<InstallRequest>>._, A<CancellationToken>._))
            .Returns(Array.Empty<InstallResult>());

        var result = await TemplateInstallTool.InstallTemplateAsync(
            engineService, new McpFeatureFlags(), "NewPackage", "1.0.0");

        // Verify that InstallTemplatePackagesAsync WAS called (not skipped)
        A.CallTo(() => engineService.InstallTemplatePackagesAsync(
            A<IEnumerable<InstallRequest>>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();

        // With empty result, should return an error
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.True(parsed.TryGetProperty("error", out _));
    }

    private static ITemplateInfo CreateFakeTemplate(string identity, string shortName)
    {
        var template = A.Fake<ITemplateInfo>();
        A.CallTo(() => template.Identity).Returns(identity);
        A.CallTo(() => template.ShortNameList).Returns(new[] { shortName });
        A.CallTo(() => template.Name).Returns(identity);
        A.CallTo(() => template.MountPointUri).Returns($"/packages/{identity}");
        A.CallTo(() => template.TagsCollection).Returns(new Dictionary<string, string> { { "language", "C#" } });
        A.CallTo(() => template.Classifications).Returns(new[] { "Test" });

        var paramDefs = A.Fake<IParameterDefinitionSet>();
        A.CallTo(() => paramDefs.GetEnumerator()).ReturnsLazily(() => new List<ITemplateParameter>().GetEnumerator());
        A.CallTo(() => template.ParameterDefinitions).Returns(paramDefs);

        return template;
    }
}
