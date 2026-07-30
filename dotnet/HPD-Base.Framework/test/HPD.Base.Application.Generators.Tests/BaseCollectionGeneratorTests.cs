using System.Collections.Immutable;
using FluentAssertions;
using HPD.Base.Application.Collections;
using HPD.Base.Application.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace HPD.Base.Application.Generators.Tests;

public sealed class BaseCollectionGeneratorTests
{
    [Fact]
    public void GenerationIsDeterministic()
    {
        const string source = """
            using HPD.Base.Application.Generation;
            using System.Text.Json.Serialization;

            namespace Example;

            [BaseCollection("projects", typeof(AppJsonContext))]
            public partial record Project
            {
                public required string Name { get; init; }
            }

            [JsonSerializable(typeof(Project))]
            public partial class AppJsonContext : JsonSerializerContext;
            """;

        var first = Run(source);
        var second = Run(source);

        first.Diagnostics.Should().BeEmpty();
        first.GeneratedSource.Should().Be(second.GeneratedSource);
        first.GeneratedSource.Should().Contain("BaseCollection<global::Example.Project>");
        first.GeneratedSource.Should().Contain("Fields.SetName");
    }

    [Fact]
    public void NonPartialCollectionReportsStableDiagnostic()
    {
        const string source = """
            using HPD.Base.Application.Generation;
            using System.Text.Json.Serialization;

            [BaseCollection("projects", typeof(AppJsonContext))]
            public record Project;

            [JsonSerializable(typeof(Project))]
            public partial class AppJsonContext : JsonSerializerContext;
            """;

        var result = Run(source);

        result.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Id == "HPDBASE001");
        result.GeneratedSource.Should().BeEmpty();
    }

    [Fact]
    public void DuplicateCollectionIdsReportStableDiagnostic()
    {
        const string source = """
            using HPD.Base.Application.Generation;
            using System.Text.Json.Serialization;

            [BaseCollection("shared", typeof(AppJsonContext))]
            public partial record First;

            [BaseCollection("shared", typeof(AppJsonContext))]
            public partial record Second;

            [JsonSerializable(typeof(First))]
            [JsonSerializable(typeof(Second))]
            public partial class AppJsonContext : JsonSerializerContext;
            """;

        var result = Run(source);

        result.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Id == "HPDBASE002");
        result.GeneratedSource.Should().Contain("partial record class First");
        result.GeneratedSource.Should().NotContain("partial record class Second");
    }

    [Fact]
    public void MissingJsonRegistrationReportsStableDiagnostic()
    {
        const string source = """
            using HPD.Base.Application.Generation;
            using System.Text.Json.Serialization;

            [BaseCollection("projects", typeof(AppJsonContext))]
            public partial record Project;

            public partial class AppJsonContext : JsonSerializerContext;
            """;

        var result = Run(source);

        result.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Id == "HPDBASE007");
        result.GeneratedSource.Should().BeEmpty();
    }

    [Fact]
    public void UnsupportedPayloadFieldReportsAtTheField()
    {
        const string source = """
            using HPD.Base.Application.Generation;
            using System;
            using System.Text.Json.Serialization;

            [BaseCollection("projects", typeof(AppJsonContext))]
            public partial record Project
            {
                public Action? Callback { get; init; }
            }

            [JsonSerializable(typeof(Project))]
            public partial class AppJsonContext : JsonSerializerContext;
            """;

        var result = Run(source);

        Diagnostic diagnostic = result.Diagnostics.Should()
            .ContainSingle(item => item.Id == "HPDBASE008")
            .Subject;
        diagnostic.Location.GetLineSpan().StartLinePosition.Line.Should().Be(7);
        result.GeneratedSource.Should().BeEmpty();
    }

    [Fact]
    public void DuplicateStoredFieldReportsAtTheSecondField()
    {
        const string source = """
            using HPD.Base.Application.Generation;
            using System.Text.Json.Serialization;

            [BaseCollection("projects", typeof(AppJsonContext))]
            public partial record Project
            {
                [BaseField(Name = "name")]
                public string First { get; init; } = "";

                [BaseField(Name = "name")]
                public string Second { get; init; } = "";
            }

            [JsonSerializable(typeof(Project))]
            public partial class AppJsonContext : JsonSerializerContext;
            """;

        var result = Run(source);

        Diagnostic diagnostic = result.Diagnostics.Should()
            .ContainSingle(item => item.Id == "HPDBASE004")
            .Subject;
        diagnostic.Location.GetLineSpan().StartLinePosition.Line.Should().Be(10);
        result.GeneratedSource.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""[BaseIndex("", nameof(Name))]""")]
    [InlineData("""[BaseIndex("empty")]""")]
    [InlineData("""
        [BaseIndex("same", nameof(Name))]
        [BaseIndex("same", nameof(Name))]
        """)]
    public void InvalidIndexesReportAtTheDeclaration(string declaration)
    {
        string source = $$"""
            using HPD.Base.Application.Generation;
            using System.Text.Json.Serialization;

            {{declaration}}
            [BaseCollection("projects", typeof(AppJsonContext))]
            public partial record Project
            {
                public string Name { get; init; } = "";
            }

            [JsonSerializable(typeof(Project))]
            public partial class AppJsonContext : JsonSerializerContext;
            """;

        var result = Run(source);

        Diagnostic diagnostic = result.Diagnostics.Should()
            .ContainSingle(item => item.Id == "HPDBASE009")
            .Subject;
        diagnostic.Location.IsInSource.Should().BeTrue();
        result.GeneratedSource.Should().BeEmpty();
    }

    private static GeneratorResult Run(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp14);
        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new BaseCollectionGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);

        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult result = driver.GetRunResult();

        return new GeneratorResult(
            result.Diagnostics,
            string.Join(
                "\n",
                result.Results.SelectMany(item => item.GeneratedSources)
                    .Select(item => item.SourceText.ToString())));
    }

    private static ImmutableArray<MetadataReference> References()
    {
        string trustedAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException(
                "Trusted platform assemblies are unavailable.");

        return trustedAssemblies
            .Split(Path.PathSeparator)
            .Append(typeof(BaseCollection<>).Assembly.Location)
            .Distinct(StringComparer.Ordinal)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }

    private sealed record GeneratorResult(
        ImmutableArray<Diagnostic> Diagnostics,
        string GeneratedSource);
}
