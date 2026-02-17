// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.TemplateEngine.MCP.Intent;

/// <summary>
/// Resolves natural-language intent descriptions to template + parameter selections.
/// </summary>
internal interface IIntentResolver
{
    /// <summary>
    /// Resolve a natural-language description to ranked template matches with pre-filled parameters.
    /// </summary>
    /// <param name="intent">Natural language description (e.g., "web API with authentication and controllers").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolution result with ranked matches and extracted keywords.</returns>
    Task<TemplateResolution> ResolveAsync(string intent, CancellationToken cancellationToken = default);
}
