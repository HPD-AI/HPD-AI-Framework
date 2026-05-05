using System.Text.RegularExpressions;

namespace HPD.Agent.Bots.Discord;

[PlatformFormatConverter]
[Bold("**{0}**")]
[Italic("*{0}*")]
[Strike("~~{0}~~")]
[Link("[{0}]({1})")]
[Code("`{0}`")]
[CodeBlock("```\n{0}\n```")]
[Blockquote("> {0}")]
[ListItem("- {0}")]
[OrderedListItem("{n}. {0}")]
public partial class DiscordFormatConverter
{
    public partial string RenderMention(string userId);
    public partial string RenderMention(string userId) => $"<@{userId}>";

    /// <summary>
    /// Discord accepts standard markdown for plain messages and embeds. Keep this
    /// method mostly pass-through, trimming only cases that commonly leak into agent input.
    /// </summary>
    public string ToDiscordMarkdown(string markdown) => markdown;

    public string ToPlainText(string discordMarkdown)
    {
        if (string.IsNullOrEmpty(discordMarkdown)) return discordMarkdown;

        var text = CustomEmojiRegex().Replace(discordMarkdown, ":${name}:");
        text = MentionRegex().Replace(text, "");
        text = text.Replace("||", "");

        return text.Trim();
    }

    [GeneratedRegex(@"<[@&#!]?\d+>")]
    private static partial Regex MentionRegex();

    [GeneratedRegex(@"<a?:(?<name>[A-Za-z0-9_]+):\d+>")]
    private static partial Regex CustomEmojiRegex();
}
