using FluentAssertions;
using HPD.Agent.TUI.Models;
using HPD.TUI.Components;

namespace HPD.Agent.TUI.Tests;

public sealed class TranscriptModelTests
{
    [Fact]
    public void Update_ReplacesExistingKeyedRow()
    {
        var model = new TranscriptModel();
        model.Append(Row("1", "tool:1", "running"));

        model.Update(Row("2", "tool:1", "completed"));

        var rows = new List<TranscriptEntry>();
        model.CopyTo(rows);
        rows.Should().ContainSingle();
        rows[0].Id.Should().Be("2");
        rows[0].Cell.Should().BeOfType<NoticeCell>()
            .Which.Title.Should().Be("completed");
    }

    [Fact]
    public void Append_WhenScrolledUp_PreservesCurrentScrollbackPosition()
    {
        var model = new TranscriptModel();
        model.Append(Row("1", null, "first"));
        model.Append(Row("2", null, "second"));
        model.ScrollUp(1);

        model.Append(Row("3", null, "third"));

        model.ViewOffsetRowsFromBottom.Should().Be(2);
    }

    [Fact]
    public void ScrollToBottom_ClearsScrollbackOffset()
    {
        var model = new TranscriptModel();
        model.Append(Row("1", null, "first"));
        model.Append(Row("2", null, "second"));
        model.ScrollUp(10);

        model.ScrollToBottom();

        model.ViewOffsetRowsFromBottom.Should().Be(0);
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
