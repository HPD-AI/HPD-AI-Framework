using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace HPD.Base.AspNetCore.Tests.TestSupport;

internal static class AspNetCoreTestExtensions
{
    public static async Task<T?> ReadBaseJsonAsync<T>(this WebApplication app, HttpContent content)
    {
        var json = await content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json, app.Services.GetRequiredService<IHPDBaseRuntime>().Json.Options);
    }

    public static IReadOnlyList<RouteEndpoint> RouteEndpoints(this WebApplication app) =>
        app.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .ToArray();

    public static JsonOptions HttpJsonOptions(this WebApplication app) =>
        app.Services.GetRequiredService<IOptions<JsonOptions>>().Value;

    public static async Task<RecordEnvelope> CreateRecordAsync(this HttpClient client, WebApplication app, string title = "hello")
    {
        var response = await client.PostAsync("/base/collections/items/records", JsonContent.Create(new RecordCreateRequest
        {
            Payload = TestBaseApp.Payload(("title", title))
        }, HPDBaseJsonSerializerContext.Default.RecordCreateRequest));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await app.ReadBaseJsonAsync<RecordEnvelope>(response.Content))!;
    }
}
