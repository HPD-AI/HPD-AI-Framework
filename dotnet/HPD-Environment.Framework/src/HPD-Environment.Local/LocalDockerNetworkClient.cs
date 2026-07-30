namespace HPD.Environment.Local;

using System.Net.Sockets;
using System.Text;
using System.Text.Json;

internal sealed record LocalEngineNetworkObservation(
    string Id,
    string Name,
    IReadOnlyDictionary<string, string> Labels,
    bool Internal);

internal interface ILocalEngineNetworkClient
{
    ValueTask<LocalEngineNetworkObservation?> ObserveAsync(
        string socketPath,
        string identifier,
        CancellationToken cancellationToken = default);

    ValueTask<LocalEngineNetworkObservation> EnsureAsync(
        string socketPath,
        string name,
        IReadOnlyDictionary<string, string> labels,
        bool internalOnly,
        CancellationToken cancellationToken = default);

    ValueTask DeleteAsync(
        string socketPath,
        LocalEngineNetworkObservation expected,
        CancellationToken cancellationToken = default);
}

internal sealed class LocalDockerNetworkClient : ILocalEngineNetworkClient
{
    private const int MaxResponseBytes = 128 * 1024;

    public async ValueTask<LocalEngineNetworkObservation?> ObserveAsync(
        string socketPath,
        string identifier,
        CancellationToken cancellationToken = default)
    {
        LocalHttpResponse inspected = await SendAsync(
            socketPath,
            "GET",
            $"/networks/{Uri.EscapeDataString(identifier)}",
            body: null,
            cancellationToken).ConfigureAwait(false);
        return inspected.StatusCode switch
        {
            200 => ParseNetwork(inspected.Body),
            404 => null,
            _ => throw HttpFailure("inspect", inspected),
        };
    }

