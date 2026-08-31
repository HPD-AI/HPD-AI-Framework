using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Microsoft.Extensions.AI; // For AIFunction
using HPD.Agent; // For ToolHarnessAttribute


namespace HPD.Agent.Tests.SourceGenerator;

public class HPDToolSourceGeneratorTests
{
    private static (string? generatedCode, ImmutableArray<Diagnostic> diagnostics) RunGenerator(
        string source,
        bool includeCompilationDiagnostics = false)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

        // Use all assemblies already loaded in the test AppDomain so that transitive
        // dependencies of HPD-Agent (IAgentMiddleware, context types, etc.) resolve
        // correctly and GetDeclaredSymbol does not return null due to binding errors.
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new global::HPDToolSourceGenerator(); // HPDToolSourceGenerator is in the global namespace
        // Pass parseOptions to the driver so generated syntax trees use the same language version
        // as the input trees, avoiding "Inconsistent language versions" with Roslyn 5.
        CSharpGeneratorDriver.Create(
                generators: new ISourceGenerator[] { generator.AsSourceGenerator() },
                additionalTexts: Enumerable.Empty<AdditionalText>(),
                parseOptions: parseOptions,
                optionsProvider: null)
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        if (includeCompilationDiagnostics)
            diagnostics = diagnostics.AddRange(outputCompilation.GetDiagnostics());

        var generatedSyntaxTrees = outputCompilation.SyntaxTrees
            .Where(st => st.FilePath.Contains("g.cs")) // Filter for generated files
            .ToImmutableArray();

        // Join all generated source code into a single string for easier assertion
        var generatedSourceCode = string.Join("\n\n", generatedSyntaxTrees.Select(st => st.GetText().ToString()));

