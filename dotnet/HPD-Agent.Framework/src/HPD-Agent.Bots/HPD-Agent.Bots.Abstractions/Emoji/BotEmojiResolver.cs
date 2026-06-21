using System.Text;

namespace HPD.Agent.Bots;

/// <summary>
/// Platform emoji output dialects supported by <see cref="BotEmojiResolver"/>.
/// </summary>
public enum BotEmojiFormat
{
    /// <summary>Render known emoji names as Unicode emoji characters.</summary>
    Unicode,

    /// <summary>Render known emoji names as Slack reaction names without surrounding colons.</summary>
    SlackName,

    /// <summary>Render known emoji names as Discord-style normalized names.</summary>
    DiscordName,

    /// <summary>Render known emoji names as one of GitHub's supported reaction values.</summary>
    GitHubReaction,
}

/// <summary>
/// Shared emoji normalization helpers for bot adapters.
/// </summary>
public static class BotEmojiResolver
{
    /// <summary>
    /// Replaces <c>{{emoji:name}}</c> placeholders in text using the requested platform format.
    /// Unknown placeholders are preserved.
    /// </summary>
    public static string ConvertPlaceholders(string text, BotEmojiFormat format)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{{emoji:", StringComparison.OrdinalIgnoreCase))
            return text;

        var builder = new StringBuilder(text.Length);
        var index = 0;
        while (index < text.Length)
        {
            var start = text.IndexOf("{{emoji:", index, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                builder.Append(text, index, text.Length - index);
                break;
            }

            var end = text.IndexOf("}}", start + "{{emoji:".Length, StringComparison.Ordinal);
            if (end < 0)
            {
                builder.Append(text, index, text.Length - index);
                break;
            }

            builder.Append(text, index, start - index);
            var name = text[(start + "{{emoji:".Length)..end];
            var resolved = Resolve(name, format);
            builder.Append(resolved is null ? text[start..(end + 2)] : resolved);
            index = end + 2;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Converts a known emoji name, Unicode emoji, or <c>{{emoji:name}}</c> placeholder to Unicode.
    /// Unknown values are returned unchanged.
    /// </summary>
    public static string ToUnicode(string emojiOrName)
        => Resolve(emojiOrName, BotEmojiFormat.Unicode) ?? emojiOrName;

    /// <summary>
    /// Converts a known emoji name, Unicode emoji, or placeholder to a Slack reaction name.
    /// Unknown values are returned without surrounding colons.
    /// </summary>
    public static string ToSlackName(string emojiOrName)
        => TrimColons(Resolve(emojiOrName, BotEmojiFormat.SlackName) ?? emojiOrName);

    /// <summary>
    /// Converts a known emoji name, Unicode emoji, or placeholder to a Discord-style emoji name.
    /// Unknown values are returned unchanged.
    /// </summary>
    public static string ToDiscordName(string emojiOrName)
        => Resolve(emojiOrName, BotEmojiFormat.DiscordName) ?? emojiOrName;

    /// <summary>
    /// Converts a known emoji name, Unicode emoji, or placeholder to a GitHub reaction value.
    /// Unknown values fall back to <c>+1</c>.
    /// </summary>
    public static string ToGitHubReaction(string emojiOrName)
        => Resolve(emojiOrName, BotEmojiFormat.GitHubReaction) ?? "+1";

    /// <summary>
    /// Attempts to convert a known emoji name, Unicode emoji, or placeholder to Unicode.
    /// </summary>
    public static bool TryToUnicode(string emojiOrName, out string emoji)
    {
        emoji = Resolve(emojiOrName, BotEmojiFormat.Unicode) ?? string.Empty;
        return emoji.Length > 0;
    }

    private static string? Resolve(string value, BotEmojiFormat format)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = NormalizeKey(UnwrapPlaceholder(value));
        return format switch
        {
            BotEmojiFormat.Unicode => Unicode(normalized),
            BotEmojiFormat.SlackName => SlackName(normalized),
            BotEmojiFormat.DiscordName => DiscordName(normalized),
            BotEmojiFormat.GitHubReaction => GitHubReaction(normalized),
            _ => null,
        };
    }

    private static string UnwrapPlaceholder(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith("{{emoji:", StringComparison.OrdinalIgnoreCase) &&
            trimmed.EndsWith("}}", StringComparison.Ordinal)
                ? trimmed["{{emoji:".Length..^2]
                : trimmed;
    }

    private static string NormalizeKey(string value)
        => TrimColons(value)
            .Trim()
            .ToLowerInvariant()
            .Replace('-', '_');

    private static string TrimColons(string value)
        => value.Trim().Trim(':');

    private static string? Unicode(string key)
        => key switch
        {
            "+1" or "thumbsup" or "thumbs_up" or "👍" => "👍",
            "-1" or "thumbsdown" or "thumbs_down" or "👎" => "👎",
            "heart" or "❤️" or "❤" => "❤️",
            "fire" or "🔥" => "🔥",
            "rocket" or "🚀" => "🚀",
            "eyes" or "👀" => "👀",
            "white_check_mark" or "check" or "check_mark" or "✅" => "✅",
            "x" or "cross_mark" or "❌" => "❌",
            "warning" or "⚠️" or "⚠" => "⚠️",
            "raised_hands" or "🙌" => "🙌",
            "wave" or "👋" => "👋",
            "thinking" or "thinking_face" or "🤔" => "🤔",
            "smile" or "😊" => "😊",
            "laugh" or "joy" or "😂" => "😂",
            "party" or "tada" or "hooray" or "🎉" => "🎉",
            "star" or "⭐" => "⭐",
            "sparkles" or "✨" => "✨",
            "100" or "💯" => "💯",
            _ => null,
        };

    private static string? SlackName(string key)
        => key switch
        {
            "+1" or "thumbsup" or "thumbs_up" or "👍" => "thumbsup",
            "-1" or "thumbsdown" or "thumbs_down" or "👎" => "thumbsdown",
            "white_check_mark" or "check" or "check_mark" or "✅" => "white_check_mark",
            "party" or "hooray" or "🎉" => "tada",
            _ => DiscordName(key),
        };

    private static string? DiscordName(string key)
        => key switch
        {
            "+1" or "thumbsup" or "thumbs_up" or "👍" => "thumbs_up",
            "-1" or "thumbsdown" or "thumbs_down" or "👎" => "thumbs_down",
            "heart" or "❤️" or "❤" => "heart",
            "fire" or "🔥" => "fire",
            "rocket" or "🚀" => "rocket",
            "eyes" or "👀" => "eyes",
            "white_check_mark" or "check" or "check_mark" or "✅" => "check",
            "x" or "cross_mark" or "❌" => "x",
            "warning" or "⚠️" or "⚠" => "warning",
            "raised_hands" or "🙌" => "raised_hands",
            "wave" or "👋" => "wave",
            "thinking" or "thinking_face" or "🤔" => "thinking",
            "smile" or "😊" => "smile",
            "laugh" or "joy" or "😂" => "laugh",
            "party" or "tada" or "hooray" or "🎉" => "party",
            "star" or "⭐" => "star",
            "sparkles" or "✨" => "sparkles",
            "100" or "💯" => "100",
            _ => null,
        };

    private static string? GitHubReaction(string key)
        => key switch
        {
            "+1" or "thumbsup" or "thumbs_up" or "👍" => "+1",
            "-1" or "thumbsdown" or "thumbs_down" or "👎" => "-1",
            "laugh" or "smile" or "joy" or "😂" or "😊" => "laugh",
            "confused" or "thinking" or "thinking_face" or "🤔" => "confused",
            "heart" or "love_eyes" or "❤️" or "❤" => "heart",
            "hooray" or "party" or "tada" or "confetti" or "🎉" => "hooray",
            "rocket" or "🚀" => "rocket",
            "eyes" or "👀" => "eyes",
            _ => null,
        };
}
