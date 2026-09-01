using System.Text;
using HPD.Agent.Tests.TestToolHarnesses;
using HPD.Agent.Collapsing;
using System.Collections.Immutable;

namespace HPD.Agent.Tests.Skills;

public sealed class SkillCapabilityContractTests
{
    [Fact]
    public async Task InlineResource_ReturnsDeclaredValue()
    {
        var resource = SkillCapabilities.Resource("read_guide", "Reads the validation guide.", "Validate first.");

        var result = await resource.ReadAsync(null!, default);

        Assert.Equal("Validate first.", result);
        Assert.Equal(typeof(string), resource.ResultType);
    }

    [Fact]
    public async Task ContentStoreResource_UsesExactExplicitAddress()
    {
        var store = new InMemoryContentStore();
        var scope = ContentScope.Create("installed-skills");
        await using var bytes = new MemoryStream(Encoding.UTF8.GetBytes("Stored guide."));
        var info = await store.WriteAsync(
            scope,
            bytes,
            new ContentMetadata { ContentType = "text/plain" },
            new ContentWriteOptions { Mode = ContentWriteMode.Create });
        var resource = new ContentStoreSkillResource(
            "read_guide",
            "Reads the installed validation guide.",
            new ContentStoreSkillContentReference(info.Address));

        var result = await resource.ReadAsync(
            new SkillResourceContext("analysis", null!, null, store),
            default);

        Assert.Equal("Stored guide.", result);
    }

    [Fact]
    public async Task ContentStoreResource_ReturnsStructuredUnavailableResult()
    {
        var resource = new ContentStoreSkillResource(
            "read_guide",
            "Reads the installed validation guide.",
            new ContentStoreSkillContentReference(
                ContentAddress.Create(ContentScope.Create("installed-skills"), "missing")));

        var result = await resource.ReadAsync(
            new SkillResourceContext("analysis", null!, null, new InMemoryContentStore()),
            default);

        var unavailable = Assert.IsType<SkillResourceUnavailableResult>(result);
        Assert.Equal("read_guide", unavailable.ResourceName);
        Assert.Equal(SkillResourceErrorCategory.NotFound, unavailable.Category);
        Assert.DoesNotContain("installed-skills", unavailable.Message);
    }

    [Fact]
    public void FileResource_RejectsAbsoluteAndEscapingPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpd-skill-root");

