using HPD.Agent.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace HPD.Agent.AspNetCore.OpenApi.Tests;

public sealed class HPDAgentOpenApiSecuritySchemeTests
{
    [Fact]
    public async Task ConsumerDocumentTransformer_CanAddBearerSecurityScheme()
    {
        var builder = new WebHostBuilder()
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddHPDAgentOpenApi(configure: options =>
                {
                    options.AddDocumentTransformer((document, _, _) =>
                    {
                        document.Components ??= new OpenApiComponents();
                        document.Components.SecuritySchemes ??=
                            new Dictionary<string, IOpenApiSecurityScheme>();
                        document.Components.SecuritySchemes["Bearer"] =
                            new OpenApiSecurityScheme
                            {
                                Type = SecuritySchemeType.Http,
                                Scheme = "bearer",
                                BearerFormat = "JWT"
                            };
                        return Task.CompletedTask;
                    });
                });
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapHPDAgentOpenApiDocuments());
            });

        using var server = new TestServer(builder);
        var document = await server.Services
            .GetRequiredKeyedService<IOpenApiDocumentProvider>(
                HPDAgentOpenApiExtensions.DefaultDocumentName)
            .GetOpenApiDocumentAsync();

        Assert.True(document.Components?.SecuritySchemes?.ContainsKey("Bearer"));
    }
}
