using HPD.Base;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests.Descriptors;

public sealed class DescriptorContributionTests
{
    [Fact]
    public async Task ExplicitContributorsComposeIntoSnapshot()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseDescriptorContributor>(new CollectionContributor("items"));
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();

        var snapshot = await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        Assert.Single(snapshot.Schema.Collections!);
        Assert.Equal("items", snapshot.Schema.Collections![0].Id);
        Assert.Single(snapshot.Manifest.Collections!);
    }

    [Fact]
    public async Task DuplicateCollectionIdsFailValidation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseDescriptorContributor>(new CollectionContributor("items"));
        services.AddSingleton<IBaseDescriptorContributor>(new CollectionContributor("items"));
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();

        var snapshot = await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        Assert.False(snapshot.Validation.Succeeded);
        Assert.Contains(snapshot.Validation.Issues!, issue => issue.Kind == BaseRuntimeValidationFailureKind.DuplicateId);
    }

    [Fact]
    public async Task DuplicateCollectionFieldNamesFailValidation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseDescriptorContributor>(new FieldContributor());
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();

        var snapshot = await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        Assert.False(snapshot.Validation.Succeeded);
        Assert.Contains(snapshot.Validation.Issues!, issue => issue.Code == "base.runtime.descriptor.duplicateField");
    }

    [Fact]
    public async Task MissingRelationTargetCollectionFailsValidation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseDescriptorContributor>(new RelationContributor());
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();

        var snapshot = await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        Assert.False(snapshot.Validation.Succeeded);
        Assert.Contains(snapshot.Validation.Issues!, issue => issue.Code == "base.runtime.descriptor.unresolvedRelation");
    }

    [Fact]
    public async Task DuplicateCollectionIndexNamesFailValidation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseDescriptorContributor>(new DuplicateIndexContributor());
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();

        var snapshot = await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        Assert.False(snapshot.Validation.Succeeded);
        Assert.Contains(snapshot.Validation.Issues!, issue => issue.Code == "base.runtime.descriptor.duplicateIndex");
    }

    [Fact]
    public async Task MissingIndexFieldFailsValidation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseDescriptorContributor>(new MissingIndexFieldContributor());
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();

        var snapshot = await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        Assert.False(snapshot.Validation.Succeeded);
        Assert.Contains(snapshot.Validation.Issues!, issue => issue.Code == "base.runtime.descriptor.unresolvedIndexField");
    }

    [Fact]
    public async Task RouteMissingDtoFailsValidation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseDescriptorContributor>(new RouteMissingDtoContributor());
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();

        var snapshot = await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        Assert.False(snapshot.Validation.Succeeded);
        Assert.Contains(snapshot.Validation.Issues!, issue => issue.Code == "base.runtime.descriptor.unresolvedDto");
    }

    [Fact]
    public async Task DuplicateCapabilityFeaturesFailValidation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseDescriptorContributor>(new DuplicateCapabilityFeatureContributor());
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();

        var snapshot = await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        Assert.False(snapshot.Validation.Succeeded);
        Assert.Contains(snapshot.Validation.Issues!, issue => issue.Kind == BaseRuntimeValidationFailureKind.DuplicateId);
    }

    [Fact]
    public async Task ModuleMissingContributedDtoFailsValidation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseDescriptorContributor>(new ModuleMissingContributionContributor());
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();

        var snapshot = await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        Assert.False(snapshot.Validation.Succeeded);
        Assert.Contains(snapshot.Validation.Issues!, issue => issue.Code == "base.runtime.descriptor.unresolvedDto");
    }

    [Fact]
    public async Task ControlCharacterIdsFailValidation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseDescriptorContributor>(new InvalidIdContributor());
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();

        var snapshot = await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        Assert.False(snapshot.Validation.Succeeded);
        Assert.Contains(snapshot.Validation.Issues!, issue => issue.Code == "base.runtime.descriptor.invalidId");
    }

    [Fact]
    public async Task RequiredDependencyOnUnavailableFeatureFailsValidation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseDescriptorContributor>(new UnavailableDependencyContributor());
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();

        var snapshot = await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        Assert.False(snapshot.Validation.Succeeded);
        Assert.Contains(snapshot.Validation.Issues!, issue => issue.Kind == BaseRuntimeValidationFailureKind.CapabilityDependencyConflict);
    }

    private sealed class CollectionContributor : IBaseDescriptorContributor
    {
        private readonly string _collectionId;

        public CollectionContributor(string collectionId)
        {
            _collectionId = collectionId;
        }

        public string Id => $"contributor.{_collectionId}";

        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            builder.AddCollection(new CollectionDefinition
            {
                Id = _collectionId,
                Name = _collectionId,
                Kind = BaseCollectionKinds.Document,
                SchemaMode = SchemaMode.Loose,
                UnknownFields = UnknownFieldPolicy.Preserve
            });

            builder.AddCapabilities(new CapabilityDescriptor
            {
                DescriptorVersion = "test",
                RuntimeId = "runtime",
                Families = []
            });
        }
    }

    private sealed class FieldContributor : IBaseDescriptorContributor
    {
        public string Id => "fields";

        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            builder.AddCollection(BaseCollection() with
            {
                Fields =
                [
                    new FieldDefinition { Id = "title", Name = "title", Type = BaseFieldTypes.String },
                    new FieldDefinition { Id = "title2", Name = "title", Type = BaseFieldTypes.String }
                ]
            });
        }
    }

    private sealed class RelationContributor : IBaseDescriptorContributor
    {
        public string Id => "relations";

        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            builder.AddCollection(BaseCollection() with
            {
                Fields =
                [
                    new FieldDefinition
                    {
                        Id = "ownerId",
                        Name = "ownerId",
                        Type = BaseFieldTypes.String,
                        Relation = new RelationDefinition
                        {
                            Id = "item-owner",
                            SourceCollectionId = "items",
                            SourceFieldId = "ownerId",
                            TargetCollectionId = "users"
                        }
                    }
                ]
            });
        }
    }

    private sealed class DuplicateIndexContributor : IBaseDescriptorContributor
    {
        public string Id => "duplicate-indexes";

        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            builder.AddCollection(BaseCollection() with
            {
                Fields =
                [
                    new FieldDefinition { Id = "title", Name = "title", Type = BaseFieldTypes.String }
                ],
                Indexes =
                [
                    Index("title_idx", "titleIndex", "title"),
                    Index("title_idx_2", "titleIndex", "title")
                ]
            });
        }
    }

    private sealed class MissingIndexFieldContributor : IBaseDescriptorContributor
    {
        public string Id => "missing-index-field";

        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            builder.AddCollection(BaseCollection() with
            {
                Fields =
                [
                    new FieldDefinition { Id = "title", Name = "title", Type = BaseFieldTypes.String }
                ],
                Indexes =
                [
                    Index("missing_idx", "missingIndex", "missing")
                ]
            });
        }
    }

    private sealed class RouteMissingDtoContributor : IBaseDescriptorContributor
    {
        public string Id => "route-missing-dto";

        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            builder.AddProjection(new ProjectionDescriptor
            {
                Id = "aspnet",
                Kind = ProjectionKind.AspNet,
                PackageId = "test",
                PackageVersion = "1.0.0",
                ContractVersionRange = "*",
                Status = ProjectionStatus.Available,
                Routes =
                [
                    new RouteDescriptor
                    {
                        OperationId = "items.list",
                        Method = HttpMethodKind.Get,
                        Path = "/items",
                        ResponseDtoId = "missing.dto"
                    }
                ]
            });
        }
    }

    private sealed class DuplicateCapabilityFeatureContributor : IBaseDescriptorContributor
    {
        public string Id => "duplicate-capability-feature";

        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            builder.AddCapabilities(new CapabilityDescriptor
            {
                DescriptorVersion = "test",
                RuntimeId = "runtime",
                Families =
                [
                    new CapabilityFamilyDescriptor
                    {
                        FamilyId = "records",
                        FamilyVersion = "1.0.0",
                        Status = CapabilityStatus.Available,
                        Features =
                        [
                            Feature("records.patch"),
                            Feature("records.patch")
                        ]
                    }
                ]
            });
        }
    }

    private sealed class ModuleMissingContributionContributor : IBaseDescriptorContributor
    {
        public string Id => "module-missing-contribution";

        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            builder.AddModule(new BaseModuleDescriptor
            {
                Id = "module.records",
                Name = "Records",
                Kind = BaseModuleKind.Core,
                Version = "1.0.0",
                Status = ModuleStatus.Installed,
                ContributedDtoIds = ["missing.dto"]
            });
        }
    }

    private sealed class InvalidIdContributor : IBaseDescriptorContributor
    {
        public string Id => "invalid-id";

        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            builder.AddDtoContract(new DtoContractDescriptor
            {
                Id = "bad\nid",
                ContractVersion = "1.0.0"
            });
        }
    }

    private sealed class UnavailableDependencyContributor : IBaseDescriptorContributor
    {
        public string Id => "unavailable-dependency";

        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            builder.AddCapabilities(new CapabilityDescriptor
            {
                DescriptorVersion = "test",
                RuntimeId = "runtime",
                Families =
                [
                    new CapabilityFamilyDescriptor
                    {
                        FamilyId = "records",
                        FamilyVersion = "1.0.0",
                        Status = CapabilityStatus.Available,
                        Features =
                        [
                            Feature("records.patch") with { Status = CapabilityStatus.Disabled }
                        ],
                        Dependencies =
                        [
                            new CapabilityDependencyDescriptor
                            {
                                FeatureId = "records.patch",
                                Required = true
                            }
                        ]
                    }
                ]
            });
        }
    }

    private static CollectionDefinition BaseCollection() => new()
    {
        Id = "items",
        Name = "items",
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve
    };

    private static IndexDefinition Index(string id, string name, string fieldPath) => new()
    {
        Id = id,
        Name = name,
        CollectionId = "items",
        Kind = IndexKind.Key,
        Parts =
        [
            new IndexPart
            {
                Kind = IndexPartKind.Field,
                FieldId = fieldPath
            }
        ]
    };

    private static CapabilityFeatureDescriptor Feature(string id) => new()
    {
        FeatureId = id,
        Version = "1.0.0",
        Status = CapabilityStatus.Available,
        SupportLevel = SupportLevel.Required,
        Scope = CapabilityScope.Runtime
    };
}
