namespace HPD.Environment.Runtime.Workspaces;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HPD.Environment.Contracts;
using Microsoft.Extensions.FileSystemGlobbing;

/// <summary>
/// Session-scoped workspace operations implemented through an Environment
/// process provider. The process lane determines whether the root is a
/// container, VM guest, or another provider-owned filesystem.
/// </summary>
internal sealed class ProcessWorkspaceFileStore
    : IWorkspaceFileStore,
      IWorkspacePathMapper,
      IAsyncDisposable
{
    private const int MaxCapturedBytes = 4 * 1024 * 1024;
    private readonly WorkspaceRootId _rootId;
    private readonly string _root;
    private readonly AccessMode _access;
    private readonly WorkspacePathSemantics _semantics;
    private readonly IProcessProvider _process;
    private readonly TargetHandle<ExecutionUnit> _unit;
    private readonly string _providerName;
    private readonly IWorkspaceRipgrepToolProvisioner? _ripgrepToolProvisioner;
    private readonly bool _enableRipgrep;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private WorkspaceRipgrepTool? _ripgrepTool;
    private bool _ripgrepToolResolutionAttempted;
    private bool _disposed;

    public ProcessWorkspaceFileStore(
        WorkspaceRootId rootId,
        string root,
        AccessMode access,
        WorkspacePathSemantics semantics,
        IProcessProvider process,
        TargetHandle<ExecutionUnit> unit,
        string providerName,
        bool enableRipgrep = true,
        IWorkspaceRipgrepToolProvisioner? ripgrepToolProvisioner = null)
    {
        _rootId = rootId;
        _root = root;
        _access = access;
        _semantics = semantics;
        _process = process;
        _unit = unit;
        _providerName = string.IsNullOrWhiteSpace(providerName)
            ? "Environment"
            : providerName;
        _enableRipgrep = enableRipgrep;
        _ripgrepToolProvisioner = ripgrepToolProvisioner;
    }

    /// <summary>
    /// Validates an absolute POSIX root used by a process-backed workspace.
    /// </summary>
    internal static void ValidateExecutionRoot(
        WorkspaceExecutionPath path)
    {
        if (!string.Equals(
                path.Value,
                path.Value.TrimEnd('/'),
                StringComparison.Ordinal) ||
            !path.Value.StartsWith('/'))
        {
            throw new ArgumentException(
                "Workspace execution path must be an absolute normalized POSIX path.",
                nameof(path));
        }
    }

    public async ValueTask<WorkspaceFileInfo?> GetInfoAsync(
        WorkspacePath path,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        GuestPathInfo info = await StatAsync(
            Resolve(path),
            cancellationToken).ConfigureAwait(false);
        return info.Exists
            ? new WorkspaceFileInfo
            {
                Path = path,
                Kind = info.Directory
                    ? WorkspaceEntryKind.Directory
                    : WorkspaceEntryKind.File,
                Version = info.Version,
            }
            : null;
    }

    public async ValueTask<WorkspaceTextSnapshot?> ReadTextAsync(
        WorkspacePath path,
        WorkspaceTextReadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureOpen();
        if (options.StartLine < 1 ||
            options.MaxLines is <= 0 ||
            options.MaxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Text read bounds must be positive.");
        }
        GuestPathInfo info = await StatAsync(
            Resolve(path),
            cancellationToken).ConfigureAwait(false);
        if (!info.Exists)
        {
            return null;
        }
        if (info.Directory)
        {
            throw new IOException(
                "The requested workspace entry is a directory.");
        }
        string target = Resolve(path);
        string script =
            RejectSymlinkScript(target) +
            $"total=$(awk 'END{{print NR}}' {Quote(target)}); " +
            $"remaining=$(tail -n +{options.StartLine.ToString(CultureInfo.InvariantCulture)} {Quote(target)} | wc -c); " +
            "printf '%s|%s\\n' \"$total\" \"$remaining\"; " +
            $"tail -n +{options.StartLine.ToString(CultureInfo.InvariantCulture)} {Quote(target)} " +
            $"| head -c {options.MaxBytes.ToString(CultureInfo.InvariantCulture)} | base64";
        byte[] output = await CaptureAsync(
            script,
            standardInput: null,
            cancellationToken).ConfigureAwait(false);
        (long totalLines, long remainingBytes, string encoded) =
            ParseMetadataAndPayload(output);
        byte[] content = Convert.FromBase64String(encoded);
        string text;
        try
        {
            text = DecodeUtf8Bounded(content);
        }
        catch (IOException exception)
        {
            throw new IOException(
                "Guest text payload was not valid UTF-8.",
                exception);
        }
        bool truncated = false;
        string returnedText = text;
        if (options.MaxLines is { } maxLines &&
            CountLines(text) > maxLines)
        {
            returnedText = TakeFirstLines(text, maxLines);
            truncated = true;
        }
        if (remainingBytes > options.MaxBytes)
        {
            truncated = true;
        }
        long returnedLines = CountLines(returnedText);
        long endLine = returnedLines == 0
            ? options.StartLine - 1
            : options.StartLine + returnedLines - 1;
        if (endLine > totalLines)
        {
            endLine = totalLines;
        }
        return new WorkspaceTextSnapshot
        {
            Path = path,
            Content = returnedText,
            Version = info.Version,
            StartLine = options.StartLine,
            EndLine = endLine,
            TotalLines = totalLines,
            Truncated = truncated,
        };
    }

    public async ValueTask<WorkspaceBinarySnapshot?> ReadBytesAsync(
        WorkspacePath path,
        WorkspaceByteReadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureOpen();
        if (options.Offset < 0 || options.MaxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Binary read bounds must be positive.");
        }
        GuestPathInfo info = await StatAsync(
            Resolve(path),
            cancellationToken).ConfigureAwait(false);
        if (!info.Exists)
        {
            return null;
        }
        if (info.Directory)
        {
            throw new IOException(
                "The requested workspace entry is a directory.");
        }
        string target = Resolve(path);
        string script =
            RejectSymlinkScript(target) +
            SizeShellFunction("length", target) +
            $"printf '%s\\n' \"$length\"; " +
            $"dd if={Quote(target)} bs=1 skip={options.Offset.ToString(CultureInfo.InvariantCulture)} " +
            $"count={options.MaxBytes.ToString(CultureInfo.InvariantCulture)} 2>/dev/null | base64";
        byte[] output = await CaptureAsync(
            script,
            standardInput: null,
            cancellationToken).ConfigureAwait(false);
        int newline = Array.IndexOf(output, (byte)'\n');
        if (newline < 0 ||
            !long.TryParse(
                Encoding.ASCII.GetString(output, 0, newline)
                    .Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long length))
        {
            throw Malformed("binary read metadata");
        }
        byte[] content = Convert.FromBase64String(
            Encoding.ASCII.GetString(
                output,
                newline + 1,
                output.Length - newline - 1));
        return new WorkspaceBinarySnapshot
        {
            Path = path,
            Content = content,
            Version = info.Version,
            Offset = options.Offset,
            Truncated = options.Offset + content.LongLength < length,
        };
    }

    public ValueTask<WorkspaceMutationResult> WriteTextAsync(
        WorkspacePath path,
        string content,
        WorkspaceWriteCondition condition,
        CancellationToken cancellationToken = default) =>
        WriteBytesAsync(
            path,
            Encoding.UTF8.GetBytes(content),
            condition,
            cancellationToken);

    public async ValueTask<WorkspaceMutationResult> WriteBytesAsync(
        WorkspacePath path,
        ReadOnlyMemory<byte> content,
        WorkspaceWriteCondition condition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);
        EnsureOpen();
        if (_access == AccessMode.ReadOnly)
        {
            return Denied(path, "The workspace root is read-only.");
        }
        string target = Resolve(path);
        await _mutationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            WorkspaceMutationResult? check =
                await ValidateConditionAsync(
                    path,
                    target,
                    condition,
                    cancellationToken).ConfigureAwait(false);
            if (check is not null)
            {
                return check;
            }
            string directory =
                PosixDirectoryName(target);
            string temporary =
                target + $".hpd-{Guid.NewGuid():N}.tmp";
            string conditionScript = condition.CreateOnly
                ? $"if [ -e {Quote(target)} ]; then exit 42; fi; "
                : condition.ExpectedVersion?.ContentDigest is { } digest
                    ? HashShellFunction() +
                        $"actual=$(hash_file {Quote(target)}); " +
                        $"if [ ! -e {Quote(target)} ] || [ \"$actual\" != {Quote(digest.Value)} ]; then exit 43; fi; "
                    : string.Empty;
            string script =
                RejectSymlinkScript(target) +
                conditionScript +
                $"mkdir -p {Quote(directory)}; " +
                $"trap 'rm -f {Quote(temporary)}' EXIT; " +
                Base64DecodeShellFunction() +
                $"decode_base64 > {Quote(temporary)}; " +
                $"mv -f {Quote(temporary)} {Quote(target)}; " +
                "trap - EXIT";
            ProcessRunResult result = await RunAsync(
                script,
                Encoding.ASCII.GetBytes(
                    Convert.ToBase64String(
                        content.ToArray())),
                cancellationToken).ConfigureAwait(false);
            if (result.ExitCode == 42)
            {
                return new WorkspaceMutationResult
                {
                    Outcome = WorkspaceMutationOutcome.AlreadyExists,
                    Path = path,
                };
            }
            if (result.ExitCode == 43)
            {
                return new WorkspaceMutationResult
                {
                    Outcome = WorkspaceMutationOutcome.Stale,
                    Path = path,
                    ObservedVersion =
                        (await StatAsync(target, cancellationToken)
                            .ConfigureAwait(false)).Version,
                };
            }
            EnsureSuccess(result, "write");
            GuestPathInfo info = await StatAsync(
                target,
                cancellationToken).ConfigureAwait(false);
            return new WorkspaceMutationResult
            {
                Outcome = WorkspaceMutationOutcome.Succeeded,
                Path = path,
                Version = info.Version,
            };
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async ValueTask<WorkspaceMutationResult> DeleteAsync(
        WorkspacePath path,
        WorkspaceWriteCondition condition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);
        EnsureOpen();
        if (_access == AccessMode.ReadOnly)
        {
            return Denied(path, "The workspace root is read-only.");
        }
        string target = Resolve(path);
        if (string.Equals(target, _root, StringComparison.Ordinal))
        {
            return Denied(path, "The workspace root cannot be deleted.");
        }
        await _mutationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            WorkspaceMutationResult? check =
                await ValidateConditionAsync(
                    path,
                    target,
                    condition,
                    cancellationToken).ConfigureAwait(false);
            if (check is not null)
            {
                return check;
            }
            string script =
                RejectSymlinkScript(target) +
                $"if [ ! -e {Quote(target)} ]; then exit 44; fi; " +
                $"rm -rf -- {Quote(target)}";
            ProcessRunResult result = await RunAsync(
                script,
                standardInput: null,
                cancellationToken).ConfigureAwait(false);
            if (result.ExitCode == 44)
            {
                return new WorkspaceMutationResult
                {
                    Outcome = WorkspaceMutationOutcome.NotFound,
                    Path = path,
                };
            }
            EnsureSuccess(result, "delete");
            return new WorkspaceMutationResult
            {
                Outcome = WorkspaceMutationOutcome.Succeeded,
                Path = path,
            };
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async ValueTask CreateDirectoryAsync(
        WorkspacePath path,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        if (_access == AccessMode.ReadOnly)
        {
            throw new UnauthorizedAccessException(
                "The workspace root is read-only.");
        }
        string target = Resolve(path);
        string script =
            RejectSymlinkScript(target) +
            $"mkdir -p {Quote(target)}";
        EnsureSuccess(
            await RunAsync(
                script,
                standardInput: null,
                cancellationToken).ConfigureAwait(false),
            "create directory");
    }

    public async ValueTask<WorkspaceListResultSet>
        ListAsync(
            WorkspacePath directory,
            WorkspaceListOptions options,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureOpen();
        if (options.MaxEntries <= 0 ||
            options.MaxDepth is <= 0 ||
            options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Listing bounds must be positive.");
        }
        string guestDirectory = Resolve(directory);
        GuestPathInfo rootInfo = await StatAsync(
            guestDirectory,
            cancellationToken).ConfigureAwait(false);
        if (!rootInfo.Exists || !rootInfo.Directory)
        {
            return new WorkspaceListResultSet();
        }
        string enumeration = options.Recursive
            ? $"find {Quote(guestDirectory)} -print0"
            : $"for entry in {Quote(guestDirectory)}/* {Quote(guestDirectory)}/.[!.]* {Quote(guestDirectory)}/..?*; " +
                "do [ -e \"$entry\" ] || continue; printf '%s\\0' \"$entry\"; done";
        string script =
            RejectSymlinkScript(guestDirectory) +
            enumeration +
            " | base64";
        byte[] encoded = await CaptureAsync(
            script,
            standardInput: null,
            cancellationToken).ConfigureAwait(false);
        string[] guestPaths = Encoding.UTF8.GetString(
                Convert.FromBase64String(
                    Encoding.ASCII.GetString(encoded)))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var glob = options.GlobPattern is { Length: > 0 }
            ? new Matcher(StringComparison.Ordinal)
            : null;
        glob?.AddInclude(options.GlobPattern!);
        IReadOnlyList<WorkspaceIgnoreRule> ignoreRules =
            options.RespectIgnoreFiles
                ? await ReadIgnoreRulesAsync(cancellationToken)
                    .ConfigureAwait(false)
                : [];
        var entries = new List<WorkspaceDirectoryEntry>();
        int ignoredCount = 0;
        bool truncated = false;
        foreach (string guestPath in guestPaths)
        {
            if (string.Equals(
                    guestPath,
                    guestDirectory,
                    StringComparison.Ordinal))
            {
                continue;
            }
            string relative = RelativeToRoot(guestPath);
            string relativeToDirectory = RelativeToRoot(
                guestDirectory,
                guestPath);
            int depth = relativeToDirectory.Count(character =>
                character == '/') + 1;
            if (!options.IncludeHidden &&
                IsHiddenRelativePath(relativeToDirectory) ||
                options.MaxDepth is { } maxDepth &&
                depth > maxDepth)
            {
                ignoredCount++;
                continue;
            }
            GuestPathInfo candidateInfo = await StatAsync(
                guestPath,
                cancellationToken).ConfigureAwait(false);
            if (options.RespectIgnoreFiles &&
                IsIgnored(
                    relative,
                    candidateInfo.Directory,
                    ignoreRules))
            {
                ignoredCount++;
                continue;
            }
            if (glob is not null &&
                !glob.Match(relative).HasMatches)
            {
                continue;
            }
            if (entries.Count >= options.MaxEntries)
            {
                truncated = true;
                break;
            }
            WorkspacePath logical = new(
                _rootId,
                relative);
            entries.Add(new WorkspaceDirectoryEntry
            {
                Path = logical,
                Kind = candidateInfo.Directory
                    ? WorkspaceEntryKind.Directory
                    : WorkspaceEntryKind.File,
                Version = candidateInfo.Version,
            });
        }
        return new WorkspaceListResultSet
        {
            Entries = entries,
            Status = truncated
                ? WorkspaceListStatus.Truncated
                : WorkspaceListStatus.Completed,
            IgnoredCount = ignoredCount,
            Message = truncated ? "entry_limit" : null,
        };
    }

    public async ValueTask<WorkspaceSearchResultSet>
        SearchTextAsync(
            WorkspaceSearchRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureOpen();
        if (request.Query.Length == 0 ||
            request.MaxResults <= 0 ||
            request.BeforeContext < 0 ||
            request.AfterContext < 0 ||
            request.MaxMatchesPerFile is <= 0 ||
            request.MaxDepth is <= 0 ||
            request.MaxColumns is <= 0 ||
            request.MaxFileSizeBytes is <= 0 ||
            request.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Search query and bounds must be valid.");
        }
        string guestRoot = Resolve(request.Root);
        GuestPathInfo rootInfo = await StatAsync(
            guestRoot,
            cancellationToken).ConfigureAwait(false);
        if (!rootInfo.Exists)
        {
            return new WorkspaceSearchResultSet
            {
                Status = WorkspaceSearchStatus.NoMatches,
            };
        }

        WorkspaceSearchResultSet? accelerated =
            _enableRipgrep
                ? await TrySearchWithGuestRipgrepAsync(
                    request,
                    guestRoot,
                    cancellationToken).ConfigureAwait(false)
                : null;
        if (accelerated is not null)
        {
            return accelerated;
        }

        WorkspaceListResultSet listing =
            rootInfo.Directory
                ? await ListAsync(
                    request.Root,
                    new WorkspaceListOptions
                    {
                        Recursive = true,
                        MaxEntries = 10_000,
                        MaxDepth = request.MaxDepth,
                        IncludeHidden = request.IncludeHidden,
                        RespectIgnoreFiles =
                            request.RespectIgnoreFiles,
                        Timeout = request.Timeout,
                    },
                    cancellationToken).ConfigureAwait(false)
                : new WorkspaceListResultSet
                {
                    Entries =
                    [
                        new WorkspaceDirectoryEntry
                        {
                            Path = request.Root,
                            Kind = WorkspaceEntryKind.File,
                            Version = rootInfo.Version,
                        },
                    ],
                };
        Regex? regex = CreateSearchRegex(request);
        var results = new List<WorkspaceSearchResult>();
        var files = new HashSet<WorkspacePath>();
        var counts = new Dictionary<WorkspacePath, long>();
        int ignored = listing.IgnoredCount;
        try
        {
            foreach (WorkspaceDirectoryEntry entry in listing.Entries)
            {
                if (entry.Kind != WorkspaceEntryKind.File)
                {
                    continue;
                }
                string relativeToSearch = request.Root.RelativePath.Length == 0
                    ? entry.Path.RelativePath
                    : RelativeToRoot(
                        Resolve(request.Root),
                        Resolve(entry.Path));
                if (request.GlobPattern is { Length: > 0 } pattern &&
                    !MatchesGlob(relativeToSearch, pattern) ||
                    request.IncludeGlobs.Count > 0 &&
                    !request.IncludeGlobs.Any(glob =>
                        MatchesGlob(relativeToSearch, glob)) ||
                    request.ExcludeGlobs.Any(glob =>
                        MatchesGlob(relativeToSearch, glob)))
                {
                    ignored++;
                    continue;
                }
                if (request.MaxFileSizeBytes is { } maxFileSize &&
                    entry.Version.Size.Value > maxFileSize)
                {
                    ignored++;
                    continue;
                }
                WorkspaceTextSnapshot? snapshot =
                    await ReadTextAsync(
                        entry.Path,
                        new WorkspaceTextReadOptions
                        {
                            StartLine = 1,
                            MaxLines = null,
                            MaxBytes = MaxCapturedBytes,
                        },
                        cancellationToken).ConfigureAwait(false);
                if (snapshot is null || snapshot.Truncated)
                {
                    ignored++;
                    continue;
                }
                string[] lines = snapshot.Content
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Split('\n');
                int matchesInFile = 0;
                var matchedLines = new HashSet<long>();
                for (int offset = 0;
                     offset <= snapshot.Content.Length;)
                {
                    Match match = regex.Match(
                        snapshot.Content,
                        offset);
                    if (!match.Success)
                    {
                        break;
                    }
                    long line = CountLinesBefore(
                        snapshot.Content,
                        match.Index);
                    if ((!request.Multiline &&
                         !matchedLines.Add(line)) ||
                        request.MaxMatchesPerFile is { } perFile &&
                        matchesInFile >= perFile)
                    {
                        offset = match.Index +
                            Math.Max(match.Length, 1);
                        continue;
                    }
                    int outputCount = request.OutputMode switch
                    {
                        WorkspaceSearchOutputMode.Matches =>
                            results.Count,
                        WorkspaceSearchOutputMode.FilesWithMatches =>
                            files.Count,
                        _ => counts.Count,
                    };
                    bool alreadyMatched =
                        request.OutputMode ==
                            WorkspaceSearchOutputMode.FilesWithMatches &&
                        files.Contains(entry.Path);
                    if (!alreadyMatched &&
                        outputCount >= request.MaxResults)
                    {
                        return new WorkspaceSearchResultSet
                        {
                            Results = results,
                            Files = files.ToArray(),
                            Counts = counts.Select(pair =>
                                new WorkspaceSearchCount
                                {
                                    Path = pair.Key,
                                    Count = pair.Value,
                                }).ToArray(),
                            Status = WorkspaceSearchStatus.Truncated,
                            IgnoredFileCount = ignored,
                            Message = "result_limit",
                        };
                    }
                    if (request.OutputMode ==
                        WorkspaceSearchOutputMode.FilesWithMatches)
                    {
                        files.Add(entry.Path);
                        matchesInFile++;
                        offset = match.Index +
                            Math.Max(match.Length, 1);
                        continue;
                    }
                    if (request.OutputMode ==
                        WorkspaceSearchOutputMode.Count)
                    {
                        counts[entry.Path] =
                            counts.GetValueOrDefault(entry.Path) + 1;
                        matchesInFile++;
                        offset = match.Index +
                            Math.Max(match.Length, 1);
                        continue;
                    }
                    int lineStart = match.Index;
                    while (lineStart > 0 &&
                           snapshot.Content[lineStart - 1] != '\n')
                    {
                        lineStart--;
                    }
                    var result = new WorkspaceSearchResult
                    {
                        Path = entry.Path,
                        Line = line,
                        Column = match.Index - lineStart + 1,
                        LineText = request.Multiline
                            ? match.Value
                            : lines[checked((int)line - 1)],
                    };
                    if (request.MaxColumns is { } maxColumns &&
                        result.LineText.Length > maxColumns)
                    {
                        offset = match.Index +
                            Math.Max(match.Length, 1);
                        continue;
                    }
                    if (request.BeforeContext > 0 ||
                        request.AfterContext > 0)
                    {
                        result = result with
                        {
                            ContextLines = CreateContextLines(
                                lines,
                                line,
                                request.BeforeContext,
                                request.AfterContext),
                        };
                    }
                    results.Add(result);
                    matchesInFile++;
                    offset = match.Index + Math.Max(match.Length, 1);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                RegexMatchTimeoutException)
        {
            return new WorkspaceSearchResultSet
            {
                Results = results,
                Files = files.ToArray(),
                Counts = counts.Select(pair =>
                    new WorkspaceSearchCount
                    {
                        Path = pair.Key,
                        Count = pair.Value,
                    }).ToArray(),
                Status = WorkspaceSearchStatus.Failed,
                IgnoredFileCount = ignored,
                Message = exception.Message,
            };
        }
        return new WorkspaceSearchResultSet
        {
            Results = results,
            Files = files
                .OrderBy(
                    path => path.RelativePath,
                    StringComparer.Ordinal)
                .ToArray(),
            Counts = counts
                .OrderBy(
                    pair => pair.Key.RelativePath,
                    StringComparer.Ordinal)
                .Select(pair => new WorkspaceSearchCount
                {
                    Path = pair.Key,
                    Count = pair.Value,
                })
                .ToArray(),
            Status = results.Count == 0 &&
                files.Count == 0 &&
                counts.Count == 0
                ? WorkspaceSearchStatus.NoMatches
                : WorkspaceSearchStatus.Completed,
            IgnoredFileCount = ignored,
        };
    }

    private async ValueTask<WorkspaceSearchResultSet?>
        TrySearchWithGuestRipgrepAsync(
            WorkspaceSearchRequest request,
            string guestRoot,
            CancellationToken cancellationToken)
    {
        string searchPath = string.Equals(
                guestRoot,
                _root,
                StringComparison.Ordinal)
            ? "."
            : "./" + RelativeToRoot(guestRoot);
        IReadOnlyList<string> arguments =
            WorkspaceRipgrep.BuildArguments(request, searchPath);
        WorkspaceRipgrepTool? tool =
            await ResolveRipgrepToolAsync(cancellationToken)
                .ConfigureAwait(false);
        string script = WorkspaceRipgrep.BuildCommand(
            tool,
            arguments);
        ProcessRunResult result;
        try
        {
            result = await RunAsync(
                script,
                standardInput: null,
                cancellationToken,
                request.Timeout).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or
                InvalidOperationException or
                System.ComponentModel.Win32Exception)
        {
            return null;
        }
        if (result.ExitCode == 127 &&
            result.Stdout.Length == 0)
        {
            return null;
        }
        bool timedOut =
            result.CompletionKind == ProcessCompletionKind.TimedOut;
        bool cancelled =
            result.CompletionKind == ProcessCompletionKind.Cancelled;
        if (cancelled)
        {
            throw new OperationCanceledException(
                cancellationToken);
        }
        WorkspaceRipgrepParseResult parsed = WorkspaceRipgrep.Parse(
            Encoding.UTF8.GetString(result.Stdout),
            request,
            result.ExitCode,
            timedOut,
            cancelled: false,
            Encoding.UTF8.GetString(result.Stderr),
            path =>
            {
                string normalized = path.Replace('\\', '/')
                    .TrimStart('.', '/');
                try
                {
                    return new WorkspacePath(
                        _rootId,
                        NormalizeRelative(normalized));
                }
                catch (UnauthorizedAccessException)
                {
                    return null;
                }
            });
        WorkspaceSearchResultSet resultSet =
            result.StdoutTruncated &&
            parsed.ResultSet.Status is
                WorkspaceSearchStatus.Completed or
                WorkspaceSearchStatus.NoMatches
                ? parsed.ResultSet with
                {
                    Status = WorkspaceSearchStatus.Truncated,
                    Message = "output_limit",
                }
                : parsed.ResultSet;
        return resultSet with
        {
            Message = resultSet.Message ??
                (timedOut ? "timeout" : null),
        };
    }

    private async ValueTask<WorkspaceRipgrepTool?>
        ResolveRipgrepToolAsync(
            CancellationToken cancellationToken)
    {
        if (_ripgrepToolResolutionAttempted)
        {
            return _ripgrepTool;
        }
        if (_ripgrepToolProvisioner is null)
        {
            _ripgrepToolResolutionAttempted = true;
            return null;
        }
        try
        {
            _ripgrepTool = await _ripgrepToolProvisioner
                .EnsureAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or
                InvalidOperationException or
                System.ComponentModel.Win32Exception)
        {
            _ripgrepTool = null;
        }
        finally
        {
            _ripgrepToolResolutionAttempted = true;
        }
        return _ripgrepTool;
    }

    private static Regex CreateSearchRegex(
        WorkspaceSearchRequest request)
    {
        string pattern = request.FixedStrings
            ? Regex.Escape(request.Query)
            : request.Query;
        if (request.WordRegexp)
        {
            pattern = $@"\b(?:{pattern})\b";
        }
        RegexOptions options = RegexOptions.CultureInvariant;
        bool insensitive = request.CaseMode switch
        {
            WorkspaceSearchCaseMode.Insensitive => true,
            WorkspaceSearchCaseMode.Sensitive => false,
            WorkspaceSearchCaseMode.Smart =>
                !request.Query.Any(char.IsUpper),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request.CaseMode)),
        };
        if (insensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }
        if (request.Multiline)
        {
            options |= RegexOptions.Singleline;
        }
        return new Regex(
            pattern,
            options,
            TimeSpan.FromMilliseconds(
                Math.Max(
                    1,
                    Math.Min(
                        request.Timeout.TotalMilliseconds,
                        int.MaxValue))));
    }

    private static long CountLinesBefore(
        string content,
        int index)
    {
        long line = 1;
        for (int position = 0; position < index; position++)
        {
            if (content[position] == '\n')
            {
                line++;
            }
        }
        return line;
    }

    private static IReadOnlyList<WorkspaceSearchContextLine>
        CreateContextLines(
            IReadOnlyList<string> lines,
            long matchLine,
            int before,
            int after)
    {
        long first = Math.Max(1, matchLine - before);
        long last = Math.Min(lines.Count, matchLine + after);
        var context = new List<WorkspaceSearchContextLine>();
        for (long line = first; line <= last; line++)
        {
            if (line == matchLine)
            {
                continue;
            }
            context.Add(new WorkspaceSearchContextLine
            {
                Line = line,
                Text = lines[checked((int)line - 1)],
            });
        }
        return context;
    }

    private static bool IsHiddenRelativePath(
        string relativePath) =>
        relativePath.Split('/').Any(part =>
            part.StartsWith(".", StringComparison.Ordinal));

    private static bool MatchesGlob(
        string relativePath,
        string pattern)
    {
        var matcher = new Matcher(StringComparison.Ordinal);
        matcher.AddInclude(pattern);
        return matcher.Match(relativePath).HasMatches;
    }

    private static bool IsIgnored(
        string relative,
        bool isDirectory,
        IReadOnlyList<WorkspaceIgnoreRule> rules)
    {
        bool ignored = false;
        foreach (WorkspaceIgnoreRule rule in rules)
        {
            string candidate =
                rule.BaseRelative.Length == 0
                    ? relative
                    : relative.StartsWith(
                            rule.BaseRelative + "/",
                            StringComparison.Ordinal)
                        ? relative[
                            (rule.BaseRelative.Length + 1)..]
                        : relative;
            bool matches = MatchesGlob(candidate, rule.Pattern);
            if (!matches &&
                !rule.Pattern.Contains(
                    '/',
                    StringComparison.Ordinal))
            {
                matches = candidate.Split('/')
                    .Any(segment =>
                        MatchesGlob(segment, rule.Pattern));
            }
            string[] segments = candidate.Split('/');
            bool matchesDirectoryAncestor =
                rule.DirectoryOnly && !isDirectory &&
                segments
                    .Take(Math.Max(0, segments.Length - 1))
                    .Any(segment =>
                        MatchesGlob(segment, rule.Pattern));
            if (matches &&
                (!rule.DirectoryOnly ||
                 isDirectory ||
                 matchesDirectoryAncestor))
            {
                ignored = !rule.Negated;
            }
        }
        return ignored;
    }

    private async ValueTask<IReadOnlyList<WorkspaceIgnoreRule>>
        ReadIgnoreRulesAsync(CancellationToken cancellationToken)
    {
        var rules = new List<WorkspaceIgnoreRule>();
        string findScript =
            $"find {Quote(_root)} -type f \\( " +
            "-name .gitignore -o -name .ignore -o -name .rgignore " +
            "-o -path '*/.git/info/exclude'" +
            $" \\) -print0 | base64";
        byte[] encodedPaths = await CaptureAsync(
            findScript,
            standardInput: null,
            cancellationToken).ConfigureAwait(false);
        string[] ignorePaths = Encoding.UTF8.GetString(
                Convert.FromBase64String(
                    Encoding.ASCII.GetString(encodedPaths)))
            .Split(
                '\0',
                StringSplitOptions.RemoveEmptyEntries);
        foreach (string path in ignorePaths)
        {
            string script =
                $"if [ -f {Quote(path)} ]; then base64 < {Quote(path)}; fi";
            byte[] encoded = await CaptureAsync(
                script,
                standardInput: null,
                cancellationToken).ConfigureAwait(false);
            if (encoded.Length == 0)
            {
                continue;
            }
            string content = Encoding.UTF8.GetString(
                Convert.FromBase64String(
                    Encoding.ASCII.GetString(encoded)));
            string baseRelative;
            if (path.EndsWith(
                    "/.git/info/exclude",
                    StringComparison.Ordinal))
            {
                string repositoryRoot = path[
                    ..^"/.git/info/exclude".Length];
                baseRelative = string.Equals(
                        repositoryRoot,
                        _root,
                        StringComparison.Ordinal)
                    ? string.Empty
                    : RelativeToRoot(repositoryRoot);
            }
            else
            {
                string directory = PosixDirectoryName(path);
                baseRelative = string.Equals(
                        directory,
                        _root,
                        StringComparison.Ordinal)
                    ? string.Empty
                    : RelativeToRoot(directory);
            }
            foreach (string rawLine in content.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 ||
                    line.StartsWith('#'))
                {
                    continue;
                }
                bool negated = line.StartsWith('!');
                if (negated)
                {
                    line = line[1..];
                }
                bool directoryOnly = line.EndsWith('/');
                if (directoryOnly)
                {
                    line = line.TrimEnd('/');
                }
                if (line.Length == 0)
                {
                    continue;
                }
                rules.Add(new WorkspaceIgnoreRule(
                    line.TrimStart('/'),
                    negated,
                    directoryOnly,
                    baseRelative));
            }
        }
        const string globalIgnoreScript =
            "for ignore in \"${GIT_GLOBAL_EXCLUDES:-}\" " +
            "\"${XDG_CONFIG_HOME:-$HOME/.config}/git/ignore\" " +
            "\"$HOME/.gitignore_global\"; do " +
            "[ -n \"$ignore\" ] && [ -f \"$ignore\" ] && " +
            "base64 < \"$ignore\" && printf '\\0'; done; exit 0";
        byte[] encodedGlobal = await CaptureAsync(
            globalIgnoreScript,
            standardInput: null,
            cancellationToken).ConfigureAwait(false);
        string[] globalRules = Encoding.UTF8.GetString(
                Convert.FromBase64String(
                    Encoding.ASCII.GetString(encodedGlobal)))
            .Split(
                '\0',
                StringSplitOptions.RemoveEmptyEntries);
        foreach (string content in globalRules)
        {
            AddIgnoreRules(
                rules,
                content,
                baseRelative: string.Empty);
        }
        return rules;
    }

    private static void AddIgnoreRules(
        ICollection<WorkspaceIgnoreRule> rules,
        string content,
        string baseRelative)
    {
        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 ||
                line.StartsWith('#'))
            {
                continue;
            }
            bool negated = line.StartsWith('!');
            if (negated)
            {
                line = line[1..];
            }
            bool directoryOnly = line.EndsWith('/');
            if (directoryOnly)
            {
                line = line.TrimEnd('/');
            }
            if (line.Length == 0)
            {
                continue;
            }
            rules.Add(new WorkspaceIgnoreRule(
                line.TrimStart('/'),
                negated,
                directoryOnly,
                baseRelative));
        }
    }

    public WorkspaceExecutionPath ToExecutionPath(
        WorkspacePath path) =>
        new(Resolve(path));

    public WorkspacePath? TryFromExecutionPath(
        WorkspaceExecutionPath path)
    {
        EnsureOpen();
        if (path.Value == _root)
        {
            return new WorkspacePath(_rootId, string.Empty);
        }
        if (!path.Value.StartsWith(
                _root + "/",
                _semantics.CaseSensitive
                    ? StringComparison.Ordinal
                    : StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        string relative = path.Value[_root.Length..]
            .TrimStart('/');
        return new WorkspacePath(
            _rootId,
            NormalizeRelative(relative));
    }

    public Uri ToExecutionFileUri(WorkspacePath path) =>
        new UriBuilder
        {
            Scheme = Uri.UriSchemeFile,
            Host = string.Empty,
            Path = Resolve(path),
        }.Uri;

    public WorkspacePath? TryFromExecutionFileUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri ||
            !string.Equals(
                uri.Scheme,
                Uri.UriSchemeFile,
                StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Length > 0)
        {
            return null;
        }
        return TryFromExecutionPath(
            new WorkspaceExecutionPath(
                Uri.UnescapeDataString(uri.AbsolutePath)));
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }
        _disposed = true;
        _mutationGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private string Resolve(WorkspacePath path)
    {
        EnsureOpen();
        if (!path.RootId.Equals(_rootId))
        {
            throw new UnauthorizedAccessException(
                $"Workspace root '{path.RootId.Value}' is not available.");
        }
        string relative = NormalizeRelative(
            path.RelativePath);
        return relative.Length == 0
            ? _root
            : _root.TrimEnd('/') + "/" + relative;
    }

    private static string NormalizeRelative(string value)
    {
        if (value.Contains('\0') ||
            value.Contains('\\') ||
            value.StartsWith('/'))
        {
            throw new UnauthorizedAccessException(
                "Workspace paths must be relative POSIX paths.");
        }
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

    private async ValueTask<WorkspaceMutationResult?>
        ValidateConditionAsync(
            WorkspacePath logical,
            string guestPath,
            WorkspaceWriteCondition condition,
            CancellationToken cancellationToken)
    {
        GuestPathInfo info = await StatAsync(
            guestPath,
            cancellationToken).ConfigureAwait(false);
        if (condition.CreateOnly && info.Exists)
        {
            return new WorkspaceMutationResult
            {
                Outcome = WorkspaceMutationOutcome.AlreadyExists,
                Path = logical,
                ObservedVersion = info.Version,
            };
        }
        if (condition.ExpectedVersion is { } expected)
        {
            if (!info.Exists)
            {
                return new WorkspaceMutationResult
                {
                    Outcome = WorkspaceMutationOutcome.NotFound,
                    Path = logical,
                };
            }
            if (!VersionsMatch(expected, info.Version))
            {
                return new WorkspaceMutationResult
                {
                    Outcome = WorkspaceMutationOutcome.Stale,
                    Path = logical,
                    ObservedVersion = info.Version,
                };
            }
        }
        if (!info.Exists && !condition.AllowCreate)
        {
            return new WorkspaceMutationResult
            {
                Outcome = WorkspaceMutationOutcome.NotFound,
                Path = logical,
            };
        }
        return null;
    }

    private async ValueTask<GuestPathInfo> StatAsync(
        string path,
        CancellationToken cancellationToken)
    {
        string script =
            RejectSymlinkScript(path) +
            $"if [ ! -e {Quote(path)} ]; then exit 44; fi; " +
            SizeShellFunction("size", path) +
            StatFormatShellFunction("mtime", path) +
            HashShellFunction() +
            $"if [ -d {Quote(path)} ]; then " +
            "printf 'D|0|%s|%s\\n' \"$mtime\" ''; " +
            $"elif [ -f {Quote(path)} ]; then " +
            "printf 'F|%s|%s|%s\\n' \"$size\" \"$mtime\" " +
            $"\"$(hash_file {Quote(path)})\"; " +
            "else exit 45; fi";
        ProcessRunResult result = await RunAsync(
            script,
            standardInput: null,
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 44)
        {
            return GuestPathInfo.Missing;
        }
        EnsureSuccess(result, "stat");
        string[] parts = Encoding.ASCII.GetString(
                result.Stdout)
            .Trim()
            .Split('|');
        if (parts.Length != 4 ||
            !long.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long size) ||
            !long.TryParse(
                parts[2],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long seconds))
        {
            throw Malformed("stat");
        }
        return new GuestPathInfo(
            true,
            parts[0] == "D",
            new WorkspaceContentVersion(
                BackendRevision: null,
                ContentDigest:
                    parts[0] == "F" && parts[3].Length > 0
                        ? new Digest("sha256", parts[3])
                        : null,
                ModifiedAt:
                    DateTimeOffset.FromUnixTimeSeconds(seconds),
                Size: new ByteSize(size)));
    }

    private async ValueTask<byte[]> CaptureAsync(
        string script,
        ReadOnlyMemory<byte>? standardInput,
        CancellationToken cancellationToken)
    {
        ProcessRunResult result = await RunAsync(
            script,
            standardInput,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, "workspace operation");
        return result.Stdout;
    }

    private async ValueTask<ProcessRunResult> RunAsync(
        string script,
        ReadOnlyMemory<byte>? standardInput,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        ProcessInvocationSpec spec = new()
        {
            Target = _unit,
            Role = ProcessRole.Exec,
            Command = new ProcessCommandSpec
            {
                FileName = "/bin/sh",
                Arguments = ["-c", script],
                WorkingDirectory = _root,
            },
            Io = new ProcessIoSpec
            {
                StandardInput = standardInput is null
                    ? ProcessInputSpec.None
                    : new ProcessInputSpec
                    {
                        Kind = ProcessInputKind.Stream,
                    },
                StandardOutput = new ProcessOutputSpec
                {
                    Capture = true,
                    Stream = false,
                    MaxCapturedBytes = MaxCapturedBytes,
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
                Timeout = timeout,
            },
            Isolation = ProcessIsolationPolicy.Default with
            {
                Mode = ProcessIsolationMode.Disabled,
            },
            PersistResource = false,
            ObservationRetention = ObservationRetentionPolicy.None,
        };
        await using IProcessInvocationHandle handle =
            await _process.StartAsync(
                spec,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        if (standardInput is { } input)
        {
            await handle.WriteStdinAsync(
                input,
                cancellationToken).ConfigureAwait(false);
        }
        await handle.CloseStdinAsync(cancellationToken)
            .ConfigureAwait(false);
        ProcessInvocationResult result = await handle.WaitAsync(
            cancellationToken).ConfigureAwait(false);
        return new ProcessRunResult(
            result.ExitCode ?? -1,
            result.Output.Stdout.CapturedBytes.ToArray(),
            result.Output.Stderr.CapturedBytes.ToArray(),
            result.CompletionKind,
            result.Output.Stdout.Truncated);
    }

    private string RejectSymlinkScript(
        string target)
    {
        if (target.Contains('\n') ||
            target.Contains('\r'))
        {
            throw new UnauthorizedAccessException(
                "Guest workspace paths cannot contain shell control characters.");
        }
        var script = new StringBuilder();
        script.Append("current=")
            .Append(Quote(_root))
            .Append("; ");
        string[] segments = RelativeToRoot(target).Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);
        foreach (string segment in segments)
        {
            if (segment == "..")
            {
                throw new UnauthorizedAccessException(
                    "Guest workspace path traversal is denied.");
            }
            script.Append(
                    "current=$(printf '%s/%s' \"$current\" ")
                .Append(Quote(segment))
                .Append("); [ -L \"$current\" ] && exit 83; ");
        }
        return script.ToString();
    }

    private (long TotalLines, long RemainingBytes, string Payload)
        ParseMetadataAndPayload(byte[] output)
    {
        int newline = Array.IndexOf(output, (byte)'\n');
        if (newline < 0)
        {
            throw new InvalidOperationException(
                $"{_providerName}.WorkspaceResponseMalformed: process text read metadata response was malformed: " +
                Encoding.UTF8.GetString(output));
        }
        string[] parts = Encoding.ASCII
            .GetString(output, 0, newline)
            .Split('|');
        if (parts.Length != 2 ||
            !long.TryParse(
                parts[0].Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long total) ||
            !long.TryParse(
                parts[1].Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long remaining))
        {
            throw new InvalidOperationException(
                $"{_providerName}.WorkspaceResponseMalformed: process text read metadata response was malformed: " +
                Encoding.UTF8.GetString(output));
        }
        return (
            total,
            remaining,
            Encoding.ASCII.GetString(
                output,
                newline + 1,
                output.Length - newline - 1));
    }

    private static string DecodeUtf8Bounded(byte[] content)
    {
        int length = content.Length;
        while (length > 0)
        {
            try
            {
                return new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true)
                    .GetString(content, 0, length);
            }
            catch (DecoderFallbackException exception)
            {
                if (length > content.Length - 4)
                {
                    length--;
                    continue;
                }
                throw new IOException(
                    "Workspace text contains invalid UTF-8.",
                    exception);
            }
        }
        return string.Empty;
    }

    private static long CountLines(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }
        long lines = 0;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                lines++;
            }
        }
        return text.EndsWith('\n') ? lines : lines + 1;
    }

    private static string TakeFirstLines(
        string text,
        int lineCount)
    {
        int seen = 0;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n')
            {
                continue;
            }
            seen++;
            if (seen >= lineCount)
            {
                return text[..(index + 1)];
            }
        }
        return text;
    }

    private static bool VersionsMatch(
        WorkspaceContentVersion expected,
        WorkspaceContentVersion observed) =>
        expected.ContentDigest is { } expectedDigest &&
        observed.ContentDigest is { } observedDigest &&
        string.Equals(
            expectedDigest.Algorithm,
            observedDigest.Algorithm,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            expectedDigest.Value,
            observedDigest.Value,
            StringComparison.OrdinalIgnoreCase);

    private static string RelativeToRoot(
        string root,
        string path) =>
        path == root
            ? string.Empty
            : path[root.Length..].TrimStart('/');

    private string RelativeToRoot(string path) =>
        RelativeToRoot(_root, path);

    private static string PosixDirectoryName(string path)
    {
        int separator = path.TrimEnd('/')
            .LastIndexOf('/');
        return separator <= 0
            ? "/"
            : path[..separator];
    }

    private static string Quote(string value) =>
        "'" + value.Replace(
            "'",
            "'\"'\"'",
            StringComparison.Ordinal) + "'";

    private static string SizeShellFunction(
        string variable,
        string path) =>
        $"{variable}=$(stat -c %s {Quote(path)} 2>/dev/null || stat -f %z {Quote(path)} 2>/dev/null || printf '0'); ";

    private static string StatFormatShellFunction(
        string variable,
        string path) =>
        $"{variable}=$(stat -c %Y {Quote(path)} 2>/dev/null || stat -f %m {Quote(path)} 2>/dev/null || printf '0'); ";

    private static string HashShellFunction() =>
        "if command -v sha256sum >/dev/null 2>&1; then " +
        "hash_file() { sha256sum \"$1\" | cut -d' ' -f1; }; " +
        "elif command -v shasum >/dev/null 2>&1; then " +
        "hash_file() { shasum -a 256 \"$1\" | cut -d' ' -f1; }; " +
        "else hash_file() { openssl dgst -sha256 \"$1\" | sed 's/^.*= //'; }; fi; ";

    private static string Base64DecodeShellFunction() =>
        "if printf '' | base64 -d >/dev/null 2>&1; then " +
        "decode_base64() { base64 -d; }; " +
        "else decode_base64() { base64 -D; }; fi; ";

    private static void EnsureSuccess(
        ProcessRunResult result,
        string operation)
    {
        if (result.ExitCode == 0)
        {
            return;
        }
        string detail = Encoding.UTF8.GetString(
            result.Stderr);
        if (detail.Length > 512)
        {
            detail = detail[..512];
        }
        throw new IOException(
            $"Guest workspace {operation} failed with exit code {result.ExitCode}: {detail}");
    }

    private static WorkspaceMutationResult Denied(
        WorkspacePath path,
        string message) =>
        new()
        {
            Outcome = WorkspaceMutationOutcome.AccessDenied,
            Path = path,
            Message = message,
        };

    private InvalidOperationException Malformed(
        string operation) =>
        new(
            $"{_providerName}.WorkspaceResponseMalformed: process {operation} response was malformed.");

    private void EnsureOpen() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record WorkspaceIgnoreRule(
        string Pattern,
        bool Negated,
        bool DirectoryOnly,
        string BaseRelative);

    private sealed record GuestPathInfo(
        bool Exists,
        bool Directory,
        WorkspaceContentVersion Version)
    {
        public static GuestPathInfo Missing { get; } = new(
            false,
            false,
            new WorkspaceContentVersion(
                null,
                null,
                null,
                default));
    }

    private sealed record ProcessRunResult(
        int ExitCode,
        byte[] Stdout,
        byte[] Stderr,
        ProcessCompletionKind CompletionKind,
        bool StdoutTruncated);
}
