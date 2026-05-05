using FluentAssertions;
using HPD.Agent.Bots;

namespace HPD.Agent.Bots.Tests.Unit;

public class BotEmojiResolverTests
{
    [Theory]
    [InlineData("{{emoji:rocket}}", "🚀")]
    [InlineData("Ship {{emoji:thumbs_up}}", "Ship 👍")]
    [InlineData("Keep {{emoji:not_real}}", "Keep {{emoji:not_real}}")]
    public void ConvertPlaceholders_ToUnicode_ReplacesKnownEmojiOnly(string input, string expected)
    {
        BotEmojiResolver.ConvertPlaceholders(input, BotEmojiFormat.Unicode)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("👍", "👍")]
    [InlineData("thumbs_up", "👍")]
    [InlineData("{{emoji:thumbs_up}}", "👍")]
    [InlineData("custom:abc", "custom:abc")]
    public void ToUnicode_NormalizesKnownNamesAndLeavesUnknownValues(string input, string expected)
    {
        BotEmojiResolver.ToUnicode(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("👍", "thumbsup")]
    [InlineData(":thumbsup:", "thumbsup")]
    [InlineData("thumbs_up", "thumbsup")]
    [InlineData("{{emoji:thumbs_up}}", "thumbsup")]
    public void ToSlackName_UsesSlackEmojiDialect(string input, string expected)
    {
        BotEmojiResolver.ToSlackName(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("👍", "thumbs_up")]
    [InlineData("thumbsup", "thumbs_up")]
    [InlineData("{{emoji:rocket}}", "rocket")]
    [InlineData("not_real", "not_real")]
    public void ToDiscordName_UsesDiscordNameDialect(string input, string expected)
    {
        BotEmojiResolver.ToDiscordName(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("👍", "+1")]
    [InlineData("thumbs_down", "-1")]
    [InlineData("party", "hooray")]
    [InlineData("🚀", "rocket")]
    [InlineData("not_real", "+1")]
    public void ToGitHubReaction_UsesGitHubReactionDialect(string input, string expected)
    {
        BotEmojiResolver.ToGitHubReaction(input).Should().Be(expected);
    }
}
