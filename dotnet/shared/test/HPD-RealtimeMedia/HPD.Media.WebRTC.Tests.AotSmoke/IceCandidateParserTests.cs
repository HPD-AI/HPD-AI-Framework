#nullable enable

using System.Buffers;
using System.Net;
using HPD.Media.WebRTC;

namespace HPD.Media.WebRTC.Tests.AotSmoke;

public sealed class IceCandidateParserTests
{
    [Fact]
    public void TryParse_ReadsHostCandidate()
    {
        const string Candidate = "candidate:842163049 1 udp 1677729535 192.0.2.33 54400 typ host generation 0 network-id 1 network-cost 10";

        bool parsed = IceCandidateParser.TryParse(
            Candidate,
            sdpMid: "audio",
            out IceCandidate candidate,
            out IceCandidateRejectReason rejectReason);

        Assert.True(parsed);
        Assert.Equal(IceCandidateRejectReason.None, rejectReason);
        Assert.Equal("842163049", candidate.Foundation);
        Assert.Equal(1, candidate.ComponentId);
        Assert.Equal("UDP", candidate.Transport);
        Assert.Equal(1_677_729_535u, candidate.Priority);
        Assert.Equal(new IPEndPoint(IPAddress.Parse("192.0.2.33"), 54400), candidate.EndPoint);
        Assert.Null(candidate.MdnsHostName);
        Assert.Equal(IceCandidateType.Host, candidate.CandidateType);
        Assert.Equal("audio", candidate.SdpMid);
        Assert.Contains(candidate.ExtensionAttributes.ToArray(), static attribute => attribute.Name == "generation" && attribute.Value == "0");
        Assert.Contains(candidate.ExtensionAttributes.ToArray(), static attribute => attribute.Name == "network-id" && attribute.Value == "1");
    }

    [Fact]
    public void TryParse_ReadsServerReflexiveCandidateAndRelatedAddress()
    {
        const string Candidate = "candidate:1 1 udp 2122194687 203.0.113.10 3478 typ srflx raddr 10.0.0.2 rport 50000";

        bool parsed = IceCandidateParser.TryParse(Candidate, null, out IceCandidate candidate, out _);

        Assert.True(parsed);
        Assert.Equal(IceCandidateType.ServerReflexive, candidate.CandidateType);
        Assert.Equal(new IPEndPoint(IPAddress.Parse("203.0.113.10"), 3478), candidate.EndPoint);
        Assert.Contains(candidate.ExtensionAttributes.ToArray(), static attribute => attribute.Name == "raddr" && attribute.Value == "10.0.0.2");
        Assert.Contains(candidate.ExtensionAttributes.ToArray(), static attribute => attribute.Name == "rport" && attribute.Value == "50000");
    }

    [Fact]
    public void TryParse_ReadsMdnsCandidate()
    {
        const string Candidate = "candidate:2 1 udp 2122260223 a1b2c3d4.local 60769 typ host generation 0";

        bool parsed = IceCandidateParser.TryParse(Candidate, "0", out IceCandidate candidate, out _);

        Assert.True(parsed);
        Assert.Null(candidate.EndPoint);
        Assert.Equal(60769, candidate.Port);
        Assert.Equal("a1b2c3d4.local", candidate.MdnsHostName);
        Assert.Equal(IceCandidateType.Host, candidate.CandidateType);
    }

    [Fact]
    public void TryParse_RejectsMdnsCandidateForNonHostType()
    {
        const string Candidate = "candidate:2 1 udp 2122260223 a1b2c3d4.local 60769 typ srflx raddr 10.0.0.1 rport 50000";

        bool parsed = IceCandidateParser.TryParse(Candidate, "0", out _, out IceCandidateRejectReason rejectReason);

        Assert.False(parsed);
        Assert.Equal(IceCandidateRejectReason.InvalidSyntax, rejectReason);
    }

    [Fact]
    public void TryWrite_RoundTripsMdnsCandidateWithPort()
    {
        var candidate = new IceCandidate
        {
            Foundation = "2",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 2_122_260_223,
            Port = 60769,
            MdnsHostName = "a1b2c3d4.local",
            CandidateType = IceCandidateType.Host
        };
        var buffer = new ArrayBufferWriter<char>();

        bool written = IceCandidateParser.TryWrite(candidate, buffer);
        bool parsed = IceCandidateParser.TryParse(new string(buffer.WrittenSpan), "0", out IceCandidate reparsed, out _);

        Assert.True(written);
        Assert.True(parsed);
        Assert.Equal(candidate.MdnsHostName, reparsed.MdnsHostName);
        Assert.Equal(candidate.Port, reparsed.Port);
    }

