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
    private static (string? generatedCode, ImmutableArray<Diagnostic> diagnostics) RunGenerator(string source)
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
        Assert.Contains("HPDToolArgumentBinder.BindOptional<SearchMode>", generatedCode);
        Assert.Contains("HPDToolArgumentBinder.ValidateNoUnmappedProperties(jsonArgs, serializerOptions, \"pattern\", \"mode\")", generatedCode);
        Assert.Contains("ParseSearchArgs(jsonArgs, arguments.GetJsonSerializerOptions())", generatedCode);
        Assert.DoesNotContain("global::System.Enum.TryParse<SearchMode>", generatedCode);
    }

    // ── T047 ─────────────────────────────────────────────────────────────────
    // §5A: middleware with a single-config-parameter constructor → emitted into
    // CollapseMiddlewareConfigFactories with the correct MiddlewareTypeName and
    // a Factory lambda that deserialises the JsonElement.
    [Fact]
    public void SourceGen_EmitsCollapseMiddlewareConfigFactories_ForConfigCtorMiddleware()
    {
        var source = @"
using HPD.Agent;
using HPD.Agent.Middleware;
using System;

namespace Ns
{
    public class MyConfig { }

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
        Assert.Contains("CollapseMiddlewareConfigFactories:", generatedCode);
        Assert.Contains(@"MiddlewareTypeName: ""ConfigCtorMiddleware""", generatedCode);
        Assert.Contains("Factory: static json => new", generatedCode);
        Assert.Contains("ConfigCtorMiddleware(", generatedCode);
        AssertDoesNotContainGenericRawTextDeserialize(generatedCode, "Ns.MyConfig", "MyConfig");
        Assert.Contains("JsonSerializer.Deserialize(json, GetJsonTypeInfo<Ns.MyConfig>())", StripGlobalQualifiers(generatedCode));
        Assert.Contains("No JSON metadata is registered for collapse middleware config type", generatedCode);
        // Parameterless bucket must be null (no parameterless-ctor middlewares)
        Assert.Contains("CollapseMiddlewareFactories: null,", generatedCode);
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
    // §factory: middleware with a parameterless constructor → emitted into
    // CollapseMiddlewareFactories as a static lambda; config bucket stays null.
    [Fact]
    public void SourceGen_EmitsCollapseMiddlewareFactories_ForParameterlessCtorMiddleware()
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
        Assert.Contains("CollapseMiddlewareFactories: new global::System.Func<global::HPD.Agent.Middleware.IAgentMiddleware>[]", generatedCode);
        Assert.Contains("static () => new", generatedCode);
        Assert.Contains("ParamlessMiddleware()", generatedCode);
        // Config bucket must be null (no config-ctor middlewares)
        Assert.Contains("CollapseMiddlewareConfigFactories: null", generatedCode);
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
    public class MultiParamMiddleware : IAgentMiddleware
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
