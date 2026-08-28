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
    public void GeneratedReadSupportsOwnedBinaryAndExactLimits()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseRead("binary.read", typeof(AppJsonContext), RequiredGrantId = "binary.read")]
            internal partial record BinaryRead
            {
                [BaseReadParameter("binary.read.id")] public required string Id { get; init; }
                public sealed partial record Row
                {
                    [BaseReadField("binary.read.payload", MinimumBytes = 4, MaximumBytes = 16)] public required BaseBinary Payload { get; init; }
                }
                public static void Configure(BaseReadDefinitionBuilder<BinaryRead, Row> read) { }
            }
            [JsonSerializable(typeof(BinaryRead), TypeInfoPropertyName = "BinaryRead")]
            [JsonSerializable(typeof(BinaryRead.Row), TypeInfoPropertyName = "BinaryReadRow")]
            internal partial class AppJsonContext : JsonSerializerContext;
            """;

        var result = Run(source);

        result.Diagnostics.Should().BeEmpty();
        result.Source.Should().Contain("ReadBinary(row, \"binary.read.payload\", 4, 16)")
            .And.Contain("MinimumBinaryBytes = 4, MaximumBinaryBytes = 16");
    }

    [Theory]
    [InlineData("[BaseReadField(\"binary.read.payload\")]", "BaseBinary")]
    [InlineData("[BaseReadField(\"binary.read.payload\", MinimumBytes = 17, MaximumBytes = 16)]", "BaseBinary")]
    [InlineData("[BaseReadField(\"binary.read.payload\", MaximumBytes = 1048577)]", "BaseBinary")]
    [InlineData("[BaseReadField(\"binary.read.payload\", MaximumBytes = 16)]", "string")]
    public void BinaryBoundsAreMandatoryExactAndBinaryOnly(string declaration, string type)
    {
        string source = $$"""
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseRead("binary.read", typeof(AppJsonContext), RequiredGrantId = "binary.read")]
            internal partial record BinaryRead
            {
                public sealed partial record Row
                {
                    {{declaration}} public required {{type}} Payload { get; init; }
                }
                public static void Configure(BaseReadDefinitionBuilder<BinaryRead, Row> read) { }
            }
            [JsonSerializable(typeof(BinaryRead), TypeInfoPropertyName = "BinaryRead")]
            [JsonSerializable(typeof(BinaryRead.Row), TypeInfoPropertyName = "BinaryReadRow")]
            internal partial class AppJsonContext : JsonSerializerContext;
            """;

        Run(source).Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "HPDBASE020");
    }

    [Fact]
    public void GenerationIsDeterministicAndProducesOnlyTypedHandles()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;

            [BaseRead("project-name", typeof(AppJsonContext), Exposure = BaseReadExposure.Admin, Authorization = BaseReadAuthorization.Admin, RequiredGrantId = "project-name.execute")]
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
        first.Source.Should().Contain("CreateGenerated(");
        first.Source.Should().Contain("BaseSerializerPropertyDeclaration.Create(typeof(global::ProjectName), \"Id\", typeof(string)");
        first.Source.Should().Contain("BaseSerializerPropertyDeclaration.Create(typeof(global::ProjectName.Row), \"Name\", typeof(string)");
        first.Source.Should().NotContain("GetContext(");
        first.Source.Should().NotContain("System.Type");
    }

    [Fact]
    public void AdminExposureWithoutAdminAuthorizationFailsGeneration()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseRead("read", typeof(AppJsonContext), Exposure = BaseReadExposure.Admin, RequiredGrantId = "read.execute")]
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
        result.Source.Should().Contain("Definition => null!").And.NotContain("CreateGenerated");
    }

    [Fact]
    public void MissingStableMemberIdentityFailsGeneration()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseRead("read", typeof(AppJsonContext), RequiredGrantId = "read.execute")]
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
        result.Source.Should().Contain("Definition => null!").And.NotContain("CreateGenerated");
    }

    [Fact]
    public void UnsupportedParameterShapeFailsAtTheDeclaredProperty()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseRead("read", typeof(AppJsonContext), RequiredGrantId = "read.execute")]
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

        result.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "HPDBASE0447");
        result.Source.Should().Contain("Definition => null!").And.NotContain("CreateGenerated");
    }

    [Fact]
    public void TypedRecordIdsUseClosedRecordIdCodecs()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;
            public sealed record Project;
            [BaseRead("read", typeof(AppJsonContext), RequiredGrantId = "read.execute")]
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
            [BaseRead("read", typeof(AppJsonContext), RequiredGrantId = "read.execute")]
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

    [Fact]
    public void CanonicalJsonProjectionUsesTheSourceBoundReadCodec()
    {
        const string source = """
            using System.Text.Json.Serialization;
            using HPD.Base;
            namespace Demo;
            [BaseRead("json-read", typeof(JsonContext), RequiredGrantId = "json.read")]
            internal sealed partial record JsonRead
            {
                public sealed partial record Row
                {
                    [BaseReadField("json-read.value")]
                    public required BaseCanonicalJson Value { get; init; }
                }
                public static void Configure(BaseReadDefinitionBuilder<JsonRead, Row> read) { }
            }
            [JsonSerializable(typeof(JsonRead))]
            [JsonSerializable(typeof(JsonRead.Row), TypeInfoPropertyName = "JsonReadRow")]
            internal sealed partial class JsonContext : JsonSerializerContext;
            """;

        Result result = Run(source);

        result.Diagnostics.Should().BeEmpty();
        result.Source.Should().Contain("BaseReadGeneratedContract.ReadCanonicalJson");
        result.Source.Should().Contain("QueryValueKind.CanonicalJson");
    }

    [Fact]
    public void CanonicalJsonParametersAndRowsAreGeneratedFromABaseNestedNamespace()
    {
        const string source = """
            using System.Text.Json.Serialization;
            namespace HPD.Base.Tests;
            [global::HPD.Base.BaseRead("json-read", typeof(JsonContext), RequiredGrantId = "json.read")]
            internal sealed partial record JsonRead
            {
                [global::HPD.Base.BaseReadParameter("json-read.parameter")]
                public global::HPD.Base.BaseCanonicalJson? Parameter { get; init; }
                public sealed partial record Row
                {
                    [global::HPD.Base.BaseReadField("json-read.value")]
                    public required global::HPD.Base.BaseCanonicalJson Value { get; init; }
                }
                public static void Configure(global::HPD.Base.BaseReadDefinitionBuilder<JsonRead, Row> read) { }
            }
            [JsonSerializable(typeof(JsonRead))]
            [JsonSerializable(typeof(JsonRead.Row), TypeInfoPropertyName = "JsonReadRow")]
            internal sealed partial class JsonContext : JsonSerializerContext;
            """;

        Result result = Run(source);

        result.Diagnostics.Should().BeEmpty();
        result.Source.Should().Contain("BaseReadGeneratedContract.Value(__Parameter)");
        result.Source.Should().Contain("BaseReadGeneratedContract.ReadCanonicalJson");
    }

    [Fact]
    public void ClosedEnumParametersAndRowsUseExactDeclaredWireLiterals()
    {
        const string source = """
            using System.Text.Json.Serialization;
            using HPD.Base;
            namespace Demo;
            internal enum Mode { [JsonStringEnumMemberName("exact-wire")] Exact }
            [BaseRead("enum-read", typeof(JsonContext), RequiredGrantId = "enum.read")]
            internal sealed partial record EnumRead
            {
                [BaseReadParameter("enum-read.mode")] public required Mode Mode { get; init; }
                public sealed partial record Row
                {
                    [BaseReadField("enum-read.row.mode")] public required Mode Mode { get; init; }
                }
                public static void Configure(BaseReadDefinitionBuilder<EnumRead, Row> read) { }
            }
            [JsonSerializable(typeof(EnumRead))]
            [JsonSerializable(typeof(EnumRead.Row), TypeInfoPropertyName = "EnumReadRow")]
            internal sealed partial class JsonContext : JsonSerializerContext;
            """;

        Result result = Run(source);

        result.Diagnostics.Should().NotContain(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        result.Source.Should().Contain("global::Demo.Mode.Exact => \"exact-wire\"");
        result.Source.Should().Contain("\"exact-wire\" => global::Demo.Mode.Exact");
    }

    [Theory]
    [InlineData("[System.Flags] internal enum Mode { A = 1, B = 2 }")]
    [InlineData("internal enum Mode { A = 1, Alias = 1 }")]
    [InlineData("internal enum Mode { [JsonStringEnumMemberName(\"same\")] A = 1, [JsonStringEnumMemberName(\"same\")] B = 2 }")]
    [InlineData("internal enum Mode : ulong { TooLarge = 18446744073709551615UL }")]
    [InlineData("internal enum Mode { [JsonStringEnumMemberName(\"\")] Empty = 1 }")]
    public void InvalidClosedEnumVocabulariesAreRejected(string declaration)
    {
        string source = $$"""
            using System.Text.Json.Serialization;
            using HPD.Base;
            namespace Demo;
            {{declaration}}
            [BaseRead("enum-read", typeof(JsonContext), RequiredGrantId = "enum.read")]
            internal sealed partial record EnumRead
            {
                [BaseReadParameter("enum-read.mode")] public required Mode Mode { get; init; }
                public sealed partial record Row { [BaseReadField("enum-read.row.mode")] public required Mode Mode { get; init; } }
                public static void Configure(BaseReadDefinitionBuilder<EnumRead, Row> read) { }
            }
            [JsonSerializable(typeof(EnumRead))]
            [JsonSerializable(typeof(EnumRead.Row), TypeInfoPropertyName = "EnumReadRow")]
            internal sealed partial class JsonContext : JsonSerializerContext;
            """;

        Run(source).Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "HPDBASE023");
    }

    [Fact]
    public void IgnoreNeverIsActiveAndAlwaysRequiresNoReadIdentity()
    {
        const string accepted = """
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseRead("read", typeof(AppJsonContext), RequiredGrantId = "read.execute")]
            public partial record Read
            {
                [BaseReadParameter("read.value")]
                [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
                public required string Value { get; init; }
                [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
                public string LocalOnly { get; init; } = string.Empty;
                public sealed partial record Row
                {
                    [BaseReadField("read.row.value")]
                    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
                    public required string Value { get; init; }
                }
                public static void Configure(BaseReadDefinitionBuilder<Read, Row> read) { }
            }
            [JsonSerializable(typeof(Read))]
            [JsonSerializable(typeof(Read.Row))]
            public partial class AppJsonContext : JsonSerializerContext;
            """;
        const string rejected = """
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseRead("read", typeof(AppJsonContext), RequiredGrantId = "read.execute")]
            public partial record Read
            {
                [BaseReadParameter("read.value")]
                [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
                public required string Value { get; init; }
                public sealed partial record Row
                {
                    [BaseReadField("read.row.value")] public required string Value { get; init; }
                }
                public static void Configure(BaseReadDefinitionBuilder<Read, Row> read) { }
            }
            [JsonSerializable(typeof(Read))]
            [JsonSerializable(typeof(Read.Row))]
            public partial class AppJsonContext : JsonSerializerContext;
            """;

        Result valid = Run(accepted);
        valid.Diagnostics.Should().BeEmpty();
        valid.Source.Should().Contain("CreateGenerated").And.Contain("LocalOnly");
        Run(rejected).Diagnostics.Should().ContainSingle(item => item.Id == "HPDBASE020");
    }

    private static Result Run(string source)
    {
        var parse = new CSharpParseOptions(LanguageVersion.CSharp14);
        var compilation = CSharpCompilation.Create("ReadGeneratorTests", [CSharpSyntaxTree.ParseText(source, parse)], References(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new BaseSchemaGenerator().AsSourceGenerator()], parseOptions: parse);
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
