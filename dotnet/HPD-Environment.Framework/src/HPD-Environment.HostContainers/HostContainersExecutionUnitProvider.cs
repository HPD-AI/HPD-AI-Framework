namespace HPD.Environment.HostContainers;

using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

internal sealed class HostContainersExecutionUnitProvider(HostContainersProviderState state)
    : IExecutionUnitProvider
{
    private static readonly ProviderResourceShape Shape = new(
        new TargetKind("execution-unit"),
        TargetRouteSegmentKind.ExecutionUnit,
        TargetHandleLifetime.Lease,
        TargetHandleAuthority.Observe |
        TargetHandleAuthority.Control |
        TargetHandleAuthority.Invoke,
        new SchemaId("hpd.execution.host-containers.execution-unit.handle.v1"));

    public ProviderId ProviderId =>
        HostContainersProviderDescriptor.ProviderId;

    public async ValueTask<ExecutionUnitStatus> EnsureAsync(
        ResourceMetadata<ExecutionUnit> metadata,
        ExecutionUnitSpec spec,
        ExecutionUnitStatus? observed,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (spec.PreferredHost is not { } host)
            return Failed(
                metadata,
                "HostContainers.ExecutionUnitHostRequired",
                "Host Containers execution units require the Host Containers runtime host.");
        ProviderLedgerLookup<
            ProviderResourceEntry<
                RuntimeHost,
                RuntimeHostSpec,
                RuntimeHostStatus>> hostLookup = state.Ledger.TryGet<
                    RuntimeHost,
                    RuntimeHostSpec,
                    RuntimeHostStatus>(host);
        if (!hostLookup.Succeeded ||
            hostLookup.Entry!.Status.HostPhase != RuntimeHostPhase.Ready)
        {
            return Failed(
                metadata,
                "HostContainers.ExecutionUnitHostNotReady",
                hostLookup.Diagnostic?.Message ??
                "The Host Containers runtime host is not ready.");
        }

        HostContainersSandboxUnitOptions? sandboxOptions =
            HostContainersSandboxUnitExtension.TryRead(spec);
        var status = new ExecutionUnitStatus
        {
            Phase = ResourcePhase.Ready,
            UnitPhase = ExecutionUnitPhase.Ready,
            ObservedGeneration = metadata.Generation,
            AssignedHost = host,
            LastTransitionAt = DateTimeOffset.UtcNow,
        };
        ProviderResourceEntry<
            ExecutionUnit,
            ExecutionUnitSpec,
            ExecutionUnitStatus> entry =
            state.Ledger.Upsert(metadata, spec, status, Shape);
        status = status with
        {
            Handle = entry.TargetHandle,
            NamespaceHandle = entry.ProviderHandle,
            WorkloadStorage = ResolveStorage(
                metadata,
                spec,
                entry.ProviderHandle),
        };
        state.Ledger.Upsert(metadata, spec, status, Shape);
        if (sandboxOptions is null)
        {
            return status;
        }

        try
        {
            HostContainersSandboxContainer container =
                await CreateSandboxContainerAsync(
                    metadata,
                    sandboxOptions,
                    spec.QuotaPolicy,
                    cancellationToken).ConfigureAwait(false);
            state.RegisterSandboxContainer(container);
            return status;
        }
        catch
        {
            state.Ledger.Remove<
                ExecutionUnit,
                ExecutionUnitSpec,
                ExecutionUnitStatus>(entry.Resource);
            throw;
        }
    }

    public async ValueTask<ExecutionUnitStatus> StopAsync(
        TargetHandle<ExecutionUnit> unit,
        StopPolicy policy,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProviderResourceEntry<
            ExecutionUnit,
            ExecutionUnitSpec,
            ExecutionUnitStatus> entry = Require(unit);
        if (state.GetSandboxContainer(
                entry.Resource.Id.Value) is { } container)
        {
            int timeoutSeconds = Math.Clamp(
                checked((int)Math.Ceiling(
                    policy.GracePeriod.TotalSeconds)),
                1,
                300);
            await HostContainersDockerCli.RunAsync(
                    state.Options,
                    container.EngineSocketPath,
                    [
                        "stop",
                        "--time",
                        timeoutSeconds.ToString(
                            System.Globalization.CultureInfo
                                .InvariantCulture),
                        container.ContainerId,
                    ],
                    TimeSpan.FromSeconds(timeoutSeconds + 20),
                    cancellationToken).ConfigureAwait(false);
        }
        ExecutionUnitStatus stopped = entry.Status with
        {
            Phase = ResourcePhase.Ready,
            UnitPhase = ExecutionUnitPhase.Stopped,
            LastTransitionAt = DateTimeOffset.UtcNow,
        };
        state.Ledger.Upsert(
            Metadata(entry.Resource),
            entry.Spec,
            stopped,
            Shape);
        return stopped;
    }

    public async ValueTask DeleteAsync(
        ResourceRef<ExecutionUnit> unit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProviderLedgerLookup<
            ProviderResourceEntry<
                ExecutionUnit,
                ExecutionUnitSpec,
                ExecutionUnitStatus>> lookup = state.Ledger.TryGet<
                    ExecutionUnit,
                    ExecutionUnitSpec,
                    ExecutionUnitStatus>(unit);
        HostContainersSandboxContainer? container =
            state.GetSandboxContainer(unit.Id.Value);
        if (container is not null)
        {
            await HostContainersDockerCli.RunAsync(
                    state.Options,
                    container.EngineSocketPath,
                    [
                        "rm",
                        "--force",
                        container.ContainerId,
                    ],
                    TimeSpan.FromSeconds(30),
                    cancellationToken).ConfigureAwait(false);
            _ = state.RemoveSandboxContainer(unit.Id.Value);
        }
        if (lookup.Succeeded &&
            lookup.Entry!.Status.WorkloadStorage is { } allocation &&
            allocation.StorageClass is not StorageClass.AppDurable)
            DeleteOwnedStorage(allocation.EffectiveRuntimePath);
        state.Ledger.Remove<
            ExecutionUnit,
            ExecutionUnitSpec,
            ExecutionUnitStatus>(unit);
        return;
    }

    private async ValueTask<HostContainersSandboxContainer>
        CreateSandboxContainerAsync(
            ResourceMetadata<ExecutionUnit> metadata,
            HostContainersSandboxUnitOptions options,
            ResourceQuotaPolicy quota,
            CancellationToken cancellationToken)
    {
        string docker = HostContainersDockerCli.ResolvePath(
            state.Options);
        string? socket = state.IsEngineReady
            ? state.CurrentEngineSocketPath
            : HostContainersDockerCli.ResolveEngineSocket(
                state.Options);
        string name = HostContainersDockerCli.ContainerName(
            metadata.Id.Value);
        IReadOnlyList<string> createArguments =
            BuildCreateArguments(
                metadata,
                name,
                options,
                quota);
        string containerId = await HostContainersDockerCli.RunAsync(
                state.Options,
                socket,
                createArguments,
                TimeSpan.FromSeconds(60),
                cancellationToken).ConfigureAwait(false);
        if (containerId.Length is < 12 or > 64 ||
            containerId.Any(character =>
                !char.IsAsciiHexDigit(character)))
        {
            throw new InvalidOperationException(
                "HostContainers.SandboxContainerIdentityInvalid: Docker did not return a valid container identity.");
        }
        try
        {
            _ = await HostContainersDockerCli.RunAsync(
                    state.Options,
                    socket,
                    [
                        "start",
                        containerId,
                    ],
                    TimeSpan.FromSeconds(30),
                    cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                _ = await HostContainersDockerCli.RunAsync(
                        state.Options,
                        socket,
                        [
                            "rm",
                            "--force",
                            containerId,
                        ],
                        TimeSpan.FromSeconds(30),
                        CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
            throw;
        }
        return new HostContainersSandboxContainer(
            metadata.Id.Value,
            metadata.Scope,
            containerId,
            name,
            options,
            docker,
            socket,
            metadata.Generation);
    }

    private IReadOnlyList<string> BuildCreateArguments(
        ResourceMetadata<ExecutionUnit> metadata,
        string name,
        HostContainersSandboxUnitOptions options,
        ResourceQuotaPolicy quota)
    {
        var arguments = new List<string>
        {
            "create",
            "--name",
            name,
            "--label",
            $"io.hpd.provider={ProviderId.Value}",
            "--label",
            $"io.hpd.execution-unit={metadata.Id.Value}",
            "--workdir",
            options.WorkingDirectory.Value,
        };
        if (options.ReadOnlyRootFilesystem)
        {
            arguments.Add("--read-only");
        }
        arguments.Add("--network");
        arguments.Add(options.NetworkMode switch
        {
            HostContainersSandboxNetworkMode.None => "none",
            HostContainersSandboxNetworkMode.EngineDefault =>
                "bridge",
            _ => throw new ArgumentOutOfRangeException(
                nameof(options),
                "Unsupported sandbox network mode."),
        });
        foreach (HostContainersSandboxMount mount in options.Mounts)
        {
            string source = Path.GetFullPath(
                mount.HostSource.Value);
            if (source.Contains(':', StringComparison.Ordinal) ||
                source.Contains(',', StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"HostContainers.SandboxMountPathUnsupported: '{source}' contains a character reserved by the Docker volume syntax.");
            }
            arguments.Add("--volume");
            arguments.Add(
                $"{source}:{mount.ContainerPath.Value}:" +
                (mount.Access == AccessMode.ReadOnly
                    ? "ro"
                    : "rw"));
        }
        foreach (GuestPath path in options.TmpfsPaths)
        {
            arguments.Add("--tmpfs");
            arguments.Add($"{path.Value}:rw,nosuid,nodev");
        }
        foreach ((string key, string value) in
                 options.Environment.OrderBy(
                     pair => pair.Key,
                     StringComparer.Ordinal))
        {
            arguments.Add("--env");
            arguments.Add($"{key}={value}");
        }
        if (options.User is not null)
        {
            arguments.Add("--user");
            arguments.Add(options.User);
        }
        if (quota.CpuCores is { } cpu)
        {
            arguments.Add("--cpus");
            arguments.Add(cpu.ToString(
                System.Globalization.CultureInfo
                    .InvariantCulture));
        }
        if (quota.MemoryBytes is { } memory)
        {
            arguments.Add("--memory");
            arguments.Add(memory.ToString(
                System.Globalization.CultureInfo
                    .InvariantCulture));
        }
        if (quota.ProcessCount is { } processes)
        {
            arguments.Add("--pids-limit");
            arguments.Add(processes.ToString(
                System.Globalization.CultureInfo
                    .InvariantCulture));
        }
        arguments.Add(options.Image);
        arguments.AddRange(options.KeepAliveCommand);
        return arguments;
    }


    private void DeleteOwnedStorage(string effectivePath)
    {
        string allocationsRoot = Path.GetFullPath(
            Path.Combine(state.WorkloadStateRoot, "allocations"));
        string candidate = Path.GetFullPath(effectivePath);
        if (!candidate.StartsWith(
                allocationsRoot + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) ||
            Path.GetDirectoryName(candidate) != allocationsRoot)
            throw new InvalidOperationException(
                "HostContainers.WorkloadStorageOwnershipInvalid: refusing to delete storage outside the provider allocation root.");
        if (Directory.Exists(candidate))
            Directory.Delete(candidate, recursive: true);
    }

    public async ValueTask<ExecutionUnitStatus> GetStatusAsync(
        TargetHandle<ExecutionUnit> unit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProviderResourceEntry<
            ExecutionUnit,
            ExecutionUnitSpec,
            ExecutionUnitStatus> entry = Require(unit);
        if (state.GetSandboxContainer(
                entry.Resource.Id.Value) is not { } container)
        {
            return entry.Status;
        }
        string running = await HostContainersDockerCli.RunAsync(
                state.Options,
                container.EngineSocketPath,
                [
                    "inspect",
                    "--format",
                    "{{.State.Running}}",
                    container.ContainerId,
                ],
                TimeSpan.FromSeconds(10),
                cancellationToken).ConfigureAwait(false);
        ExecutionUnitStatus observed = entry.Status with
        {
            UnitPhase = string.Equals(
                running,
                "true",
                StringComparison.OrdinalIgnoreCase)
                ? ExecutionUnitPhase.Running
                : ExecutionUnitPhase.Stopped,
            LastTransitionAt = DateTimeOffset.UtcNow,
        };
        state.Ledger.Upsert(
            Metadata(entry.Resource),
            entry.Spec,
            observed,
            Shape);
        return observed;
    }

    private ProviderResourceEntry<
        ExecutionUnit,
        ExecutionUnitSpec,
        ExecutionUnitStatus> Require(TargetHandle<ExecutionUnit> handle)
    {
        ProviderLedgerLookup<
            ProviderResourceEntry<
                ExecutionUnit,
                ExecutionUnitSpec,
                ExecutionUnitStatus>> lookup = state.Ledger.TryGet<
                    ExecutionUnit,
                    ExecutionUnitSpec,
                    ExecutionUnitStatus>(handle);
        return lookup.Succeeded
            ? lookup.Entry!
            : throw new InvalidOperationException(
                $"{lookup.Diagnostic!.Code.Value}: {lookup.Diagnostic.Message}");
    }

    private ExecutionUnitStatus Failed(
        ResourceMetadata<ExecutionUnit> metadata,
        string code,
        string message) =>
        new()
        {
            Phase = ResourcePhase.Failed,
            UnitPhase = ExecutionUnitPhase.Failed,
            ReconciliationOutcome =
                ResourceReconciliationOutcome.Rejected,
            ObservedGeneration = metadata.Generation,
            Diagnostics =
            [
                new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Code = new DiagnosticCode(code),
                    Message = message,
                    ProviderId = ProviderId,
                },
            ],
        };

    private WorkloadStorageAllocation? ResolveStorage(
        ResourceMetadata<ExecutionUnit> metadata,
        ExecutionUnitSpec spec,
        ProviderOpaqueHandle providerHandle)
    {
        if (spec.WorkloadStorage is not { } request)
            return null;
        if (string.IsNullOrWhiteSpace(request.LogicalId) ||
            request.LogicalId.Length > 128 ||
            request.LogicalId.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) ||
                  character is '.' or '_' or '-')) ||
            !Enum.IsDefined(request.StorageClass))
            throw new InvalidOperationException(
                "HostContainers.WorkloadStorageRequestInvalid: the workload storage request is malformed.");

        string path = Path.Combine(
            state.WorkloadStateRoot,
            "allocations",
            Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(
                        $"{metadata.Scope.Value}\n{request.LogicalId}")))
                .ToLowerInvariant()[..32]);
        Directory.CreateDirectory(path);
        return new WorkloadStorageAllocation
        {
            LogicalId = request.LogicalId,
            ProviderHandle = providerHandle,
            EffectiveRuntimePath = path,
            StorageClass = request.StorageClass,
            Generation = metadata.Generation,
        };
    }

    private static ResourceMetadata<ExecutionUnit> Metadata(
        ResourceRef<ExecutionUnit> resource) =>
        new()
        {
            Id = resource.Id,
            Kind = new ResourceKind("ExecutionUnit"),
            Scope = resource.Scope,
            Generation =
                resource.Generation ?? new ResourceGeneration(1),
            SchemaVersion = new SchemaVersion("1"),
        };
}
