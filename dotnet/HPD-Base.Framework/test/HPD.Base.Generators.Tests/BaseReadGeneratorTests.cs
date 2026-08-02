using System.Collections.Immutable;
using FluentAssertions;
using HPD.Base;
using HPD.Base.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace HPD.Base.Generators.Tests;

public sealed class BaseReadGeneratorTests
{
    [Fact]
    public void GenerationIsDeterministicAndProducesOnlyTypedHandles()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;

            [BaseRead("project-name", typeof(AppJsonContext), Exposure = BaseReadExposure.Admin, Authorization = BaseReadAuthorization.Admin)]
            public partial record ProjectName
            {
                [BaseReadParameter("project-name.id")]
                public required string Id { get; init; }

                public sealed partial record Row
                {
                    [BaseReadField("project-name.row.name")]
                    public required string Name { get; init; }
                }

                public static void Configure(BaseReadDefinitionBuilder<ProjectName, Row> read) { }
            }

            [JsonSerializable(typeof(ProjectName))]
            [JsonSerializable(typeof(ProjectName.Row))]
            public partial class AppJsonContext : JsonSerializerContext;
            """;

        var first = Run(source);
        var second = Run(source);

        first.Diagnostics.Should().BeEmpty();
        first.Source.Should().Be(second.Source);
        first.Source.Should().Contain("BaseReadParameter<global::ProjectName, string>");
        first.Source.Should().Contain("BaseReadField<global::ProjectName.Row, string>");
        first.Source.Should().Contain("BaseReadExposure.Admin, global::HPD.Base.BaseReadAuthorization.Admin");
        first.Source.Should().NotContain("System.Type");
    }

    [Fact]
    public void AdminExposureWithoutAdminAuthorizationFailsGeneration()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseRead("read", typeof(AppJsonContext), Exposure = BaseReadExposure.Admin)]
            public partial record Read
            {
                [BaseReadParameter("read.value")]
                public string Value { get; init; } = "";
                public sealed partial record Row
                {
                    [BaseReadField("read.row.value")]
                    public string Value { get; init; } = "";
                }
                public static void Configure(BaseReadDefinitionBuilder<Read, Row> read) { }
            }
            [JsonSerializable(typeof(Read))]
            [JsonSerializable(typeof(Read.Row))]
            public partial class AppJsonContext : JsonSerializerContext;
            """;

        var result = Run(source);

        result.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "HPDBASE020");
        result.Source.Should().BeEmpty();
    }

    [Fact]
    public void MissingStableMemberIdentityFailsGeneration()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseRead("read", typeof(AppJsonContext))]
            public partial record Read
            {
                public string Value { get; init; } = "";
                public sealed partial record Row
                {
                    [BaseReadField("read.row.value")]
                    public string Value { get; init; } = "";
                }
                public static void Configure(BaseReadDefinitionBuilder<Read, Row> read) { }
            }
            [JsonSerializable(typeof(Read))]
            [JsonSerializable(typeof(Read.Row))]
            public partial class AppJsonContext : JsonSerializerContext;
            """;

        var result = Run(source);

        result.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "HPDBASE021");
        result.Source.Should().BeEmpty();
    }

    [Fact]
    public void UnsupportedParameterShapeFailsAtTheDeclaredProperty()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseRead("read", typeof(AppJsonContext))]
            public partial record Read
            {
                [BaseReadParameter("read.value")]
                public required object Value { get; init; }
                public sealed partial record Row
                {
                    [BaseReadField("read.row.value")]
                    public required string Value { get; init; }
                }
                public static void Configure(BaseReadDefinitionBuilder<Read, Row> read) { }
            }
            [JsonSerializable(typeof(Read))]
            [JsonSerializable(typeof(Read.Row))]
            public partial class AppJsonContext : JsonSerializerContext;
            """;

        Result result = Run(source);

        result.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "HPDBASE023");
        result.Source.Should().BeEmpty();
    }

    [Fact]
    public void TypedRecordIdsUseClosedRecordIdCodecs()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;
            public sealed record Project;
            [BaseRead("read", typeof(AppJsonContext))]
            public partial record Read
            {
                [BaseReadParameter("read.project")]
                public required BaseRecordId<Project> ProjectId { get; init; }
                public sealed partial record Row
                {
                    [BaseReadField("read.row.project")]
                    public required BaseRecordId<Project> ProjectId { get; init; }
                }
                public static void Configure(BaseReadDefinitionBuilder<Read, Row> read) { }
            }
            [JsonSerializable(typeof(Read))]
            [JsonSerializable(typeof(Read.Row))]
            public partial class AppJsonContext : JsonSerializerContext;
            """;

        Result result = Run(source);

        result.Diagnostics.Should().BeEmpty();
        result.Source.Should().Contain("BaseReadGeneratedContract.Value(parameters.ProjectId.Value)");
        result.Source.Should().Contain("new global::HPD.Base.BaseRecordId<global::Project>");
    }

    [Fact]
    public void DateTimeAndDateTimeOffsetUseTheClosedDateTimeCodec()
    {
        const string source = """
            using HPD.Base;
            using System;
            using System.Text.Json.Serialization;
            [BaseRead("read", typeof(AppJsonContext))]
            public partial record Read
            {
                [BaseReadParameter("read.after")]
                public required DateTime After { get; init; }
                public sealed partial record Row
                {
                    [BaseReadField("read.row.at")]
                    public required DateTimeOffset At { get; init; }
                }
                public static void Configure(BaseReadDefinitionBuilder<Read, Row> read) { }
            }
            [JsonSerializable(typeof(Read))]
            [JsonSerializable(typeof(Read.Row))]
            public partial class AppJsonContext : JsonSerializerContext;
            """;

        Result result = Run(source);

        result.Diagnostics.Should().BeEmpty();
        result.Source.Should().Contain("BaseReadGeneratedContract.Value(parameters.After)");
        result.Source.Should().Contain("BaseReadGeneratedContract.Read<global::System.DateTimeOffset>");
    }

    private static Result Run(string source)
    {
        var parse = new CSharpParseOptions(LanguageVersion.CSharp14);
        var compilation = CSharpCompilation.Create("ReadGeneratorTests", [CSharpSyntaxTree.ParseText(source, parse)], References(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new BaseReadGenerator().AsSourceGenerator()], parseOptions: parse);
        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult result = driver.GetRunResult();
        return new Result(result.Diagnostics, string.Join("\n", result.Results.SelectMany(item => item.GeneratedSources).Select(item => item.SourceText.ToString())));
    }

    private static ImmutableArray<MetadataReference> References() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(BaseReadAttribute).Assembly.Location)
            .Distinct(StringComparer.Ordinal)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();

    private sealed record Result(ImmutableArray<Diagnostic> Diagnostics, string Source);
}
