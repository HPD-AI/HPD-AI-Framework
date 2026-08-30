using System.ComponentModel;
using System.Text;
using HPD.Agent;
using HPD.Agent.Middleware;

public sealed record ReadCommandOutputRequest(
    ContentAddress Address,
    int MaxBytes = 262_144,
    long Offset = 0);

public sealed record ReadCommandOutputResult(
    string ContentType,
    long StoredBytes,
    string Encoding,
    string Content,
    long? NextOffset);

public partial class CodingToolHarness
{
    [AIFunction]
    [Description("Reads an exact restart-durable execute-command output address. The address must belong to the current session and retain its version and SHA-256 constraints.")]
    public async Task<ReadCommandOutputResult> ReadCommandOutput(
        ReadCommandOutputRequest request,
        FunctionExecutionContext context = null!,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxBytes is <= 0 or > 1_048_576)
            throw new ArgumentOutOfRangeException(nameof(request), "MaxBytes must be between 1 and 1048576.");
        if (request.Offset < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Offset cannot be negative.");
        if (context.ContentStore is null)
            throw new InvalidOperationException("No content store is configured.");
        if (context.ContentStore.PersistenceCapability != ContentStorePersistenceCapability.RestartDurable)
            throw new InvalidOperationException("Command output retrieval requires a restart-durable content store.");
        if (string.IsNullOrWhiteSpace(context.SessionId) ||
            !StringComparer.Ordinal.Equals(request.Address.Scope.Value, context.SessionId))
            throw new UnauthorizedAccessException("The command output address is outside the current session scope.");
        if (string.IsNullOrWhiteSpace(request.Address.Version) || string.IsNullOrWhiteSpace(request.Address.Sha256))
            throw new InvalidOperationException("An exact command output address requires version and SHA-256 constraints.");

        await using var opened = await context.ContentStore.OpenReadAsync(request.Address, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new FileNotFoundException("The referenced command output was not found.");
        if (string.IsNullOrWhiteSpace(context.ThreadId) || opened.Info.Tags is null ||
            !opened.Info.Tags.TryGetValue("thread-id", out var ownerThreadId) ||
            !StringComparer.Ordinal.Equals(ownerThreadId, context.ThreadId))
            throw new UnauthorizedAccessException("The command output address is outside the current thread scope.");
        if (request.Offset > opened.Info.SizeBytes)
            throw new ArgumentOutOfRangeException(nameof(request), "Offset exceeds the stored content length.");
        await SkipAsync(opened.Content, request.Offset, cancellationToken).ConfigureAwait(false);
        var limit = (int)Math.Min(request.MaxBytes, opened.Info.SizeBytes - request.Offset);
        var bytes = new byte[limit];
        var read = 0;
        while (read < bytes.Length)
        {
            var count = await opened.Content.ReadAsync(bytes.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (count == 0)
                break;
            read += count;
        }
        if (read != bytes.Length)
            Array.Resize(ref bytes, read);

        var isCombined = opened.Info.Tags.TryGetValue("stream", out var streamKind) &&
            StringComparer.Ordinal.Equals(streamKind, "combined");
        var isRawOutput = StringComparer.Ordinal.Equals(streamKind, "stdout") ||
            StringComparer.Ordinal.Equals(streamKind, "stderr");
        var isBinary = opened.Info.Tags.TryGetValue("binary", out var binaryValue) &&
            bool.TryParse(binaryValue, out var parsedBinary) && parsedBinary;
        var isText = isCombined || !isRawOutput && !isBinary && (
            opened.Info.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
            opened.Info.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase));
        var consumedBytes = 0;
        var content = isCombined
            ? DecodeCombinedOutput(bytes, out consumedBytes)
            : isText ? Encoding.UTF8.GetString(bytes) : Convert.ToBase64String(bytes);
        var consumed = isCombined ? consumedBytes : read;
        if (isCombined && consumed < read && request.Offset + read == opened.Info.SizeBytes)
            consumed = read; // The capped spool ended inside a final frame; discard that incomplete tail and terminate.
        if (isCombined && consumed == 0 && read > 0 && request.Offset + read < opened.Info.SizeBytes)
            throw new InvalidOperationException("MaxBytes is too small for the next combined-output frame; retry this offset with a larger value.");
        var nextOffset = request.Offset + consumed < opened.Info.SizeBytes
            ? request.Offset + consumed
            : (long?)null;
        return new ReadCommandOutputResult(
            opened.Info.ContentType,
            opened.Info.SizeBytes,
            isCombined ? "framed-base64" : isText ? "utf-8" : "base64",
            content,
            nextOffset);
    }

    private static async ValueTask SkipAsync(Stream stream, long offset, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
        {
            stream.Seek(offset, SeekOrigin.Begin);
            return;
        }
        var buffer = new byte[81920];
        while (offset > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, offset)), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("The content ended before the requested offset.");
            offset -= read;
        }
    }

    private static string DecodeCombinedOutput(ReadOnlySpan<byte> framed, out int consumedBytes)
    {
        const string prefix = "[hpd.execute-command.interleaved.v1:";
        var result = new StringBuilder();
        var offset = 0;
        while (offset < framed.Length)
        {
            var frameStart = offset;
            var newline = framed[offset..].IndexOf((byte)'\n');
            if (newline < 0)
                break;
            var header = Encoding.UTF8.GetString(framed.Slice(offset, newline));
            var fields = header.StartsWith(prefix, StringComparison.Ordinal) && header.EndsWith(']')
                ? header[prefix.Length..^1].Split(':')
                : [];
            if (fields.Length != 3 || !int.TryParse(fields[2], out var length) || length < 0)
                throw new InvalidDataException("The combined command output framing is invalid.");
            offset += newline + 1;
            if (length > framed.Length - offset)
            {
                offset = frameStart;
                break;
            }
            var frameEnd = offset + length;
            if (frameEnd >= framed.Length || framed[frameEnd] != (byte)'\n')
            {
                offset = frameStart;
                break;
            }
            result.Append('[').Append(fields[0]).Append(' ').Append(fields[1]).Append(" base64] ");
            result.Append(Convert.ToBase64String(framed.Slice(offset, length))).AppendLine();
            offset = frameEnd + 1;
        }
        consumedBytes = offset;
        return result.ToString();
    }
}
