using System.Reflection;

namespace HPD.Base.InMemory.Tests.Contracts;

public sealed class PublicApiShapeTests
{
    private static readonly HashSet<string> AllowedNamespaces = new(StringComparer.Ordinal)
    {
        "HPD.Base.InMemory",
        "HPD.Base.InMemory.Configuration",
        "HPD.Base.InMemory.DependencyInjection"
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
            .BeEquivalentTo([typeof(IRecordStore), typeof(IRevisionedRecordStore), typeof(IStreamingRecordStore)]);
    }
}
