using System.Globalization;
using System.Text;

namespace HPD.Agent.ToolHarness.Coding.Debugging.Protocol;

public sealed record DebugProtocolFramingLimits
{
    public const int HardMaxHeaderBytes = 64 * 1024;
    public const int HardMaxBodyBytes = 16 * 1024 * 1024;

    public int MaxHeaderBytes { get; init; } = 16 * 1024;
    public int MaxBodyBytes { get; init; } = 4 * 1024 * 1024;

    internal void Validate()
    {
        if (MaxHeaderBytes is <= 0 or > HardMaxHeaderBytes)
            throw new ArgumentOutOfRangeException(nameof(MaxHeaderBytes));
        if (MaxBodyBytes is <= 0 or > HardMaxBodyBytes)
            throw new ArgumentOutOfRangeException(nameof(MaxBodyBytes));
    }
}

public enum DebugProtocolFramingError
{
    HeaderTooLarge,
    NonAsciiHeader,
    InvalidHeaderGrammar,
    MissingContentLength,
    DuplicateContentLength,
    InvalidContentLength,
    BodyTooLarge,
    InvalidUtf8
}

public sealed class DebugProtocolFramingException : Exception
{
    public DebugProtocolFramingException(DebugProtocolFramingError error)
        : base($"Invalid DAP frame ({error}).") => Error = error;

    public DebugProtocolFramingError Error { get; }
}

/// <summary>Strict incremental decoder for DAP Content-Length frames.</summary>
public sealed class DebugProtocolFramer
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly DebugProtocolFramingLimits _limits;
    private readonly byte[] _header;
    private int _headerLength;
    private byte[]? _body;
    private int _bodyLength;

    public DebugProtocolFramer(DebugProtocolFramingLimits? limits = null)
    {
        _limits = limits ?? new();
        _limits.Validate();
        _header = new byte[_limits.MaxHeaderBytes];
    }

    public IReadOnlyList<ReadOnlyMemory<byte>> Append(ReadOnlySpan<byte> bytes)
    {
        var frames = new List<ReadOnlyMemory<byte>>();
        while (!bytes.IsEmpty)
        {
            if (_body is not null)
            {
                var count = Math.Min(bytes.Length, _body.Length - _bodyLength);
                bytes[..count].CopyTo(_body.AsSpan(_bodyLength));
                _bodyLength += count;
                bytes = bytes[count..];
                if (_bodyLength == _body.Length)
                    CompleteBody(frames);
                continue;
            }

            var value = bytes[0];
            bytes = bytes[1..];
            if (value > 0x7f)
                throw new DebugProtocolFramingException(DebugProtocolFramingError.NonAsciiHeader);
            if (_headerLength == _header.Length)
                throw new DebugProtocolFramingException(DebugProtocolFramingError.HeaderTooLarge);
            _header[_headerLength++] = value;
            if (_headerLength >= 4 &&
                _header[_headerLength - 4] == '\r' && _header[_headerLength - 3] == '\n' &&
                _header[_headerLength - 2] == '\r' && _header[_headerLength - 1] == '\n')
            {
                var bodyLength = ParseHeader(_header.AsSpan(0, _headerLength - 4));
                _headerLength = 0;
                _body = new byte[bodyLength];
                _bodyLength = 0;
            }
        }
        return frames;
    }

    public static byte[] Encode(ReadOnlySpan<byte> utf8Payload, DebugProtocolFramingLimits? limits = null)
    {
        var effectiveLimits = limits ?? new();
        effectiveLimits.Validate();
        if (utf8Payload.IsEmpty)
            throw new DebugProtocolFramingException(DebugProtocolFramingError.InvalidContentLength);
        if (utf8Payload.Length > effectiveLimits.MaxBodyBytes)
            throw new DebugProtocolFramingException(DebugProtocolFramingError.BodyTooLarge);
        ValidateUtf8(utf8Payload);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {utf8Payload.Length.ToString(CultureInfo.InvariantCulture)}\r\n\r\n");
        var frame = new byte[header.Length + utf8Payload.Length];
        header.CopyTo(frame, 0);
        utf8Payload.CopyTo(frame.AsSpan(header.Length));
        return frame;
    }

    private int ParseHeader(ReadOnlySpan<byte> header)
    {
        if (header.IsEmpty)
            throw new DebugProtocolFramingException(DebugProtocolFramingError.MissingContentLength);
        var text = Encoding.ASCII.GetString(header);
        var lines = text.Split("\r\n", StringSplitOptions.None);
        var found = false;
        var contentLength = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0 || line.Contains('\r') || line.Contains('\n'))
                throw new DebugProtocolFramingException(DebugProtocolFramingError.InvalidHeaderGrammar);
            var separator = line.IndexOf(':');
            if (separator <= 0 || separator == line.Length - 1 || line[separator + 1] != ' ')
                throw new DebugProtocolFramingException(DebugProtocolFramingError.InvalidHeaderGrammar);
            var name = line[..separator];
            if (!name.All(character => character is >= '!' and <= '~' && character != ':'))
                throw new DebugProtocolFramingException(DebugProtocolFramingError.InvalidHeaderGrammar);
            if (!name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;
            if (found)
                throw new DebugProtocolFramingException(DebugProtocolFramingError.DuplicateContentLength);
            found = true;
            var value = line[(separator + 2)..];
            if (value.Length == 0 || value.Any(character => character is < '0' or > '9') ||
                !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out contentLength) || contentLength <= 0)
                throw new DebugProtocolFramingException(DebugProtocolFramingError.InvalidContentLength);
            if (contentLength > _limits.MaxBodyBytes)
                throw new DebugProtocolFramingException(DebugProtocolFramingError.BodyTooLarge);
        }
        if (!found)
            throw new DebugProtocolFramingException(DebugProtocolFramingError.MissingContentLength);
        return contentLength;
    }

    private void CompleteBody(List<ReadOnlyMemory<byte>> frames)
    {
        var body = _body!;
        ValidateUtf8(body);
        frames.Add(body);
        _body = null;
        _bodyLength = 0;
    }

    private static void ValidateUtf8(ReadOnlySpan<byte> bytes)
    {
        try
        {
            _ = StrictUtf8.GetCharCount(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new DebugProtocolFramingException(DebugProtocolFramingError.InvalidUtf8);
        }
    }
}
