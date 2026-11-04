namespace HPD.Environment.Runtime.Workspaces;

using System.Text;
using HPD.Environment.Contracts;

/// <summary>
/// Optional workspace-search accelerator supplied by a provider. Implementations
/// execute ripgrep through the Environment process lane and return null when the
/// backend has no ripgrep binary.
/// </summary>
internal interface IWorkspaceRipgrepExecutor
{
    /// <summary>Runs ripgrep against one provider-visible workspace root.</summary>
    ValueTask<WorkspaceSearchResultSet?> ExecuteAsync(
        WorkspaceRootId rootId,
        string executionRoot,
        WorkspaceSearchRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Executes workspace ripgrep searches through an Environment process provider.
/// The selected provider owns isolation, shells, and path visibility.
/// </summary>
internal sealed class ProcessWorkspaceRipgrepExecutor(
    IProcessProvider processProvider,
    TargetHandle<ExecutionUnit> unit,
    IWorkspaceRipgrepToolProvisioner? toolProvisioner = null)
    : IWorkspaceRipgrepExecutor
{
    private const int MaximumOutputBytes = 4 * 1024 * 1024;
    private WorkspaceRipgrepTool? _tool;
    private bool _toolResolutionAttempted;

    public async ValueTask<WorkspaceSearchResultSet?> ExecuteAsync(
        WorkspaceRootId rootId,
        string executionRoot,
        WorkspaceSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(executionRoot) ||
            !executionRoot.StartsWith('/'))
        {
            return null;
        }
        IReadOnlyList<string> arguments =
            WorkspaceRipgrep.BuildArguments(request, ".");
        WorkspaceRipgrepTool? tool =
            await ResolveToolAsync(cancellationToken)
                .ConfigureAwait(false);
        string script = WorkspaceRipgrep.BuildCommand(
            tool,
            arguments);
        try
        {
            ProcessInvocationResult result =
                await processProvider.RunAsync(
                    new ProcessInvocationSpec
                    {
                        Target = unit,
                        Role = ProcessRole.Task,
                        Command = new ProcessCommandSpec
                        {
                            FileName = "/bin/sh",
                            Arguments = ["-c", script],
                            WorkingDirectory = executionRoot,
                        },
                        Io = new ProcessIoSpec
                        {
                            StandardInput = ProcessInputSpec.None,
                            StandardOutput = new ProcessOutputSpec
                            {
                                Capture = true,
                                Stream = false,
                                MaxCapturedBytes =
                                    MaximumOutputBytes,
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
                            Timeout = request.Timeout,
                        },
                        Isolation = new ProcessIsolationPolicy
                        {
                            Mode = ProcessIsolationMode.Isolated,
                            Filesystem = new FilesystemAccessPolicy
                            {
                                Rules =
                                [
                                    new PathAccessRule
                                    {
                                        Kind =
                                            PathAccessRuleKind.AllowRead,
                                        Path = new HostPath(
                                            executionRoot),
                                        PatternKind =
                                            PathPatternKind
                                                .LiteralOrSubpath,
                                        Reason =
                                            "Workspace-scoped ripgrep search",
                                    },
                                    .. tool is null
                                        ? Array.Empty<PathAccessRule>()
                                        :
                                        new[]
                                        {
                                            new PathAccessRule
                                            {
                                                Kind =
                                                    PathAccessRuleKind
                                                        .AllowRead,
                                                Path = new HostPath(
                                                    tool.RootPath),
                                                PatternKind =
                                                    PathPatternKind
                                                        .LiteralOrSubpath,
                                                Reason =
                                                    "Bundled ripgrep executable",
                                            },
                                        },
                                ],
                                Symlinks =
                                    SymlinkEvaluationPolicy
                                        .DenySymlinks,
                            },
                            Network = NetworkEgressPolicy.Blocked,
                        },
                        PersistResource = false,
                        ObservationRetention =
                            ObservationRetentionPolicy.None,
                    },
                    cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            if (result.CompletionKind ==
                    ProcessCompletionKind.FailedToStart ||
                result.ExitCode == 127 &&
                result.Output.Stdout.BytesObserved == 0)
            {
                return null;
            }
            bool timedOut =
                result.CompletionKind ==
                ProcessCompletionKind.TimedOut;
            bool cancelled =
                result.CompletionKind ==
                ProcessCompletionKind.Cancelled;
            if (cancelled)
            {
                throw new OperationCanceledException(
                    cancellationToken);
            }
            WorkspaceRipgrepParseResult parsed =
                WorkspaceRipgrep.Parse(
                    Encoding.UTF8.GetString(
                        result.Output.Stdout.CapturedBytes.Span),
                    request,
                    result.ExitCode,
                    timedOut,
                    cancelled: false,
                    Encoding.UTF8.GetString(
                        result.Output.Stderr.CapturedBytes.Span),
                    path => MapRelativePath(rootId, path));
            WorkspaceSearchResultSet resultSet =
                result.Output.Stdout.Truncated &&
                parsed.ResultSet.Status is
                    WorkspaceSearchStatus.Completed or
                    WorkspaceSearchStatus.NoMatches
                    ? parsed.ResultSet with
                    {
                        Status =
                            WorkspaceSearchStatus.Truncated,
                        Message = "output_limit",
                    }
                    : parsed.ResultSet;
            return resultSet with
            {
                Message = resultSet.Message ??
                    (timedOut ? "timeout" : null),
            };
        }
        catch (Exception exception) when (
            exception is IOException or
                InvalidOperationException or
                System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private async ValueTask<WorkspaceRipgrepTool?>
        ResolveToolAsync(
            CancellationToken cancellationToken)
    {
        if (_toolResolutionAttempted)
        {
            return _tool;
        }
        if (toolProvisioner is null)
        {
            _toolResolutionAttempted = true;
            return null;
        }
        try
        {
            _tool = await toolProvisioner
                .EnsureAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or
                InvalidOperationException or
                System.ComponentModel.Win32Exception)
        {
            _tool = null;
        }
        finally
        {
            _toolResolutionAttempted = true;
        }
        return _tool;
    }

    private static WorkspacePath? MapRelativePath(
        WorkspaceRootId rootId,
        string path)
    {
        string normalized = path.Replace('\\', '/')
            .TrimStart('.', '/');
        try
        {
            return new WorkspacePath(
                rootId,
                NormalizeRelative(normalized));
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string NormalizeRelative(string value)
    {
        var segments = new List<string>();
        foreach (string segment in value.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }
            if (segment == "..")
            {
                throw new UnauthorizedAccessException(
                    "Workspace path traversal is denied.");
            }
            segments.Add(segment);
        }
        return string.Join('/', segments);
    }

}
