using System.Reflection;
using System.Xml.Linq;

namespace HPD.Base.Auth.HPDAuth.Tests.Packaging;

public sealed class PackageBoundaryTests
{
    [Fact]
    public void CoreProjectDoesNotReferenceAspNetCoreOrHPDAuthRuntimePackages()
    {
        var document = XDocument.Load(ProjectPath("src", "HPD.Base.Auth.HPDAuth", "HPD.Base.Auth.HPDAuth.csproj"));
        var references = document
            .Descendants()
            .Where(element => element.Name.LocalName is "ProjectReference" or "FrameworkReference" or "PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();

        references.Should().NotContain(reference => reference.Contains("AspNetCore", StringComparison.OrdinalIgnoreCase));
        references.Should().NotContain(reference => reference.Contains("HPD.Auth", StringComparison.OrdinalIgnoreCase));
        references.Should().NotContain(reference => reference.Contains("EntityFramework", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AspNetCoreProjectOnlyReferencesHPDAuthCore()
    {
        var document = XDocument.Load(ProjectPath("src", "HPD.Base.Auth.HPDAuth.AspNetCore", "HPD.Base.Auth.HPDAuth.AspNetCore.csproj"));
        var references = document
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .Where(reference => reference.Contains("HPD.Auth", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        references.Should().ContainSingle();
        references[0].Should().EndWith("HPD.Auth.Core.csproj");
    }

    [Fact]
    public void CorePublicSurfaceStaysInsideAdapterNamespace()
    {
        var outsideNamespace = PublicTypes(typeof(HPDAuthBaseSubjectMapper).Assembly)
            .Where(static type => type.Namespace is null || !type.Namespace.StartsWith("HPD.Base.Auth.HPDAuth", StringComparison.Ordinal))
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
