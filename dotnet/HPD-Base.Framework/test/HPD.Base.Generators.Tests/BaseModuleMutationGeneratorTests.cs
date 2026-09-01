using System.Collections.Immutable;
using FluentAssertions;
using HPD.Base;
using HPD.Base.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace HPD.Base.Generators.Tests;

public sealed class BaseModuleMutationGeneratorTests
{
    [Fact]
    public void EmitsOpaqueActivationDtoAuthorityFromFlatGeneratedRoots()
    {
        Result result = Run("""
            using HPD.Base;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            [BaseActivationDtoAuthority("work.activation.dto", 1, "work", "work.input.v1", "work.result.v1",
                typeof(AppJsonContext), typeof(Input), typeof(Output))]
            public static partial class WorkActivationDtos;
            public sealed record Input
            {
                [BaseField("work.input.id", MinimumUtf8Bytes=1, MaximumUtf8Bytes=64)]
                [BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
                public required string Id { get; init; }
            }
            public sealed record Output
            {
                [BaseField("work.result.digest", MinimumBytes=32, MaximumBytes=32)]
                [BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
                public required BaseBinary Digest { get; init; }
            }
            [JsonSerializable(typeof(Input))]
            [JsonSerializable(typeof(Output))]
            public sealed partial class AppJsonContext : JsonSerializerContext;
            """);

        result.Diagnostics.Should().BeEmpty();
        result.Source.Should().Contain("BaseGeneratedActivationDtoAuthority<global::Input, global::Output>")
            .And.Contain("BaseGeneratedActivationDtos.Register<global::Input, global::Output>")
            .And.Contain("\"work.activation.dto\", 1, \"work\", \"work.input.v1\", \"work.result.v1\"")
            .And.Contain("MinimumBinaryBytes = 32")
            .And.Contain("MaximumBinaryBytes = 32");
        result.CompilationDiagnostics.Where(static value => value.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
            && value.Id != "CS1729"
            && value.Location.SourceTree?.FilePath.Contains("HPDBaseActivationDto", StringComparison.Ordinal) == true)
            .Should().BeEmpty(result.Source);
    }

    [Fact]
    public void InternalGraphOwnedOperationRemainsInternal()
    {
        Result result = Run("""
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseRegisteredModuleMutation("inventory.internal", typeof(AppJsonContext), typeof(Request), typeof(Result), OwningModuleId="inventory", GrantId="inventory.internal")]
            internal static partial class InternalOperation { internal static BaseRegisteredModuleMutationDefinition Definition => throw null!; }
            internal sealed record Request { [BaseField("request.id")] public required string Id { get; init; } }
            internal sealed record Result { [BaseField("result.ok")] public required bool Ok { get; init; } }
            [JsonSerializable(typeof(Request))]
            [JsonSerializable(typeof(Result))]
            internal sealed partial class AppJsonContext : JsonSerializerContext;
            """);

        result.Diagnostics.Should().BeEmpty();
        result.Source.Should().Contain("internal static partial class InternalOperation")
            .And.NotContain("public static partial class InternalOperation");
        result.CompilationDiagnostics.Should().NotContain(value =>
            value.Id == "CS0050" || value.Id == "CS0051" || value.Id == "CS0053");
    }

    [Fact]
    public void EmitsOnlyInertIdentityBoundToGraphOwnedDtos()
    {
        Result result = Run("""
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseRegisteredModuleMutation("payments.apply", typeof(AppJsonContext), typeof(ApplyRequest), typeof(ApplyResult), Version=1, OwningModuleId="payments", GrantId="payments.apply")]
            public static partial class Apply
            {
                internal static BaseRegisteredModuleMutationDefinition Definition => throw null!;
            }
            public sealed record ApplyRequest { [BaseField("payments.request.id")] public required string Id { get; init; } }
            public sealed record ApplyResult { [BaseField("payments.result.ok")] public required bool Ok { get; init; } }
            [JsonSerializable(typeof(ApplyRequest))]
            [JsonSerializable(typeof(ApplyResult))]
            public sealed partial class AppJsonContext : JsonSerializerContext;
            """);

        result.Diagnostics.Should().BeEmpty();
        result.Source.Should().Contain("BaseGeneratedModuleMutationIdentity")
            .And.Contain("RegisterContext(__HPDBaseSerializerFactory.Create)")
            .And.Contain("Definition.Checksum.ToArray()")
            .And.Contain("CreateSemanticActivationKeyIdentity<TDefinition>")
            .And.Contain("BaseGeneratedSemanticActivations.Register")
            .And.NotContain("ExecuteAsync");
        result.CompilationDiagnostics.Where(static value => value.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
            && value.Id != "CS1729"
            && value.Location.SourceTree?.FilePath.Contains("HPDBaseModuleMutation", StringComparison.Ordinal) == true).Should().BeEmpty(result.Source);
    }

