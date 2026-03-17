using HPD.Agent.Adapters.Cards;

namespace HPD.Agent.Adapters.Slack;

/// <summary>
/// Converts <see cref="CardElement"/> trees to Slack Block Kit blocks.
/// The source generator emits the <c>public SlackBlock[] Render(CardElement card)</c>
/// entry point, which calls the <c>partial</c> methods below in order.
/// </summary>
[CardRenderer]
public partial class SlackCardRenderer
{
    /// <summary>
    /// Renders the root card: header block (title), context block (subtitle),
    /// image block (imageUrl), then each child in order.
    /// </summary>
    public partial SlackBlock[] RenderCard(CardElement card);

    /// <summary>
    /// Renders a text paragraph.
    /// Normal style → <c>SlackSectionBlock</c> with <c>Expand = true</c> (prevents
    /// Slack's "See more" collapse on long text).
    /// Muted style → <c>SlackContextBlock</c> for secondary/smaller appearance.
    /// </summary>
    public partial SlackBlock RenderText(CardText text);

    /// <summary>Renders a <see cref="SlackImageBlock"/>.</summary>
    public partial SlackBlock RenderImage(CardImage image);

    /// <summary>Renders a <see cref="SlackDividerBlock"/>.</summary>
    public partial SlackBlock RenderDivider(CardDivider divider);

    /// <summary>
    /// Renders a <see cref="SlackActionsBlock"/> containing
    /// <see cref="SlackButton"/> elements mapped from the card's action buttons.
    /// Button styles: "primary" → <c>primary</c>, "danger" → <c>danger</c>, else omitted.
    /// </summary>
    public partial SlackBlock[] RenderActions(CardActions actions);

    /// <summary>Renders a section by flattening its children into blocks.</summary>
    public partial SlackBlock[] RenderSection(CardSection section);

    /// <summary>
    /// Renders a fields list as a <see cref="SlackSectionBlock"/> with a
    /// <c>fields</c> array. Each field becomes a <c>*label*\nvalue</c> mrkdwn entry.
    /// </summary>
    public partial SlackBlock RenderFields(CardFields fields);

    /// <summary>
    /// Renders a hyperlink as a mrkdwn section block: <c>&lt;url|label&gt;</c>.
    /// </summary>
    public partial SlackBlock RenderLink(CardLink link);
}
