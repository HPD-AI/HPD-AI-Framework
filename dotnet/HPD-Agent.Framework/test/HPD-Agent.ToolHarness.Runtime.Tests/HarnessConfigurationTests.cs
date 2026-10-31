using System.Text.Json;
using HPD.Agent.Middleware;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;
using HPD.Agent.Tests.Infrastructure;
using HPD.Agent.Tests.Middleware.V2;
using HPD.Serialization;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests;

public sealed class HarnessConfigurationTests
{
    [Fact]
    public async Task JsonAndTypedRunOverrides_UseOneCapturedImmutableContract()
    {
        await using var agent = await Builder().WithToolHarness<ConfiguredHarness>(options => options.Configure(
            new HarnessTestSettings { MaxItems = 20, Nested = new() { Label = "agent" } })).BuildAsync();
        var json = HpdAgentConfigSerializer.DeserializeRunConfig("""
            {"harnesses":[{"name":"ConfiguredHarness","config":{"maxItems":3}}]}
            """)!;
        var typed = new AgentRunConfig();
        typed.Harnesses.For<ConfiguredHarness>().Override(new HarnessTestSettingsOverrides { MaxItems = 4 });
        var first = agent.CaptureRunConfig(json);
        var second = agent.CaptureRunConfig(typed);
        json.Harnesses.Clear(); typed.Harnesses.Clear();
        Assert.Equal("3:agent", await Invoke(agent, first));
        Assert.Equal("4:agent", await Invoke(agent, second));
        Assert.Equal("20:agent", await Invoke(agent, agent.CaptureRunConfig(null)));
    }

    [Theory]
    [InlineData(HpdConfigFormat.Json)]
    [InlineData(HpdConfigFormat.Yaml)]
    public async Task RunOverrides_RoundTrip_AndResetUsesLibraryBaseline(HpdConfigFormat format)
    {
        await using var agent = await Builder().WithToolHarness<ConfiguredHarness>(options =>
            options.Configure(new HarnessTestSettings { MaxItems = 20 })).BuildAsync();
        var original = new AgentRunConfig();
        original.Harnesses.For<ConfiguredHarness>().Override(new HarnessTestSettingsOverrides(), "/maxItems");
        var document = HpdAgentConfigSerializer.Serialize(original, format);
        var loaded = HpdAgentConfigSerializer.DeserializeRunConfig(document, format)!;
        Assert.Equal("10:library", await Invoke(agent, agent.CaptureRunConfig(loaded)));
    }

    [Theory]
    [InlineData(HpdConfigFormat.Json)]
    [InlineData(HpdConfigFormat.Yaml)]
    public void SubAgentRunConfig_RoundTripsDirectAndDescendantHarnesses(HpdConfigFormat format)
    {
        var child = new SubAgentRunConfig();
        child.Harnesses.For<ConfiguredHarness>().Override(
            new HarnessTestSettingsOverrides { MaxItems = 5 });
        var defaults = new SubAgentRunDefaults();
        defaults.Harnesses.For<ConfiguredHarness>().Override(
            new HarnessTestSettingsOverrides { MaxItems = 3 });
        child.DescendantDefaults = defaults;

        var document = HpdAgentConfigSerializer.Serialize(child, format);
        var loaded = HpdAgentConfigSerializer.DeserializeSubAgentRunConfig(document, format)!;

        Assert.Equal(child.Harnesses.Single().Name, loaded.Harnesses.Single().Name);
        Assert.Equal(defaults.Harnesses.Single().Name, loaded.DescendantDefaults!.Harnesses.Single().Name);
        using var direct = JsonDocument.Parse(loaded.Harnesses[0].Config!.Value.GetRawText());
        using var descendant = JsonDocument.Parse(
            loaded.DescendantDefaults.Harnesses[0].Config!.Value.GetRawText());
        Assert.Equal(5, direct.RootElement.GetProperty("maxItems").GetInt32());
        Assert.Equal(3, descendant.RootElement.GetProperty("maxItems").GetInt32());
    }

    [Theory]
    [InlineData("{\"destination\":\"other\"}", "HarnessRunOverrideNotSupported")]
    [InlineData("{\"MaxItems\":3}", "HarnessConfigUnknownProperty")]
    [InlineData("{\"maxItems\":3,\"maxItems\":4}", "HarnessConfigDuplicateProperty")]
    [InlineData("{\"maxItems\":\"three\"}", "HarnessConfigInvalidValue")]
    [InlineData("{\"maxItems\":0}", "invalid_test_settings")]
    public async Task InvalidRunOverrides_FailBeforeExecution(string patch, string code)
    {
        await using var agent = await Builder().WithToolHarness<ConfiguredHarness>().BuildAsync();
        var input = new AgentRunConfig { Harnesses = [new() { Name = nameof(ConfiguredHarness), Config = JsonDocument.Parse(patch).RootElement.Clone() }] };
        var error = Assert.Throws<AgentRunConfigurationException>(() => agent.CaptureRunConfig(input));
        Assert.Equal(code, error.Code);
    }