        Assert.Throws<ArgumentException>(() => new FileSkillResource(
            "read_guide", "Reads a guide.", root, Path.GetFullPath("outside.txt")));
        Assert.Throws<ArgumentException>(() => new FileSkillResource(
            "read_guide", "Reads a guide.", root, "../outside.txt"));
    }

    [Fact]
    public void Script_ExplicitEmptyInputPreservesDefaultExecutionPolicy()
    {
        var script = SkillCapabilities.Script(
            "normalize",
            "Normalizes the active dataset and returns the normalized artifact.",
            new ContentStoreScriptReference(
                ContentAddress.Create(ContentScope.Create("installed-skills"), "normalize.py"),
                "python"),
            SkillScriptInput.Empty);

        Assert.True(script.RequiresPermission);
        Assert.Equal(TimeSpan.FromMinutes(2), script.Timeout);
        Assert.Equal(1_048_576, script.MaximumOutputBytes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ScriptProjection_PreservesDeclaredPermissionPolicy(bool requiresPermission)
    {
        var script = SkillCapabilities.Script(
            "run_check",
            "Runs the packaged check.",
            new FileScriptReference("run-check.sh", "shell"),
            SkillScriptInput.Empty,
            requiresPermission: requiresPermission);
        var skill = Skill.Create(
            "verification",
            "Verification workflow.",
            SkillInstructions.FromText("Run the check."),
            [script]);

        var function = Assert.Single(SkillCapabilityFunctionProjector.CreateChildren(
            skill,
            CapabilityId.Create("test:verification")));
        var projected = Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(function);

        Assert.Equal(requiresPermission, projected.PermissionDeclaration?.RequiresPermission ?? false);
        Assert.Empty(function.JsonSchema.GetProperty("properties").EnumerateObject());
        Assert.False(function.JsonSchema.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public async Task Script_EnforcesTimeoutThroughRunnerCancellation()
    {
        var script = new SkillScript("slow", "Runs a deliberately slow operation.")
        {
            Reference = new FileScriptReference("slow.py", "python"),
            InputContract = SkillScriptInput.Empty,
            Timeout = TimeSpan.FromMilliseconds(25)
        };
        var runner = new DelegateRunner(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        });

        var timeout = await Assert.ThrowsAsync<SkillScriptExecutionException>(() => SkillCapabilityFunctionProjector
            .ExecuteScriptAsync(
                runner,
                new SkillScriptExecutionContext("test", script, EmptyArguments(), null!, null, null),
                CancellationToken.None)
            .AsTask());
        Assert.Equal(SkillScriptErrorCategory.TimedOut, timeout.Category);
    }

    [Fact]
    public async Task Script_RejectsOversizedAndNonAotResultShapes()
    {
        var script = new SkillScript("bounded", "Returns a bounded result.")
        {
            Reference = new FileScriptReference("bounded.py", "python"),
            InputContract = SkillScriptInput.Empty,
            MaximumOutputBytes = 3
        };
        var context = new SkillScriptExecutionContext("test", script, EmptyArguments(), null!, null, null);

        var oversized = await Assert.ThrowsAsync<SkillScriptExecutionException>(() => SkillCapabilityFunctionProjector
            .ExecuteScriptAsync(new DelegateRunner((_, _) => ValueTask.FromResult<object?>("large")), context, default)
            .AsTask());
        Assert.Equal(SkillScriptErrorCategory.OutputTooLarge, oversized.Category);
        var unsupported = await Assert.ThrowsAsync<SkillScriptExecutionException>(() => SkillCapabilityFunctionProjector
            .ExecuteScriptAsync(new DelegateRunner((_, _) => ValueTask.FromResult<object?>(new object())), context, default)
            .AsTask());
        Assert.Equal(SkillScriptErrorCategory.UnsupportedResult, unsupported.Category);
    }

    [Fact]
    public void GeneratedHarness_ProjectsResourceAsTypedEmptySchemaFunction()
    {
        var builder = new AgentBuilder().WithToolHarness<CombinedCapabilitiesTools>();
        var factory = Assert.Single(
            builder._selectedToolHarnessFactories,
            candidate => candidate.Name == nameof(CombinedCapabilitiesTools));

        var functions = factory.CreateFunctions(new CombinedCapabilitiesTools(), null, null);
        var resource = Assert.Single(functions, function => function.Name == "read_validation_guide");
        var metadata = Assert.IsType<HPDCapabilityMetadata>(
            resource.AdditionalProperties![HPDCapabilityMetadata.AdditionalPropertiesKey]);

        Assert.Equal(HPDCapabilityKind.SkillResource, metadata.Kind);
        Assert.Single(metadata.ParentContainerIds);
        Assert.Equal(0, resource.JsonSchema.GetProperty("properties").EnumerateObject().Count());
    }

    [Fact]
    public void TypedVisibility_RevealsOnlyActivatedSkillChildren()
    {
        var builder = new AgentBuilder().WithToolHarness<CombinedCapabilitiesTools>();
        var factory = Assert.Single(
            builder._selectedToolHarnessFactories,
            candidate => candidate.Name == nameof(CombinedCapabilitiesTools));
        var functions = factory.CreateFunctions(new CombinedCapabilitiesTools(), null, null);
        var visibility = new ToolVisibilityManager(functions);

        var before = visibility.GetToolsForAgentTurn(functions, ImmutableHashSet<string>.Empty);
        Assert.Contains(before, function => function.Name == "DataAnalysis");
        Assert.DoesNotContain(before, function => function.Name == nameof(CombinedCapabilitiesTools.AnalyzeData));
        Assert.DoesNotContain(before, function => function.Name == "read_validation_guide");

        var after = visibility.GetToolsForAgentTurn(functions, ["DataAnalysis"]);
        Assert.DoesNotContain(after, function => function.Name == "DataAnalysis");
        Assert.Contains(after, function => function.Name == nameof(CombinedCapabilitiesTools.AnalyzeData));
        Assert.Contains(after, function => function.Name == nameof(CombinedCapabilitiesTools.ValidateData));
        Assert.Contains(after, function => function.Name == "read_validation_guide");
        Assert.DoesNotContain(after, function => function.Name == nameof(CombinedCapabilitiesTools.TransformData));
    }

    [Fact]
    public void RuntimeSource_ProjectsIntoOwningHarnessGraph()
    {
        var builder = new AgentBuilder().WithToolHarness<CombinedCapabilitiesTools>();
        var factory = Assert.Single(
            builder._selectedToolHarnessFactories,
            candidate => candidate.Name == nameof(CombinedCapabilitiesTools));
        var functions = factory.CreateFunctions(new CombinedCapabilitiesTools(), null, null);
        var runtimeSkill = Skill.Create(
            id: "tenant-analysis@2",
            name: "tenant_analysis",
            description: "Provides tenant-specific analysis guidance.",
            instructions: SkillInstructions.FromText("Use the tenant validation guide."),
            capabilities:
            [
                SkillCapabilities.Resource(
                    "read_tenant_guide",
                    "Reads tenant-specific validation requirements.",
                    "Tenant records require a tenant key.")
            ]);

        functions.AddRange(RuntimeSkillFunctionProjector.Project(
            nameof(CombinedCapabilitiesTools),
            [runtimeSkill],
            functions,
            null));
        var graph = CapabilityGraph.CreateFromFunctions(functions.Where(function =>
            function.AdditionalProperties?.ContainsKey(
                HPDCapabilityMetadata.AdditionalPropertiesKey) == true));
        var activationId = graph.ModelNames["tenant_analysis"];
        var resourceId = graph.ModelNames["read_tenant_guide"];

        Assert.Equal("runtime:CombinedCapabilitiesTools:tenant-analysis@2", activationId.Value);
        Assert.Contains(activationId, graph.Nodes[resourceId].ParentContainerIds);
        Assert.Contains(resourceId, graph.Nodes[activationId].Children);
    }

    [Fact]
    public void RuntimeSkillReference_ResolvesCustomModelNameByDeclarationMember()
    {
        var builder = new AgentBuilder().WithToolHarness<NamedWeatherToolHarness>();
        var factory = Assert.Single(
            builder._selectedToolHarnessFactories,
            candidate => candidate.Name == nameof(NamedWeatherToolHarness));
        var functions = factory.CreateFunctions(new NamedWeatherToolHarness(), null, null);
        var skill = Skill.Create(
            "weather_guidance",
            "Provides weather lookup guidance.",
            SkillInstructions.FromText("Use the weather lookup."),
            [SkillCapabilities.Function<NamedWeatherToolHarness>(nameof(NamedWeatherToolHarness.GetWeather))]);

        var additions = RuntimeSkillFunctionProjector.Project(
            nameof(NamedWeatherToolHarness), [skill], functions, null);
        var activation = Assert.Single(additions, function => function.Name == "weather_guidance");
        var metadata = Assert.IsType<HPDCapabilityMetadata>(
            activation.AdditionalProperties![HPDCapabilityMetadata.AdditionalPropertiesKey]);

        Assert.Contains(metadata.Reveals, id => id.Value == "generated:NamedWeatherToolHarness.get_weather");
    }

    [Fact]
    public void ToolHarnessOptions_AttachesSkillSourceToOwner()
    {
        var source = new InMemorySkillSource();
        var builder = new AgentBuilder().WithToolHarness<CombinedCapabilitiesTools>(
            options => options.AddSkillSource(source));

        Assert.Same(source, Assert.Single(builder._skillSources[nameof(CombinedCapabilitiesTools)]));
    }

    private sealed class DelegateRunner(
        Func<SkillScriptExecutionContext, CancellationToken, ValueTask<object?>> run) : ISkillScriptRunner
    {
        public bool CanRun(SkillScript script) => true;

        public ValueTask<object?> RunAsync(
            SkillScriptExecutionContext context,
            CancellationToken cancellationToken) => run(context, cancellationToken);
    }

    private static SkillScriptArguments EmptyArguments()
    {
        using var document = System.Text.Json.JsonDocument.Parse("{}");
        return new SkillScriptArguments(
            document.RootElement,
            document.RootElement,
            null,
            SkillScriptInput.Empty.CanonicalSchemaFingerprint);
    }
}
