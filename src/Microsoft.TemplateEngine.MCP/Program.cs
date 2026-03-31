// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.MCP.Host;
using Microsoft.TemplateEngine.MCP.PostCreation;

namespace Microsoft.TemplateEngine.MCP;

internal sealed class Program
{
    public static async Task Main(string[] args)
    {
        var featureFlags = McpFeatureFlags.FromEnvironment(args);

        if (featureFlags.Transport == TransportMode.Http)
        {
            await RunHttpServerAsync(args, featureFlags).ConfigureAwait(false);
        }
        else
        {
            await RunStdioServerAsync(args, featureFlags).ConfigureAwait(false);
        }
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

        var app = builder.Build();
        app.MapMcp();
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

        app.Urls.Add(featureFlags.HttpUrl);

        Console.Error.WriteLine($"MCP Template Engine server listening on {featureFlags.HttpUrl}");
        Console.Error.WriteLine($"  MCP endpoint: {featureFlags.HttpUrl}/mcp");
        Console.Error.WriteLine($"  Health check: {featureFlags.HttpUrl}/health");

        await app.RunAsync().ConfigureAwait(false);
    }

    private static void RegisterCoreServices(IServiceCollection services, McpFeatureFlags featureFlags)
    {
        services.AddSingleton(featureFlags);
        services.AddSingleton<TemplateEngineService>();
        services.AddSingleton<TemplateEngineFacade>();
        services.AddSingleton<PostCreationProcessor>();
    }

    private static void ConfigureMcpServer(ModelContextProtocol.Server.McpServerOptions options)
    {
        options.ServerInfo = new()
        {
            Name = "Microsoft.TemplateEngine.MCP",
            Version = "1.3.0"
        };
    }
}
