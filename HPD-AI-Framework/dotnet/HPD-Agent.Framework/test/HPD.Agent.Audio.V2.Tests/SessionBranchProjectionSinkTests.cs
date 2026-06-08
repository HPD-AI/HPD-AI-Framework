using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.AgentIntegration.Branch;
using HPD.Agent.Audio.Ledger;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.V2.Tests;

public sealed class SessionBranchProjectionSinkTests
{
    [Fact]
    public async Task ProjectAsync_AppendsCommittedInputMediaTranscript_ToRealSessionBranch()
    {
        var store = new InMemorySessionStore();
        var sink = new SessionBranchProjectionSink(store);
        var content = TestInputContent.Audio("branch-real.wav", "audio/wav", sizeBytes: 8192);
        var branch = new BranchRef("session-real", "main");

        var projected = await sink.ProjectAsync(branch, new BranchProjectionRecord
        {
            TurnId = new AudioTurnId("turn-real"),
            Text = "transcript from real session seam",
            InputContentId = content.Id
        });

        var loaded = await store.LoadBranchAsync(branch.SessionId, branch.BranchId);
        var document = await store.LoadBranchDocumentAsync(branch.SessionId, branch.BranchId);

        Assert.NotNull(loaded);
        var message = Assert.Single(loaded.Messages);
        Assert.Equal(ChatRole.User, message.Role);
        Assert.Equal("audio-turn-turn-real", message.MessageId);
        Assert.Equal("transcript from real session seam", message.Text);

        Assert.NotNull(document);
        Assert.Equal(projected.EventId, document.Events.Last().EventId);
        Assert.Equal(document.Events.Last().SequenceNumber, projected.SequenceNumber);
        Assert.True(projected.SequenceNumber > 0);
        Assert.DoesNotContain(document.Events.OfType<ContentAddedEvent>(), e => e.Content is AudioContent or DataContent);

        var contentEvent = Assert.Single(document.Events.OfType<ContentAddedEvent>());
        var text = Assert.IsType<TextContent>(contentEvent.Content);
        Assert.Equal("transcript from real session seam", text.Text);
    }

    [Fact]
    public async Task BranchProjection_IdempotentForSameProjectionEpoch()
    {
        var store = new InMemorySessionStore();
        var sink = new SessionBranchProjectionSink(store);
        var content = TestInputContent.Audio("branch-idempotent.wav", "audio/wav", sizeBytes: 8192);
        var branch = new BranchRef("session-idempotent", "main");
        var record = new BranchProjectionRecord
        {
            TurnId = new AudioTurnId("turn-idempotent"),
            Text = "transcript projected once",
            InputContentId = content.Id
        };

        var first = await sink.ProjectAsync(branch, record);
        var second = await sink.ProjectAsync(branch, record);

        var loaded = await store.LoadBranchAsync(branch.SessionId, branch.BranchId);
        var document = await store.LoadBranchDocumentAsync(branch.SessionId, branch.BranchId);

        Assert.Equal(first, second);
        Assert.NotNull(loaded);
        var message = Assert.Single(loaded.Messages);
        Assert.Equal("audio-turn-turn-idempotent", message.MessageId);
        Assert.Equal("transcript projected once", message.Text);

        Assert.NotNull(document);
        Assert.Single(document.Events.OfType<MessageStartedEvent>());
        Assert.Single(document.Events.OfType<ContentAddedEvent>());
        Assert.Single(document.Events.OfType<MessageCompletedEvent>());
    }

    [Fact]
    public async Task ProjectAsync_AppendsAssistantOutput_WithAssistantRole()
    {
        var store = new InMemorySessionStore();
        var sink = new SessionBranchProjectionSink(store);
        var branch = new BranchRef("session-assistant-output", "main");

        await sink.ProjectAsync(branch, new BranchProjectionRecord
        {
            TurnId = new AudioTurnId("turn-assistant-output"),
            Text = "assistant output committed after playout",
            Kind = BranchProjectionKind.AssistantOutput,
            Role = BranchProjectionRole.Assistant,
            OutputFlowId = new OutputFlowId("output-flow-real"),
            ResponseId = new ResponseId("response-real")
        });

        var loaded = await store.LoadBranchAsync(branch.SessionId, branch.BranchId);

        Assert.NotNull(loaded);
        var message = Assert.Single(loaded.Messages);
        Assert.Equal(ChatRole.Assistant, message.Role);
        Assert.Equal("audio-output-output-flow-real", message.MessageId);
        Assert.Equal("assistant output committed after playout", message.Text);
    }
}
