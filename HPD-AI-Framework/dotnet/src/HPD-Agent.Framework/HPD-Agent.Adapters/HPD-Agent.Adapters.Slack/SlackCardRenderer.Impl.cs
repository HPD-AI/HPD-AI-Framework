using HPD.Agent.Adapters.Cards;

namespace HPD.Agent.Adapters.Slack;

/// <summary>
/// Block Kit implementations of the <see cref="SlackCardRenderer"/> partial methods.
/// Hand-written equivalent of the [CardRenderer] source generator output.
/// </summary>
public partial class SlackCardRenderer
{
    public partial SlackBlock[] RenderCard(CardElement card)
    {
        var blocks = new List<SlackBlock>();

        if (card.Title is not null)
            blocks.Add(new SlackHeaderBlock(new SlackPlainText(card.Title)));

        if (card.Subtitle is not null)
            blocks.Add(new SlackContextBlock([new SlackMrkdwn(card.Subtitle)]));

        if (card.ImageUrl is not null)
            blocks.Add(new SlackImageBlock(card.ImageUrl, card.Title ?? "image"));

        foreach (var child in card.Children ?? [])
            blocks.AddRange(RenderChild(child));

        return [.. blocks];
    }

    public partial SlackBlock RenderText(CardText text)
    {
        if (text.Style == "muted")
            return new SlackContextBlock([new SlackMrkdwn(text.Text)]);

        return new SlackSectionBlock(Text: new SlackMrkdwn(text.Text), Expand: true);
    }

    public partial SlackBlock RenderImage(CardImage image) =>
        new SlackImageBlock(
            ImageUrl: image.Url,
            AltText: image.AltText ?? image.Title ?? "image",
            Title: image.Title is not null ? new SlackPlainText(image.Title) : null);

    public partial SlackBlock RenderDivider(CardDivider _) => new SlackDividerBlock();

    public partial SlackBlock[] RenderActions(CardActions actions)
    {
        var buttons = actions.Actions
            .OfType<CardButton>()
            .Select(b => new SlackButton(
                ActionId: b.ActionId,
                Text: new SlackPlainText(b.Label),
                Value: b.Value,
                Style: b.Style is "primary" or "danger" ? b.Style : null,
                Url: b.Url))
            .ToList();

        if (buttons.Count == 0) return [];

        return [new SlackActionsBlock(buttons)];
    }

    public partial SlackBlock[] RenderSection(CardSection section)
    {
        var blocks = new List<SlackBlock>();

        if (section.Title is not null)
            blocks.Add(new SlackContextBlock([new SlackMrkdwn($"*{section.Title}*")]));

        foreach (var child in section.Children ?? [])
            blocks.AddRange(RenderChild(child));

        return [.. blocks];
    }

    public partial SlackBlock RenderFields(CardFields fields)
    {
        var slackFields = fields.Fields
            .Select(f => (SlackTextObject)new SlackMrkdwn($"*{f.Label}*\n{f.Value}"))
            .ToList();

        return new SlackSectionBlock(Fields: slackFields);
    }

    public partial SlackBlock RenderLink(CardLink link) =>
        new SlackSectionBlock(Text: new SlackMrkdwn($"<{link.Url}|{link.Label}>"));

    // Dispatches a single CardChild to zero or more blocks.
    private SlackBlock[] RenderChild(CardChild child) => child switch
    {
        CardText t      => [RenderText(t)],
        CardImage i     => [RenderImage(i)],
        CardDivider d   => [RenderDivider(d)],
        CardActions a   => RenderActions(a),
        CardSection s   => RenderSection(s),
        CardFields f    => [RenderFields(f)],
        CardLink l      => [RenderLink(l)],
        _               => []
    };
}
