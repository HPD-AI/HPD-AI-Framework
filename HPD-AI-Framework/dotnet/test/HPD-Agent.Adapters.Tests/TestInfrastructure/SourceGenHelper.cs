using System.Reflection;
using HPD.Agent.Adapters;
using HPD.Agent.Adapters.AspNetCore;
using HPD.Agent.Adapters.SourceGenerator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Adapters.Tests.TestInfrastructure;

/// <summary>
/// Helpers for running AdapterSourceGenerator in tests via CSharpGeneratorDriver.
/// </summary>
internal static class SourceGenHelper
{
    /// <summary>
    /// Minimum metadata references needed for test compilations that use HPD adapter attributes.
    /// </summary>
    private static readonly MetadataReference[] BaseReferences = BuildBaseReferences();

    private static MetadataReference[] BuildBaseReferences()
    {
        // Core runtime assemblies — use typeof() to resolve path reliably across TFMs
        var coreLib              = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var systemRuntime        = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location);
        var systemCollections    = MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location);
        var systemThreading      = MetadataReference.CreateFromFile(typeof(System.Threading.CancellationToken).Assembly.Location);
        var systemThreadingTasks = MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location);
        var systemTextJson       = MetadataReference.CreateFromFile(Assembly.Load("System.Text.Json").Location);
        var systemNetHttp        = MetadataReference.CreateFromFile(Assembly.Load("System.Net.Http").Location);
        // ReadOnlySequence<> lives in System.Memory.dll (not System.Private.CoreLib)
        var systemMemory         = MetadataReference.CreateFromFile(typeof(System.Buffers.ReadOnlySequence<byte>).Assembly.Location);
        // NameValueCollection lives in System.Collections.Specialized.dll
        var systemCollectionsSpec = MetadataReference.CreateFromFile(typeof(System.Collections.Specialized.NameValueCollection).Assembly.Location);
        var systemComponentModel  = MetadataReference.CreateFromFile(Assembly.Load("System.ComponentModel").Location);
        var systemWeb             = MetadataReference.CreateFromFile(typeof(System.Web.HttpUtility).Assembly.Location);

        // ASP.NET Core assemblies — use typeof() anchors; they live in the shared framework
        // folder and cannot be loaded by name on all platforms.
        var aspNetCoreHttp        = MetadataReference.CreateFromFile(typeof(HttpContext).Assembly.Location);
        // Results class lives in Microsoft.AspNetCore.Http.Results.dll (separate from HttpContext)
        var aspNetCoreHttpResults = MetadataReference.CreateFromFile(typeof(Results).Assembly.Location);
        // IResult lives alongside Results
        var aspNetCoreHttpAbstr   = MetadataReference.CreateFromFile(typeof(IResult).Assembly.Location);
        // Results.Forbid() requires AuthenticationProperties from Authentication.Abstractions
        var aspNetCoreAuthAbstr   = MetadataReference.CreateFromFile(
            Path.Combine(Path.GetDirectoryName(typeof(HttpContext).Assembly.Location)!,
                "Microsoft.AspNetCore.Authentication.Abstractions.dll"));
        // Microsoft.AspNetCore.Http.Extensions lives in the same assembly as HttpContext on .NET 9+
        var aspNetCoreHttpExt     = MetadataReference.CreateFromFile(
            Path.Combine(Path.GetDirectoryName(typeof(HttpContext).Assembly.Location)!,
                "Microsoft.AspNetCore.Http.Extensions.dll"));
        var aspNetCoreRouting     = MetadataReference.CreateFromFile(typeof(IEndpointRouteBuilder).Assembly.Location);
        var aspNetCoreRoutingAbst = MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Routing.RouteData).Assembly.Location);
        var aspNetCoreBuilder     = MetadataReference.CreateFromFile(typeof(IApplicationBuilder).Assembly.Location);

        // Microsoft.Extensions assemblies — use typeof() anchors
        var extensionsDi      = MetadataReference.CreateFromFile(typeof(ServiceCollection).Assembly.Location);
        var extensionsDiAbstr = MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location);
        var extensionsOptions = MetadataReference.CreateFromFile(typeof(IOptions<>).Assembly.Location);

        // HPD adapter attribute assembly (HpdAdapterAttribute, HpdWebhookHandlerAttribute, etc.)
        var abstractions = MetadataReference.CreateFromFile(typeof(HpdAdapterAttribute).Assembly.Location);

        // HPD adapter AspNetCore assembly (WebhookSignatureVerifier, AdapterRegistration, etc.)
        var aspNetCoreAdapters = MetadataReference.CreateFromFile(typeof(AdapterRegistration).Assembly.Location);

        return
        [
            coreLib,
            systemRuntime,
            systemCollections,
            systemThreading,
            systemThreadingTasks,
            systemTextJson,
            systemNetHttp,
            systemMemory,
            systemCollectionsSpec,
            systemComponentModel,
            systemWeb,
            aspNetCoreHttp,
            aspNetCoreHttpResults,
            aspNetCoreHttpAbstr,
            aspNetCoreAuthAbstr,
            aspNetCoreHttpExt,
            aspNetCoreRouting,
            aspNetCoreRoutingAbst,
            aspNetCoreBuilder,
            extensionsDi,
            extensionsDiAbstr,
            extensionsOptions,
            abstractions,
            aspNetCoreAdapters,
        ];
    }

    /// <summary>
    /// Creates a Roslyn compilation from the given C# source text.
    /// </summary>
    public static CSharpCompilation CreateCompilation(string source, string assemblyName = "TestAssembly")
    {
        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: BaseReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>
    /// Runs AdapterSourceGenerator against the given source and returns the driver run result.
    /// </summary>
    public static GeneratorDriverRunResult RunGenerator(
        string source,
        out Compilation outputCompilation,
        string assemblyName = "TestAssembly")
    {
        var compilation = CreateCompilation(source, assemblyName);
        var generator   = new AdapterSourceGenerator();
        var driver      = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
            compilation, out outputCompilation, out _);
        return driver.GetRunResult();
    }

    /// <summary>
    /// Returns all diagnostics reported by the generator for the given source.
    /// </summary>
    public static IReadOnlyList<Diagnostic> GetDiagnostics(string source)
    {
        RunGenerator(source, out _);
        // Re-run to capture generator diagnostics separately
        var compilation = CreateCompilation(source);
        var generator   = new AdapterSourceGenerator();
        var driver      = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
            compilation, out _, out var generatorDiagnostics);
        var result = driver.GetRunResult();
        return result.Diagnostics;
    }

    /// <summary>
    /// Returns the text of a generated file by hint name (filename), or null if not found.
    /// </summary>
    public static string? GetGeneratedFile(GeneratorDriverRunResult result, string hintName)
    {
        foreach (var generatorResult in result.Results)
        {
            foreach (var source in generatorResult.GeneratedSources)
            {
                if (source.HintName == hintName)
                    return source.SourceText.ToString();
            }
        }
        return null;
    }

    /// <summary>
    /// Returns all generated file hint names from a run result.
    /// </summary>
    public static IReadOnlyList<string> GetGeneratedFileNames(GeneratorDriverRunResult result)
    {
        return result.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.HintName)
            .ToList();
    }

    /// <summary>
    /// Verifies that the output compilation has zero errors.
    /// Returns the list of errors for assertion convenience.
    /// </summary>
    public static IReadOnlyList<Diagnostic> GetCompilationErrors(Compilation outputCompilation)
    {
        return outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
    }
}
