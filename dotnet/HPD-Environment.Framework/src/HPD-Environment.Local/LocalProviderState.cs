namespace HPD.Environment.Local;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

internal sealed class LocalProviderState
{
    private readonly object _engineGate = new();
    private bool _engineWasReady;
    private string? _engineFingerprint;
    private long _engineGeneration;
    private EngineAuthorityMode _engineAuthorityMode =
        EngineAuthorityMode.ProviderDefined;
    private string? _engineSocketPath;
    private readonly Dictionary<string, long>
        _authorityEngineGenerations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long>
        _endpointEngineGenerations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long>
        _networkEngineGenerations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LocalEngineNetworkObservation>
        _engineNetworks = new(StringComparer.Ordinal);
    private readonly BoundedAuditLedger<string, AuthorityAuditEvent>
        _authorityAudit = new(32);
    private Action? _releaseStorage;

    public LocalProviderState(LocalEnvironmentProviderOptions options)
    {
        Options = options;
        Ledger = new ProviderResourceLedger(
            LocalEnvironmentProviderDescriptor.ProviderId,
            LocalProviderGeneration.Next(options.WorkloadStateRoot));
    }

    public LocalEnvironmentProviderOptions Options { get; }
    public ProviderResourceLedger Ledger { get; }

    public void RegisterStorageRelease(Action releaseStorage)
    {
        ArgumentNullException.ThrowIfNull(releaseStorage);
        lock (_engineGate)
        {
            if (_releaseStorage is not null)
                throw new InvalidOperationException(
                    "LocalEnvironment.StorageReleaseAlreadyRegistered: the Local provider has more than one storage lifecycle owner.");
            _releaseStorage = releaseStorage;
        }
    }

    public void ReleaseStorageMounts()
    {
        Action? release;
        lock (_engineGate)
            release = _releaseStorage;
        release?.Invoke();
    }

    public EngineAuthorityMode CurrentEngineAuthorityMode
    {
        get
        {
            lock (_engineGate)
                return _engineAuthorityMode;
        }
    }

    public string CurrentEngineSocketPath
    {
        get
        {
            lock (_engineGate)
            {
                return _engineSocketPath ??
                    throw new InvalidOperationException(
                        "The Local engine socket is not currently available.");
            }
        }
    }

    public long CurrentEngineGeneration
    {
        get
        {
            lock (_engineGate)
                return _engineGeneration;
        }
    }

    public bool IsEngineReady
    {
        get
        {
            lock (_engineGate)
                return _engineWasReady;
        }
    }

    public void BindAuthorityToCurrentEngine(
        string authorityId)
    {
        lock (_engineGate)
            _authorityEngineGenerations[authorityId] =
                _engineGeneration;
    }

    public bool IsAuthorityBoundToCurrentEngine(
        string authorityId)
    {
        lock (_engineGate)
            return _authorityEngineGenerations.TryGetValue(
                    authorityId,
                    out long generation) &&
                generation == _engineGeneration &&
                _engineWasReady;
    }

    public void ReleaseAuthority(string authorityId)
    {
        lock (_engineGate)
            _authorityEngineGenerations.Remove(authorityId);
    }

    public void AppendAuthorityAudit(
        string authorityId,
        IReadOnlyList<AuthorityAuditEvent> events) =>
        _authorityAudit.Append(authorityId, events);

    public AuthorityAuditEvent[] GetAuthorityAudit(
        string authorityId) =>
        _authorityAudit.Get(authorityId);

    public void BindEndpointToCurrentEngine(string endpointId)
    {
        lock (_engineGate)
        {
            if (!_engineWasReady)
                throw new InvalidOperationException(
                    "The Local engine is not currently ready.");
            _endpointEngineGenerations[endpointId] = _engineGeneration;
        }
    }

    public bool IsEndpointBoundToCurrentEngine(string endpointId)
    {
        lock (_engineGate)
            return _engineWasReady &&
                _endpointEngineGenerations.TryGetValue(
                    endpointId,
                    out long generation) &&
                generation == _engineGeneration;
    }

    public void ReleaseEndpoint(string endpointId)
    {
        lock (_engineGate)
            _endpointEngineGenerations.Remove(endpointId);
    }

    public void BindNetworkResourceToCurrentEngine(string resourceKey)
    {
        lock (_engineGate)
        {
            if (!_engineWasReady)
                throw new InvalidOperationException(
                    "The Local engine is not currently ready.");
            _networkEngineGenerations[resourceKey] =
                _engineGeneration;
        }
    }

