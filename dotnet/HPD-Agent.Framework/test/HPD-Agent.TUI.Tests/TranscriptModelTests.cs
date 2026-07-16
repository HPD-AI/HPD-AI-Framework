using FluentAssertions;
using HPD.Agent.TUI.Models;
using HPD.TUI.Components;

namespace HPD.Agent.TUI.Tests;

public sealed class TranscriptModelTests
{
    [Fact]
    public void UpsertLive_ReplacesExistingKeyedLiveEntry()
    {
        var model = new TranscriptModel();
        model.UpsertLive(Row("1", "tool:1", "running"));

        model.UpsertLive(Row("2", "tool:1", "still running"));

        var rows = model.Snapshot().Entries;
        rows.Should().ContainSingle();
        rows[0].Id.Should().Be("2");
        rows[0].State.Should().Be(TranscriptEntryState.Live);
        rows[0].Cell.Should().BeOfType<NoticeCell>()
            .Which.Title.Should().Be("still running");
    }

    [Fact]
    public void FinalizeLive_ReplacesLiveEntryWithFinalEntry()
    {
        var model = new TranscriptModel();
        model.UpsertLive(Row("1", "tool:1", "running"));

        model.FinalizeLive("tool:1", Row("2", "tool:1", "completed"));

        var rows = model.Snapshot().Entries;
        rows.Should().ContainSingle();
        rows[0].Id.Should().Be("2");
        rows[0].State.Should().Be(TranscriptEntryState.Final);
        rows[0].CommitPolicy.Should().Be(TranscriptCommitPolicy.Immediate);
    }

    [Fact]
    public void ClearAll_IncrementsHistoryEpoch()
    {
        var model = new TranscriptModel();
        model.AddFinal(Row("1", null, "first"));
        var before = model.HistoryEpoch;

        model.ClearAll();

        model.Snapshot().Entries.Should().BeEmpty();
        model.HistoryEpoch.Should().Be(before + 1);
    }

    [Fact]
    public void ReplaceHistoryWith_AtomicallyReplacesArbitraryFinalAndLiveEntries()
    {
        var model = new TranscriptModel();
        model.AddFinal(Row("1", null, "message"));
        model.UpsertLive(Row("2", "custom:live", "custom extension"));
        var beforeEpoch = model.HistoryEpoch;
        var beforeVersion = model.Version;

        model.ReplaceHistoryWith(Row("checkpoint", "compaction:1", "compacted"));

        var replacement = model.Snapshot().Entries.Should().ContainSingle().Subject;
        replacement.Id.Should().Be("checkpoint");
        replacement.EntryKey.Should().Be("compaction:1");
        replacement.State.Should().Be(TranscriptEntryState.Final);
        model.HistoryEpoch.Should().Be(beforeEpoch + 1);
        model.Version.Should().Be(beforeVersion + 1);
    }

    [Fact]
    public void BeginUpdate_PublishesOneVersionForManyMutations()
    {
        var model = new TranscriptModel();

        using (model.BeginUpdate())
        {
            model.AddFinal(Row("1", null, "first"));
            model.AddFinal(Row("2", null, "second"));
            model.UpsertLive(Row("3", "live:3", "third"));

            model.Version.Should().Be(0);
        }

        model.Count.Should().Be(3);
        model.Version.Should().Be(1);
    }

    [Fact]
    public void TranscriptEntry_FromEvent_CarriesAgentMetadata()
    {
        var evt = new TextDeltaEvent("hello", "message")
        {
            Metadata = new AgentMetadata
            {
                AgentId = "root/coder",
                AgentName = "coder",
                ParentAgentId = "root",
                AgentChain = ["root", "coder"],
                Depth = 1
            }
        };

        var row = TranscriptEntry.FromEvent(evt, new AssistantMessageCell("assistant", new Text("hello")));

        row.Metadata.AgentId.Should().Be("root/coder");
        row.Metadata.AgentName.Should().Be("coder");
        row.Metadata.ParentAgentId.Should().Be("root");
        row.Metadata.AgentChainValue.Should().Equal("root", "coder");
        row.Metadata.AgentDepth.Should().Be(1);
    }

    private static TranscriptEntry Row(string id, string? rowKey, string label)
        => new(
            id,
            rowKey,
            new NoticeCell(label, new Text(label)),
            new TranscriptEntryMetadata());
}
