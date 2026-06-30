using HPD.Base;
using HPD.Base.Descriptors;
using HPD.Base.Files.AspNetCore.Http;
using HPD.Base.Files.Runtime;
using HPD.Base.Runtime.Descriptors;

namespace HPD.Base.Files.AspNetCore.Descriptors;

internal sealed class FileAspNetCoreDescriptorContributor : IBaseDescriptorContributor
{
    private readonly IFileAspNetCoreRouteMappingState _mappingState;

    public FileAspNetCoreDescriptorContributor(IFileAspNetCoreRouteMappingState mappingState)
    {
        _mappingState = mappingState;
    }

    public string Id => FileModuleIds.AspNetCoreModule;

    public void Contribute(IBaseDescriptorContributionBuilder builder)
    {
        if (!_mappingState.IsMapped)
            return;

        var routes = Routes(_mappingState.RoutePrefix);
        builder.AddModule(new BaseModuleDescriptor
        {
            Id = FileModuleIds.AspNetCoreModule,
            Name = "HPD.Base.Files.AspNetCore",
            Kind = BaseModuleKind.Projection,
            Version = "1.0.0",
            Status = ModuleStatus.Installed,
            Compatibility = new ModuleCompatibility { RequiresBaseContract = "1.0" },
            Dependencies = [new ModuleDependency { ModuleId = FileModuleIds.Module, Required = true, FailureBehavior = DependencyFailureBehavior.DisableModule }],
            ContributedCapabilities = [FileFeatureIds.Upload, FileFeatureIds.Download, FileFeatureIds.MetadataRead, FileFeatureIds.Delete, FileFeatureIds.List],
            ContributedRouteIds = routes.Select(static route => route.OperationId).ToArray(),
            Visibility = VisibilityLevel.Public
        });

        builder.AddProjection(new ProjectionDescriptor
        {
            Id = FileModuleIds.AspNetCoreModule,
            Kind = ProjectionKind.AspNet,
            PackageId = "HPD.Base.Files.AspNetCore",
            PackageVersion = "1.0.0",
            ContractVersionRange = "1.0",
            Status = ProjectionStatus.Available,
            ProvidedCapabilities = [FileFeatureIds.Upload, FileFeatureIds.Download, FileFeatureIds.MetadataRead, FileFeatureIds.Delete, FileFeatureIds.List],
            Routes = routes,
            Visibility = VisibilityLevel.Public
        });
    }

    private static RouteDescriptor[] Routes(string routePrefix) =>
    [
        Route(FileHttpRouteNames.Upload, HttpMethodKind.Post, $"{routePrefix}/{{bucketId}}/objects", FileDtoIds.ObjectUploadResult, FileFeatureIds.Upload),
        Route(FileHttpRouteNames.List, HttpMethodKind.Get, $"{routePrefix}/{{bucketId}}/objects", FileDtoIds.ObjectListResult, FileFeatureIds.List),
        Route(FileHttpRouteNames.Download, HttpMethodKind.Get, $"{routePrefix}/{{bucketId}}/objects/{{objectId}}", "application/octet-stream", FileFeatureIds.Download),
        Route(FileHttpRouteNames.Head, HttpMethodKind.Head, $"{routePrefix}/{{bucketId}}/objects/{{objectId}}", FileDtoIds.ObjectMetadata, FileFeatureIds.MetadataRead),
        Route(FileHttpRouteNames.MetadataGet, HttpMethodKind.Get, $"{routePrefix}/{{bucketId}}/objects/{{objectId}}/metadata", FileDtoIds.ObjectMetadata, FileFeatureIds.MetadataRead),
        Route(FileHttpRouteNames.Delete, HttpMethodKind.Delete, $"{routePrefix}/{{bucketId}}/objects/{{objectId}}", "none", FileFeatureIds.Delete)
    ];

    private static RouteDescriptor Route(string operationId, HttpMethodKind method, string path, string responseDto, string featureId) => new()
    {
        OperationId = operationId,
        Method = method,
        Path = path,
        Visibility = VisibilityLevel.Public,
        AuthRequirement = RouteAuthRequirement.HostPolicy,
        ResponseDtoId = responseDto,
        ErrorDtoId = "problemDetails",
        RequiredFeatureIds = [featureId]
    };
}
