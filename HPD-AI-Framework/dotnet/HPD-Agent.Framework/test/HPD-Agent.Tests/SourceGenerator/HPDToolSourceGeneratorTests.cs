using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Microsoft.Extensions.AI; // For AIFunction
using HPD.Agent; // For HarnessAttribute


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
    public void GeneratedHarness_WithDynamicCollapseInstructions_ContainsCorrectCode()
    {
        // Arrange - Using an expression (method call) as attribute value
        // The source generator detects this as an expression rather than a literal string
        var HARNESSource = @$"
using HPD.Agent;
using System;

namespace TestHarneses
{{
    public static class DynamicInstructionsProvider
    {{
        public static string GetInstructions()
        {{
            return ""Dynamic instructions for the collapsed Harness."";
        }}
    }}

    [Collapse(""Test collapsed Harness"",   FunctionResult = DynamicInstructionsProvider.GetInstructions())]
    public partial class CollapsedTestHarness
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
    public void GeneratedHarness_WithStaticCollapseInstructions_ContainsCorrectCode()
    {
        // Arrange
        var HARNESSource = @$"
using HPD.Agent;
using System;

namespace TestHarneses
{{
    [Collapse(""Test static collapsed Harness"",   FunctionResult = ""Static instructions here."")]
    public partial class StaticCollapsedTestHarness
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
    public void GeneratedHarness_WithEnumParameter_ParsesStringEnumArguments()
    {
        var source = @"
using HPD.Agent;
using System;

namespace TestHarneses
{
    public enum SearchMode
    {
        Files,
        Content
    }

    [Collapse(""Enum harness"", FunctionResult = ""ok"")]
    public partial class EnumHarness
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

    [Fact]
    public void GeneratedFunction_WithSandboxableAttribute_EmitsSandboxMetadata()
    {
        var source = @"
using HPD.Agent;
using System;

namespace TestHarneses
{
    public enum SandboxNetworkPolicy
    {
        Inherit,
        Blocked,
        Filtered,
        Unrestricted
    }

    public enum SandboxToggle
    {
        Inherit,
        Disabled,
        Enabled
    }

    public sealed class SandboxableAttribute : Attribute
    {
        public SandboxNetworkPolicy NetworkMode { get; set; } = SandboxNetworkPolicy.Inherit;
        public string AllowedDomains { get; set; } = string.Empty;
        public string DeniedDomains { get; set; } = string.Empty;
        public string AllowWrite { get; set; } = string.Empty;
        public string DenyRead { get; set; } = string.Empty;
        public string AllowRead { get; set; } = string.Empty;
        public string DenyWrite { get; set; } = string.Empty;
        public string AllowUnixSockets { get; set; } = string.Empty;
        public string AllowMachLookup { get; set; } = string.Empty;
        public SandboxToggle AllowPty { get; set; } = SandboxToggle.Inherit;
        public SandboxToggle AllowLocalBinding { get; set; } = SandboxToggle.Inherit;
        public SandboxToggle AllowAllUnixSockets { get; set; } = SandboxToggle.Inherit;
        public SandboxToggle AllowMacOSTrustdLookup { get; set; } = SandboxToggle.Inherit;
        public SandboxToggle AllowGitConfig { get; set; } = SandboxToggle.Inherit;
        public SandboxToggle EnableWeakerNestedSandbox { get; set; } = SandboxToggle.Inherit;
        public string IgnoreViolationPatterns { get; set; } = string.Empty;
        public string AllowedEnvironmentVariables { get; set; } = string.Empty;
        public int MandatoryDenySearchDepth { get; set; } = -1;
    }

    public partial class SandboxHarness
    {
        [AIFunction]
        [Sandboxable(
            NetworkMode = SandboxNetworkPolicy.Filtered,
            AllowedDomains = ""api.github.com,registry.npmjs.org"",
            DeniedDomains = ""evil.github.com"",
            AllowWrite = ""./workspace,/tmp"",
            DenyRead = ""~/.ssh,~/.aws"",
            AllowRead = ""./workspace/public"",
            DenyWrite = "".git/hooks,.npmrc"",
            AllowUnixSockets = ""/var/run/docker.sock"",
            AllowMachLookup = ""com.example.*"",
            AllowPty = SandboxToggle.Enabled,
            AllowLocalBinding = SandboxToggle.Enabled,
            AllowAllUnixSockets = SandboxToggle.Enabled,
            AllowMacOSTrustdLookup = SandboxToggle.Enabled,
            AllowGitConfig = SandboxToggle.Enabled,
            EnableWeakerNestedSandbox = SandboxToggle.Disabled,
            IgnoreViolationPatterns = ""cache,expected"",
            AllowedEnvironmentVariables = ""PATH,HOME"",
            MandatoryDenySearchDepth = 5)]
        public string RunCommand(string command) => command;
    }
}
";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.NotNull(generatedCode);
        Assert.Contains("[\"IsSandboxable\"] = true", generatedCode);
        Assert.Contains("[\"SandboxNetworkMode\"] = \"Filtered\"", generatedCode);
        Assert.Contains("[\"SandboxAllowedDomains\"] = new string[] { \"api.github.com\", \"registry.npmjs.org\" }", generatedCode);
        Assert.Contains("[\"SandboxDeniedDomains\"] = new string[] { \"evil.github.com\" }", generatedCode);
        Assert.Contains("[\"SandboxAllowWrite\"] = new string[] { \"./workspace\", \"/tmp\" }", generatedCode);
        Assert.Contains("[\"SandboxDenyRead\"] = new string[] { \"~/.ssh\", \"~/.aws\" }", generatedCode);
        Assert.Contains("[\"SandboxAllowRead\"] = new string[] { \"./workspace/public\" }", generatedCode);
        Assert.Contains("[\"SandboxDenyWrite\"] = new string[] { \".git/hooks\", \".npmrc\" }", generatedCode);
        Assert.Contains("[\"SandboxAllowUnixSockets\"] = new string[] { \"/var/run/docker.sock\" }", generatedCode);
        Assert.Contains("[\"SandboxAllowMachLookup\"] = new string[] { \"com.example.*\" }", generatedCode);
        Assert.Contains("[\"SandboxAllowPty\"] = true", generatedCode);
        Assert.Contains("[\"SandboxAllowLocalBinding\"] = true", generatedCode);
        Assert.Contains("[\"SandboxAllowAllUnixSockets\"] = true", generatedCode);
        Assert.Contains("[\"SandboxAllowMacOSTrustdLookup\"] = true", generatedCode);
        Assert.Contains("[\"SandboxAllowGitConfig\"] = true", generatedCode);
        Assert.Contains("[\"SandboxEnableWeakerNestedSandbox\"] = false", generatedCode);
        Assert.Contains("[\"SandboxIgnoreViolationPatterns\"] = new string[] { \"cache\", \"expected\" }", generatedCode);
        Assert.Contains("[\"SandboxAllowedEnvironmentVariables\"] = new string[] { \"PATH\", \"HOME\" }", generatedCode);
        Assert.Contains("[\"SandboxMandatoryDenySearchDepth\"] = 5", generatedCode);
    }

    [Fact]
    public void GeneratedFunction_WithBareSandboxableAttribute_EmitsOnlyMarker()
    {
        var source = @"
using HPD.Agent;
using System;

namespace TestHarneses
{
    public sealed class SandboxableAttribute : Attribute
    {
    }

    public partial class SandboxHarness
    {
        [AIFunction]
        [Sandboxable]
        public string RunCommand(string command) => command;
    }
}
";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.NotNull(generatedCode);
        Assert.Contains("[\"IsSandboxable\"] = true", generatedCode);
        Assert.DoesNotContain("SandboxNetworkMode", generatedCode);
        Assert.DoesNotContain("SandboxAllowWrite", generatedCode);
        Assert.DoesNotContain("SandboxDenyRead", generatedCode);
        Assert.DoesNotContain("SandboxAllowPty", generatedCode);
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

    public class ConfigCtorMiddleware : IHarnessMiddleware
    {
        public ConfigCtorMiddleware(MyConfig config) { }
    }

    [Collapse(""ConfigCtor harness"", FunctionResult = ""ok"",
        Middlewares = [typeof(ConfigCtorMiddleware)])]
    public partial class ConfigCtorHarness
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
    public class ParamlessMiddleware : IHarnessMiddleware
    {
        public ParamlessMiddleware() { }
    }

    [Collapse(""Paramless harness"", FunctionResult = ""ok"",
        Middlewares = [typeof(ParamlessMiddleware)])]
    public partial class ParamlessHarness
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

    [Collapse(""MultiParam harness"", FunctionResult = ""ok"",
        Middlewares = [typeof(MultiParamMiddleware)])]
    public partial class MultiParamHarness
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
