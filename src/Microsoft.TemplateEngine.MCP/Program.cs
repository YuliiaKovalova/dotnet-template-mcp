// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.MCP.Host;
using Microsoft.TemplateEngine.MCP.PostCreation;
using Microsoft.TemplateEngine.MCP.Security;

namespace Microsoft.TemplateEngine.MCP;

internal sealed class Program
{
    public static async Task Main(string[] args)
    {
        var featureFlags = McpFeatureFlags.FromEnvironment(args);

        if (featureFlags.Transport == TransportMode.Http)
        {
            if (!ValidateHttpSecurity(featureFlags))
            {
                Environment.ExitCode = 1;
                return;
            }

            await RunHttpServerAsync(args, featureFlags).ConfigureAwait(false);
        }
        else
        {
            await RunStdioServerAsync(args, featureFlags).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Fail closed: every tool in this server writes files or installs packages, so an
    /// unauthenticated public endpoint is a remote code execution surface. Starting without a token
    /// requires an explicit opt-in.
    /// </summary>
    private static bool ValidateHttpSecurity(McpFeatureFlags featureFlags)
    {
        if (featureFlags.HttpAllowAnonymousInvalidValue != null)
        {
            Console.Error.WriteLine(
                $"""
                FATAL: {McpFeatureFlags.HttpAllowAnonymousEnvVar} is set to '{featureFlags.HttpAllowAnonymousInvalidValue}',
                which is not a recognized boolean.

                This flag disables authentication, so an ambiguous value is not guessed.
                Use one of: true, false, 1, 0, yes, no, on, off.
                """);

            return false;
        }

        if (featureFlags.HttpAuthenticationRequired || featureFlags.HttpAllowAnonymous)
        {
            return true;
        }

        Console.Error.WriteLine(
            $"""
            FATAL: refusing to start the HTTP transport without authentication.

            The MCP tools in this server create files and install NuGet packages, so an open
            endpoint lets anyone who can reach it run those operations.

            Set a shared secret:
                {McpFeatureFlags.HttpAuthTokenEnvVar}=<token>

            ...or acknowledge the risk explicitly (only for a trusted, isolated network):
                {McpFeatureFlags.HttpAllowAnonymousEnvVar}=true
            """);

        return false;
    }

    private static async Task RunStdioServerAsync(string[] args, McpFeatureFlags featureFlags)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        RegisterCoreServices(builder.Services, featureFlags);
        builder.Services
            .AddMcpServer(ConfigureMcpServer)
            .WithStdioServerTransport()
            .WithToolsFromAssembly()
            .WithPromptsFromAssembly();

        await builder.Build().RunAsync().ConfigureAwait(false);
    }

    private static async Task RunHttpServerAsync(string[] args, McpFeatureFlags featureFlags)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        RegisterCoreServices(builder.Services, featureFlags);
        builder.Services
            .AddMcpServer(ConfigureMcpServer)
            .WithHttpTransport()
            .WithToolsFromAssembly()
            .WithPromptsFromAssembly();

        builder.Services.AddHealthChecks()
            .AddCheck("mcp-server", () => HealthCheckResult.Healthy("MCP Template Engine server is running."));

        if (featureFlags.HttpRateLimitPerMinute > 0)
        {
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                // Partition by remote address, never by the Authorization header.
                //
                // Keying on a request header lets any client mint a fresh budget by rotating that
                // header, which defeats rate limiting outright and, worse, hands an attacker an
                // unthrottled token brute-force channel: every wrong guess is a new partition.
                // The remote address is the only identity here the caller cannot choose. (With a
                // single shared secret, partitioning by token would also be meaningless — every
                // legitimate client presents the same one.)
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                {
                    // Liveness probes must never be throttled.
                    if (context.Request.Path.StartsWithSegments("/health"))
                    {
                        return RateLimitPartition.GetNoLimiter("health");
                    }

                    var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                    return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = featureFlags.HttpRateLimitPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    });
                });
            });
        }

        var app = builder.Build();

        // Deliberately ahead of the token gate: failed authentication attempts must consume budget,
        // otherwise brute-force guessing is unthrottled. This is safe only because the partition key
        // is the remote address rather than anything the caller supplies.
        if (featureFlags.HttpRateLimitPerMinute > 0)
        {
            app.UseRateLimiter();
        }

        // Health must stay reachable for probes, so the token gate is scoped to /mcp only.
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    status = report.Status.ToString(),
                    transport = "http",
                    server = "Microsoft.TemplateEngine.MCP",
                }).ConfigureAwait(false);
            }
        });

        // Gate everything except the health probe. Scoping this to a single path would be fragile:
        // MapMcp's default pattern is the application root, so a path-specific filter can silently
        // leave the real MCP endpoint open.
        if (featureFlags.HttpAuthenticationRequired)
        {
            app.UseWhen(
                context => !context.Request.Path.StartsWithSegments("/health"),
                branch => branch.UseMiddleware<BearerTokenMiddleware>(featureFlags));
        }

        // Mapped explicitly at /mcp so the endpoint matches the documented URL.
        app.MapMcp("/mcp");

        app.Urls.Add(featureFlags.HttpUrl);

        Console.Error.WriteLine($"MCP Template Engine server listening on {featureFlags.HttpUrl}");
        Console.Error.WriteLine($"  MCP endpoint: {featureFlags.HttpUrl}/mcp");
        Console.Error.WriteLine($"  Health check: {featureFlags.HttpUrl}/health");
        Console.Error.WriteLine(featureFlags.HttpAuthenticationRequired
            ? "  Auth:         bearer token required"
            : "  Auth:         DISABLED (anonymous access explicitly allowed)");
        Console.Error.WriteLine(featureFlags.HttpRateLimitPerMinute > 0
            ? $"  Rate limit:   {featureFlags.HttpRateLimitPerMinute} requests/minute per client"
            : "  Rate limit:   disabled");
        Console.Error.WriteLine(featureFlags.WorkspaceEnforcementEnabled
            ? $"  Workspace:    {featureFlags.WorkspaceRoot}"
            : "  Workspace:    UNCONFINED (writes allowed to any path)");

        await app.RunAsync().ConfigureAwait(false);
    }

    private static void RegisterCoreServices(IServiceCollection services, McpFeatureFlags featureFlags)
    {
        services.AddSingleton(featureFlags);
        services.AddSingleton<TemplateEngineService>();
        services.AddSingleton<TemplateEngineFacade>();
        services.AddSingleton<PostCreationProcessor>();
        services.AddSingleton<PostActionExecutor>(sp => new PostActionExecutor(
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<McpFeatureFlags>()));
        services.AddSingleton<PackageUpgradeService>();
    }

    private static void ConfigureMcpServer(ModelContextProtocol.Server.McpServerOptions options)
    {
        options.ServerInfo = new()
        {
            Name = "Microsoft.TemplateEngine.MCP",
            Version = "1.4.0"
        };
    }
}
