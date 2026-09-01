using HPD.Agent;
using HPD.Agent.Serialization;
using Microsoft.Extensions.AI;

namespace HPD.Agent.ActionInvocation.Tests;

public sealed class SubAgentForkPolicyMatrixTests
{
    [Fact]
    public async Task ForkDirectChildrenCopiesMultipleChildrenDeterministically()
    {
        var fixture = await CreateParentAsync("reviewer", "researcher");
        await using var agent = fixture.Agent;

        var fork = await agent.ForkThreadAsync(
            fixture.Source,
            "copy",
            fromMessageId: fixture.ForkPointId,
            new ThreadForkOptions
            {
                OperationId = "copy-multiple",
                SubAgents = new SubAgentForkOptions
                {
                    Policy = SubAgentForkPolicy.ForkDirectChildren,
                    DescendantPolicy = SubAgentForkPolicy.Detach
                }
            });

        var projection = await new SubAgentChildRegistry(fixture.Store)
            .ProjectAsync(new ThreadKey(fork.SessionId, fork.Id));
        Assert.Equal(2, projection.AvailableChildren.Count);
        foreach (var sourceChild in fixture.Children.OrderBy(child => child.LocalId.Value, StringComparer.Ordinal))
        {
            var copied = projection.AvailableChildren[sourceChild.LocalId];
            Assert.NotEqual(sourceChild.ChildThread, copied.ChildThread);
            var descriptor = Assert.IsType<ThreadDescriptor>(
                await fixture.Store.GetThreadAsync(copied.ChildThread));
            Assert.Equal(fork.Id, descriptor.RuntimeChild!.ParentThreadId);
            Assert.Equal(sourceChild.RoleName, descriptor.RuntimeChild.SubAgentName);
            Assert.Equal(sourceChild.ChildAgentId, descriptor.DefaultAgent.AgentId);
        }

        var operation = Assert.IsType<ThreadForkOperationRecord>(
            await new JournalThreadForkOperationStore(fixture.Store, fixture.SourceKey)
                .GetThreadForkOperationAsync("copy-multiple"));
        Assert.Equal(ThreadForkOperationStatus.Committed, operation.Status);
        var directOutcomes = operation.ChildOutcomes
            .Where(outcome => outcome.OwningParent == fixture.SourceKey)
            .OrderBy(outcome => outcome.LocalId, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            fixture.Children.Select(child => child.LocalId.Value).Order(StringComparer.Ordinal),
            directOutcomes.Select(outcome => outcome.LocalId));
        Assert.All(directOutcomes, outcome =>
        {
            Assert.Equal(SubAgentForkPolicy.ForkDirectChildren, outcome.Policy);
            Assert.NotNull(outcome.TargetSeedFingerprint);
            Assert.NotNull(outcome.SourceBoundary);
        });

        var replay = await agent.ForkThreadAsync(
            fixture.Source,
            "copy",
            fromMessageId: fixture.ForkPointId,
            new ThreadForkOptions
            {
                OperationId = "copy-multiple",
                SubAgents = new SubAgentForkOptions
                {
                    Policy = SubAgentForkPolicy.ForkDirectChildren,
                    DescendantPolicy = SubAgentForkPolicy.Detach
                }
            });
        Assert.Equal(fork.Id, replay.Id);
        var replayProjection = await new SubAgentChildRegistry(fixture.Store)
            .ProjectAsync(new ThreadKey(replay.SessionId, replay.Id));
        Assert.Equal(
            projection.AvailableChildren.Values.OrderBy(child => child.LocalId.Value).Select(child => child.ChildThread),
            replayProjection.AvailableChildren.Values.OrderBy(child => child.LocalId.Value).Select(child => child.ChildThread));
    }

