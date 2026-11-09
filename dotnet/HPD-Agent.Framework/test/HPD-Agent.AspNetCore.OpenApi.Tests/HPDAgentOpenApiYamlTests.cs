using HPD.Agent.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.AspNetCore.OpenApi.Tests;

public sealed class HPDAgentOpenApiYamlTests
{
    [Fact]
    public async Task MappedYamlDocument_IsAvailableAtRuntime()
    {
        var builder = new WebHostBuilder()
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddHPDAgentOpenApi();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapHPDAgentOpenApiDocuments());
            });

        using var server = new TestServer(builder);
        using var client = server.CreateClient();
        using var response = await client.GetAsync("/openapi/hpd-agent-v1.yaml");

        response.EnsureSuccessStatusCode();
        var yaml = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("openapi:", yaml, StringComparison.Ordinal);
    }
}
