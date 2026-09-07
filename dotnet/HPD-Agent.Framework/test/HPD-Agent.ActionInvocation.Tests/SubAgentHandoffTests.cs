using HPD.Agent;
using HPD.Agent.Serialization;
using HPD.Agent.Providers;
using Microsoft.Extensions.DependencyInjection;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using System.Text.Json;

public class SubAgentHandoffTests
{
    private static async Task<Agent> Build(FakeChatClient? client = null)
        => await new AgentBuilder(new AgentConfig { Name = "handoff-test" }, ProviderComposition.Create([new ProviderManifestFragment(
                [new SummaryDescriptor()], [new("test", ["local"], [ProviderClientFamily.Chat],
                    () => new TestChatClientProvider(client ?? new()))], [], [])]))
            .WithEventComposition(CoreAgentEventComposition.Instance)
            .WithChatClient(new IdentifiedClient(client ?? new())).BuildAsync();

    private sealed class SummaryDescriptor : IProviderDescriptor
    {
        public string ProviderKey => "test";
        public string DisplayName => "Summary test";
        public Uri? DocumentationUri => null;
        public IReadOnlyList<string> Aliases => [];
        public IReadOnlyDictionary<ProviderClientFamily, ProviderFamilyDescriptor> Families { get; } =
            new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor> { [ProviderClientFamily.Chat] = new() { Family = ProviderClientFamily.Chat } };
        public IReadOnlyDictionary<string, ProviderBackendDescriptor> Backends { get; } =
            new Dictionary<string, ProviderBackendDescriptor> { ["local"] = new()
            {
                BackendKey = "local", IsDefault = true,
                Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor> { [ProviderClientFamily.Chat] = new() { Family = ProviderClientFamily.Chat } },
                Authentication = [new() { Kind = ProviderAuthenticationKind.Anonymous, IsDefault = true, SupportedFamilies = new HashSet<ProviderClientFamily> { ProviderClientFamily.Chat } }]
            } };
    }