    [Fact]
    public void InvalidDeclarationGetsOneDiagnosticAndInertRecovery()
    {
        Result result = Run("""
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseRegisteredModuleMutation("payments.apply", typeof(AppJsonContext), typeof(ApplyRequest), typeof(ApplyResult), OwningModuleId="", GrantId="payments.apply")]
            public static partial class Apply;
            public sealed record ApplyRequest { [BaseField("payments.request.id")] public required string Id { get; init; } }
            public sealed record ApplyResult { [BaseField("payments.result.ok")] public required bool Ok { get; init; } }
            [JsonSerializable(typeof(ApplyRequest))]
            [JsonSerializable(typeof(ApplyResult))]
            public sealed partial class AppJsonContext : JsonSerializerContext;
            """);

        result.Diagnostics.Should().ContainSingle(value => value.Id == "HPDBASE0500");
        result.Source.Should().Contain("base.moduleMutation.invalid").And.NotContain("RegisterContext");
    }

    [Fact]
    public void EmitsNestedRequestPathsAndExactResultDisclosure()
    {
        Result result = Run("""
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseRegisteredModuleMutation("payments.nested", typeof(AppJsonContext), typeof(Request), typeof(Result), OwningModuleId="payments", GrantId="payments.nested")]
            public static partial class Nested { internal static BaseRegisteredModuleMutationDefinition Definition => throw null!; }
            public sealed record Request { [BaseField("request.owner")] public required Owner Owner { get; init; } }
            public sealed record Owner { [BaseField("owner.id")] public required string Id { get; init; } }
            public sealed record Result
            {
                [BaseField("result.secret")]
                [BaseFieldDisclosure(RecordRead = BaseRecordDisclosure.FixedMarker)]
                public required string Secret { get; init; }
            }
            [JsonSerializable(typeof(Request))]
            [JsonSerializable(typeof(Result))]
            public sealed partial class AppJsonContext : JsonSerializerContext;
            """);

        result.Diagnostics.Should().BeEmpty();
        result.Source.Should().Contain("CreatePathWire<")
            .And.Contain("new string[] { \"request.owner\", \"owner.id\",")
            .And.Contain("(global::HPD.Base.BaseRecordDisclosure)2");
    }

    [Fact]
    public void EmitsExactFrozenWirePathsForNamingPolicyAndExplicitNames()
    {
        Result result = Run("""
            using HPD.Base;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            [BaseRegisteredModuleMutation("payments.wire", typeof(AppJsonContext), typeof(Request), typeof(Result), OwningModuleId="payments", GrantId="payments.wire")]
            public static partial class Wire { internal static BaseRegisteredModuleMutationDefinition Definition => throw null!; }
            public sealed record Request
            {
                [BaseField("request.owner")]
                [JsonPropertyName("exact-owner")]
                public required Owner AccountOwner { get; init; }
            }
            public sealed record Owner { [BaseField("owner.id")] public required string SubjectValue { get; init; } }
            public sealed record Result { [BaseField("result.ok")] public required bool IsReady { get; init; } }
            [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
            [JsonSerializable(typeof(Request))]
            [JsonSerializable(typeof(Result))]
            public sealed partial class AppJsonContext : JsonSerializerContext;
            """);

        result.Diagnostics.Should().BeEmpty();
        result.Source.Should().Contain("new string[] { \"exact-owner\", \"subject_value\",")
            .And.Contain("CreateWire<global::Result, bool>(\"result.ok\", \"IsReady\", \"is_ready\"");
    }

