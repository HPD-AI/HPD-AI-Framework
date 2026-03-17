using System.Text;
using HPD.Agent.Adapters.Cards;

namespace HPD.Agent.Adapters;

/// <summary>
/// Generates plain-text fallback from a CardElement tree.
/// Used as the <c>text</c> field alongside blocks in platform API calls —
/// appears in mobile notifications, screen readers, and platforms that
/// can't render rich blocks.
/// Actions are excluded (buttons are not actionable as plain text).
/// </summary>
public static class CardFallbackText
{
    public static string From(CardElement card)
    {
        var sb = new StringBuilder();
        if (card.Title is not null)    sb.AppendLine(card.Title);
        if (card.Subtitle is not null) sb.AppendLine(card.Subtitle);
        foreach (var child in card.Children ?? [])
            AppendChild(sb, child);
        return sb.ToString().Trim();
    }

    private static void AppendChild(StringBuilder sb, CardChild child)
    {
        switch (child)
        {
            case CardText t:
                sb.AppendLine(t.Text);
                break;
            case CardFields f:
                foreach (var field in f.Fields)
                    sb.AppendLine($"{field.Label}: {field.Value}");
                break;
            case CardLink l:
                sb.AppendLine($"{l.Label} ({l.Url})");
                break;
            case CardSection s:
                foreach (var c in s.Children ?? [])
                    AppendChild(sb, c);
                break;
            case CardImage:
            case CardDivider:
            case CardActions: // excluded — buttons are not actionable as plain text
                break;
        }
    }
}