    private static CompactionSpecification SummarySpec => new()
    {
        Point = new CompactAtCurrentHead(), Preservation = new PreserveNoPreviousHistory(),
        Strategy = new SummarizingCompaction(), CommitMode = CompactionCommitMode.Soft
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandoffRespectsSoftCompactionAndContainsNoParentState(bool extraSummary)
    {
        var client = new FakeChatClient();
        if (extraSummary) client.EnqueueTextResponse("CHILD BRIEFING");
        await using var agent = await Build(client);
        await agent.CreateSessionAsync("parent");
        var store = agent.Config!.SessionStore!;
        var parent = (await store.ProjectThreadAsync("parent", "main", ThreadProjectionPurpose.ModelContext))!;
        var original = new ChatMessage(ChatRole.User, "ORIGINAL HISTORY MUST NOT RETURN") { MessageId = "old" };
        await store.AppendThreadEventsAsync(new("parent", "main"), ThreadMessageEventConverter.ToThreadEvents("parent", "main", original));
        parent.Messages.Add(original);
        var summarizer = new FakeChatClient(); summarizer.EnqueueTextResponse("EXISTING SUMMARY");
        var prepared = (await new ThreadCompactionEngine().PrepareAsync(
            new(parent, parent.Messages, null, summarizer), SummarySpec))!;
        await store.AppendThreadEventAsync("parent", "main", prepared.Checkpoint);
        await store.AppendThreadEventsAsync(new("parent", "main"), ThreadMessageEventConverter.ToThreadEvents("parent", "main",
            new ChatMessage(ChatRole.User, "RECENT CORRECTION") { MessageId = "recent" }));
        await store.AppendThreadEventAsync("parent", "main", ThreadEventFactory.ThreadMiddlewareStateCommitted(
            "parent", "main", new Dictionary<string, string> { ["parent-task"] = "{}" }));
        var before = await store.GetThreadAsync(new("parent", "main"));
        var handoff = await agent.PrepareSubAgentContextAsync(new("parent", "main"),
            extraSummary ? SummarySpec with { Strategy = new SummarizingCompaction
            { Summarizer = new ChatClientConfig { Provider = new() { Key = "test" }, ModelName = "summary" } } } : null, default);
        Assert.DoesNotContain("ORIGINAL HISTORY", handoff.Text);
        if (extraSummary) Assert.Contains("CHILD BRIEFING", handoff.Text);
        else { Assert.Contains("EXISTING SUMMARY", handoff.Text); Assert.Contains("RECENT CORRECTION", handoff.Text); }
        Assert.Equal(before!.Head, (await store.GetThreadAsync(new("parent", "main")))!.Head);
        await SubAgentRuntime.CreateEmptyThreadAsync(agent, "parent", "child", new()
        {
            ["kind"] = "subagent", ["parentSessionId"] = "parent", ["parentThreadId"] = "main", ["subAgentName"] = "worker"
        }, default, handoff);
        var events = (await store.CollectThreadEventsAsync(new("parent", "child")))!;
        Assert.Equal(2, events.Count);
        Assert.Single(events.OfType<SubAgentContextReceivedEvent>());
        var child = (await store.ProjectThreadAsync("parent", "child", ThreadProjectionPurpose.ModelContext))!;
        Assert.Single(child.Messages);
        Assert.Equal(handoff.MessageId, child.Messages[0].MessageId);
        Assert.Empty(child.MiddlewareState);
        Assert.Null(child.ForkedFrom);
        // Re-encoding after hard compaction preserves the semantic text once, not an unconditional context seed.
        var encoded = ThreadJournalEncoder.Encode(child, child.Messages);
        var replay = ThreadProjector.Project("parent", "child", encoded, ThreadProjectionPurpose.ModelContext);
        Assert.Single(replay.Messages);
        Assert.Equal(child.Messages[0].Text, replay.Messages[0].Text);
        var nested = await agent.PrepareSubAgentContextAsync(new("parent", "child"), null, default);
        Assert.Contains(extraSummary ? "CHILD BRIEFING" : "RECENT CORRECTION", nested.Text);
        Assert.DoesNotContain("ORIGINAL HISTORY", nested.Text);
    }

    [Fact]
    public async Task ChildContextCanBeCompactedAwayWithoutResurrection()
    {
        await using var agent = await Build();
        await agent.CreateSessionAsync("session");
        var store = agent.Config!.SessionStore!;
        var context = new SubAgentContextReceivedEvent("context", "BACKGROUND", new("session", "main"), new(1, 1));
        await SubAgentRuntime.CreateEmptyThreadAsync(agent, "session", "child", null, default, context);
        var child = (await store.ProjectThreadAsync("session", "child", ThreadProjectionPurpose.ModelContext))!;
        var summary = new FakeChatClient(); summary.EnqueueTextResponse("NEW SUMMARY");
        var prepared = (await new ThreadCompactionEngine().PrepareAsync(new(child, child.Messages, null, summary), SummarySpec))!;
        await store.AppendThreadEventAsync("session", "child", prepared.Checkpoint);
        var replay = (await store.ProjectThreadAsync("session", "child", ThreadProjectionPurpose.ModelContext))!;
        Assert.Single(replay.Messages);
        Assert.Equal("NEW SUMMARY", replay.Messages[0].Text);
        Assert.DoesNotContain(replay.Messages, m => m.MessageId == "context");
    }

    [Fact]
    public async Task ReservedBoundaryExcludesLaterParentActivityAndCreationRetryDoesNotDuplicateContext()
    {
        await using var agent = await Build();
        await agent.CreateSessionAsync("s");
        var store = agent.Config!.SessionStore!;
        await store.AppendThreadEventsAsync(new("s", "main"), ThreadMessageEventConverter.ToThreadEvents("s", "main",
            new ChatMessage(ChatRole.User, "AT RESERVATION") { MessageId = "first" }));
        var head = (await store.GetThreadAsync(new("s", "main")))!;
        var cursor = new ThreadJournalCursor(head.Generation, head.Head);
        await store.AppendThreadEventsAsync(new("s", "main"), ThreadMessageEventConverter.ToThreadEvents("s", "main",
            new ChatMessage(ChatRole.User, "LATER ACTIVITY") { MessageId = "later" }));
        var handoff = await agent.PrepareSubAgentContextAsync(new("s", "main"), null, default, cursor);
        Assert.Contains("AT RESERVATION", handoff.Text);
        Assert.DoesNotContain("LATER ACTIVITY", handoff.Text);
        var metadata = new Dictionary<string, object> { ["kind"] = "subagent" };
        await SubAgentRuntime.CreateEmptyThreadAsync(agent, "s", "child", metadata, default, handoff);
        await SubAgentRuntime.CreateEmptyThreadAsync(agent, "s", "child", metadata, default, handoff);
        Assert.Single((await store.CollectThreadEventsAsync(new("s", "child")))!.OfType<SubAgentContextReceivedEvent>());
    }

    [Fact]
    public async Task RemovalCompactionIsRespectedWithoutASummary()
    {
        await using var agent = await Build();
        await agent.CreateSessionAsync("s");
        var store = agent.Config!.SessionStore!;
        await store.AppendThreadEventsAsync(new("s", "main"), ThreadMessageEventConverter.ToThreadEvents("s", "main",
            new ChatMessage(ChatRole.User, "REMOVED") { MessageId = "old" }));
        var parent = (await store.ProjectThreadAsync("s", "main", ThreadProjectionPurpose.ModelContext))!;
        var specification = SummarySpec with { Strategy = new RemovalCompaction() };
        var prepared = (await new ThreadCompactionEngine().PrepareAsync(new(parent, parent.Messages, null, null), specification))!;
        await store.AppendThreadEventAsync("s", "main", prepared.Checkpoint);
        await store.AppendThreadEventsAsync(new("s", "main"), ThreadMessageEventConverter.ToThreadEvents("s", "main",
            new ChatMessage(ChatRole.User, "KEEP") { MessageId = "new" }));
        var handoff = await agent.PrepareSubAgentContextAsync(new("s", "main"), null, default);
        Assert.DoesNotContain("REMOVED", handoff.Text);
        Assert.Contains("KEEP", handoff.Text);
    }

    [Fact]
    public void OldForkChoiceIsRejectedRatherThanAliased()
    {
        using var document = JsonDocument.Parse("""{"context":"fork"}""");
        Assert.Throws<InvalidOperationException>(() => SubAgentContexts.ReadRequestedContext(document.RootElement));
        using var handoff = JsonDocument.Parse("""{"context":"handoff"}""");
        Assert.Equal(SubAgentContext.Handoff, SubAgentContexts.ReadRequestedContext(handoff.RootElement));
    }

    [Fact]
    public void DefaultsSurviveInputTransportAndDoNotDependOnModelPropagation()
    {
        var run = new SubAgentRunConfig { DescendantDefaults = new()
        {
            Context = new() { Properties = new Dictionary<string, object> { ["workspace"] = "workspace-json" } },
            Collapsing = new() { EnableErrorRecovery = true }
        } };
        var codec = new AgentInputCodec(HPD.Agent.Providers.ProviderComposition.Create([]));
        var input = new UserMessagesInputEvent { SessionId = "s", ThreadId = "t", Messages = [new(ChatRole.User, "work")], SubAgentRunConfig = run };
        var decoded = codec.Deserialize(codec.Serialize(input));
        Assert.NotNull(decoded.SubAgentRunConfig!.DescendantDefaults);
        var policy = SubAgentExecutionPolicy.Create(null,
            new AgentClientsConfig { Chat = new ChatClientConfig { ModelName = "test" } },
            new Dictionary<HPD.Agent.Providers.ProviderClientFamily, SubAgentClientSelectionSource>
            { [HPD.Agent.Providers.ProviderClientFamily.Chat] = SubAgentClientSelectionSource.ChildAgentConfig },
            new(), new NoSubAgentClientPropagation(), decoded.SubAgentRunConfig.DescendantDefaults);
        var serialized = JsonSerializer.Serialize(policy, AgentEventJsonContext.Default.SubAgentExecutionPolicy);
        var restored = JsonSerializer.Deserialize(serialized, AgentEventJsonContext.Default.SubAgentExecutionPolicy)!;
        restored.Validate();
        var descendant = SubAgentRuntime.CreateDescendantRunConfig(restored)!;
        Assert.Null(descendant.Clients.Chat);
        Assert.True(descendant.Collapsing!.EnableErrorRecovery);
        Assert.Contains("workspace", descendant.Context!.Properties!.Keys);
        Assert.NotNull(descendant.DescendantDefaults);
    }

    [Fact]
    public void UpdatingChildSettingsPreservesAdmittedModelPropagation()
    {
        var policy = SubAgentExecutionPolicy.Create(null,
            new AgentClientsConfig { Chat = new ChatClientConfig { ModelName = "locked" } },
            new Dictionary<HPD.Agent.Providers.ProviderClientFamily, SubAgentClientSelectionSource>
            { [HPD.Agent.Providers.ProviderClientFamily.Chat] = SubAgentClientSelectionSource.InputSubAgentRun },
            new(), new RemainingSubAgentClientPropagation(2));
        var supplied = new SubAgentRunConfig { Collapsing = new() { EnableErrorRecovery = true } };
        var result = SubAgentRuntime.ResolveContinuationDescendantRunConfig(supplied, policy)!;
        Assert.Equal("locked", result.Clients.Chat!.ModelName);
        Assert.Equal(2, Assert.IsType<BoundedSubAgentClientPropagation>(result.ClientPropagation).Depth);
        Assert.True(result.Collapsing!.EnableErrorRecovery);
        var explicitSelection = new SubAgentRunConfig { Clients = new() { Chat = new() { ModelName = "explicit" } } };
        Assert.Equal("explicit", SubAgentRuntime.ResolveContinuationDescendantRunConfig(explicitSelection, policy)!.Clients.Chat!.ModelName);
    }

    private static SubAgentExecutionPolicy Admit(SubAgentRunConfig run) => SubAgentExecutionPolicy.Create(run,
        new() { Chat = new() { ModelName = "child" } },
        new Dictionary<ProviderClientFamily, SubAgentClientSelectionSource> { [ProviderClientFamily.Chat] = SubAgentClientSelectionSource.InputSubAgentRun },
        new(), new NoSubAgentClientPropagation(), run.DescendantDefaults, run.HandoffCompaction);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SummaryRequiresExplicitClientAtAdmission(bool handoff)
    {
        var run = new SubAgentRunConfig();
        if (handoff) run.HandoffCompaction = SummarySpec;
        else run.Compaction = new() { Automatic = new() { Trigger = new TurnCountCompactionTrigger(5), Compaction = SummarySpec } };
        var error = Assert.Throws<AgentRunConfigurationException>(() => Admit(run));
        Assert.Equal("subagent_summarizer_required", error.Code);
    }

    [Fact]
    public void HandoffPolicySurvivesTransportSnapshotAdmissionAndNestedContinuation()
    {
        var config = new ChatClientConfig { Provider = new() { Key = "test" }, ModelName = "summary" };
        var spec = SummarySpec with { Strategy = new SummarizingCompaction { Summarizer = config } };
        var run = new SubAgentRunConfig { HandoffCompaction = spec, DescendantDefaults = new() { HandoffCompaction = spec } };
        var codec = new AgentInputCodec(ProviderComposition.Create([]));
        var input = new UserMessagesInputEvent { SessionId = "s", ThreadId = "t", Messages = [new(ChatRole.User, "work")], SubAgentRunConfig = run };
        var decoded = codec.Deserialize(codec.Serialize(input)).SubAgentRunConfig!;
        Assert.Equal("summary", Assert.IsType<SummarizingCompaction>(decoded.HandoffCompaction!.Strategy).Summarizer!.ModelName);
        var captured = AgentRunConfigSnapshot.Capture(run, ProviderComposition.Create([]))!;
        var policy = Admit(captured);
        config.ModelName = "mutated";
        Assert.Equal("summary", Assert.IsType<SummarizingCompaction>(policy.HandoffCompaction!.Strategy).Summarizer!.ModelName);
        var restored = JsonSerializer.Deserialize(JsonSerializer.Serialize(policy, AgentEventJsonContext.Default.SubAgentExecutionPolicy), AgentEventJsonContext.Default.SubAgentExecutionPolicy)!;
        restored.Validate();
        Assert.Null(restored.InitialRunConfig!.Compaction!.Automatic);
        var child = SubAgentRuntime.CreateDescendantRunConfig(restored)!;
        var grandchild = SubAgentRuntime.CreateDescendantRunConfig(Admit(child))!;
        Assert.Equal("summary", Assert.IsType<SummarizingCompaction>(grandchild.HandoffCompaction!.Strategy).Summarizer!.ModelName);
        var continued = SubAgentRuntime.ResolveContinuationDescendantRunConfig(new(), restored)!;
        Assert.NotNull(continued.HandoffCompaction);
        Assert.Throws<InvalidOperationException>(() => (restored with { HandoffCompaction = null }).Validate());
    }

    [Fact]
    public void RuntimeOnlySummarizerIsRejectedBeforeSnapshotCanDropIt()
    {
        var spec = SummarySpec with { Strategy = new SummarizingCompaction { Summarizer = new()
            { Provider = new() { Key = "test" }, ModelName = "summary", Override = ClientOverride<IChatClient>.Borrow(new FakeChatClient()) } } };
        Assert.Throws<InvalidOperationException>(() => Admit(new() { HandoffCompaction = spec }));
        Assert.Throws<InvalidOperationException>(() => Admit(new() { Compaction = new() { Automatic = new() { Trigger = new TurnCountCompactionTrigger(5), Compaction = spec } } }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitDisabledPolicyDoesNotFallBackToAgentDefaults(bool disabled)
    {
        var messages = new List<ChatMessage> { new(ChatRole.User, "history") };
        var context = new HPD.Agent.Middleware.AgentContext("agent", "conversation",
            AgentLoopState.InitialSafe(messages, "run", "conversation", "agent"),
            new HPD.Events.Core.EventCoordinator(), null, new HPD.Agent.Thread("s", "t", "agent"), default);
        var middleware = new CompactionMiddleware { Config = new() { Automatic = new()
        {
            Trigger = new TurnCountCompactionTrigger(1), Compaction = SummarySpec with { Strategy = new RemovalCompaction() }
        } }, Engine = new DetectCompactionEngine() };
        var run = disabled ? Admit(new()).InitialRunConfig! : new AgentRunConfig();
        var iteration = context.AsBeforeIteration(0, messages, new(), run);
        if (disabled) await middleware.BeforeIterationAsync(iteration, default);
        else await Assert.ThrowsAsync<CompactionDetected>(() => middleware.BeforeIterationAsync(iteration, default));
    }

    private sealed class CompactionDetected : Exception;
    private sealed class DetectCompactionEngine : IThreadCompactionEngine
    {
        public ValueTask<ThreadCompactionExecutionResult> ExecuteAsync(ThreadCompactionContext context, CompactionSpecification specification,
            string agentName, int iteration, CompactionOrigin origin, CompactionContinuation continuation, CancellationToken cancellationToken = default)
            => throw new CompactionDetected();
        public ValueTask<PreparedThreadCompaction?> PrepareAsync(ThreadCompactionContext context, CompactionSpecification specification, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public ValueTask<ThreadCompactionCommitResult> CommitAsync(ThreadCompactionContext context, PreparedThreadCompaction compaction, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class IdentifiedClient(FakeChatClient inner) : DelegatingChatClient(inner)
    {
        public override object? GetService(Type type, object? key = null)
            => type == typeof(HPD.Agent.Providers.ProviderClientExecutionIdentity)
                ? HPD.Agent.Providers.ProviderClientExecutionIdentity.CreateSafe("test", "test", HPD.Agent.Providers.ProviderClientFamily.Chat, "fake", "test/chat", "test/final")
                : base.GetService(type, key);
    }
}
