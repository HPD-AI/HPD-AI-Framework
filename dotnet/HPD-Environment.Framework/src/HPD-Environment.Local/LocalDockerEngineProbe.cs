namespace HPD.Environment.Local;

using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal sealed record LocalEngineObservation(
    string SocketPath,
    string ServerVersion,
    string ApiVersion,
    string OperatingSystem,
    string Architecture,
    string Fingerprint,
    bool IsRootless);

internal interface ILocalEngineProbe
{
    ValueTask<LocalEngineObservation> ProbeAsync(
        CancellationToken cancellationToken = default);
}

internal sealed class LocalDockerEngineProbe(
    LocalEnvironmentProviderOptions options)
    : ILocalEngineProbe
{
    private const int MaxResponseBytes = 64 * 1024;

    public async ValueTask<LocalEngineObservation> ProbeAsync(
        CancellationToken cancellationToken = default)
    {
        string socketPath = ResolveSocketPath();
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.ProbeTimeout);
        string versionJson = await SendAsync(
            socketPath,
            "/version",
            timeout.Token).ConfigureAwait(false);
        string infoJson = await SendAsync(
            socketPath,
            "/info",
            timeout.Token).ConfigureAwait(false);
        using JsonDocument versionDocument =
            JsonDocument.Parse(versionJson);
        using JsonDocument infoDocument =
            JsonDocument.Parse(infoJson);
        JsonElement root = versionDocument.RootElement;
        JsonElement info = infoDocument.RootElement;
        string version = ReadRequired(root, "Version");
        string apiVersion = ReadRequired(root, "ApiVersion");
        string operatingSystem = ReadRequired(root, "Os");
        string architecture = ReadRequired(root, "Arch");
        string stableIdentity = string.Join(
            '\n',
            version,
            apiVersion,
            operatingSystem,
            architecture,
            ReadOptional(root, "GitCommit"),
            ReadOptional(root, "KernelVersion"),
            ReadOptional(root, "BuildTime"),
            ReadOptional(info, "ID"),
            ReadOptional(info, "DockerRootDir"),
            ReadOptional(info, "Driver"),
            socketPath);
        string fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(stableIdentity)))
            .ToLowerInvariant();
        return new LocalEngineObservation(
            socketPath,
            version,
            apiVersion,
            operatingSystem,
            architecture,
            $"sha256:{fingerprint}",
            IsRootlessSocket(socketPath) ||
            HasRootlessSecurityOption(info));
    }

    internal string ResolveSocketPath()
    {
        string user = System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.UserProfile);
        string? runtime = System.Environment.GetEnvironmentVariable(
            "XDG_RUNTIME_DIR");
        string[] candidates =
        [
            Path.Combine(user, ".docker", "run", "docker.sock"),
            string.IsNullOrWhiteSpace(runtime)
                ? string.Empty
                : Path.Combine(runtime, "docker.sock"),
            string.IsNullOrWhiteSpace(runtime)
                ? string.Empty
                : Path.Combine(runtime, "podman", "podman.sock"),
            "/var/run/docker.sock",
            "/run/docker.sock",
        ];
        return SelectSocketPath(
            options.EngineSocketPath,
            options.EnableWellKnownSocketDiscovery,
            candidates,
            File.Exists);
    }

    internal static string SelectSocketPath(
        string? configuredSocketPath,
        bool enableWellKnownDiscovery,
        IEnumerable<string> wellKnownCandidates,
        Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(wellKnownCandidates);
        ArgumentNullException.ThrowIfNull(exists);
        if (!string.IsNullOrWhiteSpace(configuredSocketPath))
        {
            string configured = CanonicalSocketPath(configuredSocketPath);
            if (!exists(configured))
                throw new InvalidOperationException(
                    $"Configured local engine socket '{configured}' does not exist.");
            return configured;
        }
        if (!enableWellKnownDiscovery)
        {
            throw new InvalidOperationException(
                "No local engine socket is configured and well-known discovery is disabled.");
        }

        string[] existing = wellKnownCandidates
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(CanonicalSocketPath)
            .Distinct(StringComparer.Ordinal)
            .Where(exists)
            .ToArray();
        return existing.Length switch
        {
            1 => existing[0],
            0 => throw new InvalidOperationException(
                "No supported local container-engine socket was found. Configure EngineSocketPath explicitly."),
            _ => throw new InvalidOperationException(
                "Multiple local container-engine sockets were found. Configure EngineSocketPath explicitly."),
        };
    }

    private static string CanonicalSocketPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        try
        {
            FileSystemInfo? target =
                File.ResolveLinkTarget(
                    fullPath,
                    returnFinalTarget: true);
            return target?.FullName is { Length: > 0 } resolved
                ? Path.GetFullPath(resolved)
                : fullPath;
        }
        catch (IOException)
        {
            return fullPath;
        }
        catch (UnauthorizedAccessException)
        {
            return fullPath;
        }
    }

    private static async Task<string> SendAsync(
        string socketPath,
        string requestPath,
        CancellationToken cancellationToken)
    {
        using var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, token) =>
            {
                var socket = new Socket(
                    AddressFamily.Unix,
                    SocketType.Stream,
                    ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(
                            new UnixDomainSocketEndPoint(socketPath),
                            token)
                        .ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
        using var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"http://localhost{requestPath}"));
        request.Headers.Accept.ParseAdd("application/json");
        using HttpResponseMessage response =
            await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
            throw new InvalidOperationException(
                $"The local engine probe failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
        if (response.Content.Headers.ContentLength is > MaxResponseBytes)
            throw new InvalidOperationException(
                "The local engine probe response exceeded its byte limit.");

        await using Stream content =
            await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
        using var payload = new MemoryStream();
        byte[] buffer = new byte[4096];
        while (true)
        {
            int read = await content.ReadAsync(
                buffer,
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            if (payload.Length + read > MaxResponseBytes)
                throw new InvalidOperationException(
                    "The local engine probe response exceeded its byte limit.");
            payload.Write(buffer, 0, read);
        }
        return new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true)
            .GetString(payload.GetBuffer(), 0, checked((int)payload.Length));
    }

    private static string ReadRequired(JsonElement root, string property)
    {
        string? value = ReadOptional(root, property);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"The local engine response omitted '{property}'.");
    }

    private static string ReadOptional(JsonElement root, string property) =>
        root.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool IsRootlessSocket(string path) =>
        path.Contains("/run/user/", StringComparison.OrdinalIgnoreCase) ||
        path.Contains(
            "/podman/",
            StringComparison.OrdinalIgnoreCase);

    private static bool HasRootlessSecurityOption(JsonElement info) =>
        info.TryGetProperty(
            "SecurityOptions",
            out JsonElement options) &&
        options.ValueKind == JsonValueKind.Array &&
        options.EnumerateArray().Any(option =>
            option.ValueKind == JsonValueKind.String &&
            option.GetString()?.Contains(
                "rootless",
                StringComparison.OrdinalIgnoreCase) == true);
}