        return (generatedSourceCode, diagnostics);
    }

    [Fact]
    public void GeneratedToolHarness_WithDynamicCollapseInstructions_ContainsCorrectCode()
    {
        // Arrange - Using an expression (method call) as attribute value
        // The source generator detects this as an expression rather than a literal string
        var HARNESSource = @$"
using HPD.Agent;
using System;

namespace TestToolHarnesses
{{
    public static class DynamicInstructionsProvider
    {{
        public static string GetInstructions()
        {{
            return ""Dynamic instructions for the collapsed ToolHarness."";
        }}
    }}

    [Collapse(""Test collapsed ToolHarness"",   FunctionResult = DynamicInstructionsProvider.GetInstructions())]
    public partial class CollapsedTestToolHarness
    {{
        [AIFunction]
        public string HelloWorld() => ""Hello!"";
    }}
}}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(HARNESSource);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.NotNull(generatedCode);
        Assert.Contains("var dynamicInstructions = DynamicInstructionsProvider.GetInstructions();", generatedCode);
        Assert.Contains("dynamicInstructions", generatedCode);
    }
    
    [Fact]
    public void GeneratedToolHarness_WithCustomAIFunctionName_UsesCustomFunctionName()
    {
        var source = @"
using HPD.Agent;
using System;

namespace TestToolHarnesses
{
    public partial class WeatherToolHarness
    {
        [AIFunction(Name = ""get_weather""), AIDescription(""Gets weather for a city"")]
        public string GetWeather(string city) => city;
    }
}
";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.NotNull(generatedCode);
        Assert.Contains("FunctionNames: new string[] { \"get_weather\" }", generatedCode);
        Assert.Contains("Name = \"get_weather\"", generatedCode);
        Assert.Contains("JsonDocument.Parse", generatedCode);
        Assert.Contains("\\\"city\\\":{\\\"type\\\":\\\"string\\\"", generatedCode);
        Assert.DoesNotContain("CreateJsonSchema(\r\n        typeof(GetWeatherArgs)", generatedCode);
    }

    [Fact]
    public void GeneratedToolHarness_WithStaticCollapseInstructions_ContainsCorrectCode()
    {
        // Arrange
        var HARNESSource = @$"
using HPD.Agent;
using System;

namespace TestToolHarnesses
{{
    [Collapse(""Test static collapsed ToolHarness"",   FunctionResult = ""Static instructions here."")]
    public partial class StaticCollapsedTestToolHarness
    {{
        [AIFunction]
        public string HelloStatic() => ""Hello Static!"";
    }}
}}
";
        // Act
        var (generatedCode, diagnostics) = RunGenerator(HARNESSource);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.NotNull(generatedCode);
        Assert.Contains("Static instructions here.", generatedCode);
    }

    [Fact]
    public void GeneratedToolHarness_WithEnumParameter_ParsesStringEnumArguments()
    {
        var source = @"
using HPD.Agent;
using System;

namespace TestToolHarnesses
{
    public enum SearchMode
    {
        Files,
        Content
    }

    [Collapse(""Enum toolharness"", FunctionResult = ""ok"")]
    public partial class EnumToolHarness
    {
        [AIFunction]
        public string Search(string pattern, SearchMode mode = SearchMode.Files) => mode.ToString();
    }
}
";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.NotNull(generatedCode);
        Assert.Contains("ArgumentBinder = BindSearchArguments", generatedCode);
        Assert.Contains("HPDGeneratedToolArgumentBinder.ValidateProperties(json, \"\", \"pattern\", \"mode\")", generatedCode);
        Assert.Contains("GetBoundArguments<SearchArgs>()", generatedCode);
        Assert.Contains("\"Files\" => global::TestToolHarnesses.SearchMode.Files", generatedCode);
        Assert.Contains("\\\"enum\\\":[\\\"Files\\\",\\\"Content\\\"]", generatedCode);
        Assert.DoesNotContain("HPDToolArgumentBinder.BindOptional", generatedCode);
    }

    [Fact]
    public void GeneratedToolHarness_WithUnion_CompilesStrictDirectBinder()
    {
        var source = """
            using System.Text.Json.Serialization;
            using HPD.Agent;

            namespace GeneratedContracts
            {
                [JsonPolymorphic(TypeDiscriminatorPropertyName = "action")]
                [JsonDerivedType(typeof(LaunchRequest), "launch")]
                [JsonDerivedType(typeof(ContinueRequest), "continue")]
                public abstract record OperationRequest;
                public sealed record LaunchRequest(string Target) : OperationRequest;
                public sealed record ContinueRequest(string DebugTreeId, int? ThreadId = null) : OperationRequest;

                [Collapse("Union", FunctionResult = "ok")]
                public partial class UnionToolHarness
                {
                    [AIFunction]
                    public string Execute(OperationRequest request) => request.GetType().Name;
                }
            }
            """;

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.False(string.IsNullOrEmpty(generatedCode), string.Join("\n", diagnostics.Select(diagnostic => diagnostic.ToString())));
        Assert.Contains("unknown_union_discriminator", generatedCode);
        Assert.Contains("\"launch\" => BindContract_Execute_request_case_launch", generatedCode);
        Assert.Contains("GetBoundArguments<ExecuteArgs>()", generatedCode);
        Assert.DoesNotContain("JsonSerializer.Deserialize", generatedCode);
    }

    [Fact]
    public void GeneratedToolHarness_ActionUnion_ComposesBranchPolicyBeforeFactory()
    {
        var source = """
            using System.Text.Json.Serialization;
            using HPD.Agent;
            namespace GeneratedContracts
            {
                [JsonPolymorphic(TypeDiscriminatorPropertyName = "action")]
                [JsonDerivedType(typeof(Read), "read")]
                [JsonDerivedType(typeof(Run), "run")]
                public abstract record Request;
                [AIFunctionAction("read")]
                public sealed record Read(string Id) : Request;
                [AIFunctionAction("run", InvocationModePolicy = AIFunctionActionInvocationModePolicy.ModelChoice,
                    InvocationModeHandling = AIFunctionActionInvocationModeHandling.ToolBody)]
                public sealed record Run(string Id) : Request;
                [Collapse("Action", FunctionResult = "ok")]
                public partial class Harness
                {
                    [AIFunction]
                    public string Execute(Request request) => "ok";
                }
            }
            """;

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Contains("OperationContractSchemaComposed = true", generatedCode);
        Assert.Contains("AIFunctionOperationContract", generatedCode);
        Assert.Contains("\\\"invocationMode\\\"", generatedCode);
    }

    [Fact]
    public void GeneratedToolHarness_ActionMismatch_ReportsDiagnostic()
    {
        var source = """
            using System.Text.Json.Serialization;
            using HPD.Agent;
            namespace GeneratedContracts
            {
                [JsonPolymorphic(TypeDiscriminatorPropertyName = "action")]
                [JsonDerivedType(typeof(Read), "read")]
                public abstract record Request;
                [AIFunctionAction("wrong")]
                public sealed record Read(string Id) : Request;
                [Collapse("Action", FunctionResult = "ok")]
                public partial class Harness
                {
                    [AIFunction]
                    public string Execute(Request request) => "ok";
                }
            }
            """;

        var (_, diagnostics) = RunGenerator(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "HPD070");
    }

    [Fact]
    public void GeneratedToolHarness_InvalidActionEnum_ReportsDiagnostic()
    {
        var source = """
            using System.Text.Json.Serialization;
            using HPD.Agent;
            namespace GeneratedContracts
            {
                [JsonPolymorphic(TypeDiscriminatorPropertyName = "action")]
                [JsonDerivedType(typeof(Read), "read")]
                public abstract record Request;
                [AIFunctionAction("read", InvocationModePolicy = (AIFunctionActionInvocationModePolicy)99)]
                public sealed record Read(string Id) : Request;
                [Collapse("Action", FunctionResult = "ok")]
                public partial class Harness
                {
                    [AIFunction] public string Execute(Request request) => "ok";
                }
            }
            """;

        var (_, diagnostics) = RunGenerator(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "HPD070");
    }

    [Fact]
    public void GeneratedToolHarness_ActionAnnotationOutsideUnion_ReportsDiagnostic()
    {
        var source = """
            using HPD.Agent;
            namespace GeneratedContracts
            {
                [AIFunctionAction("read")]
                public sealed record Request(string Id);
                [Collapse("Action", FunctionResult = "ok")]
                public partial class Harness
                {
                    [AIFunction] public string Execute(Request request) => "ok";
                }
            }
            """;

        var (_, diagnostics) = RunGenerator(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "HPD070");
    }

    [Fact]
    public void GeneratedToolHarness_UndeclaredDerivedAction_ReportsDiagnostic()
    {
        var source = """
            using System.Text.Json.Serialization;
            using HPD.Agent;
            namespace GeneratedContracts
            {
                [JsonPolymorphic(TypeDiscriminatorPropertyName = "action")]
                [JsonDerivedType(typeof(Read), "read")]
                public abstract record Request;
                [AIFunctionAction("read")]
                public sealed record Read(string Id) : Request;
                [AIFunctionAction("hidden")]
                public sealed record Hidden(string Id) : Request;
                [Collapse("Action", FunctionResult = "ok")]
                public partial class Harness
                {
                    [AIFunction] public string Execute(Request request) => "ok";
                }
            }
            """;

        var (_, diagnostics) = RunGenerator(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "HPD070");
    }

    // ── T047 ─────────────────────────────────────────────────────────────────
    // Config constructors become generated execution-owned descriptors.
    [Fact]
    public void SourceGen_EmitsExecutionOwnedDescriptor_ForConfigCtorMiddleware()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.Middleware;
