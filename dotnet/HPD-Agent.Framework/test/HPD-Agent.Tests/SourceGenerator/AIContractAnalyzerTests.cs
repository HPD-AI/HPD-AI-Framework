using System.Collections.Immutable;
using HPD.Agent.SourceGenerator.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HPD.Agent.Tests.SourceGenerator;

public sealed class AIContractAnalyzerTests
{
    [Fact]
    public void Analyze_ProducesExactStringEnumContract()
    {
        var type = GetTypeSymbol("public enum SearchMode { Files, Content }");

        var result = AIContractAnalyzer.Analyze(type, "mode", "Search mode");

        var contract = Assert.IsType<ScalarContractNode>(result.Contract);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(AIScalarKind.Enum, contract.Kind);
        Assert.Equal(new[] { "Files", "Content" }, contract.AllowedValues.AsEnumerable());
        Assert.False(contract.AllowsNull);
    }

    [Fact]
    public void Analyze_PreservesNestedCollectionItemContracts()
    {
        var type = GetTypeSymbol("public sealed class Marker { public System.Collections.Generic.IReadOnlyList<int?[]> Values => null!; }")
            .GetMembers("Values")
            .OfType<IPropertySymbol>()
            .Single()
            .Type;

        var result = AIContractAnalyzer.Analyze(type, "values", null);

        var list = Assert.IsType<ArrayContractNode>(result.Contract);
        var array = Assert.IsType<ArrayContractNode>(list.Item);
        var item = Assert.IsType<ScalarContractNode>(array.Item);
        Assert.Equal(AIScalarKind.Integer, item.Kind);
        Assert.True(item.AllowsNull);
    }

    [Fact]
    public void Analyze_ProducesTypedStringDictionaryContract()
    {
        var type = GetTypeSymbol("public sealed class Marker { public System.Collections.Generic.IReadOnlyDictionary<string, bool> Values => null!; }")
            .GetMembers("Values")
            .OfType<IPropertySymbol>()
            .Single()
            .Type;

        var result = AIContractAnalyzer.Analyze(type, "values", null);

        var dictionary = Assert.IsType<DictionaryContractNode>(result.Contract);
        var value = Assert.IsType<ScalarContractNode>(dictionary.Value);
        Assert.Equal(AIScalarKind.Boolean, value.Kind);
    }

    [Fact]
    public void Analyze_RejectsOpenObjectInput()
    {
        var compilation = CreateCompilation("public sealed class Marker { public object Value => null!; }");
        var type = compilation.GetSpecialType(SpecialType.System_Object);

        var result = AIContractAnalyzer.Analyze(type, "request.payload", null);

        Assert.Null(result.Contract);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("HPDAI002", diagnostic.Id);
        Assert.Contains("request.payload", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalSchemaEmitter_EmitsDeterministicSchema()
    {
        var compilation = CreateCompilation("""
            public enum SearchMode { Files, Content }
            public sealed class Tool
            {
                public void Search(SearchMode mode, System.Collections.Generic.IReadOnlyList<int?> limits) { }
            }
            """);
        var tool = compilation.GlobalNamespace.GetTypeMembers("Tool").Single();
        var method = tool.GetMembers("Search").OfType<IMethodSymbol>().Single();
        var parameters = method.Parameters.Select(parameter =>
        {
            var analysis = AIContractAnalyzer.Analyze(parameter.Type, parameter.Name, null);
            return new AIFunctionContractParameter(parameter, parameter.Name, analysis.Contract!, IsRequired: true);
        }).ToImmutableArray();

        var schema = AICanonicalSchemaEmitter.Emit(new AIFunctionMethodContract(parameters));

        Assert.Equal(
            "{\"type\":\"object\",\"properties\":{\"mode\":{\"type\":\"string\",\"enum\":[\"Files\",\"Content\"]},\"limits\":{\"type\":\"array\",\"items\":{\"type\":[\"integer\",\"null\"]}}},\"required\":[\"mode\",\"limits\"],\"additionalProperties\":false}",
            schema);
    }

    [Fact]
    public void Analyze_ProducesClosedImmutableObjectContract()
    {
        var compilation = CreateCompilation("""
            using System.ComponentModel;
            using System.Text.Json.Serialization;
            public sealed record Request(
                [property: JsonPropertyName("target_path"), Description("Target path")] string Target,
                int? Limit = null);
            """);
        var type = GetSourceType(compilation, "Request");

        var result = AIContractAnalyzer.Analyze(type, "request", null);

        var contract = Assert.IsType<ObjectContractNode>(result.Contract);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(new[] { "target_path", "limit" }, contract.Properties.Select(property => property.JsonName));
        Assert.True(contract.Properties[0].IsRequired);
        Assert.False(contract.Properties[1].IsRequired);
        Assert.Equal("Target path", contract.Properties[0].Description);
        Assert.NotNull(contract.Construction.Constructor);
    }

    [Fact]
    public void AnalyzeAndEmit_ProducesClosedDiscriminatedUnion()
    {
        var compilation = CreateCompilation("""
            using System.Text.Json.Serialization;
            [JsonPolymorphic(TypeDiscriminatorPropertyName = "action")]
            [JsonDerivedType(typeof(LaunchRequest), "launch")]
            [JsonDerivedType(typeof(ContinueRequest), "continue")]
            public abstract record OperationRequest;
            public sealed record LaunchRequest(string Target) : OperationRequest;
            public sealed record ContinueRequest(string DebugTreeId, int? ThreadId = null) : OperationRequest;
            public sealed class Tool { public void Execute(OperationRequest request) { } }
            """);
        var method = GetSourceType(compilation, "Tool").GetMembers("Execute").OfType<IMethodSymbol>().Single();
        var parameter = method.Parameters.Single();
        var result = AIContractAnalyzer.Analyze(parameter.Type, "request", null);
        var union = Assert.IsType<UnionContractNode>(result.Contract);
        var schema = AICanonicalSchemaEmitter.Emit(new AIFunctionMethodContract([
            new AIFunctionContractParameter(parameter, "request", union, IsRequired: true)
        ]));

        Assert.Empty(result.Diagnostics);
        Assert.Equal(new[] { "launch", "continue" }, union.Cases.Select(@case => @case.Discriminator));
        Assert.Contains("\"oneOf\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"action\":{\"type\":\"string\",\"const\":\"launch\"}", schema, StringComparison.Ordinal);
        Assert.Contains("\"required\":[\"action\",\"target\"]", schema, StringComparison.Ordinal);
        Assert.Contains("\"additionalProperties\":false", schema, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_RejectsPropertyThatGeneratedCodeCannotSet()
    {
        var compilation = CreateCompilation("public sealed class Request { public string Value { get; private set; } = string.Empty; public Request() { } }");

        var result = AIContractAnalyzer.Analyze(GetSourceType(compilation, "Request"), "request", null);

        Assert.Null(result.Contract);
        Assert.Equal("HPDAI005", Assert.Single(result.Diagnostics).Id);
    }

    private static INamedTypeSymbol GetTypeSymbol(string source)
    {
        var compilation = CreateCompilation(source);
        return compilation.GlobalNamespace.GetTypeMembers()
            .Single(type => SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly));
    }

    private static INamedTypeSymbol GetSourceType(CSharpCompilation compilation, string name) =>
        compilation.GlobalNamespace.GetTypeMembers(name)
            .Single(type => SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly));

    private static CSharpCompilation CreateCompilation(string source)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToImmutableArray();

        return CSharpCompilation.Create(
            "AIContractAnalyzerTests",
            [CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }
}
