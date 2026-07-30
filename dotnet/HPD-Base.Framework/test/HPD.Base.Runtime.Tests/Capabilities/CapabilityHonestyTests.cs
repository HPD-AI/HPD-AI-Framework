using HPD.Base.Descriptors;
using HPD.Base.Runtime.DependencyInjection;
using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Stores;
using HPD.Base.Schema;
using HPD.Base.Stores;
using HPD.Base.Results;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Runtime.Tests.Capabilities;

public sealed class CapabilityHonestyTests
{
    [Fact]
    public async Task CollectionOperationCannotExceedRegisteredStoreOperations()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseDescriptorContributor>(new OperationContributor());
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IRecordStoreRegistry>().Add(new RecordStoreRegistration
        {
            StoreId = "primary",
            Store = new FakeRecordStore("primary", mutation: new RecordMutationCapability
            {
                Create = false,
                Patch = true,
                Replace = true,
                Delete = true,
                IdAuthority = IdAuthority.Hybrid,
                TimestampAuthority = TimestampAuthority.Runtime,
                Consistency = ConsistencyModel.Strong
            }),
            CollectionIds = ["items"]
        });

        var snapshot = await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        Assert.False(snapshot.Validation.Succeeded);
        Assert.Contains(snapshot.Validation.Issues!, issue => issue.Code == "base.runtime.capability.crud.unsupported");
    }

    [Fact]
    public async Task RevisionPatchClaimRequiresRevisionedStore()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseDescriptorContributor>(new RevisionContributor());
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IRecordStoreRegistry>().Add(new RecordStoreRegistration
        {
            StoreId = "primary",
            Store = new FakeRecordStore("primary"),
            CollectionIds = ["items"]
        });

        var snapshot = await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        Assert.False(snapshot.Validation.Succeeded);
        Assert.Contains(snapshot.Validation.Issues!, issue => issue.Code == "base.runtime.capability.revision.operationUnsupported");
    }

    [Fact]
    public async Task RevisionDeleteClaimRequiresAdvertisedAtomicDelete()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseDescriptorContributor>(new RevisionDeleteContributor());
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IRecordStoreRegistry>().Add(new RecordStoreRegistration
        {
            StoreId = "primary",
            Store = new FakeRevisionedRecordStore("primary"),
            CollectionIds = ["items"]
        });

        var snapshot = await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        Assert.False(snapshot.Validation.Succeeded);
        Assert.Contains(snapshot.Validation.Issues!, issue => issue.Code == "base.runtime.capability.revision.operationUnsupported");
    }

    [Fact]
    public async Task StreamingClaimRequiresStreamingStore()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseDescriptorContributor>(new StreamingContributor());
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IRecordStoreRegistry>().Add(new RecordStoreRegistration
        {
            StoreId = "primary",
            Store = new FakeRecordStore("primary"),
            CollectionIds = ["items"]
        });

        var snapshot = await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        Assert.False(snapshot.Validation.Succeeded);
        Assert.Contains(snapshot.Validation.Issues!, issue => issue.Code == "base.runtime.capability.streaming.interfaceMismatch");
    }

    private sealed class OperationContributor : IBaseDescriptorContributor
    {
        public string Id => "crud";

        public void Contribute(IBaseDescriptorContributionBuilder builder) => builder.AddCollection(Collection());
    }

    private sealed class RevisionContributor : IBaseDescriptorContributor
    {
        public string Id => "revision";

        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            builder.AddCollection(Collection());
            builder.AddCapabilities(new CapabilityDescriptor
            {
                DescriptorVersion = "test",
                RuntimeId = "runtime",
                Families =
                [
                    new CapabilityFamilyDescriptor
                    {
                        FamilyId = "store.revision",
                        FamilyVersion = "1.0",
                        Status = CapabilityStatus.Available,
                        Features =
                        [
                            new CapabilityFeatureDescriptor
                            {
                                FeatureId = "store.revision.patch",
                                Version = "1.0",
                                Status = CapabilityStatus.Available,
                                SupportLevel = SupportLevel.Required,
                                Scope = CapabilityScope.Collection,
                                AppliesTo = ["items"],
                                Constraints = new CapabilityConstraintSet
                                {
                                    StoreRevision = new StoreRevisionCapabilityConstraints
                                    {
                                        Patch = true,
                                        Guarantee = RevisionGuarantee.Store
                                    }
                                }
                            }
                        ]
                    }
                ]
            });
        }
    }

    private sealed class StreamingContributor : IBaseDescriptorContributor
    {
        public string Id => "streaming";

        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            builder.AddCollection(Collection());
            builder.AddCapabilities(new CapabilityDescriptor
            {
                DescriptorVersion = "test",
                RuntimeId = "runtime",
                Families =
                [
                    new CapabilityFamilyDescriptor
                    {
                        FamilyId = "store.streaming",
                        FamilyVersion = "1.0",
                        Status = CapabilityStatus.Available,
                        Features =
                        [
                            new CapabilityFeatureDescriptor
                            {
                                FeatureId = "store.streaming.records",
                                Version = "1.0",
                                Status = CapabilityStatus.Available,
                                SupportLevel = SupportLevel.Required,
                                Scope = CapabilityScope.Collection,
                                AppliesTo = ["items"],
                                Constraints = new CapabilityConstraintSet
                                {
                                    StoreStreaming = new StoreStreamingCapabilityConstraints
                                    {
                                        MaxItems = 100
                                    }
                                }
                            }
                        ]
                    }
                ]
            });
        }
    }

    private sealed class RevisionDeleteContributor : IBaseDescriptorContributor
    {
        public string Id => "revision-delete";

        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            builder.AddCollection(Collection());
            builder.AddCapabilities(new CapabilityDescriptor
            {
                DescriptorVersion = "test",
                RuntimeId = "runtime",
                Families =
                [
                    new CapabilityFamilyDescriptor
                    {
                        FamilyId = "store.revision",
                        FamilyVersion = "1.0",
                        Status = CapabilityStatus.Available,
                        Features =
                        [
                            new CapabilityFeatureDescriptor
                            {
                                FeatureId = "store.revision.delete",
                                Version = "1.0",
                                Status = CapabilityStatus.Available,
                                SupportLevel = SupportLevel.Required,
                                Scope = CapabilityScope.Collection,
                                AppliesTo = ["items"],
                                Constraints = new CapabilityConstraintSet
                                {
                                    StoreRevision = new StoreRevisionCapabilityConstraints
                                    {
                                        Delete = true,
                                        Guarantee = RevisionGuarantee.Store
                                    }
                                }
                            }
                        ]
                    }
                ]
            });
        }
    }

    private static CollectionDefinition Collection() => new()
    {
        Id = "items",
        Name = "items",
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve,
        Operations = new CollectionOperationMatrix
        {
            List = true,
            Get = true,
            Create = true,
            Patch = true,
            Replace = true,
            Delete = true
        }
    };
}
