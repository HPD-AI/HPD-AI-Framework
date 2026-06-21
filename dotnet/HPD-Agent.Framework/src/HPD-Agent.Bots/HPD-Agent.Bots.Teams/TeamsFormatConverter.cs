using System.Text.RegularExpressions;

namespace HPD.Agent.Bots.Teams;

[PlatformFormatConverter]
[Bold("**{0}**")]
[Italic("_{0}_")]
[Strike("~~{0}~~")]
[Link("[{0}]({1})")]
[Code("`{0}`")]
[CodeBlock("```\n{0}\n```")]
[Blockquote("> {0}")]
[ListItem("- {0}")]
[OrderedListItem("{n}. {0}")]
public partial class TeamsFormatConverter
{
    public partial string RenderMention(string userId);
    public partial string RenderMention(string userId) => $"<at>{userId}</at>";

    public string ToTeamsMarkdown(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return markdown;

        return BareMentionRegex().Replace(markdown, match => $"<at>{match.Groups["name"].Value}</at>");
    }

    public string ToPlainText(string teamsText)
    {
        if (string.IsNullOrEmpty(teamsText)) return teamsText;

        return AtMentionRegex()
            .Replace(teamsText, match => $"@{match.Groups["name"].Value}")
            .Trim();
    }

    [GeneratedRegex(@"(?<![\w<])@(?<name>[A-Za-z][\w.-]*)")]
    private static partial Regex BareMentionRegex();

    [GeneratedRegex(@"<at>(?<name>[^<]+)</at>", RegexOptions.IgnoreCase)]
    private static partial Regex AtMentionRegex();
}
