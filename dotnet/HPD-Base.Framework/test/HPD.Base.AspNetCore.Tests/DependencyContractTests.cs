using System.Xml.Linq;

namespace HPD.Base.AspNetCore.Tests;

public sealed class DependencyContractTests
{
    [Fact]
    public void SourceProjectReferencesRuntimeAndAspNetOnly()
    {
        var project = XDocument.Load(ProjectPath());

        project.Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Should()
            .BeEquivalentTo(["../HPD.Base.Runtime/HPD.Base.Runtime.csproj"]);

        project.Descendants("FrameworkReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Should()
            .BeEquivalentTo(["Microsoft.AspNetCore.App"]);

        var packages = project.Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .ToArray();

        packages.Should().BeEquivalentTo(["Microsoft.AspNetCore.OpenApi", "Microsoft.OpenApi"]);
        packages.Where(package => package is not null)
            .Should()
            .NotContain(package => package!.Contains("Swashbuckle", StringComparison.OrdinalIgnoreCase));
    }

    private static string ProjectPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HPD-Base.slnx")))
            directory = directory.Parent;

        directory.Should().NotBeNull();
        return Path.Combine(directory!.FullName, "src/HPD.Base.AspNetCore/HPD.Base.AspNetCore.csproj");
    }
}
