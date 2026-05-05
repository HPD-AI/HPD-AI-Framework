using HPD.Agent.Bots.Cards;

namespace HPD.Agent.Bots.Teams;

/// <summary>
/// Converts HPD card elements to Teams Adaptive Card payloads.
/// </summary>
public sealed class TeamsCardRenderer
{
    public TeamsAdaptiveCard Render(CardElement card)
    {
        ArgumentNullException.ThrowIfNull(card);

        var body = new List<object>();
        var actions = new List<object>();

        if (!string.IsNullOrWhiteSpace(card.Title))
            body.Add(new TeamsTextBlock(card.Title, Weight: "Bolder", Size: "Large"));

        if (!string.IsNullOrWhiteSpace(card.Subtitle))
            body.Add(new TeamsTextBlock(card.Subtitle, IsSubtle: true));

        if (!string.IsNullOrWhiteSpace(card.ImageUrl))
            body.Add(new TeamsImage(card.ImageUrl));

        foreach (var child in card.Children ?? [])
            RenderChild(child, body, actions);

        return new TeamsAdaptiveCard(
            Body: body,
            Actions: actions.Count > 0 ? actions : null);
    }

    private static void RenderChild(CardChild child, List<object> body, List<object> actions)
    {
        switch (child)
        {
            case CardText text:
                body.Add(new TeamsTextBlock(text.Text, IsSubtle: text.Style == "muted"));
                break;

            case CardFields fields:
                body.Add(new TeamsFactSet(fields.Fields
                    .Select(field => new TeamsFact(field.Label, field.Value))
                    .ToArray()));
                break;

            case CardTable table:
                RenderTable(table, body);
                break;

            case CardLink link:
                body.Add(new TeamsTextBlock($"[{link.Label}]({link.Url})"));
                break;

            case CardImage image:
                if (!string.IsNullOrWhiteSpace(image.Title))
                    body.Add(new TeamsTextBlock(image.Title, Weight: "Bolder"));
                body.Add(new TeamsImage(image.Url, image.AltText));
                break;

            case CardDivider:
                body.Add(new TeamsContainer([], Separator: true));
                break;

            case CardSection section:
                body.Add(RenderSection(section, actions));
                break;

            case CardActions cardActions:
                RenderActions(cardActions, body, actions);
                break;
        }
    }

    private static TeamsContainer RenderSection(CardSection section, List<object> actions)
    {
        var items = new List<object>();

        if (!string.IsNullOrWhiteSpace(section.Title))
            items.Add(new TeamsTextBlock(section.Title, Weight: "Bolder"));

        foreach (var child in section.Children ?? [])
            RenderChild(child, items, actions);

        return new TeamsContainer(items);
    }

    private static void RenderActions(CardActions cardActions, List<object> body, List<object> actions)
    {
        var firstSubmitButton = cardActions.Actions.OfType<CardButton>()
            .FirstOrDefault(button => string.IsNullOrWhiteSpace(button.Url));

        foreach (var action in cardActions.Actions)
        {
            switch (action)
            {
                case CardButton button:
                    actions.Add(RenderButton(button));
                    break;

                case CardSelect select:
                    body.Add(RenderChoiceSet(select, style: "compact"));
                    if (firstSubmitButton is null)
                        actions.Add(RenderImplicitSubmit(select.ActionId));
                    break;

                case CardRadioSelect radio:
                    body.Add(RenderChoiceSet(radio, style: "expanded"));
                    if (firstSubmitButton is null)
                        actions.Add(RenderImplicitSubmit(radio.ActionId));
                    break;
            }
        }
    }

    private static object RenderButton(CardButton button)
    {
        if (!string.IsNullOrWhiteSpace(button.Url))
            return new TeamsOpenUrlAction(button.Label, button.Url);

        return new TeamsSubmitAction(
            Title: button.Label,
            Style: button.Style == "danger" ? "destructive" : button.Style,
            Data: new Dictionary<string, string>
            {
                ["actionId"] = button.ActionId,
                ["value"] = button.Value ?? string.Empty,
            });
    }

    private static TeamsChoiceSet RenderChoiceSet(CardSelect select, string style)
        => new(
            Id: select.ActionId,
            Placeholder: select.Placeholder,
            Choices: select.Options.Select(option => new TeamsChoice(option.Label, option.Value)).ToArray(),
            Value: select.InitialValue,
            Style: style);

    private static TeamsChoiceSet RenderChoiceSet(CardRadioSelect select, string style)
        => new(
            Id: select.ActionId,
            Placeholder: select.Placeholder,
            Choices: select.Options.Select(option => new TeamsChoice(option.Label, option.Value)).ToArray(),
            Value: select.InitialValue,
            Style: style);

    private static void RenderTable(CardTable table, List<object> body)
    {
        if (table.Columns.Count == 0)
            return;

        body.Add(RenderTableRow(table.Columns, isHeader: true));
        foreach (var row in table.Rows)
            body.Add(RenderTableRow(row, isHeader: false));
    }

    private static TeamsColumnSet RenderTableRow(IReadOnlyList<string> cells, bool isHeader)
    {
        var columns = cells.Select(cell => new TeamsColumn(
            Items:
            [
                new TeamsTextBlock(
                    cell,
                    Weight: isHeader ? "Bolder" : null,
                    Wrap: true)
            ])).ToArray();

        return new TeamsColumnSet(columns);
    }

    private static TeamsSubmitAction RenderImplicitSubmit(string actionId)
        => new(
            Title: "Submit",
            Data: new Dictionary<string, string>
            {
                ["actionId"] = actionId,
                ["value"] = string.Empty,
            });
}
