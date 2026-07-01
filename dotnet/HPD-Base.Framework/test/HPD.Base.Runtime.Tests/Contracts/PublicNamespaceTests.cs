namespace HPD.Base.Runtime.Tests.Contracts;

public sealed class PublicNamespaceTests
{
    private static readonly HashSet<string> AllowedNamespaces = new(StringComparer.Ordinal)
    {
        "HPD.Base.Runtime",
        "HPD.Base.Runtime.Builder",
        "HPD.Base.Runtime.Configuration",
        "HPD.Base.Runtime.DependencyInjection",
        "HPD.Base.Runtime.Descriptors",
        "HPD.Base.Runtime.Capabilities",
        "HPD.Base.Runtime.Schema",
        "HPD.Base.Runtime.Stores",
        "HPD.Base.Runtime.Operations",
        "HPD.Base.Runtime.Query",
        "HPD.Base.Runtime.Policy",
        "HPD.Base.Runtime.Policy.Admin",
        "HPD.Base.Runtime.Results",
        "HPD.Base.Runtime.Events",
        "HPD.Base.Runtime.Health",
        "HPD.Base.Runtime.Serialization",
        "HPD.Base.Runtime.Observability"
    };

    [Fact]
    public void PublicTypesStayWithinApprovedRuntimeNamespaces()
    {
        var unexpected = typeof(IHPDBaseRuntime).Assembly
            .GetExportedTypes()
            .Select(type => type.Namespace ?? string.Empty)
            .Distinct()
            .Where(ns => !AllowedNamespaces.Contains(ns))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unexpected);
    }
}