using System;
using System.Text.Json.Serialization;

namespace Ns
{
    [ToolHarnessJsonContext(typeof(ConfigCtorJsonContext))]
    public class MyConfig { }

    [JsonSerializable(typeof(MyConfig))]
    public partial class ConfigCtorJsonContext : JsonSerializerContext { }

    public class ConfigCtorMiddleware : IToolHarnessMiddleware
    {
        public ConfigCtorMiddleware(MyConfig config) { }
    }

    [Collapse(""ConfigCtor toolharness"", FunctionResult = ""ok"",
        Middlewares = [typeof(ConfigCtorMiddleware)])]
    public partial class ConfigCtorToolHarness
    {
        [AIFunction]
        public string Ping() => ""pong"";
    }
}
";
        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.NotNull(generatedCode);
        Assert.Contains("Middleware: new global::HPD.Agent.ToolHarnessMiddlewareDescriptor[]", generatedCode);
        Assert.Contains("MiddlewareType = typeof(global::Ns.ConfigCtorMiddleware)", generatedCode);
        Assert.Contains("ToolHarnessMiddlewareActivation.ExecutionOwned", generatedCode);
        Assert.Contains("ConfigCtorMiddleware(", generatedCode);
        AssertDoesNotContainGenericRawTextDeserialize(generatedCode, "Ns.MyConfig", "MyConfig");
        Assert.Contains(
            "context.GetConfiguration(GetJsonTypeInfo<Ns.MyConfig>(Ns.ConfigCtorJsonContext.Default))",
            StripGlobalQualifiers(generatedCode));
    }

    [Fact]
    public void SourceGen_ToolHarnessConfigFactory_UsesJsonTypeInfoLookup()
    {
        var source = @"
using HPD.Agent;
using System;

namespace Ns
{
    public class ToolHarnessConfig
    {
        public string? Model { get; set; }
    }

    public partial class ConfiguredToolHarness
    {
        public ConfiguredToolHarness(ToolHarnessConfig config) { }

        [AIFunction]
        public string Ping() => ""pong"";
    }
}
";
        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.NotNull(generatedCode);
        AssertDoesNotContainGenericRawTextDeserialize(generatedCode, "Ns.ToolHarnessConfig", "ToolHarnessConfig");
        Assert.Contains("JsonSerializer.Deserialize(json, GetJsonTypeInfo<Ns.ToolHarnessConfig>())", StripGlobalQualifiers(generatedCode));
        Assert.Contains("No JSON metadata is registered for tool harness config type", generatedCode);
        Assert.Contains("typeof(T).FullName", generatedCode);
    }

    [Fact]
    public void SourceGen_ToolHarnessMetadataFactory_UsesJsonTypeInfoLookup()
    {
        var source = @"
using HPD.Agent;
using System;
using System.Collections.Generic;

namespace Ns
{
    public class SearchMetadata : IToolMetadata
    {
        public string? Tenant { get; set; }

        public T? GetProperty<T>(string propertyName, T? defaultValue = default) => defaultValue;

        public bool HasProperty(string propertyName) => propertyName == nameof(Tenant);

        public IEnumerable<string> GetPropertyNames() => new[] { nameof(Tenant) };
    }

    public partial class MetadataToolHarness
    {
        [AIFunction<SearchMetadata>]
        public string Search(string query) => query;
    }
}
";
        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.NotNull(generatedCode);
        AssertDoesNotContainGenericRawTextDeserialize(generatedCode, "Ns.SearchMetadata", "SearchMetadata");
        Assert.Contains("JsonSerializer.Deserialize(json, GetJsonTypeInfo<Ns.SearchMetadata>())", StripGlobalQualifiers(generatedCode));
        Assert.Contains("No JSON metadata is registered for tool metadata type", generatedCode);
        Assert.Contains("typeof(T).FullName", generatedCode);
    }

    [Fact]
    public void SourceGen_MiddlewareConfigFactory_UsesJsonTypeInfoLookup()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.Middleware;