    public async ValueTask<LocalEngineNetworkObservation> EnsureAsync(
        string socketPath,
        string name,
        IReadOnlyDictionary<string, string> labels,
        bool internalOnly,
        CancellationToken cancellationToken = default)
    {
        LocalEngineNetworkObservation? inspected =
            await ObserveAsync(
                socketPath,
                name,
                cancellationToken).ConfigureAwait(false);
        if (inspected is not null)
        {
            RequireExactOwnership(
                inspected,
                name,
                labels,
                internalOnly);
            return inspected;
        }

        byte[] body;
        using (var output = new MemoryStream())
        {
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();
                writer.WriteString("Name", name);
                writer.WriteBoolean("CheckDuplicate", true);
                writer.WriteString("Driver", "bridge");
                writer.WriteBoolean("Internal", internalOnly);
                writer.WriteBoolean("Attachable", false);
                writer.WriteStartObject("Labels");
                foreach (KeyValuePair<string, string> label in
                         labels.OrderBy(
                             static item => item.Key,
                             StringComparer.Ordinal))
                    writer.WriteString(label.Key, label.Value);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            body = output.ToArray();
        }
        LocalHttpResponse created = await SendAsync(
            socketPath,
            "POST",
            "/networks/create",
            body,
            cancellationToken).ConfigureAwait(false);
        if (created.StatusCode != 201)
            throw HttpFailure("create", created);

        LocalEngineNetworkObservation result =
            await ObserveAsync(
                socketPath,
                name,
                cancellationToken).ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "LocalEnvironment.NetworkEngineOperationFailed: Docker network disappeared after creation.");
        RequireExactOwnership(result, name, labels, internalOnly);
        return result;
    }

    public async ValueTask DeleteAsync(
        string socketPath,
        LocalEngineNetworkObservation expected,
        CancellationToken cancellationToken = default)
    {
        LocalHttpResponse inspected = await SendAsync(
            socketPath,
            "GET",
            $"/networks/{Uri.EscapeDataString(expected.Id)}",
            body: null,
            cancellationToken).ConfigureAwait(false);
        if (inspected.StatusCode == 404)
            return;
        if (inspected.StatusCode != 200)
            throw HttpFailure("pre-delete inspect", inspected);
        LocalEngineNetworkObservation current =
            ParseNetwork(inspected.Body);
        RequireExactOwnership(
            current,
            expected.Name,
            expected.Labels,
            expected.Internal);
        if (!string.Equals(
                current.Id,
                expected.Id,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "LocalEnvironment.NetworkIdentityChanged: refusing to delete a network whose engine identity changed.");

        LocalHttpResponse deleted = await SendAsync(
            socketPath,
            "DELETE",
            $"/networks/{Uri.EscapeDataString(expected.Id)}",
            body: null,
            cancellationToken).ConfigureAwait(false);
        if (deleted.StatusCode is not (204 or 404))
            throw HttpFailure("delete", deleted);
    }

    private static LocalEngineNetworkObservation ParseNetwork(
        ReadOnlyMemory<byte> body)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        string id = RequiredString(root, "Id");
        string name = RequiredString(root, "Name");
        bool internalOnly =
            root.TryGetProperty("Internal", out JsonElement internalValue) &&
            internalValue.ValueKind == JsonValueKind.True;
        var labels = new Dictionary<string, string>(
            StringComparer.Ordinal);
        if (root.TryGetProperty("Labels", out JsonElement labelValue) &&
            labelValue.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty label in labelValue.EnumerateObject())
            {
                if (label.Value.ValueKind == JsonValueKind.String)
                    labels[label.Name] =
                        label.Value.GetString() ?? string.Empty;
            }
        }
        return new LocalEngineNetworkObservation(
            id,
            name,
            labels,
            internalOnly);
    }

    private static void RequireExactOwnership(
        LocalEngineNetworkObservation observed,
        string expectedName,
        IReadOnlyDictionary<string, string> expectedLabels,
        bool expectedInternal)
    {
        if (!string.Equals(
            observed.Name,
                expectedName,
                StringComparison.Ordinal) ||
            observed.Internal != expectedInternal ||
            observed.Labels.Count != expectedLabels.Count ||
            expectedLabels.Any(label =>
                !observed.Labels.TryGetValue(
                    label.Key,
                    out string? value) ||
                !string.Equals(
                    value,
                    label.Value,
                    StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "LocalEnvironment.NetworkOwnershipConflict: an existing engine network does not exactly match HPD ownership and immutable intent.");
        }
    }

    private static string RequiredString(
        JsonElement value,
        string property) =>
        value.TryGetProperty(property, out JsonElement found) &&
        found.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(found.GetString())
            ? found.GetString()!
            : throw new InvalidOperationException(
                $"LocalEnvironment.NetworkResponseMalformed: engine response omitted '{property}'.");

    private static async ValueTask<LocalHttpResponse> SendAsync(
        string socketPath,
        string method,
        string path,
        byte[]? body,
        CancellationToken cancellationToken)
    {
        using var socket = new Socket(
            AddressFamily.Unix,
            SocketType.Stream,
            ProtocolType.Unspecified);
        await socket.ConnectAsync(
            new UnixDomainSocketEndPoint(socketPath),
            cancellationToken).ConfigureAwait(false);
        using var stream = new NetworkStream(socket, ownsSocket: false);
        int bodyLength = body?.Length ?? 0;
        byte[] headers = Encoding.ASCII.GetBytes(
            $"{method} {path} HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\nAccept: application/json\r\nContent-Type: application/json\r\nContent-Length: {bodyLength}\r\n\r\n");
        await stream.WriteAsync(headers, cancellationToken)
            .ConfigureAwait(false);
        if (body is not null)
            await stream.WriteAsync(body, cancellationToken)
                .ConfigureAwait(false);

        using var response = new MemoryStream();
        byte[] buffer = new byte[4096];
        while (true)
        {
            int read = await stream.ReadAsync(
                buffer,
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            if (response.Length + read > MaxResponseBytes)
                throw new InvalidOperationException(
                    "LocalEnvironment.NetworkResponseTooLarge: engine response exceeded 128 KiB.");
            response.Write(buffer, 0, read);
        }
        byte[] bytes = response.ToArray();
        ReadOnlySpan<byte> separator = "\r\n\r\n"u8;
        int headerEnd = bytes.AsSpan().IndexOf(separator);
        if (headerEnd < 0)
            throw new InvalidOperationException(
                "LocalEnvironment.NetworkResponseMalformed: engine returned malformed HTTP.");
        string headerText =
            Encoding.ASCII.GetString(bytes, 0, headerEnd);
        string firstLine = headerText.Split(
            "\r\n",
            StringSplitOptions.None)[0];
        string[] statusParts = firstLine.Split(' ');
        if (statusParts.Length < 2 ||
            !int.TryParse(statusParts[1], out int statusCode))
            throw new InvalidOperationException(
                "LocalEnvironment.NetworkResponseMalformed: engine returned a malformed status line.");
        int bodyOffset = headerEnd + separator.Length;
        ReadOnlyMemory<byte> responseBody =
            DecodeResponseBody(
                headerText,
                bytes.AsMemory(bodyOffset));
        return new LocalHttpResponse(
            statusCode,
            responseBody);
    }

    private static ReadOnlyMemory<byte> DecodeResponseBody(
        string headerText,
        ReadOnlyMemory<byte> body)
    {
        string[] headers = headerText.Split(
            "\r\n",
            StringSplitOptions.None);
        bool chunked = headers.Skip(1).Any(header =>
            header.StartsWith(
                "Transfer-Encoding:",
                StringComparison.OrdinalIgnoreCase) &&
            header["Transfer-Encoding:".Length..]
                .Split(',', StringSplitOptions.TrimEntries)
                .Any(value => string.Equals(
                    value,
                    "chunked",
                    StringComparison.OrdinalIgnoreCase)));
        if (chunked)
            return DecodeChunkedBody(body.Span);

        string? contentLengthHeader = headers.Skip(1)
            .SingleOrDefault(header => header.StartsWith(
                "Content-Length:",
                StringComparison.OrdinalIgnoreCase));
        if (contentLengthHeader is null)
            return body;
        if (!int.TryParse(
                contentLengthHeader["Content-Length:".Length..].Trim(),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int contentLength) ||
            contentLength < 0 ||
            contentLength != body.Length)
            throw new InvalidOperationException(
                "LocalEnvironment.NetworkResponseMalformed: engine returned an invalid Content-Length.");
        return body;
    }

    private static byte[] DecodeChunkedBody(ReadOnlySpan<byte> encoded)
    {
        using var decoded = new MemoryStream();
        int offset = 0;
        while (true)
        {
            int lineEnd = encoded[offset..].IndexOf("\r\n"u8);
            if (lineEnd < 0)
                throw MalformedChunked();
            ReadOnlySpan<byte> sizeBytes =
                encoded.Slice(offset, lineEnd);
            int extension = sizeBytes.IndexOf((byte)';');
            if (extension >= 0)
                sizeBytes = sizeBytes[..extension];
            if (!int.TryParse(
                    Encoding.ASCII.GetString(sizeBytes),
                    System.Globalization.NumberStyles.AllowHexSpecifier,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int size) ||
                size < 0)
                throw MalformedChunked();
            offset = checked(offset + lineEnd + 2);
            if (size == 0)
            {
                if (encoded.Length < offset + 2 ||
                    !encoded.Slice(offset, 2).SequenceEqual("\r\n"u8))
                    throw MalformedChunked();
                return decoded.ToArray();
            }
            if (size > MaxResponseBytes - decoded.Length ||
                encoded.Length < offset + size + 2 ||
                !encoded.Slice(offset + size, 2)
                    .SequenceEqual("\r\n"u8))
                throw MalformedChunked();
            decoded.Write(encoded.Slice(offset, size));
            offset = checked(offset + size + 2);
        }
    }

    private static InvalidOperationException MalformedChunked() =>
        new(
            "LocalEnvironment.NetworkResponseMalformed: engine returned malformed chunked HTTP.");

    private static InvalidOperationException HttpFailure(
        string operation,
        LocalHttpResponse response)
    {
        string detail;
        try
        {
            detail = Encoding.UTF8.GetString(response.Body.Span);
        }
        catch
        {
            detail = string.Empty;
        }
        if (detail.Length > 512)
            detail = detail[..512];
        return new InvalidOperationException(
            $"LocalEnvironment.NetworkEngineOperationFailed: Docker network {operation} returned HTTP {response.StatusCode}: {detail}");
    }

    private readonly record struct LocalHttpResponse(
        int StatusCode,
        ReadOnlyMemory<byte> Body);
}