    public bool IsNetworkResourceBoundToCurrentEngine(
        string resourceKey)
    {
        lock (_engineGate)
            return _engineWasReady &&
                _networkEngineGenerations.TryGetValue(
                    resourceKey,
                    out long generation) &&
                generation == _engineGeneration;
    }

    public void ReleaseNetworkResource(string resourceKey)
    {
        lock (_engineGate)
            _networkEngineGenerations.Remove(resourceKey);
    }

    public void StoreEngineNetwork(
        string resourceKey,
        LocalEngineNetworkObservation network)
    {
        lock (_engineGate)
        {
            if (!_engineWasReady)
                throw new InvalidOperationException(
                    "The Local engine is not currently ready.");
            _networkEngineGenerations[resourceKey] =
                _engineGeneration;
            _engineNetworks[resourceKey] = network;
        }
    }

    public LocalEngineNetworkObservation? GetEngineNetwork(
        string resourceKey)
    {
        lock (_engineGate)
            return _engineNetworks.GetValueOrDefault(resourceKey);
    }

    public void ForgetEngineNetwork(string resourceKey)
    {
        lock (_engineGate)
        {
            _networkEngineGenerations.Remove(resourceKey);
            _engineNetworks.Remove(resourceKey);
        }
    }

    public string WorkloadStateRoot
    {
        get
        {
            string root = string.IsNullOrWhiteSpace(
                Options.WorkloadStateRoot)
                ? Path.Combine(
                    System.Environment.GetFolderPath(
                        System.Environment.SpecialFolder.LocalApplicationData),
                    "HPD-OS",
                    "local-environment")
                : Options.WorkloadStateRoot;
            return Path.GetFullPath(root);
        }
    }

    public long AcceptEngineObservation(
        LocalEngineObservation observation,
        EngineAuthorityMode authorityMode)
    {
        lock (_engineGate)
        {
            if (!_engineWasReady ||
                !string.Equals(
                    _engineFingerprint,
                    observation.Fingerprint,
                    StringComparison.Ordinal))
            {
                _engineGeneration++;
            }
            _engineWasReady = true;
            _engineFingerprint = observation.Fingerprint;
            _engineAuthorityMode = authorityMode;
            _engineSocketPath = observation.SocketPath;
            return _engineGeneration;
        }
    }

    public void MarkEngineUnavailable()
    {
        lock (_engineGate)
        {
            _engineWasReady = false;
            _engineAuthorityMode = EngineAuthorityMode.ProviderDefined;
            _engineSocketPath = null;
        }
    }
}

internal static class LocalProviderGeneration
{
    private const int MaxGenerationBytes = 20;

    public static ulong Next(string? stateRoot)
    {
        if (string.IsNullOrWhiteSpace(stateRoot))
            return Ephemeral();

        string root = Path.GetFullPath(stateRoot);
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "provider-generation");
        string lockPath = Path.Combine(
            root,
            "provider-generation.lock");
        using var generationLock = new FileStream(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        ulong previous = File.Exists(path)
            ? Read(path)
            : 0;
        ulong next = checked(previous + 1);
        Write(path, next);
        return next;
    }

    private static ulong Ephemeral()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        RandomNumberGenerator.Fill(bytes);
        ulong value = BitConverter.ToUInt64(bytes) &
            long.MaxValue;
        return value == 0 ? 1UL : value;
    }

    private static ulong Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length is 0 or > MaxGenerationBytes)
            throw Malformed();
        string text;
        try
        {
            text = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidOperationException(
                "LocalEnvironment.ProviderGenerationMalformed: the persisted provider generation is not valid UTF-8.",
                exception);
        }
        if (!ulong.TryParse(
                text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out ulong value) ||
            value == 0 ||
            value > long.MaxValue ||
            !string.Equals(
                text,
                value.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
            throw Malformed();
        return value;
    }

    private static void Write(string path, ulong value)
    {
        string directory = Path.GetDirectoryName(path)!;
        string temporary = Path.Combine(
            directory,
            $".provider-generation.{Guid.NewGuid():N}.tmp");
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(
                value.ToString(CultureInfo.InvariantCulture));
            using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static InvalidOperationException Malformed() =>
        new(
            "LocalEnvironment.ProviderGenerationMalformed: the persisted provider generation must be one canonical positive 64-bit decimal integer.");
}
