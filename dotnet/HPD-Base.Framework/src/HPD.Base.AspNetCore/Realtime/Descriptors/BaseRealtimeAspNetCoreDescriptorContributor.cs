using HPD.Base.AspNetCore;
using HPD.Base;

namespace HPD.Base.AspNetCore;

internal sealed class BaseRealtimeAspNetCoreDescriptorContributor : IBaseDescriptorContributor
{
    /// <summary>Gets the ID.</summary>
    public string Id => "hpd.base.realtime.aspnetcore";

    /// <summary>Executes the contribute operation.</summary>
    public void Contribute(IBaseDescriptorContributionBuilder builder)
    {
        var route = Route();
        builder.AddModule(new BaseModuleDescriptor
        {
            Id = "hpd.base.realtime.aspnetcore",
            Name = "HPD.Base.AspNetCore",
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
            PackageId = "HPD.Base.AspNetCore",
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
                    JsonContextOwner = "HPD.Base",
                    Visibility = VisibilityLevel.Public
                },
                new DtoContractDescriptor
                {
                    Id = BaseRealtimeDtoIds.ServerMessage,
                    ContractVersion = "1.0",
                    JsonContextOwner = "HPD.Base",
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
    /// <summary>Gets the operation ID.</summary>
    public string OperationId => BaseRealtimeRouteIds.WebSocket;
    /// <summary>Gets the summary.</summary>
    public string Summary => "BASE realtime WebSocket";
    /// <summary>Gets the description.</summary>
    public string Description => "Opens the HPD.BASE realtime WebSocket JSON protocol. OpenAPI describes the handshake route; WebSocket frames use BaseRealtimeClientMessage and BaseRealtimeServerMessage.";
    /// <summary>Gets the tags.</summary>
    public string[] Tags => ["Realtime"];
    /// <summary>Gets the required feature IDs.</summary>
    public string[] RequiredFeatureIds =>
    [
        BaseRealtimeFeatureIds.Channels,
        BaseRealtimeFeatureIds.RecordChanges,
        BaseRealtimeFeatureIds.WebSocketTransport
    ];
}
