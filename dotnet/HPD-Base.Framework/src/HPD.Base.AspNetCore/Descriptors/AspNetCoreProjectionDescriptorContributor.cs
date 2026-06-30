using HPD.Base;
using HPD.Base.Descriptors;
using HPD.Base.Runtime.Descriptors;

namespace HPD.Base.AspNetCore.Descriptors;

internal sealed class AspNetCoreProjectionDescriptorContributor : IBaseDescriptorContributor
{
    public string Id => "hpd.base.aspnetcore";

    public void Contribute(IBaseDescriptorContributionBuilder builder)
    {
        var dtoContracts = AspNetCoreDtoContractDescriptorFactory.Create();
        foreach (var dto in dtoContracts)
            builder.AddDtoContract(dto);

        var routes = AspNetCoreRouteDescriptorFactory.Create();
        builder.AddModule(new BaseModuleDescriptor
        {
            Id = "hpd.base.aspnetcore",
            Name = "HPD.Base.AspNetCore",
            Kind = BaseModuleKind.Projection,
            Version = "1.0.0",
            Status = ModuleStatus.Installed,
            Compatibility = new ModuleCompatibility { RequiresBaseContract = "1.0" },
            ContributedCapabilities = FeatureIds(),
            ContributedDtoIds = dtoContracts.Select(static dto => dto.Id).ToArray(),
            ContributedRouteIds = routes.Select(static route => route.OperationId).ToArray(),
            Visibility = VisibilityLevel.Public
        });

        builder.AddCapabilities(new CapabilityDescriptor
        {
            DescriptorVersion = "1.0",
            RuntimeId = "hpd.base.aspnetcore",
            Families =
            [
                new CapabilityFamilyDescriptor
                {
                    FamilyId = BaseCapabilityFamilies.Projection,
                    FamilyVersion = "1.0",
                    Status = CapabilityStatus.Available,
                    OwnerModuleId = "hpd.base.aspnetcore",
                    Features =
                    [
                        Feature(AspNetCoreProjectionFeatureIds.ProjectionAspNet, VisibilityLevel.Public),
                        Feature(AspNetCoreProjectionFeatureIds.ProjectionAspNetAdmin, VisibilityLevel.Admin),
                        Feature(BaseFeatureIds.SchemaRead, VisibilityLevel.Public),
                        Feature(BaseFeatureIds.CapabilitiesRead, VisibilityLevel.Public),
                        Feature(BaseFeatureIds.HealthRead, VisibilityLevel.Public),
                        Feature(BaseFeatureIds.DiagnosticsRead, VisibilityLevel.Public),
                        Feature(BaseFeatureIds.RecordsList, VisibilityLevel.Public),
                        Feature(BaseFeatureIds.RecordsQuery, VisibilityLevel.Public),
                        Feature(BaseFeatureIds.RecordsGet, VisibilityLevel.Public),
                        Feature(BaseFeatureIds.RecordsCreate, VisibilityLevel.Public),
                        Feature(BaseFeatureIds.RecordsPatch, VisibilityLevel.Public),
                        Feature(BaseFeatureIds.RecordsReplace, VisibilityLevel.Public),
                        Feature(BaseFeatureIds.RecordsDelete, VisibilityLevel.Public)
                    ]
                }
            ]
        });

        builder.AddProjection(new ProjectionDescriptor
        {
            Id = "hpd.base.aspnetcore",
            Kind = ProjectionKind.AspNet,
            PackageId = "HPD.Base.AspNetCore",
            PackageVersion = "1.0.0",
            ContractVersionRange = "1.0",
            Status = ProjectionStatus.Available,
            Visibility = VisibilityLevel.Public,
            ProvidedCapabilities = [AspNetCoreProjectionFeatureIds.ProjectionAspNet, AspNetCoreProjectionFeatureIds.ProjectionAspNetAdmin],
            Routes = routes,
            DtoContracts = dtoContracts
        });
    }

    private static string[] FeatureIds() =>
    [
        AspNetCoreProjectionFeatureIds.ProjectionAspNet,
        AspNetCoreProjectionFeatureIds.ProjectionAspNetAdmin,
        BaseFeatureIds.SchemaRead,
        BaseFeatureIds.CapabilitiesRead,
        BaseFeatureIds.HealthRead,
        BaseFeatureIds.DiagnosticsRead,
        BaseFeatureIds.RecordsList,
        BaseFeatureIds.RecordsQuery,
        BaseFeatureIds.RecordsGet,
        BaseFeatureIds.RecordsCreate,
        BaseFeatureIds.RecordsPatch,
        BaseFeatureIds.RecordsReplace,
        BaseFeatureIds.RecordsDelete
    ];

    private static CapabilityFeatureDescriptor Feature(string featureId, VisibilityLevel visibility) => new()
    {
        FeatureId = featureId,
        Version = "1.0",
        Status = CapabilityStatus.Available,
        SupportLevel = SupportLevel.Required,
        Scope = CapabilityScope.Runtime,
        Visibility = visibility
    };
}
