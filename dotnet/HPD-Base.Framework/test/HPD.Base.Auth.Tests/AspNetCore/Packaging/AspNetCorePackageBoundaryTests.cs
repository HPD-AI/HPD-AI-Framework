using System.Reflection;

namespace HPD.Base.Auth.Tests.AspNetCore.Packaging;

public sealed class AspNetCorePackageBoundaryTests
{
    [Fact]
    public void AspNetCorePublicSurfaceStaysInsideAdapterNamespace()
    {
        var outsideNamespace = PublicTypes(typeof(HPDAuthBaseHttpPrincipalMapper).Assembly)
            .Where(static type => type.Namespace != "HPD.Base.Auth")
            .ToArray();

        outsideNamespace.Should().BeEmpty();
    }

    [Fact]
    public void AspNetCorePublicSurfaceDoesNotExposeDeferredFeatureTypes()
    {
        var forbiddenFragments = new[]
        {
            "Login",
            "Signup",
            "Register",
            "Logout",
            "Token",
            "SessionStore",
            "OAuth",
            "Mfa",
            "Passkey",
            "Studio",
            "GraphQL",
            "File",
            "Realtime",
            "Batch",
            "Upsert",
            "Search",
            "Vector",
            "Migration"
        };

        var publicNames = PublicTypes(typeof(HPDAuthBaseHttpPrincipalMapper).Assembly)
            .Select(type => type.FullName!)
            .ToArray();

        foreach (var fragment in forbiddenFragments)
            publicNames.Should().NotContain(name => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<Type> PublicTypes(Assembly assembly) =>
        assembly.GetExportedTypes().Where(static type => type.IsPublic || type.IsNestedPublic);
}
