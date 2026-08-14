// Copyright (c) 2025 Yuliia Kovalova.
// Licensed under the MIT license. See LICENSE in the repository root for details.

namespace DotnetTemplateMcp.Intent;

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
