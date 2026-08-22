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
