using System.Text;
using HPD.Agent.Bots.Cards;

namespace HPD.Agent.Bots.WhatsApp;

[PlatformFormatConverter]
[Bold("*{0}*")]
[Italic("_{0}_")]
[Strike("~{0}~")]
[Code("```{0}```")]
[CodeBlock("```\n{0}\n```")]
[Link("{1}")]
[Blockquote("{0}")]
[ListItem("- {0}")]
[OrderedListItem("{n}. {0}")]
public sealed partial class WhatsAppFormatConverter
{
    public string RenderPlain(string text)
        => Escape(BotEmojiResolver.ConvertPlaceholders(text, BotEmojiFormat.Unicode));

    public string RenderCardFallback(CardElement card)
        => RenderPlain(CardFallbackText.From(card));

    public string RenderTable(IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var table = TableToAscii(columns, rows);
        return $"```\n{table}\n```";
    }

    public string RenderFormatted(string text) => RenderPlain(text);

    internal static string Escape(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("*", "\\*", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("~", "\\~", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal);
    }

    private static string TableToAscii(IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (columns.Count == 0)
            return string.Empty;

        var widths = columns.Select(c => c.Length).ToArray();
        foreach (var row in rows)
        {
            for (var i = 0; i < Math.Min(widths.Length, row.Count); i++)
                widths[i] = Math.Max(widths[i], row[i].Length);
        }

        var sb = new StringBuilder();
        AppendRow(sb, columns, widths);
        AppendSeparator(sb, widths);
        foreach (var row in rows)
            AppendRow(sb, row, widths);
        return sb.ToString().TrimEnd();
    }

    private static void AppendRow(StringBuilder sb, IReadOnlyList<string> values, int[] widths)
    {
        sb.Append('|');
        for (var i = 0; i < widths.Length; i++)
        {
            var value = i < values.Count ? values[i] : string.Empty;
            sb.Append(' ').Append(value.PadRight(widths[i])).Append(" |");
        }
        sb.AppendLine();
    }

    private static void AppendSeparator(StringBuilder sb, int[] widths)
    {
        sb.Append('|');
        foreach (var width in widths)
            sb.Append(' ').Append(new string('-', width)).Append(" |");
        sb.AppendLine();
    }
}
