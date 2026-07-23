using HPD.Agent.Tests.TestToolHarnesses;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Tests.Skills;

public sealed class SkillHotReloadTests
{
    [Fact]
    public async Task ManualReload_PublishesValidReplacementAndRetainsInvalidEpoch()
    {
        var source = new InMemorySkillSource([RuntimeSkill("runtime_one")]);
        var agent = await new AgentBuilder(new AgentConfig { Name = "reload-test" })
            .WithToolHarness<CombinedCapabilitiesTools>(options => options.AddSkillSource(source))
            .BuildAsync();
        var events = new System.Collections.Concurrent.ConcurrentQueue<AgentEvent>();
        using var subscription = agent.SubscribeAny(@event =>
        {
            events.Enqueue(@event);
            return ValueTask.CompletedTask;
        });
        try
        {
            Assert.Equal(0, agent.SkillCatalogEpoch);

        source.Replace([RuntimeSkill("runtime_two")]);
        var published = await agent.ReloadSkillsAsync();

        Assert.True(published.Published);
        Assert.Equal(1, agent.SkillCatalogEpoch);

        source.Replace([RuntimeSkill("DataAnalysis")]);
        var rejected = await agent.ReloadSkillsAsync();

            Assert.False(rejected.Published);
            Assert.Equal(1, agent.SkillCatalogEpoch);
            Assert.Contains("Duplicate model-facing", rejected.Error);
            Assert.Contains(events, @event => @event is SkillReloadPublishedEvent published &&
                published.PreviousEpoch == 0 && published.NewEpoch == 1 &&
                published.ChangedSkillIds.Count > 0);
            Assert.Contains(events, @event => @event is SkillReloadRejectedEvent rejectedEvent &&
                rejectedEvent.RetainedEpoch == 1 &&
                !rejectedEvent.Error.Contains("Runtime guide", StringComparison.Ordinal));
        }
        finally { agent.Dispose(); }
    }

