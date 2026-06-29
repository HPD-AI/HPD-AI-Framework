namespace HPD.Base.Abstractions.Tests.Contracts;

public sealed class PublicNamespaceTests
{
    private static readonly HashSet<string> AllowedNamespaces = new(StringComparer.Ordinal)
    {
        "HPD.Base",
        "HPD.Base.Descriptors",
        "HPD.Base.Schema",
        "HPD.Base.Records",
        "HPD.Base.Query",
        "HPD.Base.Policy",
        "HPD.Base.Runtime",
        "HPD.Base.Stores",
        "HPD.Base.Results",
        "HPD.Base.Events",
        "HPD.Base.Health",
        "HPD.Base.Serialization"
    };

    [Fact]
    public void PublicTypesStayWithinApprovedNamespaces()
    {
        var unexpected = typeof(RecordId).Assembly
            .GetExportedTypes()
            .Select(type => type.Namespace ?? string.Empty)
            .Distinct()
            .Where(ns => !AllowedNamespaces.Contains(ns))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unexpected);
    }
}
