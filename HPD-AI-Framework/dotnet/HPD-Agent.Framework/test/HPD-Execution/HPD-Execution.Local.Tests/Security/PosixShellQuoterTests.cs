using FluentAssertions;
using HPD.Execution.Local.Security;
using Xunit;

namespace HPD.Execution.Local.Tests.Security;

public sealed class PosixShellQuoterTests
{
    [Theory]
    [InlineData("", "''")]
    [InlineData("simple", "'simple'")]
    [InlineData("two words", "'two words'")]
    [InlineData("it's", "'it'\\''s'")]
    [InlineData("$(touch /tmp/pwned)", "'$(touch /tmp/pwned)'")]
    public void Quote_UsesSingleQuotedPosixForm(string input, string expected)
    {
        PosixShellQuoter.Quote(input).Should().Be(expected);
    }

    [Fact]
    public void RenderCommand_QuotesEveryArgvSegment()
    {
        var invocation = CommandInvocation.From(
            "node",
            ["script.js", "safe; touch /tmp/pwned", "quote's fine"]);

        var rendered = PosixShellQuoter.RenderCommand(invocation);

        rendered.Should().Be("'node' 'script.js' 'safe; touch /tmp/pwned' 'quote'\\''s fine'");
    }
}