    [Fact]
    public void TryWrite_RejectsMdnsCandidateForNonHostType()
    {
        var candidate = new IceCandidate
        {
            Foundation = "2",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 2_122_260_223,
            Port = 60769,
            MdnsHostName = "a1b2c3d4.local",
            CandidateType = IceCandidateType.ServerReflexive
        };

        bool written = IceCandidateParser.TryWrite(candidate, new ArrayBufferWriter<char>());

        Assert.False(written);
    }

    [Fact]
    public void TryParse_RejectsTcpCandidateAsUnsupportedTransport()
    {
        const string Candidate = "candidate:3 1 tcp 1518280447 192.0.2.44 9 typ host tcptype active";

        bool parsed = IceCandidateParser.TryParse(Candidate, null, out _, out IceCandidateRejectReason rejectReason);

        Assert.False(parsed);
        Assert.Equal(IceCandidateRejectReason.UnsupportedTransport, rejectReason);
    }

    [Fact]
    public void TryParse_RejectsInvalidComponentId()
    {
        const string Candidate = "candidate:3 0 udp 1518280447 192.0.2.44 9 typ host";
        const string UnsupportedComponent = "candidate:3 3 udp 1518280447 192.0.2.44 9 typ host";

        bool parsed = IceCandidateParser.TryParse(Candidate, null, out _, out IceCandidateRejectReason rejectReason);
        bool unsupportedParsed = IceCandidateParser.TryParse(UnsupportedComponent, null, out _, out IceCandidateRejectReason unsupportedReason);

        Assert.False(parsed);
        Assert.Equal(IceCandidateRejectReason.InvalidSyntax, rejectReason);
        Assert.False(unsupportedParsed);
        Assert.Equal(IceCandidateRejectReason.InvalidSyntax, unsupportedReason);
    }

    [Fact]
    public void TryParse_RejectsBlankSdpMid()
    {
        const string Candidate = "candidate:3 1 udp 1518280447 192.0.2.44 9 typ host";

        bool parsed = IceCandidateParser.TryParse(Candidate, " ", out _, out IceCandidateRejectReason rejectReason);

        Assert.False(parsed);
        Assert.Equal(IceCandidateRejectReason.InvalidSyntax, rejectReason);
    }

    [Theory]
    [InlineData("candidate:3 1 udp 1518280447 192.0.2.44 9 typ host\r\ncandidate:evil 1 udp 1 192.0.2.1 9 typ relay")]
    [InlineData("candidate:3 1 udp 1518280447 192.0.2.44 9 typ host\tgeneration 0")]
    [InlineData("candidate:3 1 udp 1518280447 192.0.2.44 9 typ host\nnetwork-id 1")]
    public void TryParse_RejectsEmbeddedNonSpaceWhitespace(string candidateText)
    {
        bool parsed = IceCandidateParser.TryParse(candidateText, null, out _, out IceCandidateRejectReason rejectReason);

        Assert.False(parsed);
        Assert.Equal(IceCandidateRejectReason.InvalidSyntax, rejectReason);
    }

    [Fact]
    public void TryParse_RejectsKnownExtensionAttributesWithoutValues()
    {
        const string MissingValue = "candidate:3 1 udp 1518280447 192.0.2.44 9 typ host raddr";
        const string FollowedByKnownAttribute = "candidate:3 1 udp 1518280447 192.0.2.44 9 typ host raddr rport 50000";

        bool missingValueParsed = IceCandidateParser.TryParse(MissingValue, null, out _, out IceCandidateRejectReason missingValueReason);
        bool followedByKnownParsed = IceCandidateParser.TryParse(FollowedByKnownAttribute, null, out _, out IceCandidateRejectReason followedByKnownReason);

        Assert.False(missingValueParsed);
        Assert.Equal(IceCandidateRejectReason.InvalidSyntax, missingValueReason);
        Assert.False(followedByKnownParsed);
        Assert.Equal(IceCandidateRejectReason.InvalidSyntax, followedByKnownReason);
    }

    [Fact]
    public void TryWrite_RoundTripsHostCandidate()
    {
        IceCandidateAttribute[] attributes =
        [
            new() { Name = "generation", Value = "0" },
            new() { Name = "network-id", Value = "1" }
        ];
        var candidate = new IceCandidate
        {
            Foundation = "842163049",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 1_677_729_535,
            EndPoint = new IPEndPoint(IPAddress.Parse("192.0.2.33"), 54400),
            CandidateType = IceCandidateType.Host,
            SdpMid = "audio",
            ExtensionAttributes = attributes
        };
        var buffer = new ArrayBufferWriter<char>();

        bool written = IceCandidateParser.TryWrite(candidate, buffer);
        bool parsed = IceCandidateParser.TryParse(new string(buffer.WrittenSpan), "audio", out IceCandidate reparsed, out _);

        Assert.True(written);
        Assert.True(parsed);
        Assert.Equal(candidate.Foundation, reparsed.Foundation);
        Assert.Equal(candidate.Priority, reparsed.Priority);
        Assert.Equal(candidate.EndPoint, reparsed.EndPoint);
        Assert.Equal(candidate.CandidateType, reparsed.CandidateType);
        Assert.Equal(candidate.ExtensionAttributes.Length, reparsed.ExtensionAttributes.Length);
    }

