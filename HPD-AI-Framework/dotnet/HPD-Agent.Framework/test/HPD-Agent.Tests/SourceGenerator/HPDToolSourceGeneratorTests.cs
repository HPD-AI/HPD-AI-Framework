using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Microsoft.Extensions.AI; // For AIFunction
using HPD.Agent; // For ToolkitAttribute


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
    public void GeneratedToolkit_WithDynamicCollapseInstructions_ContainsCorrectCode()
    {
        // Arrange - Using an expression (method call) as attribute value
        // The source generator detects this as an expression rather than a literal string
        var ToolkitSource = @$"
using HPD.Agent;
using System;

namespace TestToolkits
{{
    public static class DynamicInstructionsProvider
    {{
        public static string GetInstructions()
        {{
            return ""Dynamic instructions for the collapsed Toolkit."";
        }}
    }}

    [Collapse(""Test collapsed Toolkit"",   FunctionResult = DynamicInstructionsProvider.GetInstructions())]
    public partial class CollapsedTestToolkit
    {{
        [AIFunction]
        public string HelloWorld() => ""Hello!"";
    }}
}}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(ToolkitSource);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.NotNull(generatedCode);
        Assert.Contains("var dynamicInstructions = DynamicInstructionsProvider.GetInstructions();", generatedCode);
        Assert.Contains("dynamicInstructions", generatedCode);
    }
    
    [Fact]
    public void GeneratedToolkit_WithStaticCollapseInstructions_ContainsCorrectCode()
    {
        // Arrange
        var ToolkitSource = @$"
using HPD.Agent;
using System;

namespace TestToolkits
{{
    [Collapse(""Test static collapsed Toolkit"",   FunctionResult = ""Static instructions here."")]
    public partial class StaticCollapsedTestToolkit
    {{
        [AIFunction]
        public string HelloStatic() => ""Hello Static!"";
    }}
}}
";
        // Act
        var (generatedCode, diagnostics) = RunGenerator(ToolkitSource);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.NotNull(generatedCode);
        Assert.Contains("Static instructions here.", generatedCode);
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

    public class ConfigCtorMiddleware : IToolkitMiddleware
    {
        public ConfigCtorMiddleware(MyConfig config) { }
    }

    [Collapse(""ConfigCtor toolkit"", FunctionResult = ""ok"",
        Middlewares = [typeof(ConfigCtorMiddleware)])]
    public partial class ConfigCtorToolkit
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
        Assert.Contains("JsonSerializer.Deserialize<", generatedCode);
        // Parameterless bucket must be null (no parameterless-ctor middlewares)
        Assert.Contains("CollapseMiddlewareFactories: null,", generatedCode);
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
    public class ParamlessMiddleware : IToolkitMiddleware
    {
        public ParamlessMiddleware() { }
    }

    [Collapse(""Paramless toolkit"", FunctionResult = ""ok"",
        Middlewares = [typeof(ParamlessMiddleware)])]
    public partial class ParamlessToolkit
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

    [Collapse(""MultiParam toolkit"", FunctionResult = ""ok"",
        Middlewares = [typeof(MultiParamMiddleware)])]
    public partial class MultiParamToolkit
    {
        [AIFunction]
        public string Ping() => ""pong"";
    }
}
";
        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "HPDAG0204" && d.Severity == DiagnosticSeverity.Error);
    }
}
