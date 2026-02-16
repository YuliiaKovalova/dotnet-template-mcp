// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.IDE;
using Microsoft.TemplateEngine.MCP.Host;
using ITemplateCreationResult = Microsoft.TemplateEngine.Edge.Template.ITemplateCreationResult;

namespace Microsoft.TemplateEngine.MCP;

/// <summary>
/// Singleton service that manages the template engine Bootstrapper lifecycle
/// and provides a clean API surface for MCP tools to consume.
/// </summary>
internal class TemplateEngineService : IDisposable
{
    private readonly Bootstrapper _bootstrapper;

    public TemplateEngineService(ILoggerFactory loggerFactory)
    {
        var host = new McpTemplateEngineHost(loggerFactory);
        _bootstrapper = new Bootstrapper(host, virtualizeConfiguration: false, loadDefaultComponents: true);
    }

    public virtual async Task<IReadOnlyList<ITemplateInfo>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        return await _bootstrapper.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<ITemplateCreationResult> CreateAsync(
        ITemplateInfo template,
        string? name,
        string outputPath,
        IReadOnlyDictionary<string, string?> parameters,
        CancellationToken cancellationToken = default)
    {
        return await _bootstrapper.CreateAsync(template, name, outputPath, parameters, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<ITemplateCreationResult> GetCreationEffectsAsync(
        ITemplateInfo template,
        string? name,
        string outputPath,
        IReadOnlyDictionary<string, string?> parameters,
        CancellationToken cancellationToken = default)
    {
        return await _bootstrapper.GetCreationEffectsAsync(template, name, outputPath, parameters, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<IReadOnlyList<InstallResult>> InstallTemplatePackagesAsync(
        IEnumerable<InstallRequest> installRequests,
        CancellationToken cancellationToken = default)
    {
        return await _bootstrapper.InstallTemplatePackagesAsync(installRequests, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<IReadOnlyList<UninstallResult>> UninstallTemplatePackagesAsync(
        IEnumerable<IManagedTemplatePackage> packages,
        CancellationToken cancellationToken = default)
    {
        return await _bootstrapper.UninstallTemplatePackagesAsync(packages, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<IReadOnlyList<IManagedTemplatePackage>> GetManagedTemplatePackagesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _bootstrapper.GetManagedTemplatePackagesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _bootstrapper.Dispose();
    }
}