    [Fact]
    public async Task ConstructionJsonAndTypedSettings_UseSameOwnerResolver()
    {
        var config = HpdAgentConfigSerializer.Deserialize("""
            {"toolHarnesses":[{"name":"ConfiguredHarness","config":{"maxItems":15}}]}
            """)!;
        await using var agent = await Builder(config).WithToolHarness<ConfiguredHarness>().BuildAsync();
        Assert.Equal("15:library", await Invoke(agent, agent.CaptureRunConfig(null)));
    }

    [Fact]
    public async Task InvalidConstructionJson_FailsWithStructuredPathAndSourceLocation()
    {
        var config = HpdAgentConfigSerializer.Deserialize(
            """
            {"toolHarnesses":[{"name":"ConfiguredHarness","config":{"maxItems":"three"}}]}
            """)!;
        var error = await Assert.ThrowsAsync<AgentRunConfigurationException>(() =>
            Builder(config).WithToolHarness<ConfiguredHarness>().BuildAsync());
        Assert.Equal("HarnessConfigInvalidValue", error.Code);
        Assert.Equal("toolHarnesses[0].config.maxItems", error.Path);
        Assert.Equal(1, error.LineNumber);
    }

    [Fact]
    public async Task HandlesRejectOtherBuiltAgent_DespiteSameTypeAndDisplayName()
    {
        var captured = new HandleCapture();
        await using var first = await Builder().WithToolHarness<ConfiguredHarness>(options => options.UseDependency(captured)).BuildAsync();
        await using var second = await Builder().WithToolHarness<ConfiguredHarness>().BuildAsync();
        var context = Context(second, second.CaptureRunConfig(null));
        var error = Assert.Throws<AgentRunConfigurationException>(() => captured.Handle!.For(context));
        Assert.Equal("HarnessConfigurationOwnerMismatch", error.Code);
    }

    [Fact]
    public async Task DirectChildOverride_SurvivesPolicyTransport_AndBindsAgainstChildCatalog()
    {
        await using var child = await Builder()
            .WithToolHarness<ConfiguredHarness>(options => options.Configure(
                new HarnessTestSettings { MaxItems = 20 }))
            .BuildAsync();
        var original = new SubAgentRunConfig();
        original.Harnesses.For<ConfiguredHarness>().Override(
            new HarnessTestSettingsOverrides { MaxItems = 5 });

        var captured = AgentRunConfigSnapshot.Capture(original, ProviderComposition.Create([]))!;
        var policy = SubAgentExecutionPolicy.Create(
            captured,
            new AgentClientsConfig { Chat = new ChatClientConfig { ModelName = "child" } },
            new Dictionary<ProviderClientFamily, SubAgentClientSelectionSource>
            {
                [ProviderClientFamily.Chat] = SubAgentClientSelectionSource.InputSubAgentRun
            },
            new AgentSecurityRunConfig(),
            new NoSubAgentClientPropagation());
        original.Harnesses.Clear();

        var restored = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(policy, AgentEventJsonContext.Default.SubAgentExecutionPolicy),
            AgentEventJsonContext.Default.SubAgentExecutionPolicy)!;
        restored.Validate();
        var childRun = AgentRunConfigSnapshot.Capture(
            restored.InitialRunConfig, child.ProviderComposition)!;
        childRun.HarnessOverrideLayers =
            AgentRunConfigSnapshot.CloneHarnessOverrideLayers(restored.HarnessOverrideLayers);

