namespace HPD.Environment.Runtime.Workspaces;

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HPD.Environment.Contracts;
using Microsoft.Extensions.FileSystemGlobbing;

/// <summary>
/// Root-scoped file operations over a host workspace. Every logical path is
/// canonicalized below the configured root, symlinks are denied, and all
/// model-facing results retain logical paths.
/// </summary>
public sealed class HostDirectoryWorkspaceFileStore
    : IWorkspaceFileStore,
      IWorkspacePathMapper,
      IAsyncDisposable
{
    private readonly WorkspaceRootId _rootId;
    private readonly string _rootPath;
    private readonly AccessMode _access;
    private readonly WorkspacePathSemantics _semantics;
    private readonly IWorkspaceRipgrepExecutor? _ripgrepExecutor;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private bool _disposed;

    public HostDirectoryWorkspaceFileStore(
        WorkspaceRootId rootId,
        string rootPath,
        AccessMode access,
        WorkspacePathSemantics semantics)
        : this(
            rootId,
            rootPath,
            access,
            semantics,
            ripgrepExecutor: null)
    {
    }

    internal HostDirectoryWorkspaceFileStore(
        WorkspaceRootId rootId,
        string rootPath,
        AccessMode access,
        WorkspacePathSemantics semantics,
        IWorkspaceRipgrepExecutor? ripgrepExecutor)
    {
        _rootId = rootId;
        _rootPath = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(rootPath));
        _access = access;
        _semantics = semantics;
        _ripgrepExecutor = ripgrepExecutor;
    }

    public ValueTask<WorkspaceFileInfo?> GetInfoAsync(
        WorkspacePath path,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        cancellationToken.ThrowIfCancellationRequested();
        string fullPath = Resolve(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            return ValueTask.FromResult<WorkspaceFileInfo?>(null);
        }

        return GetInfoCoreAsync(path, fullPath, cancellationToken);
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
        string fullPath = Resolve(path);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        WorkspaceContentVersion version =
            await ComputeVersionAsync(fullPath, cancellationToken)
                .ConfigureAwait(false);
        long totalLines = await CountLinesAsync(
                fullPath,
                cancellationToken)
            .ConfigureAwait(false);
        (byte[] bytes, long endLine, bool truncated) =
            await ReadTextBytesAsync(
                fullPath,
                options,
                cancellationToken).ConfigureAwait(false);
        return new WorkspaceTextSnapshot
        {
            Path = path,
            Content = DecodeUtf8(bytes),
            Version = version,
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
        string fullPath = Resolve(path);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        WorkspaceContentVersion version =
            await ComputeVersionAsync(fullPath, cancellationToken)
                .ConfigureAwait(false);
        byte[] content;
        bool truncated;
        await using (FileStream stream = OpenRead(fullPath))
        {
            if (options.Offset > stream.Length)
            {
                content = [];
                truncated = false;
            }
            else
            {
                stream.Position = options.Offset;
                int count = (int)Math.Min(
                    options.MaxBytes,
                    stream.Length - options.Offset);
                content = new byte[count];
                int read = await stream.ReadAsync(
                    content,
                    cancellationToken).ConfigureAwait(false);
                if (read != content.Length)
                {
                    Array.Resize(ref content, read);
                }
                truncated = options.Offset + content.Length < stream.Length;
            }
        }

        return new WorkspaceBinarySnapshot
        {
            Path = path,
            Content = content,
            Version = version,
            Offset = options.Offset,
            Truncated = truncated,
        };
    }

    public ValueTask<WorkspaceMutationResult> WriteTextAsync(
        WorkspacePath path,
        string content,
        WorkspaceWriteCondition condition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        return WriteBytesAsync(
            path,
            Encoding.UTF8.GetBytes(content),
            condition,
            cancellationToken);
    }

    public async ValueTask<WorkspaceMutationResult> WriteBytesAsync(
        WorkspacePath path,
        ReadOnlyMemory<byte> content,
        WorkspaceWriteCondition condition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);
        EnsureOpen();
        if (_access is AccessMode.ReadOnly)
        {
            return Denied(path, "The workspace root is read-only.");
        }

        string fullPath = Resolve(path);
        await _mutationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            WorkspaceMutationResult? conditionFailure =
                await ValidateWriteConditionAsync(
                    path,
                    condition,
                    cancellationToken).ConfigureAwait(false);
            if (conditionFailure is not null)
            {
                return conditionFailure;
            }

            string? directory = Path.GetDirectoryName(fullPath);
            if (directory is not null)
            {
                EnsureBeneathRoot(directory);
                Directory.CreateDirectory(directory);
            }

            if (condition.CreateOnly)
            {
                await using FileStream created = new(
                    fullPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 64 * 1024,
                    useAsync: true);
                await created.WriteAsync(content, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                string temporaryPath =
                    fullPath + $".hpd-{Guid.NewGuid():N}.tmp";
                try
                {
                    await File.WriteAllBytesAsync(
                        temporaryPath,
                        content.ToArray(),
                        cancellationToken).ConfigureAwait(false);
                    File.Move(temporaryPath, fullPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
            }

            WorkspaceContentVersion version =
                await ComputeVersionAsync(fullPath, cancellationToken)
                    .ConfigureAwait(false);
            return new WorkspaceMutationResult
            {
                Outcome = WorkspaceMutationOutcome.Succeeded,
                Path = path,
                Version = version,
            };
        }
        catch (UnauthorizedAccessException exception)
        {
            return Denied(path, exception.Message);
        }
        catch (IOException exception)
        {
            return new WorkspaceMutationResult
            {
                Outcome = WorkspaceMutationOutcome.Failed,
                Path = path,
                Message = exception.Message,
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
        if (_access is AccessMode.ReadOnly)
        {
            return Denied(path, "The workspace root is read-only.");
        }

        string fullPath = Resolve(path);
        if (string.Equals(fullPath, _rootPath, PathComparison))
        {
            return Denied(path, "The workspace root cannot be deleted.");
        }

        await _mutationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            WorkspaceMutationResult? conditionFailure =
                await ValidateWriteConditionAsync(
                    path,
                    condition,
                    cancellationToken).ConfigureAwait(false);
            if (conditionFailure is not null)
            {
                return conditionFailure;
            }

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            else if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: false);
            }
            else
            {
                return new WorkspaceMutationResult
                {
                    Outcome = WorkspaceMutationOutcome.NotFound,
                    Path = path,
                };
            }
            return new WorkspaceMutationResult
            {
                Outcome = WorkspaceMutationOutcome.Succeeded,
                Path = path,
            };
        }
        catch (UnauthorizedAccessException exception)
        {
            return Denied(path, exception.Message);
        }
        catch (IOException exception)
        {
            return new WorkspaceMutationResult
            {
                Outcome = WorkspaceMutationOutcome.Failed,
                Path = path,
                Message = exception.Message,
            };
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public ValueTask CreateDirectoryAsync(
        WorkspacePath path,
        CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        cancellationToken.ThrowIfCancellationRequested();
        if (_access is AccessMode.ReadOnly)
        {
            throw new UnauthorizedAccessException(
                "The workspace root is read-only.");
        }
        string fullPath = Resolve(path);
        Directory.CreateDirectory(fullPath);
        return ValueTask.CompletedTask;
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
        string fullDirectory = Resolve(directory);
        if (!Directory.Exists(fullDirectory))
        {
            return new WorkspaceListResultSet();
        }

        var entries = new List<WorkspaceDirectoryEntry>(
            Math.Min(options.MaxEntries, 256));
        int ignoredCount = 0;
        WorkspaceListStatus status = WorkspaceListStatus.Completed;
        string? message = null;
        using var timeout = new CancellationTokenSource(options.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((fullDirectory, 0));
        try
        {
            while (pending.Count > 0)
            {
                linked.Token.ThrowIfCancellationRequested();
                (string current, int depth) = pending.Pop();
                foreach (string fullPath in Directory.EnumerateFileSystemEntries(
                             current))
                {
                    linked.Token.ThrowIfCancellationRequested();
                    FileAttributes attributes = File.GetAttributes(fullPath);
                    bool isDirectory =
                        (attributes & FileAttributes.Directory) != 0;
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new UnauthorizedAccessException(
                            $"Workspace symlink '{fullPath}' is denied by policy.");
                    }

                    string relative = RelativeFromRoot(fullPath);
                    string relativeToDirectory = Path.GetRelativePath(
                            fullDirectory,
                            fullPath)
                        .Replace(Path.DirectorySeparatorChar, '/');
                    if (!options.IncludeHidden &&
                        IsHiddenRelativePath(relativeToDirectory))
                    {
                        ignoredCount++;
                        continue;
                    }
                    if (options.RespectIgnoreFiles &&
                        IsIgnored(fullPath, isDirectory))
                    {
                        ignoredCount++;
                        continue;
                    }
                    if (options.MaxDepth is { } maxDepth &&
                        depth + 1 > maxDepth)
                    {
                        ignoredCount++;
                        continue;
                    }
                    if (isDirectory &&
                        options.Recursive &&
                        (options.MaxDepth is null ||
                         depth + 1 < options.MaxDepth))
                    {
                        pending.Push((fullPath, depth + 1));
                    }
                    if (options.GlobPattern is { Length: > 0 } pattern &&
                        !MatchesGlob(relativeToDirectory, pattern))
                    {
                        continue;
                    }
                    if (entries.Count >= options.MaxEntries)
                    {
                        status = WorkspaceListStatus.Truncated;
                        message = "entry_limit";
                        break;
                    }

                    WorkspacePath logical = Logical(relative);
                    WorkspaceFileInfo info = await GetInfoCoreAsync(
                            logical,
                            fullPath,
                            linked.Token)
                        .ConfigureAwait(false) ??
                        throw new IOException(
                            $"Workspace entry '{logical}' disappeared during listing.");
                    entries.Add(new WorkspaceDirectoryEntry
                    {
                        Path = info.Path,
                        Kind = info.Kind,
                        Version = info.Version,
                    });
                }
                if (status == WorkspaceListStatus.Truncated)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            status = WorkspaceListStatus.TimedOut;
            message = "timeout";
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            status = WorkspaceListStatus.Cancelled;
            message = "cancelled";
        }
        catch (Exception exception) when (
            entries.Count > 0 &&
            exception is IOException or
                UnauthorizedAccessException or
                RegexMatchTimeoutException)
        {
            status = WorkspaceListStatus.Failed;
            message = exception.Message;
        }

        return new WorkspaceListResultSet
        {
            Entries = entries,
            Status = status,
            IgnoredCount = ignoredCount,
            Message = message,
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
        string searchRoot = Resolve(request.Root);
        if (!Directory.Exists(searchRoot) && !File.Exists(searchRoot))
        {
            return new WorkspaceSearchResultSet
            {
                Status = WorkspaceSearchStatus.NoMatches,
            };
        }

        WorkspaceSearchResultSet? accelerated =
            await TrySearchWithRipgrepAsync(
                request,
                searchRoot,
                cancellationToken).ConfigureAwait(false);
        if (accelerated is not null)
        {
            return accelerated;
        }
        return await SearchTextNativeAsync(
            request,
            searchRoot,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<WorkspaceSearchResultSet?>
        TrySearchWithRipgrepAsync(
            WorkspaceSearchRequest request,
            string searchRoot,
            CancellationToken cancellationToken)
    {
        if (_ripgrepExecutor is null)
        {
            return null;
        }
        WorkspaceSearchResultSet? result =
            await _ripgrepExecutor.ExecuteAsync(
                    _rootId,
                    searchRoot,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
        return result is
            {
                Status: WorkspaceSearchStatus.Failed,
                Results.Count: 0,
            }
            ? null
            : result;
    }

    private async ValueTask<WorkspaceSearchResultSet>
        SearchTextNativeAsync(
            WorkspaceSearchRequest request,
            string searchRoot,
            CancellationToken cancellationToken)
    {
        Regex expression = CreateSearchRegex(request);
        using var timeout =
            new CancellationTokenSource(request.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        var results = new List<WorkspaceSearchResult>();
        var matchingFiles = new HashSet<WorkspacePath>();
        var counts = new Dictionary<WorkspacePath, long>();
        int ignored = 0;
        WorkspaceSearchStatus status = WorkspaceSearchStatus.Completed;
        string? message = null;
        try
        {
            WorkspaceSearchEnumeration enumeration =
                EnumerateSearchFiles(
                    searchRoot,
                    request,
                    linked.Token);
            ignored = enumeration.IgnoredCount;
            foreach (string fullPath in enumeration.Files)
            {
                linked.Token.ThrowIfCancellationRequested();
                string relativeToRoot = RelativeFromRoot(fullPath);
                WorkspacePath logical = Logical(relativeToRoot);
                string content = await File.ReadAllTextAsync(
                        fullPath,
                        linked.Token)
                    .ConfigureAwait(false);
                string[] lines = SplitLinesForSearch(content);
                int matchesInFile = 0;
                var matchedLines = new HashSet<long>();
                for (int offset = 0; offset <= content.Length;)
                {
                    Match match = expression.Match(content, offset);
                    if (!match.Success)
                    {
                        break;
                    }
                    long line = CountLinesBefore(content, match.Index);
                    if ((!request.Multiline && !matchedLines.Add(line)) ||
                        request.MaxMatchesPerFile is { } perFile &&
                        matchesInFile >= perFile)
                    {
                        offset = NextSearchOffset(match);
                        continue;
                    }
                    int outputCount = request.OutputMode switch
                    {
                        WorkspaceSearchOutputMode.Matches =>
                            results.Count,
                        WorkspaceSearchOutputMode.FilesWithMatches =>
                            matchingFiles.Count,
                        _ => counts.Count,
                    };
                    bool alreadyMatched =
                        request.OutputMode ==
                            WorkspaceSearchOutputMode.FilesWithMatches &&
                        matchingFiles.Contains(logical);
                    if (!alreadyMatched &&
                        outputCount >= request.MaxResults)
                    {
                        status = WorkspaceSearchStatus.Truncated;
                        message = "result_limit";
                        break;
                    }

                    if (request.OutputMode ==
                        WorkspaceSearchOutputMode.FilesWithMatches)
                    {
                        matchingFiles.Add(logical);
                        matchesInFile++;
                        offset = NextSearchOffset(match);
                        continue;
                    }
                    if (request.OutputMode ==
                        WorkspaceSearchOutputMode.Count)
                    {
                        counts[logical] =
                            counts.GetValueOrDefault(logical) + 1;
                        matchesInFile++;
                        offset = NextSearchOffset(match);
                        continue;
                    }

                    int lineStart = match.Index;
                    while (lineStart > 0 &&
                           content[lineStart - 1] != '\n')
                    {
                        lineStart--;
                    }
                    long lineIndex = checked((int)line - 1);
                    int column = match.Index - lineStart + 1;
                    string lineText = request.Multiline
                        ? match.Value
                        : lineIndex >= 0 && lineIndex < lines.Length
                            ? lines[lineIndex]
                            : string.Empty;
                    if (request.MaxColumns is { } maxColumns &&
                        lineText.Length > maxColumns)
                    {
                        offset = NextSearchOffset(match);
                        continue;
                    }
                    var result = new WorkspaceSearchResult
                    {
                        Path = logical,
                        Line = line,
                        Column = column,
                        LineText = lineText,
                    };
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
                    offset = NextSearchOffset(match);
                }
                if (status == WorkspaceSearchStatus.Truncated)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            status = WorkspaceSearchStatus.TimedOut;
            message = "timeout";
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            status = WorkspaceSearchStatus.Cancelled;
            message = "cancelled";
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                RegexMatchTimeoutException or
                DecoderFallbackException)
        {
            status = WorkspaceSearchStatus.Failed;
            message = exception.Message;
        }

        if (status == WorkspaceSearchStatus.Completed &&
            results.Count == 0 &&
            matchingFiles.Count == 0 &&
            counts.Count == 0)
        {
            status = WorkspaceSearchStatus.NoMatches;
        }
        return new WorkspaceSearchResultSet
        {
            Results = results,
            Files = matchingFiles
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
            Status = status,
            IgnoredFileCount = ignored,
            Message = message,
        };
    }

    private WorkspaceSearchEnumeration EnumerateSearchFiles(
        string root,
        WorkspaceSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (File.Exists(root))
        {
            return new WorkspaceSearchEnumeration(
                MatchesSearchPath(root, root, request)
                    ? [root]
                    : [],
                0);
        }

        var files = new List<string>();
        int ignoredCount = 0;
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (string directory, int depth) = pending.Pop();
            foreach (string fullPath in Directory.EnumerateFileSystemEntries(
                         directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes = File.GetAttributes(fullPath);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new UnauthorizedAccessException(
                        $"Workspace symlink '{fullPath}' is denied by policy.");
                }
                bool isDirectory =
                    (attributes & FileAttributes.Directory) != 0;
                string relativeToSearch = Path.GetRelativePath(
                        root,
                        fullPath)
                    .Replace(Path.DirectorySeparatorChar, '/');
                if (!request.IncludeHidden &&
                    IsHiddenRelativePath(relativeToSearch) ||
                    request.RespectIgnoreFiles &&
                    IsIgnored(fullPath, isDirectory))
                {
                    ignoredCount++;
                    continue;
                }
                if (request.MaxDepth is { } maxDepth &&
                    depth + 1 > maxDepth)
                {
                    ignoredCount++;
                    continue;
                }
                if (isDirectory)
                {
                    pending.Push((fullPath, depth + 1));
                    continue;
                }
                if (!MatchesSearchPath(
                        root,
                        fullPath,
                        request) ||
                    request.MaxFileSizeBytes is { } maxFileSize &&
                    new FileInfo(fullPath).Length > maxFileSize)
                {
                    ignoredCount++;
                    continue;
                }
                files.Add(fullPath);
            }
        }
        return new WorkspaceSearchEnumeration(files, ignoredCount);
    }

    private static bool MatchesSearchPath(
        string searchRoot,
        string fullPath,
        WorkspaceSearchRequest request)
    {
        string relative = Directory.Exists(searchRoot)
            ? Path.GetRelativePath(searchRoot, fullPath)
                .Replace(Path.DirectorySeparatorChar, '/')
            : Path.GetFileName(fullPath);
        if (request.GlobPattern is { Length: > 0 } glob &&
            !MatchesGlob(relative, glob))
        {
            return false;
        }
        if (request.IncludeGlobs.Count > 0 &&
            !request.IncludeGlobs.Any(glob => MatchesGlob(relative, glob)))
        {
            return false;
        }
        return !request.ExcludeGlobs.Any(glob =>
            MatchesGlob(relative, glob));
    }

    private Regex CreateSearchRegex(WorkspaceSearchRequest request)
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

    private static int NextSearchOffset(Match match) =>
        match.Index + Math.Max(match.Length, 1);

    private static string[] SplitLinesForSearch(string content) =>
        content.Length == 0
            ? []
            : content.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n');

    private static long CountLinesBefore(string content, int index)
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

    private static bool IsHiddenRelativePath(string relativePath) =>
        relativePath.Split('/').Any(part =>
            part.StartsWith(".", StringComparison.Ordinal));

    private bool IsIgnored(string fullPath, bool isDirectory)
    {
        string relative = RelativeFromRoot(fullPath);
        var directories = new List<string>();
        string? current = Path.GetDirectoryName(fullPath);
        while (current is not null && IsBeneathRoot(current))
        {
            directories.Add(current);
            if (string.Equals(
                    current,
                    _rootPath,
                    PathComparison))
            {
                break;
            }
            current = Path.GetDirectoryName(current);
        }
        directories.Reverse();
        bool ignored = false;
        foreach (string directory in directories)
        {
            foreach (string ignoreFile in new[]
                     {
                         ".gitignore",
                         ".ignore",
                         ".rgignore",
                     })
            {
                string ignorePath = Path.Combine(directory, ignoreFile);
                if (!File.Exists(ignorePath))
                {
                    continue;
                }
                string baseRelative = Path.GetRelativePath(
                        _rootPath,
                        directory)
                    .Replace(Path.DirectorySeparatorChar, '/');
                if (baseRelative == ".")
                {
                    baseRelative = string.Empty;
                }
                foreach (string rawLine in File.ReadLines(ignorePath))
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
                    string candidate = baseRelative.Length == 0
                        ? relative
                        : relative.StartsWith(
                                baseRelative + "/",
                                StringComparison.Ordinal)
                            ? relative[(baseRelative.Length + 1)..]
                            : relative;
                    string pattern = line.TrimStart('/');
                    bool matches = MatchesGlob(candidate, pattern);
                    if (!matches &&
                        !line.Contains('/', StringComparison.Ordinal))
                    {
                        string[] segments = candidate.Split('/');
                        matches = (directoryOnly
                                ? segments.Take(
                                    Math.Max(0, segments.Length - 1))
                                : segments)
                            .Any(segment =>
                                MatchesGlob(segment, pattern));
                    }
                    bool matchesDirectoryAncestor =
                        directoryOnly && !isDirectory &&
                        candidate.Split('/')
                            .Take(Math.Max(
                                0,
                                candidate.Split('/').Length - 1))
                            .Any(segment =>
                                MatchesGlob(segment, pattern));
                    if (matches &&
                        (!directoryOnly ||
                         isDirectory ||
                         matchesDirectoryAncestor))
                    {
                        ignored = !negated;
                    }
                }
            }
        }
        foreach (string ignorePath in GlobalIgnorePaths())
        {
            if (!File.Exists(ignorePath))
            {
                continue;
            }
            foreach (string rawLine in File.ReadLines(ignorePath))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
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
                string pattern = line.TrimStart('/');
                string candidate = relative;
                bool matches = MatchesGlob(candidate, pattern);
                if (!matches &&
                    !line.Contains('/', StringComparison.Ordinal))
                {
                    matches = candidate.Split('/')
                        .Any(segment =>
                            MatchesGlob(segment, pattern));
                }
                string[] segments = candidate.Split('/');
                bool matchesDirectoryAncestor =
                    directoryOnly && !isDirectory &&
                    segments
                        .Take(Math.Max(0, segments.Length - 1))
                        .Any(segment =>
                            MatchesGlob(segment, pattern));
                if (matches &&
                    (!directoryOnly ||
                     isDirectory ||
                     matchesDirectoryAncestor))
                {
                    ignored = !negated;
                }
            }
        }
        return ignored;
    }

    private static IEnumerable<string> GlobalIgnorePaths()
    {
        string? configured =
            System.Environment.GetEnvironmentVariable(
                "GIT_GLOBAL_EXCLUDES");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            yield return configured;
        }
        string? xdg = System.Environment.GetEnvironmentVariable(
            "XDG_CONFIG_HOME");
        string? home = System.Environment.GetEnvironmentVariable(
            "HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
        {
            yield return Path.Combine(xdg, "git", "ignore");
        }
        else if (!string.IsNullOrWhiteSpace(home))
        {
            yield return Path.Combine(
                home,
                ".config",
                "git",
                "ignore");
        }
        if (!string.IsNullOrWhiteSpace(home))
        {
            yield return Path.Combine(
                home,
                ".gitignore_global");
        }
    }

    private sealed record WorkspaceSearchEnumeration(
        IReadOnlyList<string> Files,
        int IgnoredCount);

    public WorkspaceExecutionPath ToExecutionPath(WorkspacePath path)
    {
        EnsureOpen();
        return new WorkspaceExecutionPath(Resolve(path));
    }

    public WorkspacePath? TryFromExecutionPath(
        WorkspaceExecutionPath path)
    {
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(path.Value))
        {
            return null;
        }
        string fullPath;
        try
        {
            fullPath = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(path.Value));
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
        {
            return null;
        }
        if (!IsBeneathRoot(fullPath))
        {
            return null;
        }
        return Logical(RelativeFromRoot(fullPath));
    }

    public Uri ToExecutionFileUri(WorkspacePath path)
    {
        string executionPath = ToExecutionPath(path).Value;
        return new UriBuilder
        {
            Scheme = Uri.UriSchemeFile,
            Host = string.Empty,
            Path = executionPath,
        }.Uri;
    }

    public WorkspacePath? TryFromExecutionFileUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        EnsureOpen();
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

    private async ValueTask<WorkspaceFileInfo?> GetInfoCoreAsync(
        WorkspacePath logicalPath,
        string fullPath,
        CancellationToken cancellationToken)
    {
        FileAttributes attributes = File.GetAttributes(fullPath);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new UnauthorizedAccessException(
                $"Workspace symlink '{logicalPath}' is denied by policy.");
        }
        if ((attributes & FileAttributes.Directory) != 0)
        {
            return new WorkspaceFileInfo
            {
                Path = logicalPath,
                Kind = WorkspaceEntryKind.Directory,
                Version = new WorkspaceContentVersion(
                    BackendRevision: null,
                    ContentDigest: null,
                    ModifiedAt: Directory.GetLastWriteTimeUtc(fullPath),
                    Size: default),
            };
        }
        return new WorkspaceFileInfo
        {
            Path = logicalPath,
            Kind = WorkspaceEntryKind.File,
            Version = await ComputeVersionAsync(
                    fullPath,
                    cancellationToken)
                .ConfigureAwait(false),
        };
    }

    private async ValueTask<WorkspaceContentVersion> ComputeVersionAsync(
        string fullPath,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = OpenRead(fullPath);
        byte[] hash = await SHA256.HashDataAsync(
            stream,
            cancellationToken).ConfigureAwait(false);
        return new WorkspaceContentVersion(
            BackendRevision: null,
            ContentDigest: new Digest(
                "sha256",
                Convert.ToHexString(hash).ToLowerInvariant()),
            ModifiedAt: File.GetLastWriteTimeUtc(fullPath),
            Size: new ByteSize(stream.Length));
    }

    private static async ValueTask<long> CountLinesAsync(
        string fullPath,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = OpenRead(fullPath);
        if (stream.Length == 0)
        {
            return 0;
        }
        long lines = 0;
        byte[] buffer = new byte[16 * 1024];
        int last = -1;
        while (true)
        {
            int read = await stream.ReadAsync(
                    buffer,
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            for (int index = 0; index < read; index++)
            {
                if (buffer[index] == (byte)'\n')
                {
                    lines++;
                }
            }
            last = buffer[read - 1];
        }
        return last == (byte)'\n' ? lines : lines + 1;
    }

    private async ValueTask<WorkspaceMutationResult?>
        ValidateWriteConditionAsync(
            WorkspacePath logicalPath,
            WorkspaceWriteCondition condition,
            CancellationToken cancellationToken)
    {
        WorkspaceFileInfo? observed = await GetInfoAsync(
            logicalPath,
            cancellationToken).ConfigureAwait(false);
        if (condition.CreateOnly && observed is not null)
        {
            return new WorkspaceMutationResult
            {
                Outcome = WorkspaceMutationOutcome.AlreadyExists,
                Path = logicalPath,
                ObservedVersion = observed.Version,
                Message = "Create-only mutation target already exists.",
            };
        }
        if (condition.ExpectedVersion is { } expected)
        {
            if (observed is null)
            {
                return new WorkspaceMutationResult
                {
                    Outcome = WorkspaceMutationOutcome.NotFound,
                    Path = logicalPath,
                    Message = "Expected mutation target does not exist.",
                };
            }
            if (!VersionsMatch(expected, observed.Version))
            {
                return new WorkspaceMutationResult
                {
                    Outcome = WorkspaceMutationOutcome.Stale,
                    Path = logicalPath,
                    ObservedVersion = observed.Version,
                    Message =
                        "The workspace content changed after the supplied version was observed.",
                };
            }
        }
        if (observed is null && !condition.AllowCreate)
        {
            return new WorkspaceMutationResult
            {
                Outcome = WorkspaceMutationOutcome.NotFound,
                Path = logicalPath,
                Message = "Mutation target does not exist and creation is disabled.",
            };
        }
        return null;
    }

    private static bool VersionsMatch(
        WorkspaceContentVersion expected,
        WorkspaceContentVersion observed)
    {
        if (expected.ContentDigest is { } expectedDigest)
        {
            return observed.ContentDigest is { } observedDigest &&
                string.Equals(
                    expectedDigest.Algorithm,
                    observedDigest.Algorithm,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    expectedDigest.Value,
                    observedDigest.Value,
                    StringComparison.OrdinalIgnoreCase);
        }
        if (expected.BackendRevision is { } expectedRevision)
        {
            return observed.BackendRevision == expectedRevision;
        }
        return false;
    }

    private async ValueTask<(byte[] Content, long EndLine, bool Truncated)>
        ReadTextBytesAsync(
            string fullPath,
            WorkspaceTextReadOptions options,
            CancellationToken cancellationToken)
    {
        await using FileStream stream = OpenRead(fullPath);
        long startOffset = await FindLineOffsetAsync(
                stream,
                options.StartLine,
                cancellationToken)
            .ConfigureAwait(false);
        if (startOffset >= stream.Length)
        {
            return ([], options.StartLine - 1, false);
        }

        stream.Position = startOffset;
        int initialCapacity = (int)Math.Min(
            options.MaxBytes,
            Math.Max(0, stream.Length - startOffset));
        using var output = new MemoryStream(initialCapacity);
        long lines = 0;
        bool truncated = false;
        byte[] buffer = new byte[16 * 1024];
        while (output.Length < options.MaxBytes)
        {
            int requested = Math.Min(
                buffer.Length,
                options.MaxBytes - (int)output.Length);
            int read = await stream.ReadAsync(
                    buffer.AsMemory(0, requested),
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            int writeCount = read;
            for (int index = 0; index < read; index++)
            {
                if (buffer[index] != (byte)'\n')
                {
                    continue;
                }
                lines++;
                if (options.MaxLines is { } maxLines &&
                    lines >= maxLines)
                {
                    writeCount = index + 1;
                    break;
                }
            }
            await output.WriteAsync(
                buffer.AsMemory(0, writeCount),
                cancellationToken).ConfigureAwait(false);
            if (writeCount < read ||
                (options.MaxLines is { } limit && lines >= limit))
            {
                truncated = stream.Position < stream.Length;
                break;
            }
        }
        if (output.Length >= options.MaxBytes &&
            stream.Position < stream.Length)
        {
            truncated = true;
        }

        byte[] content = output.ToArray();
        if (startOffset == 0 &&
            content.AsSpan().StartsWith(
                new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            content = content[3..];
        }
        long newlineCount = content.LongCount(
            value => value == (byte)'\n');
        long endLine = options.StartLine;
        if (newlineCount > 0)
        {
            endLine += newlineCount -
                (content.Length > 0 &&
                 content[^1] == (byte)'\n' ? 1 : 0);
        }
        return (content, endLine, truncated);
    }

    private static async ValueTask<long> FindLineOffsetAsync(
        FileStream stream,
        long startLine,
        CancellationToken cancellationToken)
    {
        if (startLine <= 1)
        {
            return 0;
        }
        long line = 1;
        byte[] buffer = new byte[16 * 1024];
        while (true)
        {
            int read = await stream.ReadAsync(
                    buffer,
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return stream.Length;
            }
            for (int index = 0; index < read; index++)
            {
                if (buffer[index] != (byte)'\n')
                {
                    continue;
                }
                line++;
                if (line == startLine)
                {
                    return stream.Position - read + index + 1L;
                }
            }
        }
    }

    private IEnumerable<string> EnumerateEntries(
        string root,
        bool recursive,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            foreach (string path in Directory.EnumerateFileSystemEntries(
                         directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new UnauthorizedAccessException(
                        $"Workspace symlink '{path}' is denied by policy.");
                }
                yield return path;
                if (recursive &&
                    (attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(path);
                }
            }
        }
    }

    private IEnumerable<string> EnumerateFiles(
        string root,
        CancellationToken cancellationToken)
    {
        if (File.Exists(root))
        {
            yield return root;
            yield break;
        }
        foreach (string path in EnumerateEntries(
                     root,
                     recursive: true,
                     cancellationToken))
        {
            if (File.Exists(path))
            {
                yield return path;
            }
        }
    }

    private string Resolve(WorkspacePath path)
    {
        EnsureOpen();
        if (!path.RootId.Equals(_rootId))
        {
            throw new UnauthorizedAccessException(
                $"Workspace root '{path.RootId.Value}' is not available in this store.");
        }
        string relative = NormalizeRelative(path.RelativePath);
        string fullPath = relative.Length == 0
            ? _rootPath
            : Path.GetFullPath(
                Path.Combine(
                    _rootPath,
                    relative.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));
        EnsureBeneathRoot(fullPath);
        RejectSymlinkComponents(fullPath);
        return fullPath;
    }

    private void RejectSymlinkComponents(string fullPath)
    {
        string relative = Path.GetRelativePath(_rootPath, fullPath);
        string current = _rootPath;
        if (relative == ".")
        {
            return;
        }
        foreach (string segment in relative.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo? info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : File.Exists(current)
                    ? new FileInfo(current)
                    : null;
            if (info is not null &&
                (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnauthorizedAccessException(
                    $"Workspace symlink '{info.FullName}' is denied by policy.");
            }
        }
    }

    private static string NormalizeRelative(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        if (relativePath.IndexOf('\0') >= 0 ||
            Path.IsPathRooted(relativePath) ||
            relativePath.Contains('\\', StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "Workspace paths must be relative POSIX paths.");
        }
        var segments = new List<string>();
        foreach (string segment in relativePath.Split('/'))
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

    private void EnsureBeneathRoot(string fullPath)
    {
        if (!IsBeneathRoot(fullPath))
        {
            throw new UnauthorizedAccessException(
                $"Workspace path '{fullPath}' escapes '{_rootPath}'.");
        }
    }

    private bool IsBeneathRoot(string fullPath)
    {
        string normalized = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(fullPath));
        return string.Equals(
                normalized,
                _rootPath,
                PathComparison) ||
            normalized.StartsWith(
                _rootPath + Path.DirectorySeparatorChar,
                PathComparison);
    }

    private string RelativeFromRoot(string fullPath) =>
        Path.GetRelativePath(_rootPath, fullPath)
            .Replace(Path.DirectorySeparatorChar, '/');

    private WorkspacePath Logical(string relativePath) =>
        new(_rootId, relativePath);

    private StringComparison PathComparison =>
        _semantics.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

    private static bool MatchesGlob(string path, string pattern)
    {
        var matcher = new Matcher(StringComparison.Ordinal);
        matcher.AddInclude(
            pattern.TrimStart('/').Replace('\\', '/'));
        return matcher.Match(path).HasMatches;
    }

    private static int FindColumn(
        string line,
        string query,
        Regex? regex,
        StringComparison comparison)
    {
        if (regex is not null)
        {
            Match match = regex.Match(line);
            return match.Success ? match.Index : -1;
        }
        return line.IndexOf(query, comparison);
    }

    private static string DecodeUtf8(byte[] bytes)
    {
        int validLength = bytes.Length;
        while (validLength > 0)
        {
            try
            {
                return new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true)
                    .GetString(bytes, 0, validLength);
            }
            catch (DecoderFallbackException)
                when (validLength > bytes.Length - 4)
            {
                validLength--;
            }
        }
        return string.Empty;
    }

    private static FileStream OpenRead(string fullPath) =>
        new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            useAsync: true);

    private static WorkspaceMutationResult Denied(
        WorkspacePath path,
        string message) =>
        new()
        {
            Outcome = WorkspaceMutationOutcome.AccessDenied,
            Path = path,
            Message = message,
        };

    private void EnsureOpen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