    [Theory]
    [InlineData(SubAgentForkPolicy.Detach)]
    [InlineData(SubAgentForkPolicy.Share)]
    public async Task ForkDirectChildrenAppliesOneLevelDescendantPolicy(SubAgentForkPolicy descendantPolicy)
    {
        var fixture = await CreateParentAsync("reviewer");
        await using var agent = fixture.Agent;
        var sourceChild = Assert.Single(fixture.Children);
        var grandchildKey = new ThreadKey(fixture.SourceKey.SessionId, "main/subagent/reviewer-1/subagent/scout-1");
        await CreateSubAgentThreadAsync(
            fixture.Store,
            grandchildKey,
            sourceChild.ChildThread,
            "scout",
            "scout-agent",
            "create-scout",
            "call-scout");
        var grandchild = ChildReference(
            "scout-1", "scout", "scout-agent", grandchildKey, "create-scout", "call-scout");
        await new SubAgentChildRegistry(fixture.Store)
            .RegisterAsync(sourceChild.ChildThread, grandchild);

        var fork = await agent.ForkThreadAsync(
            fixture.Source,
            $"descendant-{descendantPolicy}",
            fromMessageId: fixture.ForkPointId,
            new ThreadForkOptions
            {
                OperationId = $"descendant-{descendantPolicy}",
                SubAgents = new SubAgentForkOptions
                {
                    Policy = SubAgentForkPolicy.ForkDirectChildren,
                    DescendantPolicy = descendantPolicy
                }
            });

        var parentProjection = await new SubAgentChildRegistry(fixture.Store)
            .ProjectAsync(new ThreadKey(fork.SessionId, fork.Id));
        var copiedChild = parentProjection.AvailableChildren[sourceChild.LocalId];
        var copiedChildKey = copiedChild.ChildThread;
        var descendantProjection = await new SubAgentChildRegistry(fixture.Store)
            .ProjectAsync(copiedChildKey);
        var copiedGrandchild = descendantProjection.Entries[grandchild.LocalId];

        if (descendantPolicy == SubAgentForkPolicy.Detach)
        {
            Assert.IsType<SubAgentChildTombstone>(copiedGrandchild);
            Assert.Equal(SubAgentChildAvailability.Detached, copiedGrandchild.Availability);
        }
        else
        {
            var availableGrandchild = Assert.IsType<SubAgentAvailableChild>(copiedGrandchild).Child;
            Assert.Equal(grandchildKey, availableGrandchild.ChildThread);
            Assert.True(await SubAgentControllerAuthority.IsGrantedAsync(
                fixture.Store, grandchildKey, copiedChildKey, grandchild.LocalId));
        }
    }

    [Fact]
    public async Task ShareAuthorizesForkedParentToControlIsolatedSessionChild()
    {
        var fixture = await CreateParentAsync();
        await using var agent = fixture.Agent;
        var isolatedSession = new Session("isolated-child-session");
        await fixture.Store.SaveSessionAsync(isolatedSession);
        var isolatedChildKey = new ThreadKey(isolatedSession.Id, "reviewer-child");
        await CreateSubAgentThreadAsync(
            fixture.Store,
            isolatedChildKey,
            fixture.SourceKey,
            "reviewer",
            "reviewer-agent",
            "create-reviewer",
            "call-reviewer");
        var isolatedChild = ChildReference(
            "reviewer-1",
            "reviewer",
            "reviewer-agent",
            isolatedChildKey,
            "create-reviewer",
            "call-reviewer") with
        {
            CreationContext = SubAgentCreationContext.Isolated
        };
        await new SubAgentChildRegistry(fixture.Store)
            .RegisterAsync(fixture.SourceKey, isolatedChild);
        const string isolatedForkPointId = "isolated-fork-point";
        var isolatedForkPoint = new ChatMessage(ChatRole.User, "fork isolated child")
        {
            MessageId = isolatedForkPointId
        };
        fixture.Source.AddMessage(isolatedForkPoint);
        await fixture.Store.AppendThreadEventsAsync(
            fixture.SourceKey,
            [new ContentAddedEvent(isolatedForkPointId, "user", new TextContent("fork isolated child"))
            {
                SessionId = fixture.SourceKey.SessionId,
                ThreadId = fixture.SourceKey.ThreadId
            }]);

        var fork = await agent.ForkThreadAsync(
            fixture.Source,
            "share-isolated",
            fromMessageId: isolatedForkPointId,
            new ThreadForkOptions
            {
                OperationId = "share-isolated",
                SubAgents = new SubAgentForkOptions { Policy = SubAgentForkPolicy.Share }
            });

        var forkKey = new ThreadKey(fork.SessionId, fork.Id);
        var projection = await new SubAgentChildRegistry(fixture.Store).ProjectAsync(forkKey);
        var shared = projection.AvailableChildren[isolatedChild.LocalId];
        Assert.Equal(isolatedChildKey, shared.ChildThread);
        Assert.True(await SubAgentControllerAuthority.IsGrantedAsync(
            fixture.Store, isolatedChildKey, forkKey, isolatedChild.LocalId));
        Assert.NotNull(await fixture.Store.GetThreadAsync(isolatedChildKey));
    }