using System;

namespace Ns
{
    public class StandaloneConfig
    {
        public int Limit { get; set; }
    }

    [Middleware]
    public class ConfiguredStandaloneMiddleware : IAgentMiddleware
    {
        public ConfiguredStandaloneMiddleware(StandaloneConfig config) { }
    }
}
";
        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.NotNull(generatedCode);
        AssertDoesNotContainGenericRawTextDeserialize(generatedCode, "Ns.StandaloneConfig", "StandaloneConfig");
        Assert.Contains("JsonSerializer.Deserialize(json, GetJsonTypeInfo<Ns.StandaloneConfig>())", StripGlobalQualifiers(generatedCode));
        Assert.Contains("No JSON metadata is registered for middleware config type", generatedCode);
        Assert.Contains("typeof(T).FullName", generatedCode);
    }

    // ── T048 ─────────────────────────────────────────────────────────────────
    // Parameterless constructors become generated execution-owned descriptors.
    [Fact]
    public void SourceGen_EmitsExecutionOwnedDescriptor_ForParameterlessCtorMiddleware()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.Middleware;
using System;

namespace Ns
{
    public class ParamlessMiddleware : IToolHarnessMiddleware
    {
        public ParamlessMiddleware() { }
    }

    [Collapse(""Paramless toolharness"", FunctionResult = ""ok"",
        Middlewares = [typeof(ParamlessMiddleware)])]
    public partial class ParamlessToolHarness
    {
        [AIFunction]
        public string Ping() => ""pong"";
    }
}
";
        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.NotNull(generatedCode);
        Assert.Contains("Middleware: new global::HPD.Agent.ToolHarnessMiddlewareDescriptor[]", generatedCode);
        Assert.Contains("ToolHarnessMiddlewareActivation.ExecutionOwned", generatedCode);
        Assert.Contains("ParamlessMiddleware()", generatedCode);
    }

    // ── T049 ─────────────────────────────────────────────────────────────────
    // HPDAG0204: middleware with only a multi-parameter constructor (neither
    // parameterless nor single-config-param) must produce an error diagnostic.
    [Fact]
    public void SourceGen_Emits_HPDAG0204_WhenMiddlewareHasNeitherParamlessNorConfigCtor()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.Middleware;
