using HPD.Base.AspNetCore.OpenApi;
using HPD.Base.Descriptors;
using HPD.Base.Runtime.Descriptors;

namespace HPD.Base.Realtime.AspNetCore.Descriptors;

internal sealed class BaseRealtimeAspNetCoreDescriptorContributor : IBaseDescriptorContributor
{
    public string Id => "hpd.base.realtime.aspnetcore";

    public void Contribute(IBaseDescriptorContributionBuilder builder)
    {
        var route = Route();
        builder.AddModule(new BaseModuleDescriptor
        {
            Id = "hpd.base.realtime.aspnetcore",
            Name = "HPD.Base.Realtime.AspNetCore",
            Kind = BaseModuleKind.Projection,
            Version = "1.0.0",
            Status = ModuleStatus.Installed,
            Compatibility = new ModuleCompatibility { RequiresBaseContract = "1.0" },
            Dependencies =
            [
                new ModuleDependency
                {
                    ModuleId = BaseRealtimeModuleIds.Module,
                    Required = true
                }
            ],
            ContributedCapabilities = [BaseRealtimeFeatureIds.WebSocketTransport],
            ContributedRouteIds = [BaseRealtimeRouteIds.WebSocket],
            Visibility = VisibilityLevel.Public
        });

        builder.AddProjection(new ProjectionDescriptor
        {
            Id = "hpd.base.realtime.aspnetcore",
            Kind = ProjectionKind.AspNet,
            PackageId = "HPD.Base.Realtime.AspNetCore",
            PackageVersion = "1.0.0",
            ContractVersionRange = "1.0",
            Status = ProjectionStatus.Available,
            Visibility = VisibilityLevel.Public,
            RequiredCapabilities =
            [
                BaseRealtimeFeatureIds.Channels,
                BaseRealtimeFeatureIds.RecordChanges
            ],
            ProvidedCapabilities = [BaseRealtimeFeatureIds.WebSocketTransport],
            Routes = [route],
            DtoContracts =
            [
                new DtoContractDescriptor
                {
                    Id = BaseRealtimeDtoIds.ClientMessage,
                    ContractVersion = "1.0",
                    JsonContextOwner = "HPD.Base.Realtime.Abstractions",
                    Visibility = VisibilityLevel.Public
                },
                new DtoContractDescriptor
                {
                    Id = BaseRealtimeDtoIds.ServerMessage,
                    ContractVersion = "1.0",
                    JsonContextOwner = "HPD.Base.Realtime.Abstractions",
                    Visibility = VisibilityLevel.Public
                }
            ],
            Entrypoints =
            [
                new ProjectionEntrypointDescriptor
                {
                    Id = "base.realtime.websocket",
                    Name = "Realtime WebSocket",
                    Kind = ProjectionEntrypointKind.Custom,
                    RequiredFeatureIds = [BaseRealtimeFeatureIds.WebSocketTransport],
                    RouteRefs = [BaseRealtimeRouteIds.WebSocket],
                    Visibility = VisibilityLevel.Public
                }
            ]
        });
    }

    internal static RouteDescriptor Route() => new()
    {
        OperationId = BaseRealtimeRouteIds.WebSocket,
        Method = HttpMethodKind.Get,
        Path = BaseRealtimeRoutes.WebSocket,
        Visibility = VisibilityLevel.Public,
        AuthRequirement = RouteAuthRequirement.Public,
        RequestDtoId = BaseRealtimeDtoIds.ClientMessage,
        ResponseDtoId = BaseRealtimeDtoIds.ServerMessage,
        ErrorDtoId = BaseRealtimeDtoIds.Error,
        RequiredFeatureIds =
        [
            BaseRealtimeFeatureIds.Channels,
            BaseRealtimeFeatureIds.RecordChanges,
            BaseRealtimeFeatureIds.WebSocketTransport
        ]
    };
}

internal sealed class BaseRealtimeWebSocketOpenApiMetadata : IHPDBaseModuleOpenApiMetadata
{
    public string OperationId => BaseRealtimeRouteIds.WebSocket;
    public string Summary => "BASE realtime WebSocket";
    public string Description => "Opens the HPD.BASE realtime WebSocket JSON protocol. OpenAPI describes the handshake route; WebSocket frames use BaseRealtimeClientMessage and BaseRealtimeServerMessage.";
    public string[] Tags => ["Realtime"];
    public string[] RequiredFeatureIds =>
    [
        BaseRealtimeFeatureIds.Channels,
        BaseRealtimeFeatureIds.RecordChanges,
        BaseRealtimeFeatureIds.WebSocketTransport
    ];
}
