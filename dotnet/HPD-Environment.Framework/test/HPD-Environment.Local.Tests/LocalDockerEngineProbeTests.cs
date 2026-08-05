namespace HPD.Environment.Local.Tests;

using System.Net.Sockets;
using System.Text;

public sealed class LocalDockerEngineProbeTests
{
    [Fact]
    public void Explicit_configuration_wins_over_discovery()
    {
        string selected = LocalDockerEngineProbe.SelectSocketPath(
            "/configured/docker.sock",
            enableWellKnownDiscovery: true,
            ["/discovered/docker.sock"],
            path => path is "/configured/docker.sock" or "/discovered/docker.sock");

        Assert.Equal("/configured/docker.sock", selected);
    }

    [Fact]
    public void Missing_explicit_socket_fails_closed()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            LocalDockerEngineProbe.SelectSocketPath(
                "/missing/docker.sock",
                enableWellKnownDiscovery: true,
                ["/discovered/docker.sock"],
                static _ => false));

        Assert.Contains("does not exist", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Disabled_discovery_without_configuration_fails_closed()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            LocalDockerEngineProbe.SelectSocketPath(
                configuredSocketPath: null,
                enableWellKnownDiscovery: false,
                ["/discovered/docker.sock"],
                static _ => true));

        Assert.Contains("discovery is disabled", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void No_viable_well_known_socket_fails_closed()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            LocalDockerEngineProbe.SelectSocketPath(
                configuredSocketPath: null,
                enableWellKnownDiscovery: true,
                ["/one/docker.sock", "/two/docker.sock"],
                static _ => false));

        Assert.Contains("No supported", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Multiple_viable_well_known_sockets_are_an_ambiguity_error()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            LocalDockerEngineProbe.SelectSocketPath(
                configuredSocketPath: null,
                enableWellKnownDiscovery: true,
                ["/one/docker.sock", "/two/docker.sock"],
                static _ => true));

        Assert.Contains("Multiple", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_aliases_do_not_create_false_ambiguity()
    {
        string selected = LocalDockerEngineProbe.SelectSocketPath(
            configuredSocketPath: null,
            enableWellKnownDiscovery: true,
            ["/engine/docker.sock", "/engine/./docker.sock"],
            static _ => true);

        Assert.Equal("/engine/docker.sock", selected);
    }

    [Fact]
    public async Task Docker_compatible_engine_is_actively_probed_and_classified()
    {
        string socketPath = SocketPath();
        using var listener = Listen(socketPath, backlog: 2);
        Task server = ServeAsync(
            listener,
            JsonResponse(
                """
                {"Version":"28.3.2","ApiVersion":"1.51","Os":"linux","Arch":"arm64","GitCommit":"abc","BuildTime":"2026-07-29T00:00:00Z"}
                """),
            JsonResponse(
                """
                {"ID":"engine-1","DockerRootDir":"/var/lib/docker","Driver":"overlay2","SecurityOptions":["name=rootless"]}
                """));
        try
        {
            var probe = new LocalDockerEngineProbe(
                new LocalEnvironmentProviderOptions
                {
                    EngineSocketPath = socketPath,
                    ProbeTimeout = TimeSpan.FromSeconds(2),
                });

            LocalEngineObservation observed = await probe.ProbeAsync();

            Assert.Equal("28.3.2", observed.ServerVersion);
            Assert.Equal("1.51", observed.ApiVersion);
            Assert.Equal("linux", observed.OperatingSystem);
            Assert.Equal("arm64", observed.Architecture);
            Assert.True(observed.IsRootless);
            Assert.StartsWith("sha256:", observed.Fingerprint, StringComparison.Ordinal);
            await server;
        }
        finally
        {
            listener.Close();
            DeleteSocket(socketPath);
        }
    }

    [Fact]
    public async Task Chunked_engine_responses_are_decoded_before_json_parsing()
    {
        string socketPath = SocketPath();
        using var listener = Listen(socketPath, backlog: 2);
        Task server = ServeAsync(
            listener,
            ChunkedJsonResponse(
                """{"Version":"29.4.0","ApiVersion":"1.53","Os":"linux","Arch":"arm64"}"""),
            ChunkedJsonResponse(
                """{"ID":"chunked-engine","DockerRootDir":"/var/lib/docker","Driver":"overlay2","SecurityOptions":[]}"""));
        try
        {
            var probe = new LocalDockerEngineProbe(
                new LocalEnvironmentProviderOptions
                {
                    EngineSocketPath = socketPath,
                    ProbeTimeout = TimeSpan.FromSeconds(2),
                });

            LocalEngineObservation observed = await probe.ProbeAsync();

            Assert.Equal("29.4.0", observed.ServerVersion);
            Assert.Equal("1.53", observed.ApiVersion);
            await server;
        }
        finally
        {
            listener.Close();
            DeleteSocket(socketPath);
        }
    }

    [Fact]
    public async Task Wrong_engine_response_is_rejected()
    {
        string socketPath = SocketPath();
        using var listener = Listen(socketPath, backlog: 1);
        Task server = ServeAsync(
            listener,
            JsonResponse("""{"product":"not-a-docker-compatible-engine"}"""),
            JsonResponse("""{"product":"not-a-docker-compatible-engine"}"""));
        try
        {
            var probe = new LocalDockerEngineProbe(
                new LocalEnvironmentProviderOptions
                {
                    EngineSocketPath = socketPath,
                    ProbeTimeout = TimeSpan.FromSeconds(2),
                });

            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => probe.ProbeAsync().AsTask());

            Assert.Contains("omitted 'Version'", exception.Message, StringComparison.Ordinal);
            await server;
        }
        finally
        {
            listener.Close();
            DeleteSocket(socketPath);
        }
    }

    [Fact]
    public async Task Inaccessible_configured_socket_is_rejected()
    {
        string socketPath = SocketPath();
        using (Socket listener = Listen(socketPath, backlog: 1))
            listener.Close();
        try
        {
            var probe = new LocalDockerEngineProbe(
                new LocalEnvironmentProviderOptions
                {
                    EngineSocketPath = socketPath,
                    ProbeTimeout = TimeSpan.FromMilliseconds(250),
                });

            await Assert.ThrowsAnyAsync<Exception>(
                () => probe.ProbeAsync().AsTask());
        }
        finally
        {
            DeleteSocket(socketPath);
        }
    }

    private static string SocketPath() =>
        Path.Combine("/tmp", $"hpd-engine-{Guid.NewGuid():N}.sock");

    private static Socket Listen(string socketPath, int backlog)
    {
        var listener = new Socket(
            AddressFamily.Unix,
            SocketType.Stream,
            ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(backlog);
        return listener;
    }

    private static async Task ServeAsync(
        Socket listener,
        params byte[][] responses)
    {
        foreach (byte[] response in responses)
        {
            using Socket client = await listener.AcceptAsync();
            using var stream = new NetworkStream(client, ownsSocket: false);
            byte[] request = new byte[4096];
            int used = 0;
            while (used < request.Length)
            {
                int read = await stream.ReadAsync(request.AsMemory(used));
                if (read == 0)
                    break;
                used += read;
                if (request.AsSpan(0, used).IndexOf("\r\n\r\n"u8) >= 0)
                    break;
            }
            await stream.WriteAsync(response);
        }
    }

    private static byte[] JsonResponse(string body)
    {
        byte[] payload = Encoding.UTF8.GetBytes(body);
        return Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n")
            .Concat(payload)
            .ToArray();
    }

    private static byte[] ChunkedJsonResponse(string body)
    {
        byte[] payload = Encoding.UTF8.GetBytes(body);
        byte[] prefix = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n{payload.Length:X}\r\n");
        return prefix
            .Concat(payload)
            .Concat("\r\n0\r\n\r\n"u8.ToArray())
            .ToArray();
    }

    private static void DeleteSocket(string socketPath)
    {
        if (File.Exists(socketPath))
            File.Delete(socketPath);
    }
}
