using System.Reflection;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.BuildingBlocks;
using Xunit;
using static UnicoreCRM.ArchitectureTests.ArchitectureInventory;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace UnicoreCRM.ArchitectureTests;

public sealed class EndpointMetadataTests
{
    [Fact]
    public async Task Actual_ApiHost_endpoints_obey_route_operation_and_authorization_admission()
    {
        // Program is internal. The factory still runs that actual assembly entrypoint;
        // no route registry or authentication result is substituted by this test.
        var entry = Assembly.Load("UnicoreCRM.ApiHost").EntryPoint!.DeclaringType!;
        var factoryType = typeof(WebApplicationFactory<>).MakeGenericType(entry);
        using var original = (IDisposable)Activator.CreateInstance(factoryType)!;
        var keyDirectory = Path.Combine(Path.GetTempPath(), "UnicoreCRM.ArchitectureTests", Guid.NewGuid().ToString("N"));
        var settings = new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Architecture",
            ["ASPNETCORE_ENVIRONMENT"] = "Architecture",
            ["ConnectionStrings:UnicoreCRM"] = "Server=127.0.0.1,1;Database=ArchitectureNoDatabase;Integrated Security=True;Connect Timeout=1",
            ["AI:DataProtection:KeyRingPath"] = keyDirectory,
            ["IdentityAuth:Jwt:SigningKey"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            ["IdentityAuth:RefreshTokenPepper"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            ["IdentityAuth:EmailVerification:CodePepper"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            ["IdentityAuth:EmailVerification:PayloadEncryptionKey"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            ["Communications:Telegram:Enabled"] = "false",
            ["Communications:Telegram:LiveAdmission:ProviderAccessApproved"] = "false",
            ["IdentityAuth:EmailVerification:Sender:Kind"] = "Unavailable",
            ["IdentityAuth:DevelopmentBootstrap:Enabled"] = "false",
            ["Workspace:DevelopmentBootstrap:Enabled"] = "false",
            ["AccessControl:DevelopmentBootstrap:Enabled"] = "false"
        };
        // Program reads module settings while registering services, before factory callbacks.
        // These values exist only in this isolated test process and are restored below.
        var priorEnvironment = settings.Keys.ToDictionary(k => k.Replace(":", "__", StringComparison.Ordinal),
            k => Environment.GetEnvironmentVariable(k.Replace(":", "__", StringComparison.Ordinal)));
        foreach (var pair in settings) Environment.SetEnvironmentVariable(pair.Key.Replace(":", "__", StringComparison.Ordinal), pair.Value);
        Action<IWebHostBuilder> configure = builder => builder.UseEnvironment("Architecture")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings))
            // Metadata discovery performs no background delivery, migration or database work.
            .ConfigureServices(services => services.RemoveAll<IHostedService>());
        var actual = factoryType.GetMethod("WithWebHostBuilder")!.Invoke(original, [configure])!;
        try
        {
            var services = (IServiceProvider)factoryType.GetProperty("Services")!.GetValue(actual)!;
            var endpoints = services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToArray();
            Assert.NotEmpty(endpoints);
            var errors = Validate(endpoints);
            using var scope = services.CreateScope();
            var sources = scope.ServiceProvider.GetServices<IIntegrationEventSource>().ToArray();
            if (sources.Length == 0) errors.Add("No actual integration event sources were registered.");
            foreach (var source in sources)
            {
                if (string.IsNullOrWhiteSpace(source.SourceOwner)) errors.Add("Event source owner absent.");
                if (source.EventDescriptors.Count == 0) errors.Add($"No descriptors: {source.SourceOwner}");
                foreach (var descriptor in source.EventDescriptors)
                {
                    if (string.IsNullOrWhiteSpace(descriptor.EventType) || descriptor.SchemaVersion < 1 || descriptor.SchemaVersion > IntegrationEventSchema.CurrentVersion)
                        errors.Add($"Event not explicitly versioned: {source.SourceOwner}/{descriptor.EventType}/{descriptor.SchemaVersion}");
                }
            }
            Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        }
        finally
        {
            if (actual is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
            else ((IDisposable)actual).Dispose();
            foreach (var pair in priorEnvironment) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            if (Directory.Exists(keyDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(keyDirectory)) File.Delete(file);
                Directory.Delete(keyDirectory);
            }
        }
    }

    [Fact]
    public async Task Negative_control_uses_real_ASPNET_authorization_and_route_metadata()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Architecture" });
        await using var app = builder.Build();
        app.MapGet("/unapproved", () => Results.Ok()).WithName("UnapprovedOperation");
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().ToArray();
        Assert.Single(endpoints);
        Assert.Empty(endpoints[0].Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Equal("UnapprovedOperation", endpoints[0].Metadata.GetMetadata<IEndpointNameMetadata>()!.EndpointName);
        Assert.DoesNotContain(AdmittedOperations(), o => o.GetProperty("operationId").GetString() == "UnapprovedOperation");
        Assert.Contains(Validate(endpoints), e => e.Contains("Unadmitted actual endpoint", StringComparison.Ordinal));
    }

    internal static List<string> Validate(IEnumerable<RouteEndpoint> endpoints)
    {
        var declared = EffectiveOperations();
        var errors = new List<string>();
        foreach (var endpoint in endpoints)
        {
            var name = endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
            if (name is null || !declared.TryGetValue(name, out var operation)) { errors.Add($"Unadmitted actual endpoint: {endpoint.RoutePattern.RawText} ({name})"); continue; }
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            if (methods is null || !methods.Contains(operation.GetProperty("method").GetString()!)) errors.Add($"Method mismatch: {name}");
            if (endpoint.RoutePattern.RawText != operation.GetProperty("path").GetString()) errors.Add($"Route mismatch: {name}");
            if (operation.GetProperty("actorRequired").GetBoolean()
                && (endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0 || endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)) errors.Add($"Authentication metadata missing: {name}");
            if (operation.GetProperty("workspaceRequired").GetBoolean() && endpoint.Metadata.GetMetadata<WorkspaceRequiredMetadata>() is null) errors.Add($"Trusted Workspace metadata missing: {name}");
            if (operation.GetProperty("status").GetString() == "BLOCKED")
            {
                errors.Add($"Blocked operation is exposed: {name}");
            }
        }
        return errors;
    }

    [Fact]
    public async Task Negative_control_rejects_missing_authentication_and_workspace_metadata()
    {
        await using var app = WebApplication.CreateBuilder().Build();
        app.MapPost("/tasks", () => Results.Ok()).WithName("createTask");
        var errors = Validate(((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>());
        Assert.Contains("Authentication metadata missing: createTask", errors);
        Assert.Contains("Trusted Workspace metadata missing: createTask", errors);
    }

    [Fact]
    public async Task Negative_control_rejects_a_blocked_operation_with_success_metadata()
    {
        var blocked = AdmittedOperations().First(o => o.GetProperty("status").GetString() == "BLOCKED");
        await using var app = WebApplication.CreateBuilder().Build();
        app.MapMethods(blocked.GetProperty("path").GetString()!, [blocked.GetProperty("method").GetString()!], () => Results.Ok())
            .WithName(blocked.GetProperty("operationId").GetString()!).Produces(200);
        Assert.Contains(Validate(((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()),
            error => error.Contains("Blocked operation is exposed", StringComparison.Ordinal));
    }
}
