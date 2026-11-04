namespace HPD.Environment.Search.Ripgrep;

using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime.Workspaces;

/// <summary>
/// Provisions a verified embedded ripgrep bundle into one Environment
/// execution unit through its process provider.
/// </summary>
internal sealed class BundledRipgrepToolProvisioner(
    IProcessProvider processProvider,
    TargetHandle<ExecutionUnit> unit)
    : IWorkspaceRipgrepToolProvisioner
{
    private static readonly TimeSpan ProvisionTimeout =
        TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WorkspaceRipgrepTool? _tool;
    private bool _attempted;

    /// <summary>Gets the last non-fatal provisioning failure.</summary>
    internal string? FailureReason { get; private set; }

    /// <inheritdoc />
    public async ValueTask<WorkspaceRipgrepTool?> EnsureAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_attempted)
            {
                return _tool;
            }
            _attempted = true;
            try
            {
                _tool = await ProvisionAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException or
                    InvalidOperationException or
                    System.ComponentModel.Win32Exception)
            {
                _tool = null;
                FailureReason = exception.Message;
            }
            return _tool;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Maps target-probe output to a bundled runtime identity.</summary>
    internal static string? ResolveTarget(
        string operatingSystem,
        string architecture)
    {
        string os = operatingSystem.Trim().ToLowerInvariant();
        string arch = architecture.Trim().ToLowerInvariant();
        return (os, arch) switch
        {
            ("darwin", "arm64") or ("darwin", "aarch64") =>
                "osx-arm64",
            ("darwin", "x86_64") or ("darwin", "amd64") =>
                "osx-x64",
            ("linux", "x86_64") or ("linux", "amd64") =>
                "linux-x64",
            ("linux", "arm64") or ("linux", "aarch64") =>
                "linux-arm64-musl",
            _ => null,
        };
    }

    private async ValueTask<WorkspaceRipgrepTool?> ProvisionAsync(
        CancellationToken cancellationToken)
    {
        string? target = await ProbeTargetAsync(cancellationToken)
            .ConfigureAwait(false);
        if (target is null)
        {
            FailureReason = "target_unresolved";
            return null;
        }
        BundledRipgrepBinaryManifest? manifest =
            RipgrepBundledBinaries.Manifest.FirstOrDefault(value =>
                string.Equals(
                    value.Target,
                    target,
                    StringComparison.Ordinal));
        if (manifest is null)
        {
            FailureReason = $"bundle_missing:{target}";
            return null;
        }
        byte[] bundle = ReadResource(manifest.ResourceName);
        string digest = Convert.ToHexString(
                SHA256.HashData(bundle))
            .ToLowerInvariant();
        if (!string.Equals(
                digest,
                manifest.Sha256,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "HPD.Environment.Search.Ripgrep.DigestMismatch: the embedded bundle does not match its manifest.");
        }

        string root =
            $"/tmp/hpd-ripgrep-{Guid.NewGuid():N}";
        string expectedExecutable =
            $"{root}/{manifest.ExecutablePath}";
        string expectedLoader = manifest.LoaderPath is null
            ? expectedExecutable
            : $"{root}/{manifest.LoaderPath}";
        string archive = $"{root}/bundle.tar";
        string script =
            "set -eu; umask 077; " +
            $"root={WorkspaceRipgrep.Quote(root)}; " +
            $"archive={WorkspaceRipgrep.Quote(archive)}; " +
            "mkdir -p \"$root\"; " +
            "test ! -L \"$root\"; " +
            "cat > \"$archive\"; " +
            "tar -xf \"$archive\" -C \"$root\"; " +
            "rm -f \"$archive\"; " +
            "chmod -R u=rwX,go-rwx \"$root\"; " +
            $"test -x {WorkspaceRipgrep.Quote(expectedExecutable)}; " +
            (manifest.LoaderPath is null
                ? ""
                : $"test -x {WorkspaceRipgrep.Quote(expectedLoader)}; ") +
            "printf '%s\\n' \"$root\"";

        await using IProcessInvocationHandle handle =
            await processProvider.StartAsync(
                ProvisioningSpec(
                    script,
                    "/",
                    streamInput: true,
                    writableRoot: root),
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        await handle.WriteStdinAsync(
            bundle,
            cancellationToken).ConfigureAwait(false);
        await handle.CloseStdinAsync(cancellationToken)
            .ConfigureAwait(false);
        ProcessInvocationResult result =
            await handle.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        if (result.CompletionKind is
                ProcessCompletionKind.FailedToStart or
                ProcessCompletionKind.TimedOut or
                ProcessCompletionKind.Cancelled ||
            result.ExitCode != 0)
        {
            FailureReason =
                $"provision_failed:{result.CompletionKind}:{result.ExitCode}:" +
                Encoding.UTF8.GetString(
                    result.Output.Stderr.CapturedBytes.Span);
            return null;
        }

        IReadOnlyList<string> argumentsPrefix =
            manifest.LoaderPath is null
                ? Array.Empty<string>()
                :
                [
                    "--library-path",
                    $"{root}/lib",
                    expectedExecutable,
                ];
        return new WorkspaceRipgrepTool(
            root,
            manifest.LoaderPath is null
                ? expectedExecutable
                : expectedLoader,
            argumentsPrefix,
            manifest.Version,
            manifest.Target,
            manifest.Sha256,
            "bundled");
    }

    private async ValueTask<string?> ProbeTargetAsync(
        CancellationToken cancellationToken)
    {
        ProcessInvocationResult result =
            await processProvider.RunAsync(
                ProvisioningSpec(
                    "uname -s; uname -m",
                    "/",
                    streamInput: false),
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        if (result.CompletionKind is
                ProcessCompletionKind.FailedToStart or
                ProcessCompletionKind.TimedOut or
                ProcessCompletionKind.Cancelled ||
            result.ExitCode != 0)
        {
            return null;
        }
        string[] lines = Encoding.UTF8
            .GetString(
                result.Output.Stdout.CapturedBytes.Span)
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);
        return lines.Length == 2
            ? ResolveTarget(lines[0], lines[1])
            : null;
    }

    private ProcessInvocationSpec ProvisioningSpec(
        string script,
        string workingDirectory,
        bool streamInput,
        string? writableRoot = null) =>
        new()
        {
            Target = unit,
            Role = ProcessRole.Task,
            Command = new ProcessCommandSpec
            {
                FileName = "/bin/sh",
                Arguments = ["-c", script],
                WorkingDirectory = workingDirectory,
            },
            Io = new ProcessIoSpec
            {
                StandardInput = streamInput
                    ? new ProcessInputSpec
                    {
                        Kind = ProcessInputKind.Stream,
                    }
                    : ProcessInputSpec.None,
                StandardOutput = new ProcessOutputSpec
                {
                    Capture = true,
                    Stream = false,
                    MaxCapturedBytes = 64 * 1024,
                },
                StandardError = new ProcessOutputSpec
                {
                    Capture = true,
                    Stream = false,
                    MaxCapturedBytes = 64 * 1024,
                },
                MergeStandardError = false,
            },
            Policy = new ProcessInvocationPolicy
            {
                AllowBackground = false,
                StopProcessTree = true,
                StopOnRunCancellation = true,
                Timeout = ProvisionTimeout,
            },
            Isolation = new ProcessIsolationPolicy
            {
                // This command only materializes fixed package bytes in a
                // private directory. The search process itself still runs
                // through the workspace's requested isolation policy.
                Mode = ProcessIsolationMode.Disabled,
                Filesystem = streamInput
                    ? new FilesystemAccessPolicy
                    {
                        Rules =
                        [
                            new PathAccessRule
                            {
                                Kind = PathAccessRuleKind.AllowWrite,
                                Path = new HostPath(
                                    writableRoot ??
                                    workingDirectory),
                                PatternKind =
                                    PathPatternKind.LiteralOrSubpath,
                                Reason =
                                    "Provision bundled ripgrep",
                            },
                        ],
                    }
                    : FilesystemAccessPolicy.Default,
                Network = NetworkEgressPolicy.Blocked,
                Degradation =
                    ProcessIsolationDegradationPolicy.FailClosed,
            },
            PersistResource = false,
            ObservationRetention =
                ObservationRetentionPolicy.None,
        };

    private static byte[] ReadResource(string resourceName)
    {
        using Stream stream =
            Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException(
                $"HPD.Environment.Search.Ripgrep.ResourceMissing: '{resourceName}' is not embedded in the package.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
