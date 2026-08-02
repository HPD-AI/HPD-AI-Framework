using System.Buffers;
using HPD.Gateway.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace HPD.Gateway.Inspection;

internal sealed class GatewayInspectionExecutor(GatewayInspectionRegistry registry)
{
    private readonly GatewayInspectionRegistry _registry = registry;

    internal async Task ExecuteAsync(HttpContext context, GatewayInspectionSelection selection, RequestDelegate next)
    {
        if (!_registry.TryGet(selection.InspectorName, out var inspector))
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return;
        }
        if (context.Request.ContentLength is { } length && length > selection.MaximumAcceptedBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }
        if (!IsSupported(context, selection.Mode, out var unsupportedStatus))
        {
            context.Response.StatusCode = unsupportedStatus;
            return;
        }

        if (selection.Mode == RequestInspectionMode.BoundedPrefix)
            await ExecutePrefixAsync(context, selection, inspector, next).ConfigureAwait(false);
        else
            await ExecuteCompleteAsync(context, selection, inspector, next).ConfigureAwait(false);
    }

    private static bool IsSupported(HttpContext context, RequestInspectionMode mode, out int status)
    {
        status = StatusCodes.Status415UnsupportedMediaType;
        if (context.WebSockets.IsWebSocketRequest || HttpMethods.IsConnect(context.Request.Method)) return false;
        if (context.Request.Protocol == "HTTP/3") return false;
        var contentType = context.Request.ContentType;
        if (contentType?.StartsWith("application/grpc", StringComparison.OrdinalIgnoreCase) == true) return false;
        if (mode == RequestInspectionMode.CompleteBody && contentType?.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase) == true) return false;
        if (mode == RequestInspectionMode.BoundedPrefix && context.Request.ContentLength is null)
        {
            status = StatusCodes.Status411LengthRequired;
            return false;
        }
        if (mode == RequestInspectionMode.CompleteBody && context.Request.Headers.ContainsKey("Trailer")) return false;
        return true;
    }

    private static async Task ExecutePrefixAsync(HttpContext context, GatewayInspectionSelection selection, IGatewayRequestInspector inspector, RequestDelegate next)
    {
        var original = context.Request.Body;
        var maximum = Math.Min(selection.MaximumInspectedBytes!.Value, checked((int)Math.Min(context.Request.ContentLength!.Value, int.MaxValue)));
        var rented = ArrayPool<byte>.Shared.Rent(Math.Max(1, maximum));
        var forwarding = false;
        try
        {
            var read = 0;
            while (read < maximum)
            {
                var count = await original.ReadAsync(rented.AsMemory(read, maximum - read), context.RequestAborted).ConfigureAwait(false);
                if (count == 0) break;
                read += count;
            }
            var completeness = read == 0 ? GatewayInspectionCompleteness.NoBody :
                context.Request.ContentLength == read ? GatewayInspectionCompleteness.CompleteBody : GatewayInspectionCompleteness.PrefixOnly;
            await using var view = new MemoryStream(rented, 0, read, writable: false, publiclyVisible: false);
            var decision = await inspector.InspectAsync(new GatewayInspectionContext(view, completeness, read, context.Request.Headers.ContainsKey("Content-Encoding")), context.RequestAborted).ConfigureAwait(false);
            context.Features.Set<IGatewayInspectionFeature>(new GatewayInspectionOutcome(selection.Mode, completeness, read, decision.Disposition, decision.ReasonCode));
            if (decision.Disposition == GatewayInspectionDisposition.Rejected)
            {
                context.Response.StatusCode = decision.StatusCode;
                return;
            }
            context.Request.Body = new PrefixReplayStream(rented, read, original);
            forwarding = true;
            await next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!forwarding && context.RequestAborted.IsCancellationRequested) { }
        catch (IOException) when (!forwarding)
        {
            if (!context.Response.HasStarted) context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        }
        catch (Exception) when (!forwarding)
        {
            if (!context.Response.HasStarted) context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        }
        finally
        {
            context.Request.Body = original;
            ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }
    }

    private static async Task ExecuteCompleteAsync(HttpContext context, GatewayInspectionSelection selection, IGatewayRequestInspector inspector, RequestDelegate next)
    {
        var forwarding = false;
        try
        {
            var threshold = selection.SpillPolicy == RequestInspectionSpillPolicy.Disabled
                ? checked((int)selection.MaximumAcceptedBodyBytes)
                : selection.MemoryThresholdBytes!.Value;
            context.Request.EnableBuffering(threshold, selection.MaximumAcceptedBodyBytes + 1);
            var scratch = ArrayPool<byte>.Shared.Rent(16 * 1024);
            long observed = 0;
            try
            {
                int read;
                while ((read = await context.Request.Body.ReadAsync(scratch, context.RequestAborted).ConfigureAwait(false)) != 0)
                {
                    observed += read;
                    if (observed > selection.MaximumAcceptedBodyBytes)
                    {
                        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                        return;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(scratch, clearArray: true);
            }
            var trailers = context.Features.Get<IHttpRequestTrailersFeature>();
            if (trailers is { Available: true } && trailers.Trailers.Count > 0)
            {
                context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                return;
            }
            context.Request.Body.Position = 0;
            var completeness = observed == 0 ? GatewayInspectionCompleteness.NoBody : GatewayInspectionCompleteness.CompleteBody;
            var decision = await inspector.InspectAsync(new GatewayInspectionContext(context.Request.Body, completeness, observed, context.Request.Headers.ContainsKey("Content-Encoding")), context.RequestAborted).ConfigureAwait(false);
            context.Request.Body.Position = 0;
            context.Features.Set<IGatewayInspectionFeature>(new GatewayInspectionOutcome(selection.Mode, completeness, observed, decision.Disposition, decision.ReasonCode));
            if (decision.Disposition == GatewayInspectionDisposition.Rejected)
            {
                context.Response.StatusCode = decision.StatusCode;
                return;
            }
            forwarding = true;
            await next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!forwarding && context.RequestAborted.IsCancellationRequested) { }
        catch (IOException) when (!forwarding)
        {
            if (!context.Response.HasStarted) context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        }
        catch (Exception) when (!forwarding)
        {
            if (!context.Response.HasStarted) context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        }
    }
}

internal sealed class PrefixReplayStream(byte[] prefix, int length, Stream remainder) : Stream
{
    private int _position;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _position; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        if (_position < length)
        {
            var count = Math.Min(buffer.Length, length - _position);
            prefix.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
        return remainder.Read(buffer);
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position < length)
        {
            var count = Math.Min(buffer.Length, length - _position);
            prefix.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
        return await remainder.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
    }
    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
