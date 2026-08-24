using System.Reflection;

namespace HPD.Base.Tests.InMemory.Contracts;

public sealed class PublicApiShapeTests
{
    private static readonly HashSet<string> AllowedNamespaces = new(StringComparer.Ordinal)
    {
        "HPD.Base",
        "HPD.Base.Configuration",
        "HPD.Base.DependencyInjection"
    };

    [Fact]
    public void PublicTypesStayWithinApprovedNamespaces()
    {
        var unexpected = typeof(InMemoryRecordStore).Assembly
            .GetExportedTypes()
            .Select(type => type.Namespace ?? string.Empty)
            .Distinct()
            .Where(ns => !AllowedNamespaces.Contains(ns))
            .ToArray();

        unexpected.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Microsoft.AspNetCore")]
    [InlineData("HPD.Auth")]
    [InlineData("EntityFramework")]
    [InlineData("Npgsql")]
    [InlineData("SignalR")]
    [InlineData("GraphQL")]
    [InlineData("OpenApi")]
    public void DoesNotReferenceDeferredOrHostedPackages(string forbiddenPrefix)
    {
        var unexpected = typeof(InMemoryRecordStore).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith(forbiddenPrefix, StringComparison.Ordinal))
            .ToArray();

        unexpected.Should().BeEmpty();
    }

    [Fact]
    public void StoreImplementsOnlyCurrentStoreInterfaces()
    {
        typeof(InMemoryRecordStore)
            .GetInterfaces()
            .Where(type => type.Assembly == typeof(IRecordStore).Assembly)
            .Should()
            .BeEquivalentTo(
            [
                typeof(IRecordStore),
                typeof(IRecordMutationStore),
                typeof(IAtomicRecordStore),
                typeof(IStreamingRecordStore),
                typeof(IRelationalReadStore),
                typeof(IConsistentRecordIncludeStore),
                typeof(IInMemoryProjectionAuthority),
                typeof(ITransactionalMutationJournalStore),
                typeof(IBaseSubjectAdministration),
                typeof(IBaseSubjectPublicationStore),
                typeof(IBaseSubjectValidationPlanReceiptStore),
                typeof(IBaseSubjectLifecycleStore),
                typeof(IBaseSubjectRetirementStore),
                typeof(IBaseSubjectAuthorityMaintenanceStore),
                typeof(IBaseActivationProvider),
                typeof(IBaseSemanticActivationCapabilityProvider),
                typeof(IBaseStudioDynamicStoreAuthoritySource),
                typeof(IBaseStudioControlInspectionStore),
                typeof(IBaseStudioEvidenceStore),
                typeof(IBaseStudioInfrastructureInventoryStore)
            ]);
    }

    [Theory]
    [InlineData("CreateAsync")]
    [InlineData("PatchAsync")]
    [InlineData("ReplaceAsync")]
    [InlineData("DeleteAsync")]
    [InlineData("PatchIfRevisionAsync")]
    [InlineData("ReplaceIfRevisionAsync")]
    public void StoreExposesNoLegacyDirectMutationMethods(string methodName)
    {
        typeof(InMemoryRecordStore)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)
            .Should()
            .BeNull();
    }

    [Fact]
    public void ProjectionAuthorityAndAllStateMachineryRemainInternalAndUnresolvable()
    {
        Type[] infrastructure =
        [
            typeof(IInMemoryAtomicMutationProjection),
            typeof(IInMemoryProjectionAuthority),
            typeof(IInMemoryProjectionReadSession),
            typeof(IInMemoryProjectionReplacement),
            typeof(BaseInMemoryProjectionSnapshot),
            typeof(BaseInMemoryProjectionStateReader),
            typeof(BaseInMemoryProjectionStateWriter),
            typeof(BaseInMemoryProjectionIndexHandle),
            typeof(BaseInMemoryProjectionSourceCursor),
            typeof(BaseInMemoryProjectionSourceRecord),
            typeof(BaseInMemoryProjectionSourcePage),
        ];
        infrastructure.Should().OnlyContain(static type => !type.IsPublic && !type.IsNestedPublic);

        var services = new ServiceCollection();
        services.AddHPDBase(_ => { });
        using ServiceProvider provider = services.BuildServiceProvider();
        provider.GetService<IInMemoryProjectionAuthority>().Should().BeNull();
        provider.GetService<IInMemoryAtomicMutationProjection>().Should().BeNull();
        provider.GetService<IInMemoryProjectionReadSession>().Should().BeNull();
        provider.GetService<IInMemoryProjectionReplacement>().Should().BeNull();
    }

    [Fact]
    public void NoVectorSchemaAllocatesNoVectorContributorIdentityOrServices()
    {
        var services = new ServiceCollection();
        services.AddHPDBase(_ => { });
        using ServiceProvider provider = services.BuildServiceProvider();
        InMemoryRecordStore store = provider.GetRequiredService<InMemoryRecordStore>();

        provider.GetServices<IBaseVectorProvider>().Should().BeEmpty();
        provider.GetServices<IBaseVectorAuthority>().Should().BeEmpty();
        typeof(InMemoryRecordStore).GetField("_mutationProjection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store).Should().BeNull();
        typeof(InMemoryRecordStore).GetField("_vectorIdentityDigest", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store).Should().BeNull();
    }
}