using System;

namespace Ns
{
    public class MultiParamMiddleware : IToolHarnessMiddleware
    {
        public MultiParamMiddleware(string a, int b) { }
    }

    [Collapse(""MultiParam toolharness"", FunctionResult = ""ok"",
        Middlewares = [typeof(MultiParamMiddleware)])]
    public partial class MultiParamToolHarness
    {
        [AIFunction]
        public string Ping() => ""pong"";
    }
}
";
        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "HPDAG0204" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SourceGen_Emits_HPDAG0209_WhenMiddlewareConfigHasNoGeneratedJsonContext()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.Middleware;

namespace Ns
{
    public sealed class SampleOptions { }
    public sealed class ConfiguredMiddleware : IToolHarnessMiddleware
    {
        public ConfiguredMiddleware(SampleOptions config) { }
    }

    [Collapse(""Configured toolharness"", FunctionResult = ""ok"",
        Middlewares = [typeof(ConfiguredMiddleware)])]
    public partial class ConfiguredToolHarness
    {
        [AIFunction]
        public string Ping() => ""pong"";
    }
}
";
        var (_, diagnostics) = RunGenerator(source);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "HPDAG0209" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SourceGen_EmitsBalancedAotConfigFactory_WhenContextContainsMetadata()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.Middleware;
using System.Text.Json.Serialization;

namespace Ns
{
    [ToolHarnessJsonContext(typeof(SampleJsonContext))]
    public sealed class SampleOptions { }
    [JsonSerializable(typeof(SampleOptions))]
    public partial class SampleJsonContext : JsonSerializerContext { }
    public sealed class ConfiguredMiddleware : IToolHarnessMiddleware
    {
        public ConfiguredMiddleware(SampleOptions config) { }
    }

    [Collapse(""Configured toolharness"", FunctionResult = ""ok"",
        Middlewares = [typeof(ConfiguredMiddleware)])]
    public partial class ConfiguredToolHarness
    {
        [AIFunction]
        public string Ping() => ""pong"";
    }
}
";
        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(
            "ToolHarnessMiddlewareActivation.ExecutionOwned(new global::Ns.ConfiguredMiddleware(context.GetConfiguration(GetJsonTypeInfo<global::Ns.SampleOptions>(global::Ns.SampleJsonContext.Default))))",
            generatedCode);
    }

    [Fact]
    public void SourceGen_OptionalConfigConstructor_EmitsAotSafeGeneratedDefault()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.Middleware;
using System.Text.Json.Serialization;

namespace Ns
{
    [ToolHarnessJsonContext(typeof(OptionalJsonContext))]
    public sealed class OptionalConfig { }
    [JsonSerializable(typeof(OptionalConfig))]
    public partial class OptionalJsonContext : JsonSerializerContext { }
    public sealed class OptionalMiddleware : IToolHarnessMiddleware
    {
        public OptionalMiddleware(OptionalConfig? config = null) { }
    }
    [Collapse(""Optional"", Middlewares = [typeof(OptionalMiddleware)])]
    public partial class OptionalHarness
    {
        [AIFunction]
        public string Ping() => ""pong"";
    }
}
";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Contains(
            "context.GetConfigurationOrDefault(GetJsonTypeInfo<Ns.OptionalConfig>(Ns.OptionalJsonContext.Default), static () => new Ns.OptionalConfig())",
            StripGlobalQualifiers(generatedCode));
    }

    [Fact]
    public void SourceGen_Emits_HPDAG0211_ForAmbiguousExecutionActivationShapes()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.Middleware;
namespace Ns
{
    public sealed class SampleOptions { }
    public sealed class AmbiguousMiddleware : IToolHarnessMiddleware
    {
        public AmbiguousMiddleware() { }
        public AmbiguousMiddleware(SampleOptions options) { }
    }
    [Collapse(""Ambiguous"", Middlewares = [typeof(AmbiguousMiddleware)])]
    public partial class Harness { [AIFunction] public string Ping() => ""pong""; }
}
";
        var (_, diagnostics) = RunGenerator(source);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "HPDAG0211");
    }

    [Fact]
    public void SourceGen_ServicesOwnershipSuppressesConstructorActivationAndUsesExactChildScope()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.Middleware;
