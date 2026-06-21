using System.Collections.Immutable;
using System.Linq;
using HPD.Agent.Middleware;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace HPD.Agent.Tests.SourceGenerator;

public sealed class FunctionRuntimeContextSourceGeneratorTests
{
    private static (string GeneratedCode, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "FunctionRuntimeContextGeneratorTests",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new global::HPDToolSourceGenerator();
        CSharpGeneratorDriver.Create(
                generators: new ISourceGenerator[] { generator.AsSourceGenerator() },
                additionalTexts: Enumerable.Empty<AdditionalText>(),
                parseOptions: parseOptions,
                optionsProvider: null)
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var generatedCode = string.Join(
            "\n\n",
            outputCompilation.SyntaxTrees
                .Where(st => st.FilePath.Contains("g.cs"))
                .Select(st => st.GetText().ToString()));

        return (generatedCode, diagnostics);
    }

    private static string FindRepoRoot()
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (System.IO.Directory.Exists(System.IO.Path.Combine(directory.FullName, "dotnet", "HPD-Agent.Framework")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new System.IO.DirectoryNotFoundException("Could not find repository root from test base directory.");
    }

    [Fact]
    public void RuntimeParamClassification_FullyQualifiedCancellationToken()
    {
        var source = """
using Microsoft.Extensions.AI;

public partial class RuntimeToolHarness
{
    [AIFunction]
    public string Search(string query, System.Threading.CancellationToken ct) => query;
}
""";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("public string query", generatedCode);
        Assert.DoesNotContain("public System.Threading.CancellationToken ct", generatedCode);
        Assert.Contains("Search(args.query, cancellationToken)", generatedCode);
    }

    [Fact]
    public void RuntimeParamClassification_GlobalQualifiedContext()
    {
        var source = """
using Microsoft.Extensions.AI;

public partial class RuntimeToolHarness
{
    [AIFunction]
    public string Search(string query, global::HPD.Agent.Middleware.FunctionExecutionContext context) => query;
}
""";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("public string query", generatedCode);
        Assert.DoesNotContain("public global::HPD.Agent.Middleware.FunctionExecutionContext context", generatedCode);
        Assert.Contains("Search(args.query, functionContext)", generatedCode);
    }

    [Fact]
    public void RuntimeParamClassification_AliasedContext()
    {
        var source = """
using Microsoft.Extensions.AI;
using RuntimeContext = HPD.Agent.Middleware.FunctionExecutionContext;

public partial class RuntimeToolHarness
{
    [AIFunction]
    public string Search(string query, RuntimeContext context) => query;
}
""";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("public string query", generatedCode);
        Assert.DoesNotContain("public RuntimeContext context", generatedCode);
        Assert.Contains("Search(args.query, functionContext)", generatedCode);
    }

    [Fact]
    public void GeneratedSchema_UsesDtoNotOriginalMethod()
    {
        var source = """
using Microsoft.Extensions.AI;
using HPD.Agent.Middleware;

public partial class RuntimeToolHarness
{
    [AIFunction]
    public string Search(string query, FunctionExecutionContext context, System.Threading.CancellationToken ct) => query;
}
""";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("SchemaProvider = () =>", generatedCode);
        Assert.Contains("JsonDocument.Parse", generatedCode);
        Assert.Contains("public class SearchArgs", generatedCode);
        Assert.Contains("ParseSearchArgs", generatedCode);
        Assert.DoesNotContain("CreateFunctionJsonSchema", generatedCode);
    }

    [Theory]
    [InlineData("FunctionExecutionContext context", "context")]
    [InlineData("System.Threading.CancellationToken ct", "ct")]
    [InlineData("global::Microsoft.Extensions.AI.AIFunctionArguments args", "args")]
    [InlineData("System.IServiceProvider services", "services")]
    public void GeneratedSchema_ExcludesRuntimeParameters(string runtimeParameter, string parameterName)
    {
        var source = $$"""
using Microsoft.Extensions.AI;
using HPD.Agent.Middleware;

public partial class RuntimeToolHarness
{
    [AIFunction]
    public string Search(string query, {{runtimeParameter}}) => query;
}
""";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("public string query", generatedCode);
        Assert.DoesNotContain($"public {runtimeParameter}", generatedCode);
        Assert.DoesNotContain($"JsonPropertyName(\"{parameterName}\")", generatedCode);
        Assert.Contains("public class SearchArgs", generatedCode);
        Assert.Contains("ParseSearchArgs", generatedCode);
    }

    [Fact]
    public void NoModelFacingParameters_GeneratesEmptySchema()
    {
        var source = """
using Microsoft.Extensions.AI;
using HPD.Agent.Middleware;

public partial class RuntimeToolHarness
{
    [AIFunction]
    public string Ping(FunctionExecutionContext context, System.Threading.CancellationToken ct) => "pong";
}
""";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("Ping(functionContext, cancellationToken)", generatedCode);
        Assert.DoesNotContain("class PingArgs", generatedCode);
        Assert.Contains("SchemaProvider = () =>", generatedCode);
        Assert.Contains("JsonDocument.Parse", generatedCode);
    }

    [Fact]
    public void GeneratedDto_ExcludesRuntimeParameters()
    {
        var source = """
using Microsoft.Extensions.AI;
using HPD.Agent.Middleware;

public partial class RuntimeToolHarness
{
    [AIFunction]
    public string Search(string query, FunctionExecutionContext context, System.Threading.CancellationToken ct) => query;
}
""";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("public class SearchArgs", generatedCode);
        Assert.Contains("public string query", generatedCode);
        Assert.DoesNotContain("public FunctionExecutionContext context", generatedCode);
        Assert.DoesNotContain("public System.Threading.CancellationToken ct", generatedCode);
    }

    [Fact]
    public void GeneratedParser_ParsesOnlyModelFacingParameters()
    {
        var source = """
using Microsoft.Extensions.AI;
using HPD.Agent.Middleware;

public partial class RuntimeToolHarness
{
    [AIFunction]
    public string Search(string query, FunctionExecutionContext context) => query;
}
""";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("ParseSearchArgs", generatedCode);
        Assert.Contains("result.query", generatedCode);
        Assert.DoesNotContain("dto.context", generatedCode);
    }

    [Fact]
    public void GeneratedDto_PreservesModelFacingParameterDescription()
    {
        var source = """
using System.ComponentModel;
using Microsoft.Extensions.AI;
using HPD.Agent.Middleware;

public partial class RuntimeToolHarness
{
    [AIFunction]
    public string Search([Description("The query to search for.")] string query, FunctionExecutionContext context) => query;
}
""";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("[System.ComponentModel.Description(\"The query to search for.\")]", generatedCode);
        Assert.DoesNotContain("Description(\"context", generatedCode);
    }

    [Fact]
    public void ConditionalParameters_IgnoreRuntimeParameters()
    {
        var source = """
using Microsoft.Extensions.AI;
using HPD.Agent.Middleware;
using HPD.Agent;

public partial class RuntimeToolHarness
{
    [AIFunction]
    public string Search(
        string query,
        FunctionExecutionContext context,
        [ConditionalParameter("query == 'advanced'")] string? advanced = null) => query;
}
""";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("advanced", generatedCode);
        Assert.DoesNotContain("context", generatedCode.Substring(generatedCode.IndexOf("ParameterDescriptions", StringComparison.Ordinal)));
    }

    [Fact]
    public void GeneratedInvocation_PreservesParameterOrder()
    {
        var source = """
using Microsoft.Extensions.AI;
using HPD.Agent.Middleware;

public partial class RuntimeToolHarness
{
    [AIFunction]
    public string Run(string a, FunctionExecutionContext context, int b, System.Threading.CancellationToken ct) => a + b;
}
""";

        var (generatedCode, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("Run(args.a, functionContext, args.b, cancellationToken)", generatedCode);
    }

    [Fact]
    public void SubAgentWrapperGenerator_UsesExplicitFunctionExecutionContext()
    {
        var source = System.IO.File.ReadAllText(System.IO.Path.Combine(
            FindRepoRoot(),
            "dotnet/HPD-Agent.Framework/src/HPD-Agent.SourceGenerator/Capabilities/SubAgentCapability.cs"));

        Assert.Contains("async (arguments, functionContext, cancellationToken)", source);
        Assert.DoesNotContain("CurrentFunctionContext", source);
    }

    [Fact]
    public void MultiAgentWrapperGenerator_UsesExplicitFunctionExecutionContext()
    {
        var source = System.IO.File.ReadAllText(System.IO.Path.Combine(
            FindRepoRoot(),
            "dotnet/HPD-Agent.Framework/src/HPD-Agent.SourceGenerator/Capabilities/MultiAgentCapability.cs"));

        Assert.Contains("async (arguments, functionContext, cancellationToken)", source);
        Assert.DoesNotContain("CurrentFunctionContext", source);
    }

    [Fact]
    public void UnsupportedParameter_HookContext_ReportsDiagnostic()
    {
        var source = """
using Microsoft.Extensions.AI;
using HPD.Agent.Middleware;

public partial class RuntimeToolHarness
{
    [AIFunction]
    public string Search(string query, HookContext context) => query;
}
""";

        var (_, diagnostics) = RunGenerator(source);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "HPD020"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Use FunctionExecutionContext", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("AgentContext context")]
    [InlineData("AgentLoopState state")]
    [InlineData("HPD.Events.IEventCoordinator events")]
    [InlineData("HPD.Events.IEventFlowRegistry streams")]
    [InlineData("ToolResultMetadata metadata")]
    public void UnsupportedRuntimeParameters_ReportDiagnostic(string unsupportedParameter)
    {
        var source = $$"""
using Microsoft.Extensions.AI;
using HPD.Agent;
using HPD.Agent.Middleware;

public partial class RuntimeToolHarness
{
    [AIFunction]
    public string Search(string query, {{unsupportedParameter}}) => query;
}
""";

        var (_, diagnostics) = RunGenerator(source);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "HPD020"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Use FunctionExecutionContext", diagnostic.GetMessage());
    }

    [Fact]
    public void StaticGuard_RuntimeParamFilterCentralized()
    {
        var sourceGeneratorRoot = System.IO.Path.Combine(
            FindRepoRoot(),
            "dotnet/HPD-Agent.Framework/src/HPD-Agent.SourceGenerator");
        var source = string.Join(
            "\n",
            System.IO.Directory.EnumerateFiles(sourceGeneratorRoot, "*.cs", System.IO.SearchOption.AllDirectories)
                .Select(System.IO.File.ReadAllText));

        Assert.DoesNotContain("p.Type != \"CancellationToken\"", source);
        Assert.DoesNotContain("p.Type != \"AIFunctionArguments\"", source);
        Assert.DoesNotContain("p.Type != \"IServiceProvider\"", source);
    }
}
