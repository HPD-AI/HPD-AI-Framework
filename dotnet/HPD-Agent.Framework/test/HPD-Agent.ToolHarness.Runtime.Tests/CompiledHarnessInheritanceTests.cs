using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using HPD.Agent.Middleware;
using HPD.Agent.Tests.Middleware.V2;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests;

public sealed class CompiledHarnessInheritanceTests
{
    private static readonly MetadataReference[] References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)).ToArray();

    private static CSharpCompilation Compilation(string name, string source,
        params MetadataReference[] additional) => CSharpCompilation.Create(name,
        [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
        References.Concat(additional), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static byte[] Emit(Microsoft.CodeAnalysis.Compilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(System.Environment.NewLine,
            result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        return stream.ToArray();
    }

    private static AIFunctionArguments Arguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        var arguments = new AIFunctionArguments();
        arguments.SetJson(document.RootElement.Clone());
        return arguments;
    }

    private static Microsoft.CodeAnalysis.Compilation Generate(CSharpCompilation compilation,
        out IReadOnlyList<Diagnostic> diagnostics)
    {
        var driver = CSharpGeneratorDriver.Create(
            [new global::HPDToolSourceGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var reported);
        diagnostics = reported;
        return generated;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FluentSkillFromCompiledBase_IsEvaluatedOnce_AndKeepsReturnedName(bool filterByMember)
    {
        var (owner, producer) = CompileSkillOwner();
        var config = new AgentConfig();
        if (filterByMember)
            config.ToolHarnesses = [new ToolHarnessReference
            {
                Name = $"{owner.Assembly.GetName().Name}:{owner.Namespace}:{owner.Name}", Functions = ["Research", "Backtest"]
            }];
        var builder = new AgentBuilder(config)
            .WithChatClient(new MiddlewareTestHelpers.TestChatClient())
            .WithEventComposition(Infrastructure.TestEventComposition.Current)
            .WithAPIConfiguration(new ConfigurationBuilder().Build())
            .WithToolHarness(owner);
        await using var agent = await builder.BuildAsync();
        var tools = agent.DefaultOptions!.Tools!.OfType<AIFunction>().ToArray();
        var skill = Assert.Single(tools, function => function.Name == "research-live");
        var metadata = (HPDCapabilityMetadata)skill.AdditionalProperties[HPDCapabilityMetadata.AdditionalPropertiesKey]!;
        var backtest = Assert.Single(tools, function => function.Name == "Backtest");
        Assert.Contains(((HPDCapabilityMetadata)backtest.AdditionalProperties[
            HPDCapabilityMetadata.AdditionalPropertiesKey]!).Id, metadata.Reveals);
        Assert.Equal(1, producer.GetType("FluentPackage.BaseHarness")!.GetProperty("FactoryCalls")!.GetValue(null));
        Assert.Equal(0, producer.GetType("FluentPackage.BaseHarness")!.GetProperty("InstructionCalls")!.GetValue(null));
        Assert.Single(tools, function => function.Name == owner.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitFilters_DoNotEvaluateExcludedFluentFactories(bool empty)
    {
        var (owner, producer) = CompileSkillOwner();
        var config = new AgentConfig
        {
            ToolHarnesses = [new ToolHarnessReference
            {
                Name = $"{owner.Assembly.GetName().Name}:{owner.Namespace}:{owner.Name}", Functions = empty ? [] : ["Backtest"]
            }]
        };
        await using var agent = await new AgentBuilder(config)
            .WithChatClient(new MiddlewareTestHelpers.TestChatClient())
            .WithEventComposition(Infrastructure.TestEventComposition.Current)
            .WithAPIConfiguration(new ConfigurationBuilder().Build())
            .WithToolHarness(owner)
            .BuildAsync();
        Assert.Equal(0, producer.GetType("FluentPackage.BaseHarness")!.GetProperty("FactoryCalls")!.GetValue(null));
        Assert.DoesNotContain(agent.DefaultOptions!.Tools!, tool => tool.Name == "research-live");
    }

    [Fact]
    public void FactoryAdmission_AndOwnerRelativeProjection_AreExplicit()
    {
        var (owner, producer) = CompileSkillOwner();
        var factory = Assert.Single((ToolHarnessFactory[])owner.Assembly
            .GetType("HPD.Agent.Generated.ToolHarnessRegistry")!
            .GetField("All")!.GetValue(null)!);
        var instance = factory.CreateInstance();

        Assert.Empty(factory.CreateSkills!(instance, null, new HashSet<string>()));
        Assert.Equal(0, producer.GetType("FluentPackage.BaseHarness")!
            .GetProperty("FactoryCalls")!.GetValue(null));

        var definitions = factory.CreateSkills(instance, null,
            new HashSet<string> { "Research" });
        var definition = Assert.Single(definitions);
        Assert.Equal(1, producer.GetType("FluentPackage.BaseHarness")!
            .GetProperty("FactoryCalls")!.GetValue(null));
        Assert.Equal(0, producer.GetType("FluentPackage.BaseHarness")!
            .GetProperty("InstructionCalls")!.GetValue(null));

        var functions = factory.CreateFunctions(instance, null, null);
        var backtest = Assert.Single(functions, function => function.Name == "Backtest");
        var projected = RuntimeSkillFunctionProjector.Project(
            factory.ActivationIdentity,
            [definition.Definition],
            functions,
            serialization: null,
            reference => functions.Single(function => TryMetadata(function, out var metadata) &&
                metadata.OwnerHarnessIdentity == factory.ActivationIdentity &&
                metadata.DeclarationMemberName == reference.MemberName),
            [definition]);
        var activation = Assert.Single(projected, function => function.Name == "research-live");
        Assert.Contains(((HPDCapabilityMetadata)backtest.AdditionalProperties[
            HPDCapabilityMetadata.AdditionalPropertiesKey]!).Id,
            ((HPDCapabilityMetadata)activation.AdditionalProperties[
                HPDCapabilityMetadata.AdditionalPropertiesKey]!).Reveals);
    }

    private static bool TryMetadata(AIFunction function, out HPDCapabilityMetadata metadata)
    {
        metadata = (HPDCapabilityMetadata)function.AdditionalProperties[
            HPDCapabilityMetadata.AdditionalPropertiesKey]!;
        return true;
    }

    private static (Type Owner, Assembly Producer) CompileSkillOwner()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var bytes = Emit(Compilation("FluentProducer" + suffix, """
            using HPD.Agent;
            namespace FluentPackage;
            [Collapse("Research package", SystemPrompt = "Research carefully.")]
            public class BaseHarness
            {
                public static int FactoryCalls { get; private set; }
                public static int InstructionCalls { get; private set; }
                [AIFunction] public string Backtest() => "done";
                [Skill] public Skill Research() => MakeDefinition();
                private Skill MakeDefinition()
                {
                    FactoryCalls++;
                    return Skill.Create("research-live", "Research a strategy.",
                        (context, cancellationToken) =>
                        {
                            InstructionCalls++;
                            return System.Threading.Tasks.ValueTask.FromResult("Inspect before testing.");
                        },
                        [SkillCapabilities.OwnerFunction(nameof(Backtest))]);
                }
            }
            """));
        var producer = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(bytes));
        var consumer = Compilation("FluentConsumer" + suffix, """
            namespace FluentConsumer;
            public class StrategyHarness : FluentPackage.BaseHarness { }
            """, MetadataReference.CreateFromImage(bytes));
        var generated = Generate(consumer, out var diagnostics);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var assembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(Emit(generated)));
        return (assembly.GetType("FluentConsumer.StrategyHarness")!, producer);
    }

    [Fact]
    public void CompiledSubAgentFactory_AppliesInheritedMetadataBeforeFactoryEvaluation()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var bytes = Emit(Compilation("RoleProducer" + suffix, """
            using HPD.Agent;
            namespace RolePackage;
            public class MetadataBase : IToolMetadata
            {
                public bool Enabled { get; set; }
                public string Label { get; set; } = "research";
                public T? GetProperty<T>(string name, T? value = default) => value;
                public bool HasProperty(string name) => true;
                public System.Collections.Generic.IEnumerable<string> GetPropertyNames() => new[] { "Enabled", "Label" };
            }
            public class RoleMetadata : MetadataBase { }
            public class BaseHarness
            {
                public static int FactoryCalls { get; private set; }
                [SubAgent<RoleMetadata>, ConditionalSubAgent("Enabled"), AIDescription("Delegate {metadata.Label}")]
                public SubAgent Explore() => MakeRole();
                private SubAgent MakeRole()
                {
                    FactoryCalls++;
                    return SubAgent.FromConfig("packaged/explorer", "explorer-live", "Explores.", new AgentConfig());
                }
            }
            """));
        var producer = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(bytes));
        var generated = Generate(Compilation("RoleConsumer" + suffix, """
            namespace RoleConsumer;
            public class ResearchHarness : RolePackage.BaseHarness { }
            """, MetadataReference.CreateFromImage(bytes)), out var diagnostics);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var assembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(Emit(generated)));
        var factory = Assert.Single((ToolHarnessFactory[])assembly.GetType("HPD.Agent.Generated.ToolHarnessRegistry")!
            .GetField("All")!.GetValue(null)!);
        var instance = factory.CreateInstance();
        var metadata = (IToolMetadata)Activator.CreateInstance(factory.MetadataType!)!;
        Assert.Empty(factory.CreateSubAgentActions!(instance, metadata, null));
        factory.MetadataType!.GetProperty("Enabled")!.SetValue(metadata, true);
        Assert.Empty(factory.CreateSubAgentActions(instance, metadata, new HashSet<string>()));
        var role = Assert.Single(factory.CreateSubAgentActions(instance, metadata, null));
        Assert.Equal("explorer-live", role.Action);
        Assert.Equal("Explore", role.DeclarationMemberName);
        Assert.Equal("Delegate research", role.Description);
        Assert.Equal(1, producer.GetType("RolePackage.BaseHarness")!.GetProperty("FactoryCalls")!.GetValue(null));
    }

    [Fact]
    public async Task CompiledSourceProviders_ReachCollectorsWithDerivedIdentity()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var bytes = Emit(Compilation("SourceProducer" + suffix, """
            using HPD.Agent;
            public class McpServerConfig { }
            public class OpenApiConfig { }
            public static class SourceProviderState
            {
                public static int McpCalls;
                public static int OpenApiCalls;
            }
            namespace HPD.Agent.MCP
            {
                public static class McpCapabilitySourceFactory
                {
                    public static HPD.Agent.IAgentCapabilitySourceFactory? FromServer(
                        string id, McpServerConfig config, string owner, bool collapse, bool? permission) => null;
                }
            }
            namespace SourcePackage
            {
                public class BaseHarness
                {
                    [AIFunction]
                    public string Backtest() => "done";
                    [McpServer("fixture")]
                    public McpServerConfig Tools()
                    {
                        System.Threading.Interlocked.Increment(ref SourceProviderState.McpCalls);
                        return new();
                    }
                    [OpenApi(Prefix = "stocks")]
                    public OpenApiConfig Market()
                    {
                        System.Threading.Interlocked.Increment(ref SourceProviderState.OpenApiCalls);
                        return new();
                    }
                }
            }
            """));
        var producer = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(bytes));
        var generated = Generate(Compilation("SourceConsumer" + suffix, """
            namespace SourceConsumer;
            public class TradingHarness : SourcePackage.BaseHarness { }
            """, MetadataReference.CreateFromImage(bytes)), out var diagnostics);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var assembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(Emit(generated)));
        var factory = Assert.Single((ToolHarnessFactory[])assembly.GetType("HPD.Agent.Generated.ToolHarnessRegistry")!
            .GetField("All")!.GetValue(null)!);
        var expectedOwner = $"{assembly.GetName().Name}:SourceConsumer:TradingHarness";

        var mcp = new List<McpServerSource>();
        factory.CollectMcpServers!(factory.CreateInstance(), null, null, mcp.Add);
        Assert.Single(mcp);
        Assert.Equal(expectedOwner, mcp[0].ParentToolHarness);

        var openApi = new List<(string Name, object Config, string Owner)>();
        factory.CollectOpenApiSources!(factory.CreateInstance(), null, null,
            (name, config, owner) => openApi.Add((name, config, owner)));
        var source = Assert.Single(openApi);
        Assert.Equal("stocks", source.Name);
        Assert.Equal(expectedOwner, source.Owner);
        var providerState = producer.GetType("SourceProviderState")!;
        Assert.Equal(0, providerState.GetField("McpCalls")!.GetValue(null));
        Assert.Equal(1, providerState.GetField("OpenApiCalls")!.GetValue(null));
        providerState.GetField("McpCalls")!.SetValue(null, 0);
        providerState.GetField("OpenApiCalls")!.SetValue(null, 0);

        var filteredConfig = new AgentConfig
        {
            ToolHarnesses =
            [
                new ToolHarnessReference
                {
                    Name = $"{assembly.GetName().Name}:SourceConsumer:TradingHarness",
                    Functions = ["Backtest"]
                }
            ]
        };
        await using (await new AgentBuilder(filteredConfig)
            .WithChatClient(new MiddlewareTestHelpers.TestChatClient())
            .WithEventComposition(Infrastructure.TestEventComposition.Current)
            .WithAPIConfiguration(new ConfigurationBuilder().Build())
            .WithToolHarness(assembly.GetType("SourceConsumer.TradingHarness")!)
            .BuildAsync())
        {
        }
        Assert.Equal(0, providerState.GetField("McpCalls")!.GetValue(null));
        Assert.Equal(0, providerState.GetField("OpenApiCalls")!.GetValue(null));
    }

    [Fact]
    public void CompiledBaseEventManifest_BecomesDerivedModuleRequirement()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var bytes = Emit(Compilation("EventProducer" + suffix, """
            using HPD.Agent;
            using HPD.Agent.Serialization;
            [assembly: HpdAgentEventModuleManifestAttribute(
                "package.events", typeof(EventPackage.PackageEvents))]
            namespace EventPackage;
            public static class PackageEvents
            {
                public static AgentEventModuleFragment Fragment => null!;
            }
            public class BaseHarness
            {
                [AIFunction] public string Work() => "done";
            }
            """));
        _ = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(bytes));
        var generated = Generate(Compilation("EventConsumer" + suffix, """
            namespace EventConsumer;
            public class TradingHarness : EventPackage.BaseHarness { }
            """, MetadataReference.CreateFromImage(bytes)), out var diagnostics);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var text = string.Join(System.Environment.NewLine,
            generated.SyntaxTrees.Where(tree => tree.FilePath.Contains("ToolHarnessRegistry"))
                .Select(tree => tree.GetText().ToString()));
        Assert.Contains("RequiredEventModules", text);
        Assert.Contains("global::EventPackage.PackageEvents.Fragment", text);
    }

    [Fact]
    public void MultiAgentFactory_TransportsDeadlineAndConstructionDelegate()
    {
        var generated = Generate(Compilation("WorkflowInheritance", """
            using HPD.Agent;
            using HPD.Agent.Middleware;
            using HPD.Events;
            namespace WorkflowPackage;
            public sealed class AgentWorkflowInstance : IMultiAgentWorkflow
            {
                public async System.Collections.Generic.IAsyncEnumerable<Event> ExecuteStreamingAsync(
                    string input, FunctionExecutionContext? parent, System.Threading.CancellationToken ct = default)
                {
                    await System.Threading.Tasks.Task.CompletedTask;
                    yield break;
                }
                public System.Threading.Tasks.Task<string> RunAsync(
                    string input, FunctionExecutionContext? parent, System.Threading.CancellationToken ct = default)
                    => System.Threading.Tasks.Task.FromResult(input);
            }
            public class BaseHarness
            {
                [MultiAgent("Executes a workflow.", TimeoutSeconds = 12)]
                public AgentWorkflowInstance Workflow() => new();
            }
            """), out var diagnostics);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var text = string.Join(System.Environment.NewLine,
            generated.SyntaxTrees.Select(tree => tree.GetText().ToString()));
        Assert.Contains("WorkflowFactory = () =>", text);
        Assert.Contains("Timeout = global::System.TimeSpan.FromSeconds(12)", text);
        Assert.DoesNotContain("var workflow = instance.Workflow()", text);
    }

    [Fact]
    public async Task InheritedMcpSource_LoadsAndDisposesThroughCapabilityLifecycle()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var bytes = Emit(Compilation("LifecycleProducer" + suffix, """
            using System.Collections.Immutable;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using HPD.Agent;
            using Microsoft.Extensions.AI;
            public class McpServerConfig { }
            public static class SourceState
            {
                public static int LoadCalls;
                public static int SourceDisposeCalls;
                public static int RevisionDisposeCalls;
            }
            namespace HPD.Agent.MCP
            {
                public static class McpCapabilitySourceFactory
                {
                    public static IAgentCapabilitySourceFactory FromServer(
                        string id, McpServerConfig config, string owner, bool collapse, bool? permission)
                        => new FakeFactory(CapabilitySourceId.Create(id));
                }
                internal sealed class FakeFactory(CapabilitySourceId id) : IAgentCapabilitySourceFactory
                {
                    public CapabilitySourceId Id { get; } = id;
                    public ValueTask<IAgentCapabilitySource> CreateAsync(
                        System.IServiceProvider? services, System.Threading.CancellationToken ct)
                        => ValueTask.FromResult<IAgentCapabilitySource>(new FakeSource(Id));
                }
                internal sealed class FakeSource(CapabilitySourceId id) : IAgentCapabilitySource
                {
                    public CapabilitySourceId Id { get; } = id;
                    public ValueTask<CapabilitySourceLoadResult> LoadAsync(
                        CapabilityLoadContext context, System.Threading.CancellationToken ct)
                    {
                        System.Threading.Interlocked.Increment(ref SourceState.LoadCalls);
                        return ValueTask.FromResult(new CapabilitySourceLoadResult(new Owner(Id)));
                    }
                    public async System.Collections.Generic.IAsyncEnumerable<CapabilityInvalidation> WatchAsync(
                        [EnumeratorCancellation] System.Threading.CancellationToken ct = default)
                    {
                        await System.Threading.Tasks.Task.CompletedTask;
                        yield break;
                    }
                    public ValueTask DisposeAsync()
                    {
                        System.Threading.Interlocked.Increment(ref SourceState.SourceDisposeCalls);
                        return ValueTask.CompletedTask;
                    }
                }
                internal sealed class Owner(CapabilitySourceId id) : ICapabilitySourceRevisionOwner
                {
                    public CapabilitySourceId SourceId { get; } = id;
                    public CapabilitySourceRevision Revision { get; } = CapabilitySourceRevision.Create(1);
                    public CapabilitySourceSnapshot Snapshot { get; } = new()
                    {
                        Functions = ImmutableArray<AIFunction>.Empty,
                        Descriptors = ImmutableDictionary<CapabilityId, CapabilityDescriptor>.Empty
                    };
                    public ValueTask DisposeAsync()
                    {
                        System.Threading.Interlocked.Increment(ref SourceState.RevisionDisposeCalls);
                        return ValueTask.CompletedTask;
                    }
                }
            }
            namespace LifecyclePackage
            {
                public class BaseHarness
                {
                    [McpServer("fixture")]
                    public McpServerConfig Tools() => new();
                }
            }
            """));
        var producer = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(bytes));
        var generated = Generate(Compilation("LifecycleConsumer" + suffix, """
            namespace LifecycleConsumer;
            public class DerivedHarness : LifecyclePackage.BaseHarness { }
            """, MetadataReference.CreateFromImage(bytes)), out var diagnostics);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var assembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(Emit(generated)));
        var owner = assembly.GetType("LifecycleConsumer.DerivedHarness")!;

        await using (await new AgentBuilder(new AgentConfig())
            .WithChatClient(new MiddlewareTestHelpers.TestChatClient())
            .WithEventComposition(Infrastructure.TestEventComposition.Current)
            .WithAPIConfiguration(new ConfigurationBuilder().Build())
            .WithToolHarness(owner)
            .BuildAsync())
        {
        }

        var state = producer.GetType("SourceState")!;
        Assert.Equal(1, state.GetField("LoadCalls")!.GetValue(null));
        Assert.Equal(1, state.GetField("SourceDisposeCalls")!.GetValue(null));
        Assert.Equal(1, state.GetField("RevisionDisposeCalls")!.GetValue(null));
    }

    [Fact]
    public void EmptyPartialDerivedOwner_InheritsClosedGenericContractAndMetadata()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var producerBytes = Emit(Compilation("GenericBase" + suffix, """
            using HPD.Agent;
            namespace PackagedGeneric;
            public class BaseMetadata : IToolMetadata
            {
                public bool Enabled { get; set; }
                public T? GetProperty<T>(string name, T? defaultValue = default) => defaultValue;
                public bool HasProperty(string name) => name == nameof(Enabled);
                public System.Collections.Generic.IEnumerable<string> GetPropertyNames() => new[] { nameof(Enabled) };
            }
            public abstract class Base<T>
            {
                [AIFunction<BaseMetadata>, ConditionalFunction("Enabled")]
                public T Echo(T value) => value;
            }
            """));
        _ = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(producerBytes));
        var consumer = Compilation("GenericOwner" + suffix, """
            namespace SpecializedGeneric;
            public partial class ClosedHarness { }
            public partial class ClosedHarness : PackagedGeneric.Base<string> { }
            """, MetadataReference.CreateFromImage(producerBytes));
        var generated = Generate(consumer, out var diagnostics);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var assembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(Emit(generated)));
        var factory = Assert.Single((ToolHarnessFactory[])assembly.GetType("HPD.Agent.Generated.ToolHarnessRegistry")!
            .GetField("All")!.GetValue(null)!);
        Assert.Equal("PackagedGeneric.BaseMetadata", factory.MetadataType!.FullName);
        var metadata = (IToolMetadata)Activator.CreateInstance(factory.MetadataType)!;
        Assert.Empty(factory.CreateFunctions(factory.CreateInstance(), metadata, null));
        factory.MetadataType.GetProperty("Enabled")!.SetValue(metadata, true);
        Assert.Single(factory.CreateFunctions(factory.CreateInstance(), metadata, null));
    }

    [Fact]
    public void ExplicitNewFunction_ReplacesDifferentInheritedContract()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var baseBytes = Emit(Compilation("ReplacementBase" + suffix, """
            public class BaseHarness { [AIFunction] public string Run(int value) => value.ToString(); }
            """));
        _ = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(baseBytes));
        var consumer = Compilation("HiddenOwner" + suffix, """
            public class DerivedHarness : BaseHarness { [AIFunction] public new string Run(string value) => value; }
            """, MetadataReference.CreateFromImage(baseBytes));
        var generated = Generate(consumer, out var diagnostics);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var assembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(Emit(generated)));
        var factory = Assert.Single((ToolHarnessFactory[])assembly.GetType("HPD.Agent.Generated.ToolHarnessRegistry")!
            .GetField("All")!.GetValue(null)!);
        Assert.Equal(["Run"], factory.FunctionNames);
        var function = Assert.Single(factory.CreateFunctions(factory.CreateInstance(), null, null));
        Assert.Equal("Run", function.Name);
        Assert.Contains("\"string\"", function.JsonSchema.ToString());
    }

    [Fact]
    public void UnmarkedDifferentFunction_ReportsCollision()
    {
        var consumer = Compilation("UnmarkedHiddenOwner" + Guid.NewGuid().ToString("N"), """
            public class BaseHarness { [AIFunction] public string Run(int value) => value.ToString(); }
            public class DerivedHarness : BaseHarness { [AIFunction] public string Run(string value) => value; }
            """);
        var generated = Generate(consumer, out var diagnostics);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "HPDHIN001");
        Assert.DoesNotContain(generated.SyntaxTrees, tree => tree.FilePath.EndsWith("DerivedHarnessRegistration.g.cs"));
    }

    [Fact]
    public async Task CompiledBase_ComposesCollapseAndMiddleware_AndDispatchesDerivedOverride()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var producerBytes = Emit(Compilation("InheritanceProducer" + suffix, """
            using HPD.Agent;
            using HPD.Agent.Middleware;
            namespace Packaged;
            public sealed class BaseMiddleware : IToolHarnessMiddleware { }
            [Collapse("Base tools", SystemPrompt = "Base instructions", FunctionResult = "Base activation",
                Middlewares = [typeof(BaseMiddleware)])]
            public class CodingBase
            {
                [AIFunction, AIDescription("Echoes text")]
                public virtual string Echo([AIDescription("Input text")] string text = "default") => "base:" + text;
                [AIFunction] public static int Count(int value) => value + 1;
            }
            """));
        var producer = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(producerBytes));
        var consumer = Compilation("InheritanceConsumer" + suffix, """
            using HPD.Agent;
            using HPD.Agent.Middleware;
            namespace Specialized;
            public sealed class DerivedMiddleware : IToolHarnessMiddleware { }
            [Collapse("Strategy tools", SystemPrompt = "Strategy instructions", FunctionResult = "Strategy activation",
                Middlewares = [typeof(Packaged.BaseMiddleware), typeof(DerivedMiddleware)])]
            public class StrategyHarness : Packaged.CodingBase
            {
                public override string Echo(string text) => "derived:" + text;
                [AIFunction] public string Backtest() => "backtested";
            }
            """, MetadataReference.CreateFromImage(producerBytes));

        var driver = CSharpGeneratorDriver.Create(
            [new global::HPDToolSourceGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview));
        driver.RunGeneratorsAndUpdateCompilation(consumer, out var generated, out var diagnostics);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var assembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(Emit(generated)));
        var registration = assembly.GetType("StrategyHarnessRegistration")!;
        var instance = Activator.CreateInstance(assembly.GetType("Specialized.StrategyHarness")!)!;
        var functions = (List<AIFunction>)registration.GetMethod("CreateToolHarness")!
            .Invoke(null, [instance, null, null])!;
        Assert.Equal(4, functions.Count); // One derived container and three tools.
        var echo = Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(
            Assert.Single(functions, function => function.Name == "Echo"));
        var echoMetadata = (HPDCapabilityMetadata)echo.AdditionalProperties[
            HPDCapabilityMetadata.AdditionalPropertiesKey]!;
        Assert.Equal($"{producer.GetName().Name}:Packaged:CodingBase",
            echoMetadata.DeclaringHarnessIdentity);
        Assert.Equal($"{assembly.GetName().Name}:Specialized:StrategyHarness",
            echoMetadata.OwnerHarnessIdentity);
        Assert.Equal($"{assembly.GetName().Name}:Specialized.StrategyHarness:Echo",
            echoMetadata.ImplementationIdentity);
        var arguments = new Dictionary<string, object?> { ["text"] = "sample" };
        var request = MiddlewareTestHelpers.CreateFunctionRequest(echo, arguments: arguments);
        var context = new FunctionExecutionContext(MiddlewareTestHelpers.CreateBeforeIterationContext(), request);
        Assert.Equal("derived:sample", await echo.InvokeAsync(Arguments("""{"text":"sample"}"""), context));

        var registry = assembly.GetType("HPD.Agent.Generated.ToolHarnessRegistry")!;
        var factory = Assert.Single((ToolHarnessFactory[])registry.GetField("All")!.GetValue(null)!);
        var baseIdentity = $"{producer.GetName().Name}:Packaged:CodingBase";
        Assert.Contains(baseIdentity, factory.InheritedHarnessIdentities!);
        Assert.True(AgentGeneratedRegistry.ToolHarnessInherits(factory.Name, baseIdentity));
        Assert.Equal("Base instructions\n\nStrategy instructions", factory.SystemPrompt);
        Assert.Equal("Base activation\n\nStrategy activation", factory.FunctionResult);
        Assert.Equal([producer.GetType("Packaged.BaseMiddleware"), assembly.GetType("Specialized.DerivedMiddleware")],
            factory.Middleware!.Select(descriptor => descriptor.MiddlewareType));
        Assert.Equal("derived:default", await echo.InvokeAsync(Arguments("{}"), context));
        var count = Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(
            Assert.Single(functions, function => function.Name == "Count"));
        Assert.Equal(3, Assert.IsType<JsonElement>(
            await count.InvokeAsync(Arguments("""{"value":2}"""), context)).GetInt32());
    }
}
