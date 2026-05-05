using System.Text;
using HPD.Agent.Bots;
using HPD.Agent.Bots.Cards;

namespace HPD.Agent.Bots.Telegram;

[PlatformFormatConverter]
[Bold("*{0}*")]
[Italic("_{0}_")]
[Strike("~~{0}~~")]
[Link("{0} ({1})")]
[Code("`{0}`")]
[CodeBlock("```\n{0}\n```")]
[Blockquote("> {0}")]
[ListItem("- {0}")]
[OrderedListItem("{n}. {0}")]
public sealed partial class TelegramFormatConverter
{
    public string RenderCardFallback(CardElement card, TelegramRenderMode mode = TelegramRenderMode.Plain)
    {
        var text = BotEmojiResolver.ConvertPlaceholders(
            CardFallbackText.From(card),
            BotEmojiFormat.Unicode);
        return mode == TelegramRenderMode.MarkdownV2
            ? TelegramMarkdownV2.EscapeText(text)
            : text;
    }

    public string RenderPlain(string text) => text;

    public string RenderMarkdownV2Text(string text) => TelegramMarkdownV2.EscapeText(text);

    public string RenderTable(IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var table = TableToAscii(columns, rows);
        return $"```\n{TelegramMarkdownV2.EscapeCode(table)}\n```";
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
