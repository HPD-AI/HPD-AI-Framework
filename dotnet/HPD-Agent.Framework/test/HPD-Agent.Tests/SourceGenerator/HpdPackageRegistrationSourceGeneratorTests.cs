using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace HPD.Agent.Tests.SourceGenerator;

public sealed class HpdPackageRegistrationSourceGeneratorTests
{
    private static (string GeneratedCode, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "HpdPackageRegistrationGeneratorTests",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new global::HpdPackageRegistrationSourceGenerator();
        CSharpGeneratorDriver.Create(
                generators: new ISourceGenerator[] { generator.AsSourceGenerator() },
                additionalTexts: Enumerable.Empty<AdditionalText>(),
                parseOptions: parseOptions,
                optionsProvider: null)
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var generatedCode = string.Join(
            "\n\n",
            outputCompilation.SyntaxTrees
                .Where(static tree => tree.FilePath.Contains("g.cs"))
                .Select(static tree => tree.GetText().ToString()));

        return (generatedCode, diagnostics);
    }

    [Fact]
    public void RegisteredPackage_GeneratesModuleInitializerRegistration()
    {
        var source = """
using HPD.Agent.Packages;

namespace TestPackages;

[HpdPackageRegistration]
public sealed class WeatherPackage : HpdPackage
{
    public override HpdPackageManifest Manifest { get; } = new()
    {
        Id = "test.weather",
        DisplayName = "Weather",
        Version = new System.Version(1, 0, 0)
    };

    public override void Configure(IHpdPackageBuilder builder)
    {
    }
}
""";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Contains("ModuleInitializer", generatedCode);
        Assert.Contains("HpdPackageRegistry.Register<global::TestPackages.WeatherPackage>();", generatedCode);
    }

    [Fact]
    public void RegisteredNonPackage_ReportsDiagnostic()
    {
        var source = """
using HPD.Agent.Packages;

[HpdPackageRegistration]
public sealed class NotAPackage
{
}
""";

        var (_, diagnostics) = RunGenerator(source);

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Id == "HPDPKG001");
    }

    [Fact]
    public void RegisteredPackageWithoutPublicParameterlessConstructor_ReportsDiagnostic()
    {
        var source = """
using HPD.Agent.Packages;

[HpdPackageRegistration]
public sealed class WeatherPackage : HpdPackage
{
    public WeatherPackage(string id)
    {
    }

    public override HpdPackageManifest Manifest { get; } = new()
    {
        Id = "test.weather",
        DisplayName = "Weather",
        Version = new System.Version(1, 0, 0)
    };

    public override void Configure(IHpdPackageBuilder builder)
    {
    }
}
""";

        var (_, diagnostics) = RunGenerator(source);

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Id == "HPDPKG002");
    }
}
