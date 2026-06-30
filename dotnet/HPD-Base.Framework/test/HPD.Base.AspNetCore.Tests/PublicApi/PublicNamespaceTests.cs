namespace HPD.Base.AspNetCore.Tests.PublicApi;

public sealed class PublicNamespaceTests
{
    [Fact]
    public void ExportedTypesStayInApprovedNamespaces()
    {
        var approved = new[]
        {
            "HPD.Base.AspNetCore",
            "HPD.Base.AspNetCore.Configuration",
            "HPD.Base.AspNetCore.DependencyInjection",
            "HPD.Base.AspNetCore.EndpointMapping",
            "HPD.Base.AspNetCore.Http",
            "HPD.Base.AspNetCore.QueryBinding",
            "HPD.Base.AspNetCore.Results",
            "HPD.Base.AspNetCore.Serialization"
        };

        var namespaces = typeof(HPDBaseEndpointRouteBuilderExtensions).Assembly
            .GetExportedTypes()
            .Select(type => type.Namespace)
            .Distinct()
            .ToArray();

        namespaces.Should().NotContainNulls();
        namespaces.All(ns => approved.Contains(ns!, StringComparer.Ordinal)).Should().BeTrue();
    }
}