    [Fact]
    public void EmitsCompleteSpecialScalarAuthorityWithoutReflection()
    {
        Result result = Run("""
            using HPD.Base;
            using System;
            using System.Text.Json.Serialization;
            [BaseRegisteredModuleMutation("work.special", typeof(AppJsonContext), typeof(Request), typeof(Result), OwningModuleId="work", GrantId="work.special")]
            public static partial class Special { internal static BaseRegisteredModuleMutationDefinition Definition => throw null!; }
            public enum ProcessingMode { Ready = 0, Done = 1 }
            public sealed record Request
            {
                [BaseField("request.id")]
                [JsonConverter(typeof(BaseCanonicalGuidJsonConverter))]
                public required Guid Id { get; init; }
                [BaseField("request.payload", MaximumBytes = 4096)]
                public required BaseBinary Payload { get; init; }
                [BaseField("request.metadata", MaximumCanonicalJsonBytes = 8192, JsonShape = BaseJsonShape.Object,
                    MaximumJsonDepth = 8, MaximumJsonArrayItems = 32, MaximumJsonObjectProperties = 32,
                    MaximumJsonTotalNodes = 128, MaximumJsonTotalStringUtf8Bytes = 4096, MaximumJsonTotalNameUtf8Bytes = 2048)]
                public required BaseCanonicalJson Metadata { get; init; }
                [BaseField("request.mode", AllowedEnumLiterals = new[] { "ready", "done" })]
                [JsonConverter(typeof(BaseClosedEnumJsonConverter<ProcessingMode>))]
                public required ProcessingMode Mode { get; init; }
            }
            public sealed record Result
            {
                [BaseField("result.id")]
                [JsonConverter(typeof(BaseCanonicalGuidJsonConverter))]
                public required Guid Id { get; init; }
            }
            [JsonSerializable(typeof(Request))]
            [JsonSerializable(typeof(Result))]
            public sealed partial class AppJsonContext : JsonSerializerContext;
            """);

        result.Diagnostics.Should().BeEmpty();
        result.CompilationDiagnostics.Where(static value => value.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
            && value.Id != "CS1729"
            && value.Location.SourceTree?.FilePath.Contains("HPDBaseModuleMutation", StringComparison.Ordinal) == true)
            .Should().BeEmpty(result.Source);
        result.Source.Should().Contain("BaseModuleValueKind.Guid")
            .And.Contain("BaseModuleRequestProperty<global::Request, global::System.Guid> Id")
            .And.Contain("BaseModuleResultProperty<global::Result, global::System.Guid> Id")
            .And.Contain("MaximumBinaryBytes = 4096")
            .And.Contain("MaximumCanonicalJsonBytes = 8192")
            .And.Contain("AllowedEnumLiterals = [\"done\", \"ready\"]");
    }

    [Fact]
    public void EmitsOptionalNonNullValueAuthorityWithoutTreatingAbsenceAsNullability()
    {
        Result result = Run("""
            using HPD.Base;
            using System;
            using System.Text.Json.Serialization;
            [BaseCollection("work.targets", typeof(AppJsonContext))]
            public sealed partial record WorkTarget
            {
                [BaseField("work.targets.id", MinimumUtf8Bytes=1, MaximumUtf8Bytes=64)]
                public required string Id { get; init; }
            }
            public enum ProcessingMode { [JsonStringEnumMemberName("ready")] Ready }
            [BaseRegisteredModuleMutation("work.optional", typeof(AppJsonContext), typeof(Request), typeof(Result), OwningModuleId="work", GrantId="work.optional")]
            public static partial class OptionalOperation { internal static BaseRegisteredModuleMutationDefinition Definition => throw null!; }
            public sealed record Request
            {
                [BaseField("request.instant", Presence=BaseFieldPresence.Optional, Nullability=BaseFieldNullability.NonNullable)]
                [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
                public DateTimeOffset? Instant { get; init; }
                [BaseField("request.target", Presence=BaseFieldPresence.Optional, Nullability=BaseFieldNullability.NonNullable)]
                public BaseRecordId<WorkTarget>? Target { get; init; }
                [BaseField("request.guid", Presence=BaseFieldPresence.Optional, Nullability=BaseFieldNullability.NonNullable)]
                [JsonConverter(typeof(BaseCanonicalNullableGuidJsonConverter))]
                public Guid? CorrelationId { get; init; }
                [BaseField("request.count", Presence=BaseFieldPresence.Optional, Nullability=BaseFieldNullability.NonNullable)]
                public int? Count { get; init; }
                [BaseField("request.mode", Presence=BaseFieldPresence.Optional, Nullability=BaseFieldNullability.NonNullable, AllowedEnumLiterals=new[] { "ready" })]
                [JsonConverter(typeof(BaseClosedEnumJsonConverter<ProcessingMode>))]
                public ProcessingMode? Mode { get; init; }
            }
            public sealed record Result
            {
                [BaseField("result.ok")]
                public required bool Ok { get; init; }
            }
            [JsonSerializable(typeof(WorkTarget))]
            [JsonSerializable(typeof(Request))]
            [JsonSerializable(typeof(Result))]
            [JsonSourceGenerationOptions(DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull)]
            public sealed partial class AppJsonContext : JsonSerializerContext;
            """);

        result.Diagnostics.Should().BeEmpty();
        result.CompilationDiagnostics.Where(static value => value.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
            && value.Id != "CS1729"
            && value.Location.SourceTree?.FilePath.Contains("HPDBaseModuleMutation", StringComparison.Ordinal) == true)
            .Should().BeEmpty(result.Source);
        result.Source.Should().Contain("BaseModuleRequestProperty<global::Request, global::System.DateTimeOffset?> Instant")
            .And.Contain("(global::HPD.Base.BaseFieldPresence)1, (global::HPD.Base.BaseFieldNullability)0")
            .And.Contain("BaseModuleRequestProperty<global::Request, global::HPD.Base.BaseRecordId<global::WorkTarget>?> Target")
            .And.Contain("BaseRecordIdJsonConverterFactory.Register<global::WorkTarget>()")
            .And.Contain("BaseModuleRequestProperty<global::Request, global::System.Guid?> CorrelationId")
            .And.Contain("BaseModuleRequestProperty<global::Request, int?> Count")
            .And.Contain("BaseModuleRequestProperty<global::Request, global::ProcessingMode?> Mode")
            .And.Contain("JsonIgnoreCondition.WhenWritingNull");
    }

