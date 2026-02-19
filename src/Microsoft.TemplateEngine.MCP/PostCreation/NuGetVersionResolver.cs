// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;

namespace Microsoft.TemplateEngine.MCP.PostCreation;

/// <summary>
/// Resolves the latest stable version of a NuGet package using the NuGet V3 API.
/// Uses the registration endpoint for efficient version lookups.
/// </summary>
internal static class NuGetVersionResolver
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    private const string NuGetRegistrationBaseUrl = "https://api.nuget.org/v3-flatcontainer/";

    /// <summary>
    /// Get the latest stable version of a NuGet package.
    /// Returns null if the package is not found or on error.
    /// </summary>
    public static async Task<string?> GetLatestStableVersionAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{NuGetRegistrationBaseUrl}{packageId.ToLowerInvariant()}/index.json";
            var response = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var doc = JsonDocument.Parse(content);

            if (!doc.RootElement.TryGetProperty("versions", out var versionsElement))
            {
                return null;
            }

            // versions array is sorted ascending — find last stable version
            string? latestStable = null;
            foreach (var version in versionsElement.EnumerateArray())
            {
                var versionStr = version.GetString();
                if (versionStr != null && IsStableVersion(versionStr))
                {
                    latestStable = versionStr;
                }
            }

            return latestStable;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// A stable version has no prerelease suffix (no dash after the version numbers).
    /// </summary>
    internal static bool IsStableVersion(string version)
    {
        return !version.Contains('-');
    }
}
