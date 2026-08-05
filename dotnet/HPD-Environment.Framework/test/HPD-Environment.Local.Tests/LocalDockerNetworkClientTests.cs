using System.Net.Sockets;
using System.Text;

namespace HPD.Environment.Local.Tests;

public sealed class LocalDockerNetworkClientTests
{
    [Fact]
    public async Task Ensure_accepts_bounded_chunked_docker_response()
    {
        string socketPath = Path.Combine(
            "/tmp",
            $"hpd-net-{Guid.NewGuid():N}.sock");
        using var listener = new Socket(
            AddressFamily.Unix,
            SocketType.Stream,
            ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(4);
        string networkId = new('a', 64);
        string response =
            $$$"""{"Id":"{{{networkId}}}","Name":"hpd-test","Internal":false,"Labels":{"io.hpd.owner":"hpdos"}}""";
        Task server = ServeAsync(
            listener,
            Http(404, "{}"),
            Http(201, $$"""{"Id":"{{networkId}}"}"""),
            Chunked(200, response));
        try
        {
            var client = new LocalDockerNetworkClient();
            LocalEngineNetworkObservation observed =
                await client.EnsureAsync(
                    socketPath,
                    "hpd-test",
                    new Dictionary<string, string>(
                        StringComparer.Ordinal)
                    {
                        ["io.hpd.owner"] = "hpdos",
                    },
                    internalOnly: false);

            Assert.Equal(networkId, observed.Id);
            Assert.Equal("hpd-test", observed.Name);
            await server;
        }
        finally
        {
            listener.Close();
            if (File.Exists(socketPath))
                File.Delete(socketPath);
        }
    }

    [Fact]
    public async Task Ensure_refuses_foreign_existing_network()
    {
        string socketPath = Path.Combine(
            "/tmp",
            $"hpd-net-{Guid.NewGuid():N}.sock");
        using var listener = new Socket(
            AddressFamily.Unix,
            SocketType.Stream,
            ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(1);
        Task server = ServeAsync(
            listener,
            Http(
                200,
                $$$"""{"Id":"{{{new string('b', 64)}}}","Name":"hpd-test","Internal":false,"Labels":{"io.hpd.owner":"foreign"}}"""));
        try
        {
            var client = new LocalDockerNetworkClient();
            InvalidOperationException error =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => client.EnsureAsync(
                        socketPath,
                        "hpd-test",
                        new Dictionary<string, string>(
                            StringComparer.Ordinal)
                        {
                            ["io.hpd.owner"] = "hpdos",
                        },
                        internalOnly: false).AsTask());

            Assert.Contains(
                "NetworkOwnershipConflict",
                error.Message,
                StringComparison.Ordinal);
            await server;
        }
        finally
        {
            listener.Close();
            if (File.Exists(socketPath))
                File.Delete(socketPath);
        }
    }

    [Fact]
    public async Task Ensure_refuses_extra_labels_as_external_mutation()
    {
        string socketPath = Path.Combine(
            "/tmp",
            $"hpd-net-{Guid.NewGuid():N}.sock");
        using var listener = new Socket(
            AddressFamily.Unix,
            SocketType.Stream,
            ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(1);
        Task server = ServeAsync(
            listener,
            Http(
                200,
                $$$"""{"Id":"{{{new string('c', 64)}}}","Name":"hpd-test","Internal":false,"Labels":{"io.hpd.owner":"hpdos","external.mutation":"true"}}"""));
        try
        {
            var client = new LocalDockerNetworkClient();
            InvalidOperationException error =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => client.EnsureAsync(
                        socketPath,
                        "hpd-test",
                        new Dictionary<string, string>(
                            StringComparer.Ordinal)
                        {
                            ["io.hpd.owner"] = "hpdos",
                        },
                        internalOnly: false).AsTask());

            Assert.Contains(
                "NetworkOwnershipConflict",
                error.Message,
                StringComparison.Ordinal);
            await server;
        }
        finally
        {
            listener.Close();
            if (File.Exists(socketPath))
                File.Delete(socketPath);
        }
    }

    private static async Task ServeAsync(
        Socket listener,
        params byte[][] responses)
    {
        foreach (byte[] response in responses)
        {
            using Socket client = await listener.AcceptAsync();
            using var stream = new NetworkStream(
                client,
                ownsSocket: false);
            await ReadRequestAsync(stream);
            await stream.WriteAsync(response);
        }
    }

    private static async Task ReadRequestAsync(NetworkStream stream)
    {
        var bytes = new List<byte>();
        byte[] buffer = new byte[1024];
        int headerEnd = -1;
        int contentLength = 0;
        while (true)
        {
            int read = await stream.ReadAsync(buffer);
            if (read == 0)
                return;
            bytes.AddRange(buffer.AsSpan(0, read).ToArray());
            if (headerEnd < 0)
            {
                headerEnd = bytes.ToArray().AsSpan()
                    .IndexOf("\r\n\r\n"u8);
                if (headerEnd >= 0)
                {
                    string headers = Encoding.ASCII.GetString(
                        bytes.ToArray(),
                        0,
                        headerEnd);
                    string? length = headers.Split(
                            "\r\n",
                            StringSplitOptions.None)
                        .SingleOrDefault(line => line.StartsWith(
                            "Content-Length:",
                            StringComparison.OrdinalIgnoreCase));
                    contentLength = length is null
                        ? 0
                        : int.Parse(
                            length["Content-Length:".Length..]);
                }
            }
            if (headerEnd >= 0 &&
                bytes.Count >= headerEnd + 4 + contentLength)
                return;
        }
    }

    private static byte[] Http(int status, string body)
    {
        byte[] payload = Encoding.UTF8.GetBytes(body);
        string reason = status switch
        {
            200 => "OK",
            201 => "Created",
            404 => "Not Found",
            _ => "Status",
        };
        return Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n")
            .Concat(payload)
            .ToArray();
    }

    private static byte[] Chunked(int status, string body)
    {
        byte[] payload = Encoding.UTF8.GetBytes(body);
        byte[] prefix = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} OK\r\nContent-Type: application/json\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n{payload.Length:x}\r\n");
        return prefix
            .Concat(payload)
            .Concat("\r\n0\r\n\r\n"u8.ToArray())
            .ToArray();
    }
}
