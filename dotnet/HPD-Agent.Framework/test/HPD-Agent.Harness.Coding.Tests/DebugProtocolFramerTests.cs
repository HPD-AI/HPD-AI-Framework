using System.Text;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugProtocolFramerTests
{
    [Fact]
    public void Arbitrary_chunking_preserves_utf8_byte_lengths()
    {
        var payload = Encoding.UTF8.GetBytes("{\"value\":\"héllo 🌍\"}");
        var encoded = DebugProtocolFramer.Encode(payload);

        for (var chunkSize = 1; chunkSize <= encoded.Length; chunkSize++)
        {
            var framer = new DebugProtocolFramer();
            var frames = new List<ReadOnlyMemory<byte>>();
            for (var offset = 0; offset < encoded.Length; offset += chunkSize)
                frames.AddRange(framer.Append(encoded.AsSpan(offset, Math.Min(chunkSize, encoded.Length - offset))));
            frames.Should().ContainSingle();
            frames[0].ToArray().Should().Equal(payload);
        }
    }

    [Fact]
    public void Coalesced_messages_are_all_drained_in_order()
    {
        var first = DebugProtocolFramer.Encode("{\"seq\":1}"u8);
        var second = DebugProtocolFramer.Encode("{\"seq\":2}"u8);
        var framer = new DebugProtocolFramer();

        var frames = framer.Append(first.Concat(second).ToArray());

        frames.Select(frame => Encoding.UTF8.GetString(frame.Span)).Should().Equal("{\"seq\":1}", "{\"seq\":2}");
    }

    [Theory]
    [InlineData("X-Test: value\r\n\r\n{}", DebugProtocolFramingError.MissingContentLength)]
    [InlineData("Content-Length: 2\r\nContent-Length: 2\r\n\r\n{}", DebugProtocolFramingError.DuplicateContentLength)]
    [InlineData("Content-Length: -1\r\n\r\n", DebugProtocolFramingError.InvalidContentLength)]
    [InlineData("Content-Length: 0\r\n\r\n", DebugProtocolFramingError.InvalidContentLength)]
    [InlineData("Content-Length: 999999999999999999999\r\n\r\n", DebugProtocolFramingError.InvalidContentLength)]
    [InlineData("Content-Length:2\r\n\r\n{}", DebugProtocolFramingError.InvalidHeaderGrammar)]
    [InlineData("junk\nContent-Length: 2\r\n\r\n{}", DebugProtocolFramingError.InvalidHeaderGrammar)]
    public void Invalid_headers_fail_closed(string frame, DebugProtocolFramingError expected)
    {
        var action = () => new DebugProtocolFramer().Append(Encoding.ASCII.GetBytes(frame));

        action.Should().Throw<DebugProtocolFramingException>().Which.Error.Should().Be(expected);
    }

    [Fact]
    public void Header_and_body_limits_are_enforced()
    {
        var limits = new DebugProtocolFramingLimits { MaxHeaderBytes = 32, MaxBodyBytes = 8 };
        var headerAction = () => new DebugProtocolFramer(limits).Append(Encoding.ASCII.GetBytes(new string('A', 33)));
        var bodyAction = () => new DebugProtocolFramer(limits).Append("Content-Length: 9\r\n\r\n"u8);

        headerAction.Should().Throw<DebugProtocolFramingException>().Which.Error.Should().Be(DebugProtocolFramingError.HeaderTooLarge);
        bodyAction.Should().Throw<DebugProtocolFramingException>().Which.Error.Should().Be(DebugProtocolFramingError.BodyTooLarge);
    }

    [Fact]
    public void Invalid_utf8_body_fails_closed()
    {
        var framer = new DebugProtocolFramer();
        var action = () => framer.Append([.. "Content-Length: 2\r\n\r\n"u8, 0xc3, 0x28]);

        action.Should().Throw<DebugProtocolFramingException>().Which.Error.Should().Be(DebugProtocolFramingError.InvalidUtf8);
    }
}
