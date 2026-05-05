using FluentAssertions;
using HPD.Agent.Bots.Cards;
using HPD.Agent.Bots.Teams;

namespace HPD.Agent.Bots.Tests.Unit.TeamsBot;

public class TeamsCardRendererTests
{
    [Fact]
    public void Render_CardWithTextFieldsImageAndButtons_CreatesAdaptiveCard()
    {
        var card = new CardElement(
            Title: "Deploy",
            Subtitle: "Ready to ship",
            ImageUrl: "https://example.com/root.png",
            Children:
            [
                new CardText("Review the changes."),
                new CardText("Low risk", Style: "muted"),
                new CardFields([new CardField("Status", "Green")]),
                new CardImage("https://example.com/image.png", "Diagram", "Architecture"),
                new CardActions(
                [
                    new CardButton("approve", "Approve", "yes", Style: "primary"),
                    new CardButton("docs", "Docs", Url: "https://example.com/docs")
                ])
            ]);

        var rendered = new TeamsCardRenderer().Render(card);

        rendered.Type.Should().Be("AdaptiveCard");
        rendered.Version.Should().Be("1.4");
        HasBody<TeamsTextBlock>(rendered, block => block.Text == "Deploy" && block.Weight == "Bolder" && block.Size == "Large").Should().BeTrue();
        HasBody<TeamsTextBlock>(rendered, block => block.Text == "Ready to ship" && block.IsSubtle == true).Should().BeTrue();
        HasBody<TeamsImage>(rendered, image => image.Url == "https://example.com/root.png").Should().BeTrue();
        HasBody<TeamsTextBlock>(rendered, block => block.Text == "Review the changes.").Should().BeTrue();
        HasBody<TeamsTextBlock>(rendered, block => block.Text == "Low risk" && block.IsSubtle == true).Should().BeTrue();
        HasBody<TeamsFactSet>(rendered, facts => facts.Facts.Count == 1).Should().BeTrue();
        HasBody<TeamsTextBlock>(rendered, block => block.Text == "Architecture" && block.Weight == "Bolder").Should().BeTrue();
        HasBody<TeamsImage>(rendered, image => image.Url == "https://example.com/image.png" && image.AltText == "Diagram").Should().BeTrue();
        HasAction<TeamsSubmitAction>(rendered, action => action.Title == "Approve" && action.Style == "primary").Should().BeTrue();
        HasAction<TeamsOpenUrlAction>(rendered, action => action.Title == "Docs" && action.Url == "https://example.com/docs").Should().BeTrue();
    }

    [Fact]
    public void Render_SelectWithoutButton_AddsChoiceSetAndImplicitSubmit()
    {
        var card = new CardElement(
            Children:
            [
                new CardActions(
                [
                    new CardSelect(
                        "priority",
                        "Priority",
                        [new CardSelectOption("High", "high")],
                        InitialValue: "high")
                ])
            ]);

        var rendered = new TeamsCardRenderer().Render(card);

        HasBody<TeamsChoiceSet>(rendered, select =>
            select.Id == "priority"
            && select.Placeholder == "Priority"
            && select.Value == "high"
            && select.Style == "compact").Should().BeTrue();
        rendered.Actions.Should().ContainSingle()
            .Which.Should().BeOfType<TeamsSubmitAction>()
            .Which.Data["actionId"].Should().Be("priority");
    }

    [Fact]
    public void Render_RadioSelectWithButton_UsesExistingSubmitButton()
    {
        var card = new CardElement(
            Children:
            [
                new CardActions(
                [
                    new CardRadioSelect(
                        "choice",
                        "Choose",
                        [new CardSelectOption("A", "a")]),
                    new CardButton("save", "Save")
                ])
            ]);

        var rendered = new TeamsCardRenderer().Render(card);

        HasBody<TeamsChoiceSet>(rendered, select => select.Id == "choice" && select.Style == "expanded").Should().BeTrue();
        rendered.Actions.Should().ContainSingle()
            .Which.Should().BeOfType<TeamsSubmitAction>()
            .Which.Data["actionId"].Should().Be("save");
    }

    [Fact]
    public void Render_SectionAndDivider_CreateContainerElements()
    {
        var card = new CardElement(
            Children:
            [
                new CardSection("Details", [new CardText("Inside")]),
                new CardDivider()
            ]);

        var rendered = new TeamsCardRenderer().Render(card);

        HasBody<TeamsContainer>(rendered, container => container.Items.Count == 2).Should().BeTrue();
        HasBody<TeamsContainer>(rendered, container => container.Separator == true).Should().BeTrue();
    }

    [Fact]
    public void Render_Table_CreatesColumnSetRows()
    {
        var card = new CardElement(
            Children:
            [
                new CardTable(
                    ["Name", "Status"],
                    [
                        ["Build", "Passing"],
                        ["Deploy", "Ready"]
                    ])
            ]);

        var rendered = new TeamsCardRenderer().Render(card);

        rendered.Body.OfType<TeamsColumnSet>().Should().HaveCount(3);
        var header = rendered.Body.OfType<TeamsColumnSet>().First();
        header.Columns.Should().HaveCount(2);
        header.Columns[0].Items.Should().ContainSingle()
            .Which.Should().BeOfType<TeamsTextBlock>()
            .Which.Weight.Should().Be("Bolder");
    }

    private static bool HasBody<T>(TeamsAdaptiveCard card, Func<T, bool> predicate)
        => card.Body.OfType<T>().Any(predicate);

    private static bool HasAction<T>(TeamsAdaptiveCard card, Func<T, bool> predicate)
        => card.Actions?.OfType<T>().Any(predicate) == true;
}