namespace Ns
{
    [ToolHarnessMiddlewareLifetime(ToolHarnessMiddlewareOwnership.Services)]
    public sealed class ServicesMiddleware : IToolHarnessMiddleware
    {
        public ServicesMiddleware() { }
    }
    [Collapse(""Services"", Middlewares = [typeof(ServicesMiddleware)])]
    public partial class Harness { [AIFunction] public string Ping() => ""pong""; }
}
";
        var (generatedCode, diagnostics) = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(
            "ToolHarnessMiddlewareActivation.ServicesOwned(context.GetRequiredService<global::Ns.ServicesMiddleware>())",
            generatedCode);
        Assert.DoesNotContain("new global::Ns.ServicesMiddleware()", generatedCode);
    }

    [Fact]
    public void SourceGen_PreservesMiddlewareDeclarationOrder()
    {
        var source = """
            using HPD.Agent;
            using HPD.Agent.Middleware;
            namespace Ns
            {
                public sealed class First : IToolHarnessMiddleware { }
                public sealed class Second : IToolHarnessMiddleware { }
                public sealed class Third : IToolHarnessMiddleware { }
                [Collapse("Ordered", Middlewares = [typeof(Second), typeof(First), typeof(Third)])]
                public partial class Harness { [AIFunction] public string Ping() => "pong"; }
            }
            """;

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var second = generatedCode!.IndexOf("MiddlewareType = typeof(global::Ns.Second)", StringComparison.Ordinal);
        var first = generatedCode.IndexOf("MiddlewareType = typeof(global::Ns.First)", StringComparison.Ordinal);
        var third = generatedCode.IndexOf("MiddlewareType = typeof(global::Ns.Third)", StringComparison.Ordinal);
        Assert.True(second >= 0 && second < first && first < third);
    }

    [Fact]
    public void SourceGen_DoesNotInferServicesOwnershipFromDisposalOrConstructorShape()
    {
        var source = """
            using System;
            using System.Threading.Tasks;
            using HPD.Agent;
            using HPD.Agent.Middleware;
            namespace Ns
            {
                public sealed class DisposableMiddleware : IToolHarnessMiddleware, IDisposable, IAsyncDisposable
                {
                    public void Dispose() { }
                    public ValueTask DisposeAsync() => default;
                }
                [Collapse("Owned", Middlewares = [typeof(DisposableMiddleware)])]
                public partial class Harness { [AIFunction] public string Ping() => "pong"; }
            }
            """;

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Contains("ToolHarnessMiddlewareActivation.ExecutionOwned(new global::Ns.DisposableMiddleware())", generatedCode);
        Assert.DoesNotContain("ToolHarnessMiddlewareActivation.ServicesOwned", generatedCode);
    }

    [Fact]
    public void SourceGen_DoesNotInferServicesOwnershipFromDiRegistrationPresence()
    {
        var source = """
            using HPD.Agent;
            using HPD.Agent.Middleware;
            using Microsoft.Extensions.DependencyInjection;
            namespace Ns
            {
                public sealed class Candidate : IToolHarnessMiddleware { }
                public static class Registration
                {
                    public static void Add(IServiceCollection services) => services.AddScoped<Candidate>();
                }
                [Collapse("DI", Middlewares = [typeof(Candidate)])]
                public partial class Harness { [AIFunction] public string Ping() => "pong"; }
            }
            """;

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Contains(
            "ToolHarnessMiddlewareActivation.ExecutionOwned(new global::Ns.Candidate())",
            generatedCode);
        Assert.DoesNotContain("ToolHarnessMiddlewareActivation.ServicesOwned", generatedCode);
    }

    [Theory]
    [InlineData("public sealed class Candidate { }", "Outer.Candidate", "HPDAG0203")]
    [InlineData("public abstract class Candidate : IToolHarnessMiddleware { }", "Outer.Candidate", "HPDAG0206")]
    [InlineData("public sealed class Candidate<T> : IToolHarnessMiddleware { }", "Outer.Candidate<int>", "HPDAG0206")]
    [InlineData("private sealed class Candidate : IToolHarnessMiddleware { }", "Outer.Candidate", "HPDAG0206")]
    public void SourceGen_RejectsInvalidMiddlewareContracts(
        string declaration,
        string typeExpression,
        string diagnosticId)
    {
        var source = $$"""
            using HPD.Agent;
            using HPD.Agent.Middleware;
            namespace Ns
            {
                public static class Outer
                {
                    {{declaration}}
                }
                [Collapse("Invalid", Middlewares = [typeof({{typeExpression}})])]
                public partial class Harness { [AIFunction] public string Ping() => "pong"; }
            }
            """;

        var (_, diagnostics) = RunGenerator(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == diagnosticId && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SourceGen_Emits_HPDAG0205_ForDuplicateMiddleware()
    {
        var source = """
            using HPD.Agent;
            using HPD.Agent.Middleware;
            namespace Ns
            {
                public sealed class Candidate : IToolHarnessMiddleware { }
                [Collapse("Duplicate", Middlewares = [typeof(Candidate), typeof(Candidate)])]
                public partial class Harness { [AIFunction] public string Ping() => "pong"; }
            }
            """;

        var (_, diagnostics) = RunGenerator(source);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "HPDAG0205");
    }

    [Fact]
    public void SourceGen_Emits_HPDAG0207_ForInvalidOwnershipMetadata()
    {
        var source = """
            using HPD.Agent;
            using HPD.Agent.Middleware;
            namespace Ns
            {
                [ToolHarnessMiddlewareLifetime((ToolHarnessMiddlewareOwnership)99)]
                public sealed class Candidate : IToolHarnessMiddleware { }
                [Collapse("Lifetime", Middlewares = [typeof(Candidate)])]
                public partial class Harness { [AIFunction] public string Ping() => "pong"; }
            }
            """;

        var (_, diagnostics) = RunGenerator(source);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "HPDAG0207");
    }

    [Fact]
    public void SourceGen_ConflictingOwnershipAttributes_FailCompilation()
    {
        var source = """
            using HPD.Agent;
            using HPD.Agent.Middleware;
            namespace Ns
            {
                [ToolHarnessMiddlewareLifetime(ToolHarnessMiddlewareOwnership.Execution)]
                [ToolHarnessMiddlewareLifetime(ToolHarnessMiddlewareOwnership.Services)]
                public sealed class Candidate : IToolHarnessMiddleware { }
                [Collapse("Conflicting lifetime", Middlewares = [typeof(Candidate)])]
                public partial class Harness { [AIFunction] public string Ping() => "pong"; }
            }
            """;

        var (_, diagnostics) = RunGenerator(source, includeCompilationDiagnostics: true);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "CS0579");
    }

    [Fact]
    public void SourceGen_Emits_HPDAG0208_ForAbstractAgentResourceImplementation()
    {
        var source = """
            using System.Text.Json.Serialization;
            using HPD.Agent;
            using HPD.Agent.Middleware;
            namespace Ns
            {
                public abstract class ResourceImplementation { }
                [ToolHarnessAgentResource(typeof(ResourceImplementation))]
                public interface IResource { }
                [ToolHarnessJsonContext(typeof(OptionsJsonContext))]
                public sealed class CandidateOptions { }
                [JsonSerializable(typeof(CandidateOptions))]
                public partial class OptionsJsonContext : JsonSerializerContext { }
                public sealed class Candidate : IToolHarnessMiddleware
                {
                    public Candidate(IResource resource, string workspace, CandidateOptions options) { }
                }
                [Collapse("Resource", Middlewares = [typeof(Candidate)])]
                public partial class Harness { [AIFunction] public string Ping() => "pong"; }
            }
            """;

        var (_, diagnostics) = RunGenerator(source);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "HPDAG0208");
    }

    private static string StripGlobalQualifiers(string? generatedCode)
    {
        return generatedCode?.Replace("global::", "") ?? "";
    }

    private static void AssertDoesNotContainGenericRawTextDeserialize(string? generatedCode, params string[] typeNames)
    {
        var normalizedCode = StripGlobalQualifiers(generatedCode);

        foreach (var typeName in typeNames)
        {
            Assert.DoesNotContain($"JsonSerializer.Deserialize<{typeName}>(json.GetRawText())", normalizedCode);
        }
    }
}
