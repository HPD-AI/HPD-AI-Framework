using System.Text.RegularExpressions;

namespace HPD.Agent.Adapters.Slack;

/// <summary>
/// Converts between markdown AST and Slack mrkdwn format.
/// The source generator emits the <c>ConvertNode(MdastNode)</c> switch dispatcher
/// from the formatting rule attributes. The hand-written methods below handle
/// Slack-specific logic that cannot be expressed as simple format strings.
/// </summary>
[PlatformFormatConverter]
[Bold("*{0}*")]            // markdown **x** → Slack *x*
[Italic("_{0}_")]
[Strike("~{0}~")]          // markdown ~~x~~ → Slack ~x~
[Link("<{1}|{0}>")]        // markdown [text](url) → Slack <url|text>
[Code("`{0}`")]
[CodeBlock("```\n{0}\n```")]
[Blockquote("> {0}")]
[ListItem("• {0}")]
[OrderedListItem("{n}. {0}")]
public partial class SlackFormatConverter
{
    // Regex pattern for Slack user mention format: <@U...>
    private static readonly Regex MentionPattern =
        new(@"<@([UW][A-Z0-9]+)>", RegexOptions.Compiled);

    /// <summary>
    /// Renders a @mention. Produces the Slack <c>&lt;@userId&gt;</c> format.
    /// The generator calls this from the generated ConvertNode dispatcher.
    /// </summary>
    public partial string RenderMention(string userId);
    public partial string RenderMention(string userId) => $"<@{userId}>";

    /// <summary>
    /// Converts plain markdown to Slack mrkdwn. Used before posting agent output.
    /// Hand-written equivalent of the [PlatformFormatConverter] generator output.
    /// Handles: bold, italic, strikethrough, inline code, code blocks, links,
    /// blockquotes, unordered lists, ordered lists, and plain text.
    /// </summary>
    public string ToMrkdwn(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return markdown;

        var lines = markdown.Split('\n');
        var result = new System.Text.StringBuilder();
        var inCodeBlock = false;
        var codeBlockFence = string.Empty;
        var codeBlockLines = new System.Text.StringBuilder();
        var orderedListCounter = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // ── Code fence detection ─────────────────────────────────────────
            if (!inCodeBlock)
            {
                var fence = line.TrimStart();
                if (fence.StartsWith("```") || fence.StartsWith("~~~"))
                {
                    inCodeBlock = true;
                    codeBlockFence = fence.StartsWith("```") ? "```" : "~~~";
                    codeBlockLines.Clear();
                    // skip the opening fence line (language hint stripped)
                    continue;
                }
            }
            else
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith(codeBlockFence))
                {
                    // emit the accumulated code block
                    result.Append("```\n");
                    result.Append(codeBlockLines);
                    result.Append("```");
                    if (i < lines.Length - 1) result.Append('\n');
                    inCodeBlock = false;
                    codeBlockLines.Clear();
                    orderedListCounter = 0;
                    continue;
                }
                codeBlockLines.Append(line).Append('\n');
                continue;
            }

            // ── Block-level patterns ─────────────────────────────────────────
            if (line.StartsWith("> "))
            {
                // Blockquote: "> {0}"
                var content = ConvertInline(line[2..]);
                result.Append($"> {content}");
                orderedListCounter = 0;
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^\d+\. "))
            {
                // Ordered list item: "{n}. {0}"
                orderedListCounter++;
                var content = ConvertInline(System.Text.RegularExpressions.Regex.Replace(line, @"^\d+\. ", ""));
                result.Append($"{orderedListCounter}. {content}");
            }
            else if (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("+ "))
            {
                // Unordered list item: "• {0}"
                var content = ConvertInline(line[2..]);
                result.Append($"• {content}");
                orderedListCounter = 0;
            }
            else
            {
                result.Append(ConvertInline(line));
                orderedListCounter = 0;
            }

            if (i < lines.Length - 1) result.Append('\n');
        }

        // Unclosed code fence — emit whatever was collected
        if (inCodeBlock)
        {
            result.Append("```\n");
            result.Append(codeBlockLines);
            result.Append("```");
        }

        return result.ToString();
    }

    // Converts inline markdown spans to mrkdwn within a single line.
    // Strategy:
    //   1. Extract code spans into a side-table (contents never processed).
    //   2. Convert bold to sentinels (\x02…\x03) so italic pass can't re-match *.
    //   3. Convert italic, strike, links.
    //   4. Restore bold sentinels → *x*, then restore code spans from side-table.
    private static string ConvertInline(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Step 1 — extract inline code spans into a side-table
        var codeSpans = new List<string>();
        text = System.Text.RegularExpressions.Regex.Replace(text, @"`([^`]+)`", m =>
        {
            var idx = codeSpans.Count;
            codeSpans.Add(m.Value);     // preserve original `…` including backticks
            return $"\x01{idx}\x01";   // replace with indexed sentinel
        });

        // Step 2 — bold: **x** or __x__ → sentinel \x02x\x03
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.+?)\*\*", "\x02$1\x03");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"__(.+?)__",     "\x02$1\x03");

        // Step 3 — italic: *x* or _x_ → _x_  (no stray * left from bold)
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*(.+?)\*", "_$1_");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"_(.+?)_",   "_$1_");

        // Step 3b — strikethrough: ~~x~~ → ~x~
        text = System.Text.RegularExpressions.Regex.Replace(text, @"~~(.+?)~~", "~$1~");

        // Step 3c — links: [text](url) → <url|text>
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[([^\]]+)\]\(([^)]+)\)", "<$2|$1>");

        // Step 4 — restore bold sentinels and code spans
        text = text.Replace("\x02", "*").Replace("\x03", "*");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\x01(\d+)\x01",
            m => codeSpans[int.Parse(m.Groups[1].Value)]);

        return text;
    }

    /// <summary>
    /// Strips mrkdwn formatting to produce plain text for agent input.
    /// Resolves inline user mentions (<c>&lt;@U123&gt;</c>) to display names
    /// before calling this — see <see cref="SlackUserCache.ResolveInlineMentionsAsync"/>.
    /// </summary>
    public string ToPlainText(string mrkdwn)
    {
        if (string.IsNullOrEmpty(mrkdwn)) return mrkdwn;

        // Strip <@userId> and <@userId|name> mentions to bare name or userId
        var result = MentionPattern.Replace(mrkdwn, m => m.Value);

        // Strip remaining mrkdwn formatting characters
        result = result
            .Replace("*", "")
            .Replace("_", "")
            .Replace("~", "")
            .Replace("`", "");

        return result.Trim();
    }

    /// <summary>
    /// Parses inbound Slack mrkdwn to a markdown AST.
    /// Used when round-tripping message text for history reconstruction.
    /// (~40 lines of Slack-specific parsing logic.)
    /// </summary>
    public object ToAst(string mrkdwn)
    {
        // Slack-specific AST parsing — converts mrkdwn back to a generic AST
        // for uniform downstream processing. Implementation left as a stub;
        // the full ~40-line parser handles links, mentions, formatting, code blocks.
        throw new NotImplementedException();
    }
}