    private static async Task<ParentFixture> CreateParentAsync(params string[] roles)
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var agent = new Agent(
            new AgentConfig
            {
                Name = "test-agent",
                SessionStore = store,
                EventComposition = CoreAgentEventComposition.Instance
            },
            baseClient: null,
            mergedOptions: null);
        var session = new Session("subagent-fork-matrix");
        await store.SaveSessionAsync(session);
        var source = session.CreateThread("test-agent", "main");
        await store.SaveInitialThreadAsync(session.Id, source);
        source.Session = session;
        var sourceKey = new ThreadKey(session.Id, source.Id);
        var children = new List<SubAgentChildReference>();
        for (var index = 0; index < roles.Length; index++)
        {
            var role = roles[index];
            var ordinal = index + 1;
            var localId = $"{role}-{ordinal}";
            var childKey = new ThreadKey(session.Id, $"main/subagent/{localId}");
            await CreateSubAgentThreadAsync(
                store, childKey, sourceKey, role, $"{role}-agent", $"create-{role}", $"call-{role}");
            var child = ChildReference(
                localId, role, $"{role}-agent", childKey, $"create-{role}", $"call-{role}");
            await new SubAgentChildRegistry(store).RegisterAsync(sourceKey, child);
            children.Add(child);
        }

        const string forkPointId = "fork-point";
        var forkPoint = new ChatMessage(ChatRole.User, "fork here") { MessageId = forkPointId };
        source.AddMessage(forkPoint);
        await store.AppendThreadEventsAsync(
            sourceKey,
            [new ContentAddedEvent(forkPointId, "user", new TextContent("fork here"))
            {
                SessionId = sourceKey.SessionId,
                ThreadId = sourceKey.ThreadId
            }]);
        return new ParentFixture(agent, store, source, sourceKey, forkPointId, children);
    }

    private static async Task CreateSubAgentThreadAsync(
        ISessionStore store,
        ThreadKey child,
        ThreadKey parent,
        string role,
        string agentId,
        string invocationId,
        string toolCallId) =>
        _ = await store.AppendThreadEventsAsync(
            child,
            [new ThreadCreatedEvent(
                agentId, null, null, null, null, DateTime.UtcNow,
                ThreadKind.SubAgent, ThreadVisibility.Hidden,
                parent.SessionId, parent.ThreadId, role,
                InvocationId: invocationId,
                ParentToolCallId: toolCallId,
                ContextPolicy: "Fresh")
            {
                SessionId = child.SessionId,
                ThreadId = child.ThreadId
            }],
            new ThreadAppendCondition(ThreadJournalCursor.Start(1)));

    private static SubAgentChildReference ChildReference(
        string localId,
        string role,
        string agentId,
        ThreadKey child,
        string invocationId,
        string toolCallId) => new()
        {
            LocalId = new SubAgentLocalId(localId),
            RoleName = role,
            CapabilityId = CapabilityId.Create($"test:{role}"),
            ChildAgentId = agentId,
            ChildThread = child,
            CreationContext = SubAgentCreationContext.Fresh,
            CreationInvocationId = invocationId,
            ParentToolCallId = toolCallId,
            ExecutionPolicy = SubAgentRunConfig.Inherit().CompilePolicy(),
            CreatedAt = DateTimeOffset.UtcNow
        };

    private sealed record ParentFixture(
        Agent Agent,
        InMemorySessionStore Store,
        Thread Source,
        ThreadKey SourceKey,
        string ForkPointId,
        IReadOnlyList<SubAgentChildReference> Children);
}
