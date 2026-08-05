using System.Reflection;
using System.Xml.Linq;

namespace HPD.Base.Auth.Tests.Packaging;

public sealed class PackageBoundaryTests
{
    [Fact]
    public void AuthPackageReferencesOnlyItsRequiredHostBoundaries()
    {
        var document = XDocument.Load(ProjectPath("src", "HPD.Base.Auth", "HPD.Base.Auth.csproj"));
        var references = document
            .Descendants()
            .Where(element => element.Name.LocalName is "ProjectReference" or "FrameworkReference" or "PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();

        references.Should().NotContain(reference => reference.Contains("EntityFramework", StringComparison.OrdinalIgnoreCase));
        references.Should().Contain(reference => reference.EndsWith("HPD.Auth.Core.csproj", StringComparison.Ordinal));
        references.Should().Contain(reference => reference.EndsWith("HPD.Base.AspNetCore.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public void CorePublicSurfaceStaysInsideAdapterNamespace()
    {
        var outsideNamespace = PublicTypes(typeof(HPDAuthBaseSubjectMapper).Assembly)
            .Where(static type => type.Namespace != "HPD.Base.Auth")
            .ToArray();

        outsideNamespace.Should().BeEmpty();
    }

    [Fact]
    public void CorePublicSurfaceDoesNotExposeDeferredFeatureTypes()
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

        var publicNames = PublicTypes(typeof(HPDAuthBaseSubjectMapper).Assembly)
            .Select(type => type.FullName!)
            .ToArray();

        foreach (var fragment in forbiddenFragments)
            publicNames.Should().NotContain(name => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<Type> PublicTypes(Assembly assembly) =>
        assembly.GetExportedTypes().Where(static type => type.IsPublic || type.IsNestedPublic);

    private static string ProjectPath(params string[] segments)
    {
        var root = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(root) && !File.Exists(Path.Combine(root, "HPD-Base.slnx")))
            root = Directory.GetParent(root)?.FullName ?? string.Empty;

        root.Should().NotBeNullOrEmpty();
        return Path.Combine(new[] { root }.Concat(segments).ToArray());
    }
}
