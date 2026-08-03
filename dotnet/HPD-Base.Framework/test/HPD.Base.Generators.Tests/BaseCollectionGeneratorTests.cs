using System.Collections.Immutable;
using FluentAssertions;
using HPD.Base;
using HPD.Base.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace HPD.Base.Generators.Tests;

public sealed class BaseCollectionGeneratorTests
{
    [Fact]
    public void GenerationIsDeterministic()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;

            namespace Example;

            [BaseCollection("projects", typeof(AppJsonContext))]
            public partial record Project
            {
                [BaseField("project.name")]
                public required string Name { get; init; }

                [BaseField("project.owner")]
                public required BaseRecordId<User> OwnerId { get; init; }
            }

            public sealed record User;

            [JsonSerializable(typeof(Project))]
            public partial class AppJsonContext : JsonSerializerContext;
            """;

        var first = Run(source);
        var second = Run(source);

        first.Diagnostics.Should().BeEmpty();
        first.GeneratedSource.Should().Be(second.GeneratedSource);
        first.GeneratedSource.Should().Contain("BaseCollection<global::Example.Project>");
        first.GeneratedSource.Should().Contain("Fields.SetName");
        first.GeneratedSource.Should().Contain("BaseRecordIdJsonConverterFactory.Register<global::Example.User>()");
    }

    [Fact]
    public void NonPartialCollectionReportsStableDiagnostic()
    {
        const string source = """
            using HPD.Base;
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
            using HPD.Base;
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
            using HPD.Base;
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
    public void InvalidMutationModeReportsStableDiagnosticInsteadOfDowngradingToMutable()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;

            [BaseCollection("history", typeof(AppJsonContext), MutationMode = (BaseCollectionMutationMode)99)]
            public partial record History;

            [JsonSerializable(typeof(History))]
            public partial class AppJsonContext : JsonSerializerContext;
            """;

        GeneratorResult result = Run(source);

        result.Diagnostics.Should().ContainSingle(item => item.Id == "HPDBASE013");
        result.GeneratedSource.Should().BeEmpty();
    }

    [Fact]
    public void UnsupportedPayloadFieldReportsAtTheField()
    {
        const string source = """
            using HPD.Base;
            using System;
            using System.Text.Json.Serialization;

            [BaseCollection("projects", typeof(AppJsonContext))]
            public partial record Project
            {
                [BaseField("project.callback")]
                public Action? Callback { get; init; }
            }

            [JsonSerializable(typeof(Project))]
            public partial class AppJsonContext : JsonSerializerContext;
            """;

        var result = Run(source);

        Diagnostic diagnostic = result.Diagnostics.Should()
            .ContainSingle(item => item.Id == "HPDBASE008")
            .Subject;
        diagnostic.Location.GetLineSpan().StartLinePosition.Line.Should().Be(8);
        result.GeneratedSource.Should().BeEmpty();
    }

    [Fact]
    public void DuplicateStoredFieldReportsAtTheSecondField()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;

            [BaseCollection("projects", typeof(AppJsonContext))]
            public partial record Project
            {
                [BaseField("project.first", Name = "name")]
                public string First { get; init; } = "";

                [BaseField("project.second", Name = "name")]
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
            using HPD.Base;
            using System.Text.Json.Serialization;

            {{declaration}}
            [BaseCollection("projects", typeof(AppJsonContext))]
            public partial record Project
            {
                [BaseField("project.name")]
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

    [Fact]
    public void MissingStableFieldIdentityReportsAtTheProperty()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;

            [BaseCollection("projects", typeof(AppJsonContext))]
            public partial record Project
            {
                public string Name { get; init; } = "";
            }

            [JsonSerializable(typeof(Project))]
            public partial class AppJsonContext : JsonSerializerContext;
            """;

        var result = Run(source);

