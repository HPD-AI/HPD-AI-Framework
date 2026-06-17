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
