using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Interaction;
using HPD.Agent.Audio.Interruptions;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Audio.Runtime;
using HPD.Agent.Audio.Runtime.Thread;
using HPD.Agent.Audio.Runtime.Ledger;
using HPD.Agent.Audio.Runtime.Output;
using HPD.Agent.Audio.Runtime.Trace;
using HPD.Agent.Audio.Trace;

namespace HPD.Agent.Audio.V2.Tests;

public sealed class FakeProviderOutputTruthTests
{
    [Fact]
    public async Task InterruptionSmoke_LocalOnlyRepair_ExcludesUnplayedTail()
    {
        var ids = new RuntimeIdFactory();
        var clock = new RuntimeClock();
        var sessionId = new AudioSessionId("session-interruption-smoke");
        var turnId = new AudioTurnId("turn-interruption-smoke");
        var threadRef = new ThreadRef("audio-test-agent", "session-interruption-smoke", "main");
        var ledger = new InMemoryRealtimeConversationLedger();
        var trace = new InMemoryRealtimeAudioTraceStore();
        var thread = new InMemoryThreadProjectionSink();
        IOutputProjectionSinkV2 outputFlow = new InMemoryOutputProjectionSinkV2(ids.NextOutputFlowId());
        var responseId = ids.NextResponseId();
        const string generatedText = "hello there, this tail was never heard";
        const string playedPrefix = "hello there";
        var correlation = new AudioCorrelation
        {
            SessionId = sessionId,
            TurnId = turnId,
            OutputFlowId = outputFlow.Id
        };

        await outputFlow.AppendTextAsync(responseId, generatedText, isFinal: true);
        var segmentId = ids.NextOutputSegmentId();
        await outputFlow.MarkQueuedAsync(CreatePlaybackRequest(outputFlow.Id, responseId, segmentId, generatedText.Length));
        Assert.Equal(OutputFlowState.Queued, outputFlow.Snapshot.State);
        await outputFlow.MarkPlaybackStartedAsync(CreatePlaybackStarted(outputFlow.Id, responseId, segmentId));

        var draftSnapshot = outputFlow.Snapshot;
        Assert.Equal(OutputFlowState.Playing, draftSnapshot.State);
        Assert.Equal(generatedText, draftSnapshot.Text);

        var draftRecord = new AssistantOutputLedgerRecord
        {
            Id = ids.NextLedgerRecordId(),
            SessionId = sessionId,
            Family = LedgerRecordFamily.AssistantOutput,
            RecordedAt = clock.Tick(),
            OutputFlowId = outputFlow.Id,
            ResponseId = responseId,
            Text = draftSnapshot.Text,
            Disposition = OutputDisposition.Draft,
            Correlation = correlation
        };
        await ledger.AppendAsync(draftRecord);
        var draftTrace = new AudioAssistantOutputTraceRecord
        {
            Id = ids.NextTraceRecordId(),
            SessionId = sessionId,
            Family = RealtimeAudioTraceRecordFamily.AssistantOutput,
            RecordedAt = clock.Tick(),
            OutputFlowId = outputFlow.Id,
            ResponseId = responseId,
            Text = draftSnapshot.Text,
            Disposition = OutputDisposition.Draft,
            Correlation = correlation
        };
        await trace.AppendAsync(draftTrace);

        Assert.Empty(thread.ProjectedTurns);

        var interruptionEvidence = new InterruptionCandidate
        {
            SessionId = sessionId,
            ObservedAt = clock.Tick(),
            Reason = "fake-user-barge-in"
        };
        var boundary = new OutputPlaybackBoundary
        {
            OutputFlowId = outputFlow.Id,
            ResponseId = responseId,
            PlayedTextLength = playedPrefix.Length,
            ObservedAt = interruptionEvidence.ObservedAt
        };
        var commit = await outputFlow.CommitInterruptedAsync(boundary);

        Assert.Equal(OutputCommitDisposition.Interrupted, commit.Disposition);
        Assert.Equal(playedPrefix, commit.Text);
        Assert.Equal(OutputFlowState.Interrupted, outputFlow.Snapshot.State);

        var repair = new InterruptionRepairRecord
        {
            OutputFlowId = outputFlow.Id,
            ResponseId = responseId,
            OriginalGeneratedText = generatedText,
            CommittedText = commit.Text,
            ExcludedText = generatedText[playedPrefix.Length..],
            PlaybackBoundary = boundary,
            RepairQuality = InterruptionRepairQuality.LocalOnly,
            ProviderRepairStatus = ProviderRepairStatus.Unsupported
        };
        var repairLedgerRecord = new InterruptionRepairLedgerRecord
        {
            Id = ids.NextLedgerRecordId(),
            SessionId = sessionId,
            Family = LedgerRecordFamily.InterruptionRepair,
            RecordedAt = clock.Tick(),
            Repair = repair,
            Correlation = correlation
        };
        await ledger.AppendAsync(repairLedgerRecord);
        var repairTrace = new AudioInterruptionRepairTraceRecord
        {
            Id = ids.NextTraceRecordId(),
            SessionId = sessionId,
            Family = RealtimeAudioTraceRecordFamily.InterruptionRepair,
            RecordedAt = clock.Tick(),
            Repair = repair,
            Correlation = correlation
        };
        await trace.AppendAsync(repairTrace);

        var interruptedOutputRecord = new AssistantOutputLedgerRecord
        {
            Id = ids.NextLedgerRecordId(),
            SessionId = sessionId,
            Family = LedgerRecordFamily.AssistantOutput,
            RecordedAt = clock.Tick(),
            OutputFlowId = commit.OutputFlowId,
            ResponseId = commit.ResponseId,
            Text = commit.Text,
            Disposition = OutputDisposition.Interrupted,
            Correlation = correlation
        };
        await ledger.AppendAsync(interruptedOutputRecord);
        var interruptedOutputTrace = new AudioAssistantOutputTraceRecord
        {
            Id = ids.NextTraceRecordId(),
            SessionId = sessionId,
            Family = RealtimeAudioTraceRecordFamily.AssistantOutput,
            RecordedAt = clock.Tick(),
            OutputFlowId = commit.OutputFlowId,
            ResponseId = commit.ResponseId,
            Text = commit.Text,
            Disposition = OutputDisposition.Interrupted,
            Correlation = correlation
        };
        await trace.AppendAsync(interruptedOutputTrace);

        var projection = new ThreadProjectionRecord
        {
            TurnId = turnId,
            Text = commit.Text,
            Kind = ThreadProjectionKind.AssistantOutput,
            Role = ThreadProjectionRole.Assistant,
            OutputFlowId = commit.OutputFlowId,
            ResponseId = commit.ResponseId
        };
        var projectedEvent = await thread.ProjectAsync(threadRef, projection);
        var projectionRecord = new ThreadProjectionLedgerRecord
        {
            Id = ids.NextLedgerRecordId(),
            SessionId = sessionId,
            Family = LedgerRecordFamily.ThreadProjection,
            RecordedAt = clock.Tick(),
            ProjectionId = ids.NextThreadProjectionId(),
            Thread = threadRef,
            Projection = projection,
            ProjectedEvent = projectedEvent,
            Correlation = correlation
        };
        await ledger.AppendAsync(projectionRecord);
        var threadTrace = new AudioThreadProjectionTraceRecord
        {
            Id = ids.NextTraceRecordId(),
            SessionId = sessionId,
            Family = RealtimeAudioTraceRecordFamily.ThreadProjection,
            RecordedAt = clock.Tick(),
            ProjectionId = projectionRecord.ProjectionId,
            ProjectedEvent = projectedEvent,
            Correlation = correlation
        };
        await trace.AppendAsync(threadTrace);

        var ledgerRecords = ledger.ToArray();
        var repairRecord = Assert.Single(ledgerRecords.OfType<InterruptionRepairLedgerRecord>());
        var projectedTurn = Assert.Single(thread.ProjectedTurns);

        Assert.True(IndexOf(ledgerRecords, draftRecord) < IndexOf(ledgerRecords, repairLedgerRecord));
        Assert.True(IndexOf(ledgerRecords, repairLedgerRecord) < IndexOf(ledgerRecords, projectionRecord));
        Assert.True(IndexOf(ledgerRecords, interruptedOutputRecord) < IndexOf(ledgerRecords, projectionRecord));
        Assert.Equal(InterruptionRepairQuality.LocalOnly, repairRecord.Repair.RepairQuality);
        Assert.Equal(ProviderRepairStatus.Unsupported, repairRecord.Repair.ProviderRepairStatus);
        Assert.Equal(generatedText, repairRecord.Repair.OriginalGeneratedText);
        Assert.Equal(playedPrefix, repairRecord.Repair.CommittedText);
        Assert.Equal(", this tail was never heard", repairRecord.Repair.ExcludedText);
        Assert.Equal(playedPrefix.Length, repairRecord.Repair.PlaybackBoundary.PlayedTextLength);

        Assert.Equal(playedPrefix, projectedTurn.Record.Text);
        Assert.DoesNotContain(repairRecord.Repair.ExcludedText, projectedTurn.Record.Text);
        Assert.DoesNotContain("tail was never heard", projectedTurn.Record.Text);
        Assert.Equal(outputFlow.Id, projectedTurn.Record.OutputFlowId);
        Assert.Equal(responseId, projectedTurn.Record.ResponseId);

        var traceRecords = trace.ToArray();
        var repairTraceRecord = Assert.Single(traceRecords.OfType<AudioInterruptionRepairTraceRecord>());
        Assert.True(IndexOf(traceRecords, draftTrace) < IndexOf(traceRecords, repairTrace));
        Assert.True(IndexOf(traceRecords, repairTrace) < IndexOf(traceRecords, threadTrace));
        Assert.True(IndexOf(traceRecords, interruptedOutputTrace) < IndexOf(traceRecords, threadTrace));
        Assert.Equal(InterruptionRepairQuality.LocalOnly, repairTraceRecord.Repair.RepairQuality);
        Assert.Equal(ProviderRepairStatus.Unsupported, repairTraceRecord.Repair.ProviderRepairStatus);
    }

