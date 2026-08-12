using HPD.Base.AspNetCore;
using HPD.Base;

namespace HPD.Base.AspNetCore.Tests.Realtime.Descriptors;

public sealed class RealtimeAspNetCoreDescriptorTests
{
    [Fact]
    public async Task DescriptorContributesWebSocketProjectionRoute()
    {
        await using var app = await TestRealtimeApp.CreateAsync();
        var snapshot = app.Services.GetRequiredService<IBaseDescriptorRegistry>().Current;

        var projection = snapshot.Manifest.Projections!
            .Single(item => item.Id == "hpd.base.realtime.aspnetcore");

        projection.Routes.Should().Contain(route =>
            route.OperationId == BaseRealtimeRouteIds.WebSocketV2
            && route.Method == HttpMethodKind.Get
            && route.Path == BaseRealtimeRoutes.WebSocketV2
            && route.ResponseDtoId == BaseRealtimeDtoIds.ServerMessage);
    }

    [Fact]
    public async Task WebSocketEndpointCarriesModuleOpenApiMetadata()
    {
        await using var app = await TestRealtimeApp.CreateAsync();
        var endpoint = app.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Single(item => item.RoutePattern.RawText == BaseRealtimeRoutes.WebSocketV2);

        var metadata = endpoint.Metadata.GetMetadata<IHPDBaseModuleOpenApiMetadata>();
        metadata.Should().NotBeNull();
        metadata!.OperationId.Should().Be(BaseRealtimeRouteIds.WebSocketV2);
        metadata.RequiredFeatureIds.Should().Contain(BaseRealtimeFeatureIds.WebSocketTransport);
    }
}
