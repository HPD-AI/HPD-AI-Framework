using System.Collections.Immutable;
using HPD.Agent.SourceGenerator.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HPD.Agent.Tests.SourceGenerator;

public sealed class ProviderManifestSourceGeneratorTests
{
    [Fact]
    public void ProviderDeclaration_EmitsImmutableManifestWithoutModuleInitializer()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using HPD.Agent;
            using HPD.Agent.ErrorHandling;
            using HPD.Agent.Providers;
            using Microsoft.Extensions.AI;

            namespace GeneratedProviders;

            [HpdProvider("sample", "Sample", DocumentationUrl = "https://example.test/")]
            [HpdProviderAlias("sample-ai")]
            [HpdProviderFamily(ProviderClientFamily.Chat, DefaultModelName = "sample-model")]
            [HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(ProviderClientConfig), typeof(HPDJsonContext))]
            internal sealed class SampleProvider : IChatClientProvider
            {
                public string ProviderKey => "sample";
                public string DisplayName => "Sample";
                public IProviderErrorHandler CreateErrorHandler() => throw new NotSupportedException();
                public ProviderMetadata GetMetadata() => new();
                public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family) => ProviderValidationResult.Success();
                public ValueTask<IChatClient> CreateChatClientAsync(ProviderClientConfig config, IServiceProvider? services = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            }
            """;

        var (generated, diagnostics) = Run(source);

        Assert.Empty(diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Contains("public static class GeneratedProviders_SampleProviderProviderManifest", generated);
        Assert.Contains("ProviderManifestFragment", generated);
        Assert.Contains("HpdProviderManifestAttribute", generated);
        Assert.Contains("sample-ai", generated);
        Assert.Contains("sample-model", generated);
        Assert.Contains("ProviderPayloadJsonContract", generated);
        Assert.Contains("HPDJsonContext.Default.GetTypeInfo", generated);
        Assert.DoesNotContain("ModuleInitializer", generated);
        Assert.DoesNotContain("ProviderDiscovery", generated);
    }

    [Fact]
    public void InvalidProviderKey_ReportsStableDiagnostic()
    {
        const string source = """
            using HPD.Agent.Providers;

            [HpdProvider("Bad Key", "Bad")]
            [HpdProviderFamily(ProviderClientFamily.Chat)]
            internal sealed class BadProvider
            {
            }
            """;

        var (_, diagnostics) = Run(source);

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Id == "HPDP002");
    }

    [Fact]
    public void ManifestMarker_EmitsClosedHostComposition()
    {
        const string source = """
            using System;
            using HPD.Agent.Providers;

            [assembly: HpdProviderManifest(typeof(SampleManifest), "sample", ProviderClientFamily.Chat)]

            public static class SampleManifest
            {
                public static ProviderManifestFragment Fragment { get; } = new(
                    Array.Empty<IProviderDescriptor>(),
                    Array.Empty<ProviderRuntimeFactoryRegistration>(),
                    Array.Empty<ProviderPayloadJsonContract>());
            }
            """;

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location));
        var compilation = CSharpCompilation.Create(
            "GeneratedProviderComposition",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new ProviderCompositionSourceGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var generated = string.Join(
            "\n",
            driver.GetRunResult().Results
                .SelectMany(static result => result.GeneratedSources)
                .Select(static result => result.SourceText.ToString()));
        var errors = diagnostics.Concat(output.GetDiagnostics())
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        Assert.Empty(errors);
        Assert.Contains("internal static class GeneratedProviderComposition", generated);
        Assert.Contains("global::SampleManifest.Fragment", generated);
        Assert.Contains("ProviderComposition.Create", generated);
        Assert.DoesNotContain("Assembly.Load", generated);
        Assert.DoesNotContain("GetTypes", generated);
    }

    private static (string Generated, ImmutableArray<Diagnostic> Diagnostics) Run(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location));
        var compilation = CSharpCompilation.Create(
            "GeneratedProviderManifest",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new ProviderManifestSourceGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
        var generated = string.Join(
            "\n",
            driver.GetRunResult().Results
                .SelectMany(static result => result.GeneratedSources)
                .Select(static result => result.SourceText.ToString()));
        var diagnostics = generatorDiagnostics
            .Concat(output.GetDiagnostics())
            .ToImmutableArray();
        return (generated, diagnostics);
    }
}
