namespace HPD.Execution.AppleVirtualization.Activation;

using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using HPD.Execution.AppleVirtualization.Protocol;
using HPD.Execution.AppleVirtualization.State;
using HPD.Execution.Contracts;

internal sealed class AppleVirtualizationHelperActivator :
    IProviderActivator,
    IAppleVirtualizationHelperClient,
    IAsyncDisposable
{
    private const int MaxStdoutLineBytes = 64 * 1024;

    private static readonly ResourceKind ActivationKind = new("provider-activation");
    private static readonly SchemaVersion ActivationSchemaVersion = new("v1");
    private static readonly DiagnosticCode HelperUnavailableCode = new("AppleVirtualization.HelperUnavailable");
    private static readonly DiagnosticCode MissingPathCode = new("AppleVirtualization.HelperPathMissing");
    private static readonly DiagnosticCode NotFoundCode = new("AppleVirtualization.HelperExecutableNotFound");
    private static readonly DiagnosticCode EarlyExitCode = new("AppleVirtualization.HelperExitedBeforeHandshake");
    private static readonly DiagnosticCode StartupTimeoutCode = new("AppleVirtualization.HelperStartupTimeout");
    private static readonly DiagnosticCode MalformedResponseCode = new("AppleVirtualization.HelperMalformedResponse");
    private static readonly DiagnosticCode ProtocolMismatchCode = new("AppleVirtualization.HelperProtocolMismatch");
    private static readonly DiagnosticCode HealthProbeFailedCode = new("AppleVirtualization.HelperHealthProbeFailed");

    private readonly AppleVirtualizationProviderOptions _options;
    private readonly AppleVirtualizationProviderStateLedger _ledger;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly BoundedByteCapture _stderr;
    private Process? _process;
    private Task? _stderrTask;
    private ProviderActivationStatus? _status;
    private ResourceSnapshot<ProviderActivation, ProviderActivationSpec, ProviderActivationStatus>? _snapshot;
    private TargetHandle<ProviderActivation>? _activationHandle;
    private long _requestSequence;
    private bool _hasSuccessfulActivation;
    private bool _disposed;

    public AppleVirtualizationHelperActivator(
        AppleVirtualizationProviderOptions options,
        AppleVirtualizationProviderStateLedger ledger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _stderr = new BoundedByteCapture(Math.Max(0, options.StartupStderrCaptureBytes));
    }

    public async ValueTask<ResourceSnapshot<ProviderActivation, ProviderActivationSpec, ProviderActivationStatus>> ActivateAsync(
        ProviderActivationSpec spec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ThrowIfDisposed();

        ResourceMetadata<ProviderActivation> metadata = Metadata(spec);
        if (_hasSuccessfulActivation)
        {
            await StopProcessAsync(force: true, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            _ledger.AdvanceProviderGeneration();
            _activationHandle = null;
        }

        TargetHandle<ProviderActivation> handle = ActivationHandle(metadata);
        ProviderActivationStatus starting = CreateStatus(
            ProviderActivationPhase.Starting,
            metadata,
            spec,
            handle,
            diagnostics: Array.Empty<Diagnostic>(),
            preflightChecks: Array.Empty<ProviderPreflightCheck>(),
            helperVersion: null,
            protocolVersion: null,
            helperReady: false);
        StoreSnapshot(metadata, spec, starting);

        Diagnostic? configurationFailure = ValidateConfiguration();
        if (configurationFailure is not null)
        {
            ProviderActivationStatus failed = CreateStatus(
                ProviderActivationPhase.Failed,
                metadata,
                spec,
                handle,
                [configurationFailure],
                [Preflight("hpd-vz-helper", PreflightCheckState.Failed, configurationFailure.Message, DiagnosticSeverity.Error)],
                helperVersion: null,
                protocolVersion: null,
                helperReady: false);
            return StoreSnapshot(metadata, spec, failed);
        }

        if (_options.FeatureGates.EnableRealVmBoot)
        {
            AppleVirtualizationRealModePreconditionResult realModePreconditions =
                AppleVirtualizationRealModePreconditions.Evaluate(_options);
            if (!realModePreconditions.Passed)
            {
                ProviderActivationStatus failed = CreateStatus(
                    ProviderActivationPhase.Failed,
                    metadata,
                    spec,
                    handle,
                    realModePreconditions.Diagnostics,
                    realModePreconditions.Facts.Select(ToPreflightCheck).ToArray(),
                    helperVersion: null,
                    protocolVersion: null,
                    helperReady: false);
                return StoreSnapshot(metadata, spec, failed);
            }
        }

        using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        TimeSpan timeout = _options.HelperStartupTimeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(5) : _options.HelperStartupTimeout;
        startup.CancelAfter(timeout);

        try
        {
            StartProcess();

            AppleVirtualizationHelperEnvelope hello = await SendCoreAsync(
                Request(AppleVirtualizationHelperOperation.Hello, AppleVirtualizationHelperProtocol.HelloRequestSchema) with
                {
                    HelloRequest = new AppleVirtualizationHelperHelloRequest(),
                    ProviderGeneration = _ledger.ProviderGeneration,
                },
                startup.Token).ConfigureAwait(false);

            if (!TryValidateHello(hello, out AppleVirtualizationHelperHelloResponse? helloResponse, out Diagnostic? helloDiagnostic))
            {
                await StopProcessAsync(force: true, cancellationToken: CancellationToken.None).ConfigureAwait(false);
                return StoreSnapshot(metadata, spec, Failed(metadata, spec, handle, helloDiagnostic));
            }

            if (helloResponse.ProviderGeneration > 0)
            {
                while (_ledger.ProviderGeneration < helloResponse.ProviderGeneration)
                {
                    _ledger.AdvanceProviderGeneration();
                }
            }

            ulong activationGeneration = _ledger.ProviderGeneration;

            AppleVirtualizationHelperEnvelope health = await SendCoreAsync(
                Request(AppleVirtualizationHelperOperation.HealthProbe, AppleVirtualizationHelperProtocol.HealthResponseSchema) with
                {
                    HealthProbeRequest = new AppleVirtualizationHealthProbeRequest(IncludeGuestControl: false),
                    ProviderGeneration = activationGeneration,
                },
                startup.Token).ConfigureAwait(false);

            if (!TryValidateHealth(health, out Diagnostic? healthDiagnostic))
            {
                await StopProcessAsync(force: true, cancellationToken: CancellationToken.None).ConfigureAwait(false);
                return StoreSnapshot(metadata, spec, Failed(metadata, spec, handle, healthDiagnostic, helloResponse));
            }

            ProviderActivationStatus ready = CreateStatus(
                ProviderActivationPhase.Ready,
                metadata,
                spec,
                handle,
                diagnostics: Array.Empty<Diagnostic>(),
                preflightChecks: helloResponse.PreflightChecks,
                helperVersion: helloResponse.HelperVersion,
                protocolVersion: helloResponse.ProtocolVersion,
                helperReady: true,
                providerGeneration: activationGeneration,
                startedAt: DateTimeOffset.UtcNow);
            _hasSuccessfulActivation = true;
            return StoreSnapshot(metadata, spec, ready);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await StopProcessAsync(force: true, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            Diagnostic diagnostic = Failure(
                StartupTimeoutCode,
                "Timed out while starting hpd-vz and waiting for helper handshake.",
                "activation",
                includeStderr: true);
            return StoreSnapshot(metadata, spec, Failed(metadata, spec, handle, diagnostic));
        }
        catch (Exception ex) when (IsEarlyExit())
        {
            await StopProcessAsync(force: true, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            Diagnostic diagnostic = Failure(
                EarlyExitCode,
                $"hpd-vz exited before activation handshake completed. {ex.Message}",
                "activation",
                includeStderr: true);
            return StoreSnapshot(metadata, spec, Failed(metadata, spec, handle, diagnostic));
        }
        catch (JsonException ex)
        {
            await StopProcessAsync(force: true, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            Diagnostic diagnostic = Failure(
                MalformedResponseCode,
                $"hpd-vz returned a malformed activation response. {ex.Message}",
                "activation",
                includeStderr: true);
            return StoreSnapshot(metadata, spec, Failed(metadata, spec, handle, diagnostic));
        }
        catch (Exception ex)
        {
            await StopProcessAsync(force: true, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            Diagnostic diagnostic = Failure(
                EarlyExitCode,
                $"hpd-vz activation failed before handshake completed. {ex.Message}",
                "activation",
                includeStderr: true);
            return StoreSnapshot(metadata, spec, Failed(metadata, spec, handle, diagnostic));
        }
    }

    public ValueTask<ProviderActivationStatus> GetStatusAsync(
        ResourceRef<ProviderActivation> activation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateGate)
        {
            return ValueTask.FromResult(_status ?? NotActivatedStatus());
        }
    }

    public async ValueTask StopAsync(
        TargetHandle<ProviderActivation> activation,
        ProviderStopOptions options,
        CancellationToken cancellationToken = default)
    {
        await StopProcessAsync(options.Force, cancellationToken).ConfigureAwait(false);
        lock (_stateGate)
        {
            if (_status is not null)
            {
                _status = _status with
                {
                    Phase = ResourcePhase.Ready,
                    ActivationPhase = ProviderActivationPhase.Stopped,
                    StoppedAt = DateTimeOffset.UtcNow,
                    Components = [Component(ProviderComponentPhase.Stopped, null)],
                };
            }
        }
    }

    public ValueTask<AppleVirtualizationHelperEnvelope> SendAsync(
        AppleVirtualizationHelperEnvelope request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_process is null || _process.HasExited)
        {
            return ValueTask.FromResult(Unavailable(request));
        }

        return SendCoreAsync(request, cancellationToken);
    }

    public async IAsyncEnumerable<AppleVirtualizationHelperEnvelope> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        yield break;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopProcessAsync(force: true, cancellationToken: CancellationToken.None).ConfigureAwait(false);
        _sendGate.Dispose();
    }

    private async ValueTask<AppleVirtualizationHelperEnvelope> SendCoreAsync(
        AppleVirtualizationHelperEnvelope request,
        CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Process process = _process ?? throw new InvalidOperationException("hpd-vz is not running.");
            byte[] payload = AppleVirtualizationHelperJsonCodec.Encode(request);
            await process.StandardInput.BaseStream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await process.StandardInput.BaseStream.WriteAsync(new byte[] { 0x0A }, cancellationToken).ConfigureAwait(false);
            await process.StandardInput.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);

            byte[]? line = await ReadLineAsync(process.StandardOutput.BaseStream, MaxStdoutLineBytes, cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                throw new InvalidOperationException("hpd-vz closed stdout before writing a response.");
            }

            return AppleVirtualizationHelperJsonCodec.Decode(line);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private void StartProcess()
    {
        var startInfo = new ProcessStartInfo(_options.HelperPath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in _options.HelperArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException("hpd-vz process did not start.");
        }

        _process = process;
        _stderrTask = Task.Run(() => DrainStderrAsync(process.StandardError.BaseStream));
    }

    private async Task DrainStderrAsync(Stream stream)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(512);
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                _stderr.Append(buffer.AsSpan(0, read));
            }
        }
        catch
        {
            // Stderr is diagnostic-only. Process exit can close the pipe while stopping.
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async ValueTask StopProcessAsync(bool force, CancellationToken cancellationToken)
    {
        Process? process = _process;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                try
                {
                    process.StandardInput.Close();
                }
                catch
                {
                }

                using var stopTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                stopTimeout.CancelAfter(_options.HelperStopTimeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(2) : _options.HelperStopTimeout);
                try
                {
                    await process.WaitForExitAsync(stopTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    if (force || !process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }

            if (_stderrTask is not null)
            {
                await _stderrTask.WaitAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
            }
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        finally
        {
            process.Dispose();
            _process = null;
        }
    }

    private Diagnostic? ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.HelperPath))
        {
            return Failure(MissingPathCode, "The Apple Virtualization helper path is empty.", "activation.helperPath", includeStderr: false);
        }

        if (Path.IsPathFullyQualified(_options.HelperPath) ||
            _options.HelperPath.Contains(Path.DirectorySeparatorChar) ||
            _options.HelperPath.Contains(Path.AltDirectorySeparatorChar))
        {
            if (!File.Exists(_options.HelperPath))
            {
                return Failure(NotFoundCode, $"The Apple Virtualization helper executable was not found at '{_options.HelperPath}'.", "activation.helperPath", includeStderr: false);
            }
        }
        else if (FindOnPath(_options.HelperPath) is null)
        {
            return Failure(NotFoundCode, $"The Apple Virtualization helper executable '{_options.HelperPath}' was not found on PATH.", "activation.helperPath", includeStderr: false);
        }

        return null;
    }

    private static string? FindOnPath(string fileName)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private bool TryValidateHello(
        AppleVirtualizationHelperEnvelope response,
        out AppleVirtualizationHelperHelloResponse? hello,
        out Diagnostic? diagnostic)
    {
        hello = response.HelloResponse;
        if (response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error)
        {
            diagnostic = response.Error?.Code == ProtocolMismatchCode.Value
                ? ToDiagnostic(response.Error, ProtocolMismatchCode, "activation.hello")
                : ToDiagnostic(response.Error, MalformedResponseCode, "activation.hello");
            return false;
        }

        if (hello is null)
        {
            diagnostic = Failure(MalformedResponseCode, "The helper hello response did not contain a HelloResponse payload.", "activation.hello", includeStderr: true);
            return false;
        }

        if (!hello.ProtocolCompatible ||
            !string.Equals(hello.ProtocolVersion, AppleVirtualizationHelperProtocol.CurrentVersion, StringComparison.Ordinal))
        {
            diagnostic = Failure(
                ProtocolMismatchCode,
                $"Helper protocol '{hello.ProtocolVersion}' is not compatible with provider protocol '{AppleVirtualizationHelperProtocol.CurrentVersion}'.",
                "activation.hello",
                includeStderr: true);
            return false;
        }

        diagnostic = null;
        return true;
    }

    private bool TryValidateHealth(AppleVirtualizationHelperEnvelope response, out Diagnostic? diagnostic)
    {
        if (response.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error)
        {
            diagnostic = ToDiagnostic(response.Error, HealthProbeFailedCode, "activation.health");
            return false;
        }

        if (response.HealthProbeResponse is not { Ready: true })
        {
            string detail = response.HealthProbeResponse?.Detail ?? "The helper did not include a health response payload.";
            diagnostic = Failure(HealthProbeFailedCode, $"The Apple Virtualization helper health probe failed. {detail}", "activation.health", includeStderr: true);
            return false;
        }

        diagnostic = null;
        return true;
    }

    private ProviderActivationStatus Failed(
        ResourceMetadata<ProviderActivation> metadata,
        ProviderActivationSpec spec,
        TargetHandle<ProviderActivation> handle,
        Diagnostic? diagnostic,
        AppleVirtualizationHelperHelloResponse? hello = null) =>
        CreateStatus(
            ProviderActivationPhase.Failed,
            metadata,
            spec,
            handle,
            diagnostic is null ? Array.Empty<Diagnostic>() : [diagnostic],
            hello?.PreflightChecks ?? Array.Empty<ProviderPreflightCheck>(),
            hello?.HelperVersion,
            hello?.ProtocolVersion,
            helperReady: false,
            providerGeneration: hello?.ProviderGeneration ?? _ledger.ProviderGeneration);

    private ProviderActivationStatus CreateStatus(
        ProviderActivationPhase phase,
        ResourceMetadata<ProviderActivation> metadata,
        ProviderActivationSpec spec,
        TargetHandle<ProviderActivation> handle,
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<ProviderPreflightCheck> preflightChecks,
        string? helperVersion,
        string? protocolVersion,
        bool helperReady,
        ulong? providerGeneration = null,
        DateTimeOffset? startedAt = null) =>
        new()
        {
            Phase = phase == ProviderActivationPhase.Failed ? ResourcePhase.Failed : ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            LastTransitionAt = DateTimeOffset.UtcNow,
            ActivationPhase = phase,
            ActivationId = new ProviderActivationId(metadata.Id.Value),
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            ActivationKind = spec.ActivationKind,
            ActivationHandle = handle,
            Components =
            [
                Component(
                    helperReady ? ProviderComponentPhase.Ready : phase == ProviderActivationPhase.Failed ? ProviderComponentPhase.Failed : ProviderComponentPhase.Starting,
                    _process?.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    helperVersion,
                    protocolVersion,
                    providerGeneration ?? _ledger.ProviderGeneration),
            ],
            Diagnostics = diagnostics,
            PreflightChecks = preflightChecks,
            StartedAt = startedAt,
        };

    private static ProviderComponentStatus Component(
        ProviderComponentPhase phase,
        string? processId,
        string? helperVersion = null,
        string? protocolVersion = null,
        ulong? providerGeneration = null)
    {
        string name = helperVersion is null
            ? "hpd-vz"
            : $"hpd-vz {helperVersion} protocol {protocolVersion} generation {providerGeneration}";
        return new ProviderComponentStatus(ProviderComponentKind.Driver, name, phase, processId);
    }

    private ResourceSnapshot<ProviderActivation, ProviderActivationSpec, ProviderActivationStatus> StoreSnapshot(
        ResourceMetadata<ProviderActivation> metadata,
        ProviderActivationSpec spec,
        ProviderActivationStatus status)
    {
        var snapshot = new ResourceSnapshot<ProviderActivation, ProviderActivationSpec, ProviderActivationStatus>(metadata, spec, status);
        lock (_stateGate)
        {
            _status = status;
            _snapshot = snapshot;
        }

        return snapshot;
    }

    private ProviderActivationStatus NotActivatedStatus() =>
        new()
        {
            Phase = ResourcePhase.Degraded,
            ActivationPhase = ProviderActivationPhase.Stopped,
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            ActivationKind = ProviderActivationKind.SupervisedExecutable,
            Diagnostics =
            [
                new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Code = HelperUnavailableCode,
                    Message = "The Apple Virtualization helper has not been activated.",
                    ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                    TargetPath = "activation",
                },
            ],
        };

    private AppleVirtualizationHelperEnvelope Request(AppleVirtualizationHelperOperation operation, SchemaId schema)
    {
        long sequence = Interlocked.Increment(ref _requestSequence);
        return AppleVirtualizationHelperEnvelope.Request(operation, "apple-vz-activation-" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), sequence, schema);
    }

    private AppleVirtualizationHelperEnvelope Unavailable(AppleVirtualizationHelperEnvelope request)
    {
        var error = new AppleVirtualizationHelperError
        {
            Code = HelperUnavailableCode.Value,
            Message = "The Apple Virtualization hpd-vz helper is not activated for this provider module instance.",
            Operation = AppleVirtualizationHelperOperationNames.ToWireName(request.Operation),
            Retryable = true,
            FailedPhase = "Activation",
            Severity = DiagnosticSeverity.Error,
        };

        return request.ToErrorResponse(Interlocked.Increment(ref _requestSequence), error);
    }

    private Diagnostic ToDiagnostic(AppleVirtualizationHelperError? error, DiagnosticCode fallbackCode, string targetPath)
    {
        if (error is null)
        {
            return Failure(fallbackCode, "The helper returned an error response without an error payload.", targetPath, includeStderr: true);
        }

        return new Diagnostic
        {
            Severity = error.Severity,
            Code = new DiagnosticCode(error.Code),
            Message = WithStderr(error.Message),
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = error.Operation ?? targetPath,
        };
    }

    private Diagnostic Failure(DiagnosticCode code, string message, string targetPath, bool includeStderr) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = code,
            Message = includeStderr ? WithStderr(message) : message,
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };

    private string WithStderr(string message)
    {
        string stderr = _stderr.ToUtf8String();
        return string.IsNullOrEmpty(stderr)
            ? message
            : $"{message} Startup stderr: {stderr}";
    }

    private static ProviderPreflightCheck Preflight(string name, PreflightCheckState state, string message, DiagnosticSeverity severity) =>
        new(name, state, severity, message);

    private static ProviderPreflightCheck ToPreflightCheck(AppleVirtualizationPreflightFact fact) =>
        new(fact.Name, ToCheckState(fact.State), fact.Severity, fact.Message);

    private static PreflightCheckState ToCheckState(AppleVirtualizationPreflightFactState state) =>
        state switch
        {
            AppleVirtualizationPreflightFactState.Supported => PreflightCheckState.Passed,
            AppleVirtualizationPreflightFactState.Unsupported => PreflightCheckState.Failed,
            AppleVirtualizationPreflightFactState.RequiresConfiguration => PreflightCheckState.RequiresRemediation,
            AppleVirtualizationPreflightFactState.RequiresRemediation => PreflightCheckState.RequiresRemediation,
            _ => PreflightCheckState.Unknown,
        };

    private ResourceMetadata<ProviderActivation> Metadata(ProviderActivationSpec spec)
    {
        string id = string.IsNullOrWhiteSpace(spec.ScopeKey) ? "apple-vz-activation" : "apple-vz-" + spec.ScopeKey;
        return new ResourceMetadata<ProviderActivation>
        {
            Id = new ResourceId<ProviderActivation>(id),
            Kind = ActivationKind,
            Scope = new ResourceScope(spec.ScopeKey),
            Generation = new ResourceGeneration(1),
            SchemaVersion = ActivationSchemaVersion,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private TargetHandle<ProviderActivation> ActivationHandle(ResourceMetadata<ProviderActivation> metadata)
    {
        if (_activationHandle is { } existing)
        {
            return existing;
        }

        var providerHandle = new ProviderOpaqueHandle(
            AppleVirtualizationProviderDescriptor.ProviderId,
            "provider-activation:" + metadata.Scope.Value + ":" + metadata.Id.Value,
            new SchemaId("hpd.execution.apple-virtualization.handle.provider-activation.v1"),
            _ledger.ProviderGeneration);
        _activationHandle = new TargetHandle<ProviderActivation>(
            new TargetRoute
            {
                Kind = new TargetKind("apple-virtualization.provider-activation"),
                Scope = metadata.Scope,
                Segments = [new TargetRouteSegment(TargetRouteSegmentKind.ProviderActivation, metadata.Id.Value)],
                BackingResourceKind = metadata.Kind,
                BackingResourceId = metadata.Id.Value,
                ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                ProviderHandle = providerHandle,
            },
            TargetHandleLifetime.LiveCapability,
            TargetHandleAuthority.Observe | TargetHandleAuthority.Control,
            _ledger.ProviderGeneration);
        return _activationHandle.Value;
    }

    private bool IsEarlyExit() => _process is { HasExited: true };

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AppleVirtualizationHelperActivator));
        }
    }

    private static async ValueTask<byte[]?> ReadLineAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(Math.Min(maxBytes, 4096));
        try
        {
            using var line = new MemoryStream();
            while (true)
            {
                int read = await stream.ReadAsync(rented.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return line.Length == 0 ? null : line.ToArray();
                }

                if (rented[0] == 0x0A)
                {
                    return line.ToArray();
                }

                if (line.Length >= maxBytes)
                {
                    throw new JsonException("Helper response exceeded the maximum activation line length.");
                }

                line.WriteByte(rented[0]);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private sealed class BoundedByteCapture
    {
        private readonly byte[] _buffer;
        private int _length;
        private bool _truncated;
        private readonly object _gate = new();

        public BoundedByteCapture(int capacity)
        {
            _buffer = capacity == 0 ? Array.Empty<byte>() : new byte[capacity];
        }

        public void Append(ReadOnlySpan<byte> bytes)
        {
            if (_buffer.Length == 0)
            {
                if (!bytes.IsEmpty)
                {
                    _truncated = true;
                }

                return;
            }

            lock (_gate)
            {
                int available = _buffer.Length - _length;
                int copy = Math.Min(available, bytes.Length);
                if (copy > 0)
                {
                    bytes[..copy].CopyTo(_buffer.AsSpan(_length));
                    _length += copy;
                }

                _truncated |= copy < bytes.Length;
            }
        }

        public string ToUtf8String()
        {
            lock (_gate)
            {
                if (_length == 0)
                {
                    return _truncated ? "[stderr truncated]" : string.Empty;
                }

                string text = Encoding.UTF8.GetString(_buffer, 0, _length).Trim();
                return _truncated ? text + " [stderr truncated]" : text;
            }
        }
    }
}