        result.Diagnostics.Should().ContainSingle(item => item.Id == "HPDBASE010");
        result.GeneratedSource.Should().BeEmpty();
    }

    [Fact]
    public void DuplicateStableFieldIdentityReportsExactDiagnostic()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseCollection("projects", typeof(AppJsonContext))]
            public partial record Project
            {
                [BaseField("project.value")] public string First { get; init; } = "";
                [BaseField("project.value")] public string Second { get; init; } = "";
            }
            [JsonSerializable(typeof(Project))] public partial class AppJsonContext : JsonSerializerContext;
            """;

        GeneratorResult result = Run(source);

        result.Diagnostics.Should().ContainSingle(item => item.Id == "HPDBASE011");
        result.GeneratedSource.Should().BeEmpty();
    }

    [Fact]
    public void MalformedStableFieldIdentityReportsExactDiagnostic()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseCollection("projects", typeof(AppJsonContext))]
            public partial record Project
            {
                [BaseField("bad identity")] public string Value { get; init; } = "";
            }
            [JsonSerializable(typeof(Project))] public partial class AppJsonContext : JsonSerializerContext;
            """;

        GeneratorResult result = Run(source);

        result.Diagnostics.Should().ContainSingle(item => item.Id == "HPDBASE008");
        result.GeneratedSource.Should().BeEmpty();
    }

    [Fact]
    public void DeclarationOrderDoesNotChangeGeneratedContract()
    {
        const string prefix = """
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseCollection("projects", typeof(AppJsonContext))]
            public partial record Project
            {
            """;
        const string suffix = """
            }
            [JsonSerializable(typeof(Project))] public partial class AppJsonContext : JsonSerializerContext;
            """;
        string first = prefix + "[BaseField(\"project.a\")] public string A { get; init; } = \"\";\n[BaseField(\"project.b\")] public string B { get; init; } = \"\";\n" + suffix;
        string reversed = prefix + "[BaseField(\"project.b\")] public string B { get; init; } = \"\";\n[BaseField(\"project.a\")] public string A { get; init; } = \"\";\n" + suffix;

        GeneratorResult left = Run(first);
        GeneratorResult right = Run(reversed);

        left.Diagnostics.Should().BeEmpty();
        right.Diagnostics.Should().BeEmpty();
        left.GeneratedSource.Should().Be(right.GeneratedSource);
    }

    [Fact]
    public void ManyValuedTypedRecordIdsGenerateBoundedArrayRelationMetadata()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseCollection("owners", typeof(AppJsonContext))]
            public partial record Owner { [BaseField("owner.name")] public required string Name { get; init; } }
            [BaseCollection("teams", typeof(AppJsonContext))]
            public partial record Team
            {
                [BaseField("team.members")]
                [BaseRelation("team.members", typeof(Owner), LocalMultiplicity = BaseRelationMultiplicity.Many, MinimumCount = 1, MaximumCount = 4, IncludeAllowed = true, IncludeFilterAllowed = true, IncludeSortAllowed = true, IncludeMaximumDepth = 2)]
                public required BaseRecordId<Owner>[] Members { get; init; }
            }
            [JsonSerializable(typeof(Owner))]
            [JsonSerializable(typeof(Team))]
            public partial class AppJsonContext : JsonSerializerContext;
            """;

        GeneratorResult result = Run(source);

        result.Diagnostics.Should().BeEmpty();
        result.GeneratedSource.Should().Contain("LocalMultiplicity = (global::HPD.Base.BaseRelationMultiplicity)2");
        result.GeneratedSource.Should().Contain("MinimumCount = 1");
        result.GeneratedSource.Should().Contain("MaximumCount = 4");
        result.GeneratedSource.Should().Contain("Allowed = true, FilterAllowed = true, SortAllowed = true, MaxDepth = 2");
    }

    [Theory]
    [InlineData("BaseRecordId<Owner>", "BaseRelationMultiplicity.Many")]
    [InlineData("BaseRecordId<Owner>[]", "BaseRelationMultiplicity.ExactlyOne")]
    public void RelationMultiplicityMustMatchTheTypedRecordIdShape(string propertyType, string multiplicity)
    {
        string source = $$"""
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseCollection("owners", typeof(AppJsonContext))]
            public partial record Owner { [BaseField("owner.name")] public required string Name { get; init; } }
            [BaseCollection("teams", typeof(AppJsonContext))]
            public partial record Team
            {
                [BaseField("team.members")]
                [BaseRelation("team.members", typeof(Owner), LocalMultiplicity = {{multiplicity}})]
                public required {{propertyType}} Members { get; init; }
            }
            [JsonSerializable(typeof(Owner))]
            [JsonSerializable(typeof(Team))]
            public partial class AppJsonContext : JsonSerializerContext;
            """;

        Run(source).Diagnostics.Should().ContainSingle(item => item.Id == "HPDBASE012");
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
