using System.Reflection;

namespace HPD.Base.Tests.Volatile.Contracts;

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
        var unexpected = typeof(VolatileRecordStore).Assembly
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
        var unexpected = typeof(VolatileRecordStore).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith(forbiddenPrefix, StringComparison.Ordinal))
            .ToArray();

        unexpected.Should().BeEmpty();
    }

    [Fact]
    public void StoreImplementsOnlyCurrentStoreInterfaces()
    {
        typeof(VolatileRecordStore)
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
                typeof(IConsistentRecordIncludeStore)
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
        typeof(VolatileRecordStore)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)
            .Should()
            .BeNull();
    }
}