    [Fact]
    public async Task FakeProviderOutput_CommitsOnlyAfterPlayedDisposition()
    {
        var ids = new RuntimeIdFactory();
        var clock = new RuntimeClock();
        var sessionId = new AudioSessionId("session-output-truth");
        var turnId = new AudioTurnId("turn-output-truth");
        var threadRef = new ThreadRef("audio-test-agent", "session-output-truth", "main");
        var ledger = new InMemoryRealtimeConversationLedger();
        var trace = new InMemoryRealtimeAudioTraceStore();
        var thread = new InMemoryThreadProjectionSink();
        IOutputProjectionSinkV2 outputFlow = new InMemoryOutputProjectionSinkV2(ids.NextOutputFlowId());
        var responseId = ids.NextResponseId();
        var interactionSessionId = ids.NextInteractionSessionId();
        var correlation = new AudioCorrelation
        {
            SessionId = sessionId,
            TurnId = turnId,
            OutputFlowId = outputFlow.Id
        };

        var providerUpdateTrace = new AudioInteractionUpdateTraceRecord
        {
            Id = ids.NextTraceRecordId(),
            SessionId = sessionId,
            Family = RealtimeAudioTraceRecordFamily.InteractionUpdate,
            RecordedAt = clock.Tick(),
            Update = new OutputTextUpdate
            {
                SessionId = interactionSessionId,
                ObservedAt = clock.UtcNow,
                RouteEpochId = new ProviderRouteEpochId("route-epoch-output-truth"),
                ResponseId = responseId,
                Delta = "draft assistant text",
                IsFinal = true,
                Correlation = correlation
            },
            Correlation = correlation
        };
        await trace.AppendAsync(providerUpdateTrace);

        const string assistantText = "draft assistant text";
        await outputFlow.AppendTextAsync(responseId, assistantText, isFinal: true);
        var segmentId = ids.NextOutputSegmentId();
        await outputFlow.MarkQueuedAsync(CreatePlaybackRequest(outputFlow.Id, responseId, segmentId, assistantText.Length));
        Assert.Equal(OutputFlowState.Queued, outputFlow.Snapshot.State);
        await outputFlow.MarkPlaybackStartedAsync(CreatePlaybackStarted(outputFlow.Id, responseId, segmentId));

        var draftSnapshot = outputFlow.Snapshot;
        Assert.Equal(OutputFlowState.Playing, draftSnapshot.State);
        Assert.Equal(assistantText, draftSnapshot.Text);

        var draftRecord = new AssistantOutputLedgerRecord
        {
            Id = ids.NextLedgerRecordId(),
            SessionId = sessionId,
            Family = LedgerRecordFamily.AssistantOutput,
            RecordedAt = clock.Tick(),
            OutputFlowId = outputFlow.Id,
            ResponseId = responseId,
            Text = draftSnapshot.Text,
            Disposition = OutputDisposition.Draft,
            Correlation = correlation
        };
        await ledger.AppendAsync(draftRecord);
        await trace.AppendAsync(new AudioAssistantOutputTraceRecord
        {
            Id = ids.NextTraceRecordId(),
            SessionId = sessionId,
            Family = RealtimeAudioTraceRecordFamily.AssistantOutput,
            RecordedAt = clock.Tick(),
            OutputFlowId = outputFlow.Id,
            ResponseId = responseId,
            Text = draftSnapshot.Text,
            Disposition = OutputDisposition.Draft,
            Correlation = correlation
        });

        Assert.Empty(thread.ProjectedTurns);
        Assert.DoesNotContain(ledger.ToArray().OfType<ThreadProjectionLedgerRecord>(), r =>
            r.Projection.OutputFlowId == outputFlow.Id);

        var commit = await outputFlow.CompletePlayedAsync(new OutputPlaybackCursor
        {
            OutputFlowId = outputFlow.Id,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0,
            PlayedDuration = TimeSpan.FromSeconds(1),
            PlayedTextLength = assistantText.Length,
            Precision = OutputAlignmentPrecision.LocalOnly,
            ObservedAt = clock.Tick()
        });
        Assert.Equal(outputFlow.Id, commit.OutputFlowId);
        Assert.Equal(responseId, commit.ResponseId);
        Assert.Equal(OutputCommitDisposition.PlayedComplete, commit.Disposition);

        var completedRecord = new AssistantOutputLedgerRecord
        {
            Id = ids.NextLedgerRecordId(),
            SessionId = sessionId,
            Family = LedgerRecordFamily.AssistantOutput,
            RecordedAt = clock.Tick(),
            OutputFlowId = commit.OutputFlowId,
            ResponseId = commit.ResponseId,
            Text = commit.Text,
            Disposition = OutputDisposition.PlayedComplete,
            Correlation = correlation
        };
        await ledger.AppendAsync(completedRecord);
        await trace.AppendAsync(new AudioAssistantOutputTraceRecord
        {
            Id = ids.NextTraceRecordId(),
            SessionId = sessionId,
            Family = RealtimeAudioTraceRecordFamily.AssistantOutput,
            RecordedAt = clock.Tick(),
            OutputFlowId = commit.OutputFlowId,
            ResponseId = commit.ResponseId,
            Text = commit.Text,
            Disposition = OutputDisposition.PlayedComplete,
            Correlation = correlation
        });

        var projection = new ThreadProjectionRecord
        {
            TurnId = turnId,
            Text = commit.Text,
            Kind = ThreadProjectionKind.AssistantOutput,
            Role = ThreadProjectionRole.Assistant,
            OutputFlowId = commit.OutputFlowId,
            ResponseId = commit.ResponseId
        };
        var projectedEvent = await thread.ProjectAsync(threadRef, projection);
        var projectionRecord = new ThreadProjectionLedgerRecord
        {
            Id = ids.NextLedgerRecordId(),
            SessionId = sessionId,
            Family = LedgerRecordFamily.ThreadProjection,
            RecordedAt = clock.Tick(),
            ProjectionId = ids.NextThreadProjectionId(),
            Thread = threadRef,
            Projection = projection,
            ProjectedEvent = projectedEvent,
            Correlation = correlation
        };
        await ledger.AppendAsync(projectionRecord);
        await trace.AppendAsync(new AudioThreadProjectionTraceRecord
        {
            Id = ids.NextTraceRecordId(),
            SessionId = sessionId,
            Family = RealtimeAudioTraceRecordFamily.ThreadProjection,
            RecordedAt = clock.Tick(),
            ProjectionId = projectionRecord.ProjectionId,
            ProjectedEvent = projectedEvent,
            Correlation = correlation
        });

        var ledgerRecords = ledger.ToArray();
        var assistantRecords = ledgerRecords.OfType<AssistantOutputLedgerRecord>().ToArray();
        var threadProjectionRecord = Assert.Single(ledgerRecords.OfType<ThreadProjectionLedgerRecord>());
        var projectedTurn = Assert.Single(thread.ProjectedTurns);

        Assert.Collection(assistantRecords,
            draft =>
            {
                Assert.Equal(OutputDisposition.Draft, draft.Disposition);
                Assert.Equal(outputFlow.Id, draft.OutputFlowId);
            },
            completed =>
            {
                Assert.Equal(OutputDisposition.PlayedComplete, completed.Disposition);
                Assert.Equal(outputFlow.Id, completed.OutputFlowId);
            });
        Assert.True(IndexOf(ledgerRecords, draftRecord) < IndexOf(ledgerRecords, completedRecord));
        Assert.True(IndexOf(ledgerRecords, completedRecord) < IndexOf(ledgerRecords, threadProjectionRecord));
        Assert.Equal(outputFlow.Id, threadProjectionRecord.Projection.OutputFlowId);
        Assert.Equal(responseId, threadProjectionRecord.Projection.ResponseId);
        Assert.Equal(ThreadProjectionKind.AssistantOutput, threadProjectionRecord.Projection.Kind);
        Assert.Equal(ThreadProjectionRole.Assistant, threadProjectionRecord.Projection.Role);
        Assert.Equal("draft assistant text", projectedTurn.Record.Text);
        Assert.Equal(outputFlow.Id, projectedTurn.Record.OutputFlowId);
        Assert.Equal(ThreadProjectionRole.Assistant, projectedTurn.Record.Role);

        var traceRecords = trace.ToArray();
        var outputTraceRecords = traceRecords.OfType<AudioAssistantOutputTraceRecord>().ToArray();
        Assert.Collection(outputTraceRecords,
            draft => Assert.Equal(OutputDisposition.Draft, draft.Disposition),
            completed => Assert.Equal(OutputDisposition.PlayedComplete, completed.Disposition));
        Assert.True(IndexOf(traceRecords, providerUpdateTrace) < IndexOf(traceRecords, outputTraceRecords[1]));
        Assert.True(IndexOf(traceRecords, outputTraceRecords[1]) <
            IndexOf(traceRecords, traceRecords.OfType<AudioThreadProjectionTraceRecord>().Single()));
    }

    private static int IndexOf<T>(IReadOnlyList<T> records, T record)
        where T : notnull
    {
        return records.ToList().IndexOf(record);
    }

    private static OutputPlaybackRequest CreatePlaybackRequest(
        OutputFlowId outputFlowId,
        ResponseId responseId,
        OutputSegmentId segmentId,
        int sourceTextLength)
    {
        return new OutputPlaybackRequest
        {
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0,
            EstimatedDuration = TimeSpan.FromSeconds(1),
            SourceTextStart = 0,
            SourceTextLength = sourceTextLength,
            MediaType = "audio/mpeg"
        };
    }

    private static OutputPlaybackStartedEvent CreatePlaybackStarted(
        OutputFlowId outputFlowId,
        ResponseId responseId,
        OutputSegmentId segmentId)
    {
        return new OutputPlaybackStartedEvent
        {
            OutputFlowId = outputFlowId,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0,
            ObservedAt = DateTimeOffset.UtcNow
        };
    }
}