    [Fact]
    public async Task DirectoryReload_RetainsCompleteEpochAcrossPartialEditThenPublishesFixedPackage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpd-directory-reload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        var skillPath = Path.Combine(path, "SKILL.md");
        await File.WriteAllTextAsync(skillPath, SkillDocument("Version one."));
        var source = new DirectorySkillSource(path, SkillDirectoryImportMode.Compatibility);
        var agent = await new AgentBuilder(new AgentConfig { Name = "directory-reload-test" })
            .WithToolHarness<CombinedCapabilitiesTools>(options => options.AddSkillSource(source))
            .BuildAsync();
        try
        {
            await File.WriteAllTextAsync(skillPath, "---\nname: watched_skill\ndescription: Watched.\n---\n");
            var rejected = await agent.ReloadSkillsAsync();

            Assert.False(rejected.Published);
            Assert.Equal(0, agent.SkillCatalogEpoch);
            Assert.Contains("no instructions", rejected.Error, StringComparison.OrdinalIgnoreCase);

            await File.WriteAllTextAsync(skillPath, SkillDocument("Version two."));
            var published = await agent.ReloadSkillsAsync();

            Assert.True(published.Published);
            Assert.Equal(1, agent.SkillCatalogEpoch);
        }
        finally
        {
            agent.Dispose();
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task DirectoryReload_RetainsEpochWhenContractSidecarIsPartial()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpd-sidecar-reload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        Directory.CreateDirectory(Path.Combine(path, "scripts"));
        Directory.CreateDirectory(Path.Combine(path, "contracts"));
        await File.WriteAllTextAsync(Path.Combine(path, "SKILL.md"), SkillDocument("Run the sidecar script."));
        await File.WriteAllTextAsync(Path.Combine(path, "scripts", "run.py"), "print('ok')");
        var contractPath = Path.Combine(path, "contracts", "RunInput.schema.json");
        await File.WriteAllTextAsync(contractPath,
            """{"type":"object","properties":{},"required":[],"additionalProperties":false}""");
        await File.WriteAllTextAsync(Path.Combine(path, "skill.json"), """
            {
              "scripts": {
                "scripts/run.py": {
                  "description": "Runs the sidecar-backed script.",
                  "runtime": "python",
                  "parameters": {
                    "$hpdContract": "contracts/RunInput.schema.json"
                  }
                }
              }
            }
            """);
        var services = new ServiceCollection()
            .AddSingleton<ISkillScriptRunner, SuccessfulScriptRunner>()
            .BuildServiceProvider();
        var source = new DirectorySkillSource(path);
        var agent = await new AgentBuilder(new AgentConfig { Name = "sidecar-reload-test" })
            .WithServiceProvider(services)
            .WithToolHarness<CombinedCapabilitiesTools>(options => options.AddSkillSource(source))
            .BuildAsync();
        try
        {
            await File.WriteAllTextAsync(contractPath, """{"type":"object","properties":""");
            var rejected = await agent.ReloadSkillsAsync();

            Assert.False(rejected.Published);
            Assert.Equal(0, agent.SkillCatalogEpoch);
            Assert.Contains("not valid JSON", rejected.Error);

            await File.WriteAllTextAsync(contractPath, """
                {
                  "type": "object",
                  "properties": { "inputFile": { "type": "string" } },
                  "required": ["inputFile"],
                  "additionalProperties": false
                }
                """);
            var published = await agent.ReloadSkillsAsync();

            Assert.True(published.Published);
            Assert.Equal(1, agent.SkillCatalogEpoch);
        }
        finally
        {
            agent.Dispose();
            await services.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task WatchableSource_AutomaticallyPublishesReplacementEpoch()
    {
        var source = new InMemorySkillSource([RuntimeSkill("runtime_one")]);
        var agent = await new AgentBuilder(new AgentConfig { Name = "watch-test" })
            .WithToolHarness<CombinedCapabilitiesTools>(options => options.AddSkillSource(source))
            .BuildAsync();
        try
        {
            source.Replace([RuntimeSkill("runtime_two")]);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (agent.SkillCatalogEpoch == 0)
                await Task.Delay(25, timeout.Token);
            Assert.Equal(1, agent.SkillCatalogEpoch);
        }
        finally { agent.Dispose(); }
    }

    [Fact]
    public async Task SharedWatchableSource_ReloadsEveryAttachedAgent()
    {
        var source = new InMemorySkillSource([RuntimeSkill("runtime_one")]);
        var first = await new AgentBuilder(new AgentConfig { Name = "first-watch-agent" })
            .WithToolHarness<CombinedCapabilitiesTools>(options => options.AddSkillSource(source))
            .BuildAsync();
        var second = await new AgentBuilder(new AgentConfig { Name = "second-watch-agent" })
            .WithToolHarness<CombinedCapabilitiesTools>(options => options.AddSkillSource(source))
            .BuildAsync();
        try
        {
            source.Replace([RuntimeSkill("runtime_two")]);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (first.SkillCatalogEpoch == 0 || second.SkillCatalogEpoch == 0)
                await Task.Delay(25, timeout.Token);

            Assert.Equal(1, first.SkillCatalogEpoch);
            Assert.Equal(1, second.SkillCatalogEpoch);
        }
        finally
        {
            first.Dispose();
            second.Dispose();
        }
    }

    [Fact]
    public async Task Build_RejectsDuplicateRuntimeSkillNamesAcrossSources()
    {
        var first = new InMemorySkillSource([RuntimeSkill("duplicate_skill")]);
        var second = new InMemorySkillSource([RuntimeSkill("duplicate_skill")]);
        var builder = new AgentBuilder(new AgentConfig { Name = "duplicate-source-test" })
            .WithToolHarness<CombinedCapabilitiesTools>(options =>
            {
                options.AddSkillSource(first);
                options.AddSkillSource(second);
            });

        var exception = await Assert.ThrowsAsync<CapabilityGraphValidationException>(() => builder.BuildAsync());

        Assert.Contains("Duplicate capability ID", exception.Message);
    }

    [Fact]
    public async Task WatchableSource_DebouncesBurstIntoOneReconciledEpoch()
    {
        var source = new InMemorySkillSource([RuntimeSkill("runtime_one")]);
        var agent = await new AgentBuilder(new AgentConfig { Name = "watch-debounce-test" })
            .WithToolHarness<CombinedCapabilitiesTools>(options => options.AddSkillSource(source))
            .BuildAsync();
        try
        {
            source.Replace([RuntimeSkill("runtime_intermediate")]);
            source.Replace([RuntimeSkill("runtime_two")]);
            source.Replace([RuntimeSkill("runtime_two")]);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (agent.SkillCatalogEpoch == 0)
                await Task.Delay(25, timeout.Token);
            await Task.Delay(350, timeout.Token);

            Assert.Equal(1, agent.SkillCatalogEpoch);
        }
        finally { agent.Dispose(); }
    }

    [Fact]
    public async Task Build_RejectsScriptWithoutExactlyOneRunner()
    {
        var scriptSkill = Skill.Create(
            name: "script_skill",
            description: "Runs a packaged normalization workflow.",
            instructions: SkillInstructions.FromText("Normalize the active dataset."),
            capabilities:
            [
                SkillCapabilities.Script(
                    "normalize",
                    "Normalizes the active dataset and returns the normalized artifact.",
                    new FileScriptReference("scripts/normalize.py", "python"),
                    SkillScriptInput.Empty)
            ]);
        var source = new InMemorySkillSource([scriptSkill]);
        var builder = new AgentBuilder(new AgentConfig { Name = "runner-test" })
            .WithToolHarness<CombinedCapabilitiesTools>(options => options.AddSkillSource(source));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => builder.BuildAsync());

        Assert.Contains("exactly one compatible runner", exception.Message);
    }

    [Fact]
    public async Task RuntimeSource_MaterializesCrossHarnessGeneratedReferenceFromCatalog()
    {
        var skill = Skill.Create(
            "runtime_weather",
            "Provides runtime weather lookup guidance.",
            SkillInstructions.FromText("Use the weather lookup."),
            [SkillCapabilities.Function<NamedWeatherToolHarness>(nameof(NamedWeatherToolHarness.GetWeather))]);
        var source = new InMemorySkillSource([skill]);

        var agent = await new AgentBuilder(new AgentConfig { Name = "cross-harness-runtime-skill" })
            .WithToolHarness<CombinedCapabilitiesTools>(options => options.AddSkillSource(source))
            .BuildAsync();
        try
        {
            var functions = agent.DefaultOptions!.Tools!.OfType<Microsoft.Extensions.AI.AIFunction>().ToArray();
            Assert.Contains(functions, function => function.Name == "runtime_weather");
            Assert.Contains(functions, function => function.Name == "get_weather");
        }
        finally { agent.Dispose(); }
    }

    [Fact]
    public async Task GeneratedActivation_EmitsBoundedLifecycleEvents()
    {
        var client = new FakeChatClient();
        client.EnqueueToolCall("DataAnalysis", "activate-analysis");
        client.EnqueueToolCall("read_validation_guide", "read-guide");
        client.EnqueueTextResponse("done");
        var config = new AgentConfig
        {
            Name = "skill-event-test",
            Clients = new AgentClientConfig
            {
                Chat = new ClientProviderConfig { ProviderKey = "test", ModelName = "test-model" }
            }
        };
        var agent = await new AgentBuilder(config, new TestProviderRegistry(client))
            .WithToolHarness<CombinedCapabilitiesTools>()
            .BuildAsync();
        var events = new System.Collections.Concurrent.ConcurrentQueue<AgentEvent>();
        using var subscription = agent.SubscribeAny(@event =>
        {
            events.Enqueue(@event);
            return ValueTask.CompletedTask;
        });
        try
        {
            await agent.RunAsync("Analyze the data.");

            Assert.Contains(events, @event => @event is SkillActivationStartedEvent started &&
                started.Name == "DataAnalysis");
            Assert.Contains(events, @event => @event is SkillActivatedEvent activated &&
                activated.Name == "DataAnalysis" && activated.RevealedCapabilityCount == 3);
            Assert.Contains(events, @event => @event is SkillResourceReadStartedEvent started &&
                started.Name == "read_validation_guide");
            Assert.Contains(events, @event => @event is SkillResourceReadCompletedEvent completed &&
                completed.Name == "read_validation_guide");
        }
        finally { agent.Dispose(); }
    }

    [Fact]
    public async Task RuntimeScript_EmitsRunnerLifecycleEventsWithoutSourceContent()
    {
        var script = new SkillScript("run_check", "Runs the packaged verification check.")
        {
            Reference = new FileScriptReference("scripts/check.py", "python"),
            InputContract = SkillScriptInput.Empty,
            RequiresPermission = false
        };
        var source = new InMemorySkillSource([
            Skill.Create(
                "script_guidance",
                "Provides packaged verification guidance.",
                SkillInstructions.FromText("Run the verification check."),
                [script])
        ]);
        var client = new FakeChatClient();
        client.EnqueueToolCall("script_guidance", "activate-script");
        client.EnqueueToolCall("run_check", "run-script");
        client.EnqueueTextResponse("done");
        var services = new ServiceCollection()
            .AddSingleton<ISkillScriptRunner, SuccessfulScriptRunner>()
            .BuildServiceProvider();
        var config = new AgentConfig
        {
            Name = "script-event-test",
            Clients = new AgentClientConfig
            {
                Chat = new ClientProviderConfig { ProviderKey = "test", ModelName = "test-model" }
            }
        };
        var agent = await new AgentBuilder(config, new TestProviderRegistry(client))
            .WithServiceProvider(services)
            .WithToolHarness<CombinedCapabilitiesTools>(options => options.AddSkillSource(source))
            .BuildAsync();
        var events = new System.Collections.Concurrent.ConcurrentQueue<AgentEvent>();
        using var subscription = agent.SubscribeAny(@event =>
        {
            events.Enqueue(@event);
            return ValueTask.CompletedTask;
        });
        try
        {
            await agent.RunAsync("Run the packaged check.");

            var started = Assert.Single(events.OfType<SkillScriptStartedEvent>());
            Assert.Equal("run_check", started.Name);
            Assert.DoesNotContain("check.py", started.Runner, StringComparison.Ordinal);
            Assert.Contains(events, @event => @event is SkillScriptCompletedEvent completed &&
                completed.Name == "run_check");
        }
        finally
        {
            agent.Dispose();
            await services.DisposeAsync();
        }
    }

    [Fact]
    public async Task ReloadDuringModelResponse_ExecutesReturnedCallsAgainstAdvertisedEpoch()
    {
        var source = new InMemorySkillSource([RuntimeSkill("runtime_one")]);
        var innerClient = new FakeChatClient();
        innerClient.EnqueueToolCall("runtime_one", "activate-old");
        innerClient.EnqueueToolCall("runtime_one_guide", "read-old");
        innerClient.EnqueueTextResponse("done");
        using var client = new BlockingFirstResponseChatClient(innerClient);
        var config = new AgentConfig
        {
            Name = "leased-epoch-test",
            Clients = new AgentClientConfig
            {
                Chat = new ClientProviderConfig { ProviderKey = "test", ModelName = "test-model" }
            }
        };
        var agent = await new AgentBuilder(config, new TestProviderRegistry(client))
            .WithToolHarness<CombinedCapabilitiesTools>(options => options.AddSkillSource(source))
            .BuildAsync();
        try
        {
            var run = agent.RunAsync("Use the runtime guidance.");
            await client.FirstRequestEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            source.Replace([RuntimeSkill("runtime_two")]);
            var reload = await agent.ReloadSkillsAsync();
            Assert.True(reload.Published);
            client.ReleaseFirstResponse.TrySetResult();
            await run;

            Assert.Contains("runtime_one", innerClient.CapturedRequestSnapshots[0].ToolNames);
            Assert.DoesNotContain("runtime_two", innerClient.CapturedRequestSnapshots[0].ToolNames);
            Assert.Contains(innerClient.CapturedRequests.SelectMany(request => request), message =>
                message.Contents.OfType<FunctionResultContent>().Any(result =>
                    result.CallId == "read-old" &&
                    !result.Result?.ToString()!.Contains("not found", StringComparison.OrdinalIgnoreCase) == true));
        }
        finally
        {
            client.ReleaseFirstResponse.TrySetResult();
            agent.Dispose();
        }
    }

    private static Skill RuntimeSkill(string name)
        => Skill.Create(
            id: name + "@1",
            name: name,
            description: "Provides runtime-specific analysis guidance.",
            instructions: SkillInstructions.FromText("Use runtime guidance."),
            capabilities:
            [
                SkillCapabilities.Resource(
                    name + "_guide",
                    "Reads the runtime-specific analysis guide.",
                    "Runtime guide.")
            ]);

    private static string SkillDocument(string instructions) => $$"""
        ---
        name: watched_skill
        description: Provides watched guidance.
        ---
        {{instructions}}
        """;

    private sealed class SuccessfulScriptRunner : ISkillScriptRunner
    {
        public bool CanRun(SkillScript script) => script.Reference.Runtime == "python";

        public ValueTask<object?> RunAsync(
            SkillScriptExecutionContext context,
            CancellationToken cancellationToken) => ValueTask.FromResult<object?>("verified");
    }

    private sealed class BlockingFirstResponseChatClient(FakeChatClient inner) : IChatClient
    {
        private int _blocked;

        public TaskCompletionSource FirstRequestEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstResponse { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ChatClientMetadata Metadata => inner.Metadata;

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await BlockFirstAsync(cancellationToken);
            return await inner.GetResponseAsync(chatMessages, options, cancellationToken);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await BlockFirstAsync(cancellationToken);
            await foreach (var update in inner.GetStreamingResponseAsync(
                chatMessages, options, cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => inner.GetService(serviceType, serviceKey);

        public TService? GetService<TService>(object? serviceKey = null) where TService : class
            => inner.GetService<TService>(serviceKey);

        public void Dispose() => inner.Dispose();

        private async Task BlockFirstAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _blocked, 1) != 0)
                return;
            FirstRequestEntered.TrySetResult();
            await ReleaseFirstResponse.Task.WaitAsync(cancellationToken);
        }
    }
}
