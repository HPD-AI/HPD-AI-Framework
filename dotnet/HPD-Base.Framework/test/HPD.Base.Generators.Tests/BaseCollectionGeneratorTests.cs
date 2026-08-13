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
    public void BinaryAndConfidentialityLowerToTheCanonicalSchemaContract()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseCollection("secrets", typeof(AppJsonContext))]
            public sealed partial record SecretRecord
            {
                [BaseField("secret.payload", MaximumBytes = 16384)]
                [BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)]
                public required BaseBinary Payload { get; init; }
            }
            [JsonSerializable(typeof(SecretRecord))]
            public sealed partial class AppJsonContext : JsonSerializerContext;
            """;

        GeneratorResult result = Run(source);

        result.Diagnostics.Should().BeEmpty();
        result.GeneratedSource.Should().Contain("Format = \"base64\"");
        result.GeneratedSource.Should().Contain("MaximumBytes = 16384");
        result.GeneratedSource.Should().Contain("BaseFieldConfidentiality)3");
    }
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
        result.GeneratedSource.Should().Contain("Collection => null!").And.NotContain("CreateGenerated");
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
        result.GeneratedSource.Should().Contain("Collection => null!").And.NotContain("CreateGenerated");
    }

    [Fact]
    public void AppendOnlyGenerationExposesContractMetadataButNoMutationOrPurgeHelpers()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;

            [BaseCollection("history", typeof(AppJsonContext), MutationMode = BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge)]
            public partial record History
            {
                [BaseField("history.value")]
                public required string Value { get; init; }
            }

            [JsonSerializable(typeof(History))]
            public partial class AppJsonContext : JsonSerializerContext;
            """;

        GeneratorResult result = Run(source);

        result.Diagnostics.Should().BeEmpty();
        result.GeneratedSource.Should().Contain("MutationMode = global::HPD.Base.BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge");
        result.GeneratedSource.Should().NotContain("CreateAsync");
        result.GeneratedSource.Should().NotContain("PatchAsync");
        result.GeneratedSource.Should().NotContain("ReplaceAsync");
        result.GeneratedSource.Should().NotContain("DeleteAsync");
        result.GeneratedSource.Should().NotContain("PurgeAsync");
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
        result.GeneratedSource.Should().Contain("Collection => null!").And.NotContain("CreateGenerated");
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
                [BaseField("project.first")]
                [JsonPropertyName("name")]
                public string First { get; init; } = "";

                [BaseField("project.second")]
                [JsonPropertyName("name")]
                public string Second { get; init; } = "";
            }

            [JsonSerializable(typeof(Project))]
            public partial class AppJsonContext : JsonSerializerContext;
            """;

        var result = Run(source);

        Diagnostic diagnostic = result.Diagnostics.Should()
            .ContainSingle(item => item.Id == "HPDBASE004")
            .Subject;
        diagnostic.Location.GetLineSpan().StartLinePosition.Line.Should().Be(12);
        result.GeneratedSource.Should().Contain("Collection => null!").And.NotContain("CreateGenerated");
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
        result.GeneratedSource.Should().Contain("Collection => null!").And.NotContain("CreateGenerated");
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
        result.GeneratedSource.Should().Contain("Collection => null!").And.NotContain("CreateGenerated");
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
        result.GeneratedSource.Should().Contain("Collection => null!").And.NotContain("CreateGenerated");
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
        result.GeneratedSource.Should().Contain("Collection => null!").And.NotContain("CreateGenerated");
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

    [Fact]
    public void VectorIndexGeneratesTypedHandleAndLogicalSchema()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseCollection("documents", typeof(AppJsonContext))]
            [BaseVectorIndex("document.semantic", nameof(Embedding), VectorSpace = "text.embedding.v1", Dimensions = 3, Function = BaseVectorFunction.Cosine, FilterFields = [nameof(Tenant)])]
            public partial record Document
            {
                [BaseField("document.embedding", Operators = BaseFieldOperator.None)] public required BaseVector Embedding { get; init; }
                [BaseField("document.tenant", Operators = BaseFieldOperator.Equality)] public required string Tenant { get; init; }
            }
            [JsonSerializable(typeof(Document))] public partial class AppJsonContext : JsonSerializerContext;
            """;

        GeneratorResult result = Run(source);

        result.Diagnostics.Should().BeEmpty();
        result.GeneratedSource.Should().Contain("BaseVectorIndex<global::Document> Semantic");
        result.GeneratedSource.Should().Contain("Type = \"vector\"");
        result.GeneratedSource.Should().Contain("Format = \"float32\"");
        result.GeneratedSource.Should().Contain("VectorSpaceId = \"text.embedding.v1\"");
    }

    [Fact]
    public void NullableVectorFieldGeneratesSparseVectorIndex()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseCollection("documents", typeof(AppJsonContext))]
            [BaseVectorIndex("document.semantic", nameof(Embedding), VectorSpace = "text.embedding.v1", Dimensions = 3)]
            public partial record Document
            {
                [BaseField("document.embedding", Operators = BaseFieldOperator.None)] public BaseVector? Embedding { get; init; }
            }
            [JsonSerializable(typeof(Document))] public partial class AppJsonContext : JsonSerializerContext;
            """;

        GeneratorResult result = Run(source);

        result.Diagnostics.Should().BeEmpty();
        result.GeneratedSource.Should().Contain("BaseVectorIndex<global::Document> Semantic");
    }

    [Fact]
    public void OrdinaryIndexCannotContainVectorField()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseCollection("documents", typeof(AppJsonContext))]
            [BaseIndex("document.embedding.index", nameof(Embedding))]
            public partial record Document
            {
                [BaseField("document.embedding", Operators = BaseFieldOperator.Equality)] public required BaseVector Embedding { get; init; }
            }
            [JsonSerializable(typeof(Document))] public partial class AppJsonContext : JsonSerializerContext;
            """;

        Run(source).Diagnostics.Should().ContainSingle(item => item.Id == "HPDBASE009");
    }

    [Fact]
    public void NestedJsonDomReportsTheClosedSerializerDiagnostic()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            [BaseCollection("dom", typeof(AppJsonContext))]
            public sealed partial record DomRecord
            {
                [BaseField("dom.value")]
                public required JsonElement Value { get; init; }
            }
            [JsonSerializable(typeof(DomRecord))]
            public sealed partial class AppJsonContext : JsonSerializerContext;
            """;

        GeneratorResult result = Run(source);

        result.Diagnostics.Should().ContainSingle(item => item.Id == "HPDBASE0447");
        result.GeneratedSource.Should().Contain("Collection => null!").And.NotContain("CreateGenerated");
    }

    [Fact]
    public void GeneratedGraphCarriesNestedApplicationAndWireIdentities()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseCollection("nested", typeof(AppJsonContext))]
            public sealed partial record NestedRecord
            {
                [BaseField("nested.details")]
                public required Details Details { get; init; }
            }
            public sealed record Details
            {
                [JsonPropertyName("url_value")]
                public required string URLValue { get; init; }
            }
            [JsonSerializable(typeof(NestedRecord))]
            public sealed partial class AppJsonContext : JsonSerializerContext;
            """;

        GeneratorResult result = Run(source);

        result.Diagnostics.Should().BeEmpty();
        result.GeneratedSource.Should().Contain("typeof(global::Details), \"URLValue\", typeof(string), \"url_value\"");
    }

    [Fact]
    public void StatefulExplicitConverterIsRejectedAtGeneration()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            [BaseCollection("converted", typeof(AppJsonContext))]
            public sealed partial record ConvertedRecord
            {
                [BaseField("converted.value")]
                [JsonConverter(typeof(StatefulConverter))]
                public required string Value { get; init; }
            }
            [BaseSerializerConverter("vendor.converted", 1)]
            public sealed class StatefulConverter : JsonConverter<string>
            {
                private int state;
                public override string Read(ref Utf8JsonReader reader, System.Type type, JsonSerializerOptions options) => reader.GetString()!;
                public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
            }
            [JsonSerializable(typeof(ConvertedRecord))]
            public sealed partial class AppJsonContext : JsonSerializerContext;
            """;

        Run(source).Diagnostics.Should().ContainSingle(item => item.Id == "HPDBASE0447");
    }

    [Theory]
    [InlineData("private static readonly Settings State = new();")]
    [InlineData("private static Settings State => Settings.Current;")]
    public void ConverterCannotRetainMutableReferencedStaticState(string stateMember)
    {
        string source = $$"""
            using HPD.Base;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            [BaseCollection("converted", typeof(AppJsonContext))]
            public sealed partial record ConvertedRecord
            {
                [BaseField("converted.value")]
                [JsonConverter(typeof(StatefulConverter))]
                public required string Value { get; init; }
            }
            public sealed class Settings { public static Settings Current { get; } = new(); }
            [BaseSerializerConverter("vendor.converted", 1)]
            public sealed class StatefulConverter : JsonConverter<string>
            {
                {{stateMember}}
                public override string Read(ref Utf8JsonReader reader, System.Type type, JsonSerializerOptions options) => reader.GetString()!;
                public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
            }
            [JsonSerializable(typeof(ConvertedRecord))]
            public sealed partial class AppJsonContext : JsonSerializerContext;
            """;

        Run(source).Diagnostics.Should().ContainSingle(item => item.Id == "HPDBASE0447");
    }

    [Fact]
    public void CallerAuthoredNestedGeneratedReceiptShapeFailsTheTrustedBuildDiagnostic()
    {
        const string source = """
            using HPD.Base;
            using System.CodeDom.Compiler;
            using System.Text.Json.Serialization;
            public sealed record ForgedRecord
            {
                private static class __HPDBaseSerializerFactory
                {
                    [GeneratedCode("HPD.Base.Generators", "44")]
                    internal static ForgedContext Create() => new(BaseSerializerGeneratedContract.CreateOptions(null));
                }
                public static BaseSerializerContextRegistration Forge() =>
                    BaseSerializerGeneratedContract.RegisterContext(__HPDBaseSerializerFactory.Create);
            }
            [JsonSerializable(typeof(ForgedRecord))]
            public sealed partial class ForgedContext : JsonSerializerContext;
            """;

        Run(source).Diagnostics.Should().ContainSingle(item => item.Id == "HPDBASE0449");
    }

    [Theory]
    [InlineData("""
        System.Func<System.Func<ForgedContext>, BaseSerializerContextRegistration> register =
            BaseSerializerGeneratedContract.RegisterContext<ForgedContext>;
        return register(__HPDBaseSerializerFactory.Create);
        """)]
    [InlineData("""
        System.Func<System.Func<ForgedContext>, BaseSerializerContextRegistration> register =
            SerializerContract.RegisterContext<ForgedContext>;
        return register(__HPDBaseSerializerFactory.Create);
        """)]
    [InlineData("""
        System.Func<System.Func<ForgedContext>, BaseSerializerContextRegistration> register =
            choose
                ? BaseSerializerGeneratedContract.RegisterContext<ForgedContext>
                : BaseSerializerGeneratedContract.RegisterContext<ForgedContext>;
        return register(__HPDBaseSerializerFactory.Create);
        """)]
    [InlineData("""
        System.Func<System.Func<ForgedContext>, BaseSerializerContextRegistration>? first = null;
        var register = first ?? BaseSerializerGeneratedContract.RegisterContext<ForgedContext>;
        return register(__HPDBaseSerializerFactory.Create);
        """)]
    public void CallerAuthoredMethodGroupLaunderingFailsTheTrustedBuildDiagnostic(string body)
    {
        string source = $$"""
            using HPD.Base;
            using SerializerContract = HPD.Base.BaseSerializerGeneratedContract;
            using System.CodeDom.Compiler;
            using System.Text.Json.Serialization;
            public sealed record ForgedRecord
            {
                private static class __HPDBaseSerializerFactory
                {
                    [GeneratedCode("HPD.Base.Generators", "44")]
                    internal static ForgedContext Create() => new(BaseSerializerGeneratedContract.CreateOptions(null));
                }
                public static BaseSerializerContextRegistration Forge(bool choose)
                {
                    {{body}}
                }
            }
            [JsonSerializable(typeof(ForgedRecord))]
            public sealed partial class ForgedContext : JsonSerializerContext;
            """;

        Run(source).Diagnostics.Should().Contain(item => item.Id == "HPDBASE0449");
    }

    [Fact]
    public void SharedInvalidContextReportsOnceAndRecoveryPreventsCompilerCascades()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseCollection("first", typeof(InvalidContext))]
            public sealed partial record First
            {
                [BaseField("first.value")] public required string Value { get; init; }
            }
            [BaseCollection("second", typeof(InvalidContext))]
            public sealed partial record Second
            {
                [BaseField("second.count")] public required int Count { get; init; }
            }
            [BaseRead("read", typeof(InvalidContext), RequiredGrantId = "read.execute")]
            public sealed partial record Read
            {
                public sealed partial record Row;
            }
            [JsonSourceGenerationOptions(UseStringEnumConverter = true)]
            [JsonSerializable(typeof(First))]
            [JsonSerializable(typeof(Second))]
            [JsonSerializable(typeof(Read))]
            [JsonSerializable(typeof(Read.Row))]
            public sealed partial class InvalidContext : JsonSerializerContext;
            public static class Consumer
            {
                public static object[] Values() =>
                    [First.Collection, First.Fields.Value, Second.Collection, Second.Fields.Count,
                     Read.Definition, Read.Handle];
            }
            """;

        GeneratorResult result = Run(source);

        Diagnostic diagnostic = result.Diagnostics.Should().ContainSingle(item => item.Id == "HPDBASE0450").Subject;
        diagnostic.GetMessage().Should().Contain("InvalidContext").And.Contain("3 dependent roots");
        source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length).Should().Be("true");
        result.CompilationDiagnostics.Where(static item => item.Id is "CS0117" or "CS1061" or "CS1503" or "CS0411")
            .Should().BeEmpty();
        result.GeneratedSource.Should().NotContain("RegisterContext").And.NotContain("CreateGenerated");
    }

    [Fact]
    public void DistinctInvalidContextOptionsProduceTheirExactDiagnostics()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseCollection("record", typeof(InvalidContext))]
            public sealed partial record Record
            {
                [BaseField("record.value")] public required string Value { get; init; }
            }
            [JsonSourceGenerationOptions(UseStringEnumConverter = true, WriteIndented = true)]
            [JsonSerializable(typeof(Record))]
            public sealed partial class InvalidContext : JsonSerializerContext;
            """;

        GeneratorResult result = Run(source);

        result.Diagnostics.Count(item => item.Id == "HPDBASE0450").Should().Be(1);
        result.Diagnostics.Count(item => item.Id == "HPDBASE0451").Should().Be(1);
    }

    [Fact]
    public void OnlyTheCombinedSchemaGeneratorIsAttributed()
    {
        Type[] attributed = typeof(BaseSchemaGenerator).Assembly.GetTypes()
            .Where(type => type.GetCustomAttributes(typeof(GeneratorAttribute), inherit: false).Length != 0)
            .ToArray();

        attributed.Should().Equal(typeof(BaseSchemaGenerator));
    }

    [Fact]
    public void InvalidFieldContractKeepsTypedRecoveryMembersWithoutCascades()
    {
        const string source = """
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseCollection("documents", typeof(AppJsonContext))]
            [BaseVectorIndex("document.semantic", nameof(Embedding), VectorSpace = "text.embedding.v1", Dimensions = 3)]
            public sealed partial record Document
            {
                [BaseField("duplicate")] public required string Tenant { get; init; }
                [BaseField("duplicate")] public required BaseVector Embedding { get; init; }
            }
            [JsonSerializable(typeof(Document))]
            public sealed partial class AppJsonContext : JsonSerializerContext;
            public static class Consumer
            {
                public static object[] Values() =>
                    [Document.Collection, Document.Fields.Tenant, Document.Fields.Embedding,
                     Document.VectorIndexes.Semantic];
            }
            """;

        GeneratorResult result = Run(source);

        result.Diagnostics.Should().ContainSingle(item => item.Id == "HPDBASE011");
        result.GeneratedSource.Should().Contain("VectorIndexes").And.NotContain("RegisterContext");
        result.CompilationDiagnostics.Where(static item => item.Id is "CS0117" or "CS1061" or "CS1503" or "CS0411")
            .Should().BeEmpty();
    }

    [Fact]
    public void GatewayScaleSharedContextStillProducesOneContextDiagnostic()
    {
        var source = new System.Text.StringBuilder("""
            using HPD.Base;
            using System.Text.Json.Serialization;
            """);
        for (int index = 0; index < 18; index++)
        {
            source.AppendLine($$"""
                [BaseCollection("gateway.collection.{{index}}", typeof(GatewayContext))]
                public sealed partial record GatewayRecord{{index}}
                {
                    [BaseField("gateway.field.{{index}}")] public required string Value { get; init; }
                }
                """);
        }
        source.AppendLine("[JsonSourceGenerationOptions(UseStringEnumConverter = true)]");
        for (int index = 0; index < 18; index++)
            source.AppendLine($"[JsonSerializable(typeof(GatewayRecord{index}))]");
        source.AppendLine("public sealed partial class GatewayContext : JsonSerializerContext;");

        GeneratorResult result = Run(source.ToString());

        Diagnostic diagnostic = result.Diagnostics.Should().ContainSingle(item => item.Id == "HPDBASE0450").Subject;
        diagnostic.GetMessage().Should().Contain("18 dependent roots");
        result.CompilationDiagnostics.Where(static item => item.Id is "CS0117" or "CS1061" or "CS1503" or "CS0411")
            .Should().BeEmpty();
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
            [new BaseSchemaGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        GeneratorDriverRunResult result = driver.GetRunResult();

        return new GeneratorResult(
            result.Diagnostics,
            string.Join(
                "\n",
                result.Results.SelectMany(item => item.GeneratedSources)
                    .Select(item => item.SourceText.ToString())),
            output.GetDiagnostics());
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
            .Append(typeof(BaseVector).Assembly.Location)
            .Distinct(StringComparer.Ordinal)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }

    private sealed record GeneratorResult(
        ImmutableArray<Diagnostic> Diagnostics,
        string GeneratedSource,
        ImmutableArray<Diagnostic> CompilationDiagnostics);
}