    [Fact]
    public void EmitsGeneratedSubjectReferenceAndIncarnationAuthorityWithoutRawContracts()
    {
        Result result = Run("""
            using HPD.Base;
            using System.Text.Json.Serialization;
            [BaseCollection("private.users", typeof(AppJsonContext), Name="users", System=true, SystemOwnerModuleId="identity")]
            public sealed partial record UserRecord
            {
                [BaseField("user.active")] public required bool Active { get; init; }
                [BaseField("user.tombstoned")] public required bool Tombstoned { get; init; }
            }
            [BaseExportedSubject("identity.user", OwningModuleId="identity", PrivateRecordType=typeof(UserRecord),
                AcquisitionGrantId="identity.user.acquire", ValidationGrantId="identity.user.validate",
                AdministrationGrantId="identity.user.admin", ValidationPlanId="identity.user.validation",
                ActiveFieldId="user.active", TombstoneFieldId="user.tombstoned")]
            public partial class UserSubject;
            [BaseRegisteredModuleMutation("identity.subject", typeof(AppJsonContext), typeof(Request), typeof(Result),
                OwningModuleId="identity", GrantId="identity.subject")]
            public static partial class Operation { internal static BaseRegisteredModuleMutationDefinition Definition => throw null!; }
            public sealed record Request
            {
                [BaseField("request.subject")]
                [BaseSubjectReference(typeof(UserSubject), Requirement=BaseSubjectReferenceRequirement.Exists)]
                public required BaseSubjectReference<UserSubject> Subject { get; init; }
                [BaseField("request.incarnation")]
                public required BaseSubjectIncarnation Incarnation { get; init; }
            }
            public sealed record Result
            {
                [BaseField("result.subject")]
                [BaseSubjectReference(typeof(UserSubject), Requirement=BaseSubjectReferenceRequirement.Exists)]
                public required BaseSubjectReference<UserSubject> Subject { get; init; }
            }
            [JsonSerializable(typeof(Request))]
            [JsonSerializable(typeof(Result))]
            [JsonSerializable(typeof(UserRecord))]
            public sealed partial class AppJsonContext : JsonSerializerContext;
            """);

        result.Diagnostics.Should().BeEmpty();
        result.CompilationDiagnostics.Where(static value => value.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
            && value.Id != "CS1729"
            && value.Location.SourceTree?.FilePath.Contains("HPDBase", StringComparison.Ordinal) == true)
            .Should().BeEmpty(result.Source);
        result.Source.Should().Contain("BaseGeneratedModuleScalarManifest.Subject<global::UserSubject>")
            .And.Contain("BaseGeneratedModuleScalarManifest.SubjectIncarnation()")
            .And.NotContain("ContractChecksum = \"identity.user");
    }

    private static Result Run(string source)
    {
        var parse = new CSharpParseOptions(LanguageVersion.CSharp14);
        var compilation = CSharpCompilation.Create("ModuleMutationGeneratorTests", [CSharpSyntaxTree.ParseText(source, parse)], References(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new BaseSchemaGenerator().AsSourceGenerator()], parseOptions: parse);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        GeneratorDriverRunResult result = driver.GetRunResult();
        return new Result(result.Diagnostics, string.Join("\n", result.Results.SelectMany(value => value.GeneratedSources).Select(value => value.SourceText.ToString())), output.GetDiagnostics());
    }

    private static ImmutableArray<MetadataReference> References() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(BaseRegisteredModuleMutationAttribute).Assembly.Location)
            .Distinct(StringComparer.Ordinal).Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToImmutableArray();

    private sealed record Result(ImmutableArray<Diagnostic> Diagnostics, string Source, ImmutableArray<Diagnostic> CompilationDiagnostics);
}
