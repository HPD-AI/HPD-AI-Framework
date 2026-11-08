using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HPD.Agent.AspNetCore.RuntimeExtensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.RuntimeExtensions.AspNetCore.Tests;

public sealed class RuntimeAgentExtensionEndpointTests
{
    [Fact]
    public async Task HostedService_HydratesPersistedSchemasBeforeTraffic()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"hpd-runtime-hydration-{Guid.NewGuid():N}");
        try
        {
            var store = new FileRuntimeAgentExtensionSchemaStore(directory);
            await store.SaveAsync(Schema());
            var registry = new RuntimeAgentExtensionRegistry(store: store);
            var hosted = new RuntimeAgentExtensionHydrationHostedService(registry, store);

            await hosted.StartAsync(CancellationToken.None);

            Assert.Single(registry.GetCatalog().Schemas);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ManagementEndpoint_RequiresAuthorizationAndRegistersSchema()
    {
        var registry = new RuntimeAgentExtensionRegistry();
        using var deniedServer = CreateServer(registry, authorizer: null);
        using var deniedClient = deniedServer.CreateClient();
        var schema = Schema();

        var denied = await deniedClient.PostAsJsonAsync("/runtime-extension-schemas", schema);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var allowedServer = CreateServer(registry, new AllowAuthorizer());
        using var client = allowedServer.CreateClient();
        var created = await client.PostAsJsonAsync("/runtime-extension-schemas", schema);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var descriptor = await created.Content.ReadFromJsonAsync<RuntimeAgentExtensionDescriptor>();
        Assert.Equal("tenant.acme.analysis:progress@1", descriptor!.Key);
        Assert.Equal("object", descriptor.PayloadSchema.GetProperty("type").GetString());
        Assert.Single(registry.GetCatalog().Schemas);
    }

    private static TestServer CreateServer(
        RuntimeAgentExtensionRegistry registry,
        IRuntimeAgentExtensionMutationAuthorizer? authorizer)
    {
        var builder = new WebHostBuilder()
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton(registry);
                if (authorizer is not null)
                    services.AddSingleton(authorizer);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                    endpoints.MapGroup("").MapRuntimeAgentExtensionManagement());
            });
        return new TestServer(builder);
    }

    private static RuntimeAgentExtensionSchema Schema() =>
        new()
        {
            Namespace = "tenant.acme.analysis",
            Name = "progress",
            Version = 1,
            Usage = RuntimeAgentExtensionUsage.LiveOutput,
            PayloadSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                additionalProperties = false
            })
        };

    private sealed class AllowAuthorizer : IRuntimeAgentExtensionMutationAuthorizer
    {
        public ValueTask<RuntimeAgentExtensionAuthorizationResult> AuthorizeAsync(
            HttpContext context,
            RuntimeAgentExtensionSchema schema,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new RuntimeAgentExtensionAuthorizationResult(true));
    }
}
