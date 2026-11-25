using System.Collections.Immutable;
using HPD.Agent.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HPD.Agent.Tests.SourceGenerator;

public sealed class MiddlewareStateGeneratorTests
{
    [Fact]
    public void GlobalNamespaceState_GeneratesCompilableAbsoluteTypeReferences()
    {
        const string source = """
            using HPD.Agent;

            [MiddlewareState]
            public sealed record GlobalTurnCounter(int Count);
            """;

        var (generated, diagnostics) = Run(source);

        Assert.DoesNotContain(
            diagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("global::GlobalTurnCounter", generated);
        Assert.DoesNotContain(
            "public static GlobalTurnCounter?",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NamespacedState_PreservesItsExtensionNamespace()
    {
        const string source = """
            using HPD.Agent;

            namespace GeneratedState;

            [MiddlewareState]
            public sealed record NamespacedTurnCounter(int Count);
            """;

        var (generated, diagnostics) = Run(source);

        Assert.DoesNotContain(
            diagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("namespace GeneratedState;", generated);
        Assert.Contains("global::GeneratedState.NamespacedTurnCounter", generated);
    }

    [Fact]
    public void JsonSerializableContextForState_IsIncludedInRegistry()
    {
        const string source = """
            using System;
            using HPD.Agent;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;

            namespace GeneratedState;

            [MiddlewareState]
            public sealed record ContextBackedState(int Count);

            [JsonSerializable(typeof(ContextBackedState))]
            internal sealed class ContextBackedStateJsonContext
            {
                internal static ContextBackedStateJsonContext Default { get; } = new();
                internal JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """;

        var (generated, diagnostics) = Run(source);

        Assert.DoesNotContain(
            diagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(
            "global::GeneratedState.ContextBackedStateJsonContext.Default.GetTypeInfo(typeof(T))",
            generated);
    }

    private static (string Generated, ImmutableArray<Diagnostic> Diagnostics) Run(
        string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(
            LanguageVersion.Latest);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly =>
                !assembly.IsDynamic &&
                !string.IsNullOrEmpty(assembly.Location))
            .Select(static assembly =>
                MetadataReference.CreateFromFile(assembly.Location));
        var compilation = CSharpCompilation.Create(
            "MiddlewareStateGeneratorFixture",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new MiddlewareStateGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var output,
            out var generatorDiagnostics);
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