    [Fact]
    public void TryWrite_RejectsInvalidCandidateShapes()
    {
        var baseCandidate = new IceCandidate
        {
            Foundation = "842163049",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 1_677_729_535,
            EndPoint = new IPEndPoint(IPAddress.Parse("192.0.2.33"), 54400),
            CandidateType = IceCandidateType.Host
        };

        Assert.False(IceCandidateParser.TryWrite(baseCandidate with { ComponentId = 0 }, new ArrayBufferWriter<char>()));
        Assert.False(IceCandidateParser.TryWrite(baseCandidate with { ComponentId = 3 }, new ArrayBufferWriter<char>()));
        Assert.False(IceCandidateParser.TryWrite(baseCandidate with { Transport = "TCP" }, new ArrayBufferWriter<char>()));
        Assert.False(IceCandidateParser.TryWrite(baseCandidate with { CandidateType = (IceCandidateType)99 }, new ArrayBufferWriter<char>()));
        Assert.False(IceCandidateParser.TryWrite(baseCandidate with { EndPoint = null, Port = null }, new ArrayBufferWriter<char>()));
        Assert.False(IceCandidateParser.TryWrite(baseCandidate with { MdnsHostName = "a1b2c3d4.local" }, new ArrayBufferWriter<char>()));
        Assert.False(IceCandidateParser.TryWrite(
            baseCandidate with
            {
                ExtensionAttributes = new[] { new IceCandidateAttribute { Name = "network id", Value = "1" } }
            },
            new ArrayBufferWriter<char>()));
        Assert.False(IceCandidateParser.TryWrite(
            baseCandidate with
            {
                ExtensionAttributes = new[] { new IceCandidateAttribute { Name = "network-id", Value = "1 2" } }
            },
            new ArrayBufferWriter<char>()));
        Assert.False(IceCandidateParser.TryWrite(
            baseCandidate with
            {
                ExtensionAttributes = new[] { new IceCandidateAttribute { Name = "network-id", Value = "" } }
            },
            new ArrayBufferWriter<char>()));
        Assert.False(IceCandidateParser.TryWrite(
            baseCandidate with
            {
                ExtensionAttributes = new[] { new IceCandidateAttribute { Name = "generation", Value = null } }
            },
            new ArrayBufferWriter<char>()));
    }

    [Fact]
    public void TryWrite_ValidatesBeforeRequestingDestinationStorage()
    {
        var baseCandidate = new IceCandidate
        {
            Foundation = "842163049",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 1_677_729_535,
            EndPoint = new IPEndPoint(IPAddress.Parse("192.0.2.33"), 54400),
            CandidateType = IceCandidateType.Host
        };

        Assert.False(IceCandidateParser.TryWrite(
            baseCandidate with { Foundation = "842163049\r\ntyp relay" },
            new ThrowingCharBufferWriter()));
        Assert.False(IceCandidateParser.TryWrite(
            baseCandidate with
            {
                ExtensionAttributes = new[] { new IceCandidateAttribute { Name = "generation", Value = null } }
            },
            new ThrowingCharBufferWriter()));
        Assert.False(IceCandidateParser.TryWrite(
            baseCandidate with
            {
                ExtensionAttributes = new[] { new IceCandidateAttribute { Name = "network-id", Value = "1\r\ncandidate:evil" } }
            },
            new ThrowingCharBufferWriter()));
        Assert.False(IceCandidateParser.TryWrite(
            baseCandidate with
            {
                EndPoint = null,
                Port = 60769,
                MdnsHostName = "a1b2c3d4.local\r\ncandidate:evil"
            },
            new ThrowingCharBufferWriter()));
    }

    private sealed class ThrowingCharBufferWriter : IBufferWriter<char>
    {
        public void Advance(int count)
        {
            throw new InvalidOperationException("Advance should not be called.");
        }

        public Memory<char> GetMemory(int sizeHint = 0)
        {
            throw new InvalidOperationException("GetMemory should not be called.");
        }

        public Span<char> GetSpan(int sizeHint = 0)
        {
            throw new InvalidOperationException("GetSpan should not be called.");
        }
    }
}
