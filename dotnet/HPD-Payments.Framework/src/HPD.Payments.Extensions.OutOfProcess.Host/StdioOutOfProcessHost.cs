using System.Buffers.Binary;
using HPD.Payments.Extensions.OutOfProcess;

namespace HPD.Payments.Extensions.OutOfProcess.Host;

/// <summary>Runs one bounded authenticated request over an explicitly supplied byte-stream boundary.</summary>
internal static class StdioOutOfProcessHost
{
    private const int FrameOverheadAllowance = 4096;

    internal static async Task<int> RunSingleAsync(AuthenticatedOutOfProcessHost host, Stream input, Stream output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host); ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(output);
        var lengthBytes = new byte[4];
        if (!await ReadExactAsync(input, lengthBytes, cancellationToken).ConfigureAwait(false)) return 80;
        int length = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);
        if (length is < 1 or > OutOfProcessFrame.MaximumPayloadBytes + FrameOverheadAllowance) return 81;
        var wire = new byte[length];
        if (!await ReadExactAsync(input, wire, cancellationToken).ConfigureAwait(false) ||
            !OutOfProcessProtocol.TryDecode(wire, out OutOfProcessFrame? request) || request is null) return 82;
        OutOfProcessTransportResult result = await host.ProcessAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.State != OutOfProcessTransportState.ResponseReceived || result.Response is null) return 83;
        byte[] responseWire = OutOfProcessProtocol.Encode(result.Response);
        BinaryPrimitives.WriteInt32BigEndian(lengthBytes, responseWire.Length);
        await output.WriteAsync(lengthBytes, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(responseWire, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private static async Task<bool> ReadExactAsync(Stream stream, Memory<byte> destination, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < destination.Length)
        {
            int read = await stream.ReadAsync(destination[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0) return false;
            offset += read;
        }
        return true;
    }
}