        Assert.Equal("5:library", await Invoke(child, child.CaptureRunConfig(childRun)));
    }

    [Fact]
    public async Task DescendantDefaults_ComposeBeforeMoreSpecificOverride_AndKeepIsolatedSnapshots()
    {
        await using var child = await Builder()
            .WithToolHarness<ConfiguredHarness>(options => options.Configure(
                new HarnessTestSettings { MaxItems = 20 }))
            .BuildAsync();
        var defaults = new SubAgentRunDefaults();
        defaults.Harnesses.For<ConfiguredHarness>().Override(
            new HarnessTestSettingsOverrides { MaxItems = 3 });
        var policy = SubAgentExecutionPolicy.Create(
            null,
            new AgentClientsConfig { Chat = new ChatClientConfig { ModelName = "child" } },
            new Dictionary<ProviderClientFamily, SubAgentClientSelectionSource>
            {
                [ProviderClientFamily.Chat] = SubAgentClientSelectionSource.InputSubAgentRun
            },
            new AgentSecurityRunConfig(),
            new NoSubAgentClientPropagation(),
            defaults);
        var specific = new SubAgentRunConfig();
        specific.Harnesses.For<ConfiguredHarness>().Override(
            new HarnessTestSettingsOverrides { MaxItems = 4 });
        var merged = SubAgentRuntime.ResolveContinuationDescendantRunConfig(specific, policy)!;

        var codec = new AgentInputCodec(ProviderComposition.Create([]));
        var input = new UserMessagesInputEvent
        {
            SessionId = "child",
            ThreadId = "main",
            Messages = [new ChatMessage(ChatRole.User, "work")],
            SubAgentRunConfig = merged
        };
        var serialized = codec.Serialize(input);
        defaults.Harnesses.Clear();
        specific.Harnesses.Clear();
        var transported = codec.Deserialize(serialized).SubAgentRunConfig!;
        var childRun = AgentRunConfigSnapshot.Capture(
            transported, child.ProviderComposition)!;

        Assert.Single(transported.HarnessOverrideLayers!);
        Assert.Equal("4:library", await Invoke(child, child.CaptureRunConfig(childRun)));
    }

    [Fact]
    public async Task DurablePolicy_FingerprintsAndRoundTripsInheritedHarnessLayers()
    {
        await using var child = await Builder()
            .WithToolHarness<ConfiguredHarness>(options => options.Configure(
                new HarnessTestSettings { MaxItems = 20 }))
            .BuildAsync();
        var defaults = new AgentRunConfig();
        defaults.Harnesses.For<ConfiguredHarness>().Override(
            new HarnessTestSettingsOverrides { MaxItems = 3 });
        var current = new AgentRunConfig();
        current.Harnesses.For<ConfiguredHarness>().Override(
            new HarnessTestSettingsOverrides { MaxItems = 4 });
        var clients = new AgentClientsConfig { Chat = new ChatClientConfig { ModelName = "child" } };
        var sources = new Dictionary<ProviderClientFamily, SubAgentClientSelectionSource>
        {
            [ProviderClientFamily.Chat] = SubAgentClientSelectionSource.InputSubAgentRun
        };
        var withoutLayer = SubAgentExecutionPolicy.Create(
            current, clients, sources, new AgentSecurityRunConfig(), new NoSubAgentClientPropagation());
        var policy = SubAgentExecutionPolicy.Create(
            current,
            clients,
            sources,
            new AgentSecurityRunConfig(),
            new NoSubAgentClientPropagation(),
            harnessOverrideLayers: [defaults.Harnesses]);
        var restored = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(policy, AgentEventJsonContext.Default.SubAgentExecutionPolicy),
            AgentEventJsonContext.Default.SubAgentExecutionPolicy)!;
        restored.Validate();
        var childRun = AgentRunConfigSnapshot.Capture(
            restored.InitialRunConfig, child.ProviderComposition)!;
        childRun.HarnessOverrideLayers =
            AgentRunConfigSnapshot.CloneHarnessOverrideLayers(restored.HarnessOverrideLayers);

        Assert.NotEqual(withoutLayer.Fingerprint, policy.Fingerprint);
        Assert.Single(restored.HarnessOverrideLayers!);
        Assert.Equal("4:library", await Invoke(child, child.CaptureRunConfig(childRun)));
    }

    [Fact]
    public void ParentHarnessOverrides_DoNotPopulateChildOrDefaultSnapshots()
    {
        var parentRun = new AgentRunConfig();
        parentRun.Harnesses.For<ConfiguredHarness>().Override(
            new HarnessTestSettingsOverrides { MaxItems = 2 });
        var childRun = new SubAgentRunConfig();
        var defaults = new SubAgentRunDefaults();

        var childSnapshot = AgentRunConfigSnapshot.Capture(
            childRun, ProviderComposition.Create([]))!;
        var policy = SubAgentExecutionPolicy.Create(
            null,
            new AgentClientsConfig { Chat = new ChatClientConfig { ModelName = "child" } },
            new Dictionary<ProviderClientFamily, SubAgentClientSelectionSource>
            {
                [ProviderClientFamily.Chat] = SubAgentClientSelectionSource.InputSubAgentRun
            },
            new AgentSecurityRunConfig(),
            new NoSubAgentClientPropagation(),
            defaults);

        Assert.NotEmpty(parentRun.Harnesses);
        Assert.Empty(childSnapshot.Harnesses);
        Assert.Empty(SubAgentRuntime.CreateDescendantRunConfig(policy)!.Harnesses);
    }

    [Fact]
    public async Task ChildSpecificOverride_UsesChildDiagnosticPathForMissingOwner()
    {
        await using var child = await Builder()
            .WithToolHarness<ConfiguredHarness>()
            .BuildAsync();
        var run = new AgentRunConfig
        {
            Harnesses = [new() { Name = "MissingChildHarness" }],
            HarnessOverridePath = "subAgentRunConfig.harnesses"
        };

        var error = Assert.Throws<AgentRunConfigurationException>(() =>
            child.CaptureRunConfig(run));

        Assert.Equal("HarnessNotRegistered", error.Code);
        Assert.Equal("subAgentRunConfig.harnesses[0].name", error.Path);
    }

    [Theory]
    [InlineData(HpdConfigFormat.Json, "{\n  \"harnesses\": [\n    {\"name\":\"ConfiguredHarness\",\n     \"config\":{\"maxItems\":\"3\"}}\n  ]\n}")]
    [InlineData(HpdConfigFormat.Yaml, "harnesses:\n  - name: ConfiguredHarness\n    config:\n      maxItems: \"3\"\n")]
    public async Task DeferredBinding_PreservesDocumentLocation_AndDoesNotCoerceStrings(HpdConfigFormat format, string text)
    {
        await using var agent = await Builder().WithToolHarness<ConfiguredHarness>().BuildAsync();
        var loaded = HpdAgentConfigSerializer.DeserializeRunConfig(text, format)!;
        var error = Assert.Throws<AgentRunConfigurationException>(() => agent.CaptureRunConfig(loaded));
        Assert.Equal("harnesses[0].config.maxItems", error.Path);
        Assert.Equal(format == HpdConfigFormat.Yaml ? 5 : 4, error.LineNumber);
        Assert.True(error.ColumnNumber > 0);
    }

    [Theory]
    [InlineData("{\"harnesses\":[],\"harnesses\":[]}")]
    [InlineData("{\"harnesses\":[{\"name\":\"ConfiguredHarness\",\"config\":{\"maxItems\":1,\"maxItems\":2}}]}")]
    public void DuplicateKeys_FailBeforeLossyNormalization(string json)
        => Assert.Throws<JsonException>(() => HpdAgentConfigSerializer.DeserializeRunConfig(json));

    private static AgentBuilder Builder(AgentConfig? config = null) => new AgentBuilder(config ?? new())
        .WithChatClient(new MiddlewareTestHelpers.TestChatClient())
        .WithEventComposition(TestEventComposition.Current);
    private static FunctionExecutionContext Context(Agent agent, AgentRunConfig config)
    {
        var tool = Assert.Single(agent.DefaultOptions!.Tools!.OfType<AIFunction>(), tool => tool.Name == "ReadSettings");
        var request = MiddlewareTestHelpers.CreateFunctionRequest(tool) with { RunConfig = config };
        return new FunctionExecutionContext(MiddlewareTestHelpers.CreateBeforeIterationContext(), request);
    }
    private static async Task<object?> Invoke(Agent agent, AgentRunConfig config)
    {
        var tool = Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(Assert.Single(agent.DefaultOptions!.Tools!.OfType<AIFunction>(), tool => tool.Name == "ReadSettings"));
        var arguments = new AIFunctionArguments();
        using var json = JsonDocument.Parse("{}"); arguments.SetJson(json.RootElement.Clone());
        return await tool.InvokeAsync(arguments, Context(agent, config));
    }
}

public sealed record HarnessNestedSettings
{
    public string Label { get; init; } = "library";
}
public sealed record HarnessTestSettings : IHarnessSettingsValidator
{
    [HarnessRunSetting] public int MaxItems { get; init; } = 10;
    [HarnessRunSetting] public HarnessNestedSettings Nested { get; init; } = new();
    public string Destination { get; init; } = "host";
    public void ValidateSettings()
    {
        if (MaxItems < 1) throw new AgentRunConfigurationException("invalid_test_settings", "config.maxItems", "Must be positive.");
    }
}
public sealed class HandleCapture { public IHarnessConfig<HarnessTestSettings>? Handle { get; set; } }
public sealed class ConfiguredHarness
{
    private readonly IHarnessConfig<HarnessTestSettings> _settings;
    public ConfiguredHarness(IHarnessConfig<HarnessTestSettings> settings, HandleCapture? capture = null)
    {
        _settings = settings;
        if (capture is not null) capture.Handle = settings;
    }
    [AIFunction] public string ReadSettings(FunctionExecutionContext context)
    {
        var value = _settings.For(context);
        return $"{value.MaxItems}:{value.Nested.Label}";
    }
}
