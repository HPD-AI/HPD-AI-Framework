using FluentAssertions;
using HPD.Agent.Bots.Modals;
using HPD.Agent.Bots.Teams;

namespace HPD.Agent.Bots.Tests.Unit.TeamsBot;

public class TeamsModalConverterTests
{
    [Fact]
    public void Render_ModalElement_CreatesAdaptiveCardInputs()
    {
        var modal = new ModalElement(
            "Deploy",
            [
                new ModalTextInput("Reason", "reason_block", "reason", Placeholder: "Why?", Multiline: true),
                new ModalSelect("Env", "env_block", "env", [new ModalOption("Prod", "prod")]),
                new ModalRadioGroup("Mode", "mode_block", "mode", [new ModalOption("Fast", "fast")]),
                new ModalSection("Review carefully", "section"),
                new ModalDivider("divider")
            ],
            SubmitLabel: "Ship",
            CallbackId: "deploy");

        var card = new TeamsModalConverter().Render(modal);

        card.Actions.Should().ContainSingle()
            .Which.Should().BeOfType<TeamsSubmitAction>()
            .Which.Data["actionId"].Should().Be("deploy");
        card.Body.OfType<TeamsInputText>().Should().ContainSingle(input => input.Id == "reason" && input.IsMultiline == true);
        card.Body.OfType<TeamsChoiceSet>().Should().Contain(select => select.Id == "env" && select.Style == "compact");
        card.Body.OfType<TeamsChoiceSet>().Should().Contain(select => select.Id == "mode" && select.Style == "expanded");
        card.Body.OfType<TeamsTextBlock>().Should().Contain(block => block.Text == "Review carefully");
        card.Body.OfType<TeamsContainer>().Should().Contain(container => container.Separator == true);
    }

    [Fact]
    public void ToTaskSubmitResponse_Close_ReturnsMessageTask()
    {
        var response = new TeamsModalConverter().ToTaskSubmitResponse(new ModalCloseResponse());

        response.Task.Type.Should().Be("message");
    }

    [Fact]
    public void Render_WithErrors_InsertsAttentionText()
    {
        var modal = new ModalElement(
            "Deploy",
            [new ModalTextInput("Reason", "reason_block", "reason")]);

        var card = new TeamsModalConverter().Render(
            modal,
            new Dictionary<string, string> { ["reason_block"] = "Reason is required" });

        card.Body.OfType<TeamsTextBlock>()
            .Should()
            .Contain(block => block.Text == "Reason is required" && block.Color == "Attention");
    }
}
