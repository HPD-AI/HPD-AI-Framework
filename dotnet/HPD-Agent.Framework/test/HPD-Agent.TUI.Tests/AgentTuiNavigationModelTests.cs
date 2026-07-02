using FluentAssertions;
using HPD.Agent.TUI.Models;

namespace HPD.Agent.TUI.Tests;

public sealed class AgentTuiNavigationModelTests
{
    [Fact]
    public void Navigation_StartsAtTranscript()
    {
        var navigation = new AgentTuiNavigationModel();

        navigation.IsTranscriptActive.Should().BeTrue();
        navigation.ActivePageId.Should().BeNull();
        navigation.CanGoBack.Should().BeFalse();
        navigation.BackStack.Should().BeEmpty();
    }

    [Fact]
    public void GoToPage_ActivatesPageAndRecordsTranscriptRoot()
    {
        var navigation = new AgentTuiNavigationModel();

        navigation.GoToPage("sessions");

        navigation.ActivePageId.Should().Be("sessions");
        navigation.IsTranscriptActive.Should().BeFalse();
        navigation.CanGoBack.Should().BeTrue();
        navigation.BackStack.Should().ContainSingle()
            .Which.Kind.Should().Be(AgentTuiNavigationFrameKind.Transcript);
    }

    [Fact]
    public void Back_FromFirstPage_ReturnsToTranscript()
    {
        var navigation = new AgentTuiNavigationModel();
        navigation.GoToPage("sessions");

        navigation.Back().Should().BeTrue();

        navigation.IsTranscriptActive.Should().BeTrue();
        navigation.ActivePageId.Should().BeNull();
        navigation.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public void Back_FromNestedPage_ReturnsToPreviousPageThenTranscript()
    {
        var navigation = new AgentTuiNavigationModel();
        navigation.GoToPage("sessions");
        navigation.GoToPage("threads");

        navigation.Back().Should().BeTrue();
        navigation.ActivePageId.Should().Be("sessions");

        navigation.Back().Should().BeTrue();
        navigation.IsTranscriptActive.Should().BeTrue();
        navigation.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public void Back_PopsDialogsBeforePages()
    {
        var closed = new List<string>();
        var navigation = new AgentTuiNavigationModel();
        navigation.GoToPage("sessions");
        navigation.PushDialog("switch", () => closed.Add("switch"));
        navigation.PushDialog("confirm", () => closed.Add("confirm"));
        navigation.PushDialog("rename", () => closed.Add("rename"));

        navigation.ActiveFrame.Kind.Should().Be(AgentTuiNavigationFrameKind.Dialog);
        navigation.ActivePageId.Should().Be("sessions");

        navigation.Back().Should().BeTrue();
        closed.Should().Equal("rename");
        navigation.ActiveFrame.Title.Should().Be("confirm");
        navigation.ActivePageId.Should().Be("sessions");

        navigation.Back().Should().BeTrue();
        closed.Should().Equal("rename", "confirm");
        navigation.ActiveFrame.Title.Should().Be("switch");
        navigation.ActivePageId.Should().Be("sessions");

        navigation.Back().Should().BeTrue();
        closed.Should().Equal("rename", "confirm", "switch");
        navigation.ActiveFrame.Kind.Should().Be(AgentTuiNavigationFrameKind.Page);
        navigation.ActivePageId.Should().Be("sessions");

        navigation.Back().Should().BeTrue();
        navigation.ActiveFrame.Kind.Should().Be(AgentTuiNavigationFrameKind.Transcript);
        navigation.ActivePageId.Should().BeNull();
        navigation.Back().Should().BeFalse();
    }

    [Fact]
    public void RemoveDialog_RemovesExternallyClosedDialogFrameWithoutInvokingClose()
    {
        var closed = false;
        var navigation = new AgentTuiNavigationModel();
        navigation.GoToPage("sessions");
        var frameId = navigation.PushDialog("switch", () => closed = true);

        navigation.RemoveDialog(frameId).Should().BeTrue();

        closed.Should().BeFalse();
        navigation.ActiveFrame.Kind.Should().Be(AgentTuiNavigationFrameKind.Page);
        navigation.Back().Should().BeTrue();
        navigation.IsTranscriptActive.Should().BeTrue();
    }

    [Fact]
    public void Clear_ReturnsToTranscriptAndClearsHistory()
    {
        var navigation = new AgentTuiNavigationModel();
        navigation.GoToPage("sessions");
        navigation.GoToPage("threads");

        navigation.Clear();

        navigation.IsTranscriptActive.Should().BeTrue();
        navigation.ActivePageId.Should().BeNull();
        navigation.BackStack.Should().BeEmpty();
        navigation.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public void GoToTranscript_ReturnsToTranscriptAndClearsHistory()
    {
        var navigation = new AgentTuiNavigationModel();
        navigation.GoToPage("sessions");
        navigation.GoToPage("threads");

        navigation.GoToTranscript();

        navigation.IsTranscriptActive.Should().BeTrue();
        navigation.ActivePageId.Should().BeNull();
        navigation.BackStack.Should().BeEmpty();
        navigation.CanGoBack.Should().BeFalse();
    }
}
