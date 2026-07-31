using System.Text.Json;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests.Descriptors;

public sealed class DescriptorFilteringTests
{
    [Fact]
    public async Task PublicSchemaRemovesHiddenSystemAndStoreDetails()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseDescriptorContributor, SensitiveSchemaContributor>();
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        var result = await provider.GetRequiredService<IBaseSchemaProvider>().GetSchemaAsync(
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.SchemaRead),
            VisibilityLevel.Public);

        var collection = Assert.Single(result.Value!.Collections!);
        Assert.Null(collection.Store);
        Assert.DoesNotContain(collection.Fields!, field => field.Name == "secret");
        Assert.DoesNotContain(collection.Fields!, field => field.Name == "systemId");
        Assert.Null(Assert.Single(collection.Fields!).Store);
    }

    [Fact]
    public async Task AdminSchemaKeepsAdminVisibleDetails()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseDescriptorContributor, SensitiveSchemaContributor>();
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        var result = await provider.GetRequiredService<IBaseSchemaProvider>().GetSchemaAsync(
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.SchemaRead),
            VisibilityLevel.Admin);

        var collection = Assert.Single(result.Value!.Collections!);
        Assert.NotNull(collection.Store);
        Assert.Contains(collection.Fields!, field => field.Name == "secret");
        Assert.Contains(collection.Fields!, field => field.Name == "systemId");
    }

    [Fact]
    public async Task PublicManifestPrunesRefsToFilteredDescriptors()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseDescriptorContributor, DanglingRefContributor>();
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        var result = await provider.GetRequiredService<IBaseDescriptorProvider>().GetManifestAsync(
            new BaseManifestRequest
            {
                Principal = RuntimeTestData.AnonymousPrincipal,
                Operation = RuntimeTestData.Operation(BaseOperationKind.SchemaRead),
                View = VisibilityLevel.Public
            });

        var projection = Assert.Single(result.Value!.Projections!);
        Assert.Single(projection.Routes!);
        Assert.Equal("public.route", projection.Routes![0].OperationId);
        var module = Assert.Single(result.Value.Modules!);
        Assert.Equal(["public.dto"], module.ContributedDtoIds!);
        Assert.Equal(["public.route"], module.ContributedRouteIds!);
    }

    [Fact]
    public async Task FilteredManifestEtagsAreViewSpecificAndUsedByExpansion()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseDescriptorContributor, DanglingRefContributor>();
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();
        var descriptors = provider.GetRequiredService<IBaseDescriptorProvider>();

        var publicManifest = await descriptors.GetManifestAsync(ManifestRequest(VisibilityLevel.Public));
        var adminManifest = await descriptors.GetManifestAsync(ManifestRequest(VisibilityLevel.Admin));
        var publicExpanded = await descriptors.GetExpandedManifestAsync(new BaseManifestExpansionRequest
        {
            Principal = RuntimeTestData.AnonymousPrincipal,
            Operation = RuntimeTestData.Operation(BaseOperationKind.SchemaRead),
            View = VisibilityLevel.Public,
            Expand = ["schema"]
        });

        Assert.False(string.IsNullOrWhiteSpace(publicManifest.Value!.ETag));
        Assert.False(string.IsNullOrWhiteSpace(adminManifest.Value!.ETag));
        Assert.NotEqual(publicManifest.Value.ETag, adminManifest.Value.ETag);
        Assert.Equal(publicManifest.Value.ETag, publicExpanded.Value!.ETag);
        Assert.Equal(publicManifest.Value.ETag, publicExpanded.Value.Manifest.ETag);
    }

    private sealed class SensitiveSchemaContributor : IBaseDescriptorContributor
    {
        public string Id => "sensitive-schema";

        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            builder.AddCollection(new CollectionDefinition
            {
                Id = "items",
                Name = "items",
                Kind = BaseCollectionKinds.Document,
                SchemaMode = SchemaMode.Loose,
                UnknownFields = UnknownFieldPolicy.Preserve,
                Store = new StoreAnnotation
                {
                    StoreId = "primary",
                    NativeName = "native_items"
                },
                Fields =
                [
                    new FieldDefinition
                    {
                        Id = "title",
                        Name = "title",
                        Type = BaseFieldTypes.String,
                        Store = new StoreAnnotation { NativeName = "native_title" }
                    },
                    new FieldDefinition
                    {
                        Id = "secret",
                        Name = "secret",
                        Type = BaseFieldTypes.String,
                        Hidden = true,
                        Visibility = new FieldVisibilityAnnotation { Visibility = VisibilityLevel.Admin }
                    },
                    new FieldDefinition
                    {
                        Id = "systemId",
                        Name = "systemId",
                        Type = BaseFieldTypes.String,
                        System = true
                    }
                ],
                Extensions = new Dictionary<string, JsonElement>()
            });
        }
    }

    private static BaseManifestRequest ManifestRequest(VisibilityLevel view) => new()
    {
        Principal = RuntimeTestData.AnonymousPrincipal,
        Operation = RuntimeTestData.Operation(BaseOperationKind.SchemaRead),
        View = view
    };

    private sealed class DanglingRefContributor : IBaseDescriptorContributor
    {
        public string Id => "dangling-refs";

        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            builder.AddDtoContract(new DtoContractDescriptor
            {
                Id = "public.dto",
                ContractVersion = "1.0.0",
                Visibility = VisibilityLevel.Public
            });
            builder.AddDtoContract(new DtoContractDescriptor
            {
                Id = "admin.dto",
                ContractVersion = "1.0.0",
                Visibility = VisibilityLevel.Admin
            });
            builder.AddModule(new BaseModuleDescriptor
            {
                Id = "module.records",
                Name = "Records",
                Kind = BaseModuleKind.Core,
                Version = "1.0.0",
                Status = ModuleStatus.Installed,
                Visibility = VisibilityLevel.Public,
                ContributedDtoIds = ["public.dto", "admin.dto"],
                ContributedRouteIds = ["public.route", "admin.route"]
            });
            builder.AddProjection(new ProjectionDescriptor
            {
                Id = "aspnet",
                Kind = ProjectionKind.AspNet,
                PackageId = "test",
                PackageVersion = "1.0.0",
                ContractVersionRange = "*",
                Status = ProjectionStatus.Available,
                Visibility = VisibilityLevel.Public,
                Routes =
                [
                    new RouteDescriptor
                    {
                        OperationId = "public.route",
                        Method = HttpMethodKind.Get,
                        Path = "/public",
                        ResponseDtoId = "public.dto",
                        Visibility = VisibilityLevel.Public
                    },
                    new RouteDescriptor
                    {
                        OperationId = "admin.route",
                        Method = HttpMethodKind.Get,
                        Path = "/admin",
                        ResponseDtoId = "admin.dto",
                        Visibility = VisibilityLevel.Public
                    }
                ]
            });
        }
    }
}
