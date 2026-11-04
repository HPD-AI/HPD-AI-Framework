using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HPD.Agent.Sandbox;
using HPD.Agent.ToolHarness.Coding;
using HPD.Environment.Contracts;
using Microsoft.Extensions.FileSystemGlobbing;

namespace HPD.Agent.ToolHarness.Coding.Tests;

internal static class TestRuntimeWorkspace
{
    public static RuntimeWorkspaceBinding FromRunConfig(
        AgentRunConfig runConfig)
    {
        ArgumentNullException.ThrowIfNull(runConfig);
        return AgentWorkspace.TryFrom(
                runConfig,
                out AgentWorkspace workspace,
                out _)
            ? FromAgentWorkspace(workspace)
            : Create(Path.GetTempPath());
    }

    public static RuntimeWorkspaceBinding FromAgentWorkspace(
        AgentWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        WorkspaceRootBinding[] roots = workspace.Roots
            .Select(root => new WorkspaceRootBinding
            {
                Id = new WorkspaceRootId(root.Id),
                DisplayName = root.Label ?? root.Id,
                Access = root.Access == AgentWorkspaceAccess.ReadWrite
                    ? AccessMode.ReadWrite
                    : AccessMode.ReadOnly,
                DefaultExecutionPath =
                    new WorkspacePath(
                        new WorkspaceRootId(root.Id),
                        string.Empty),
                Content = new WorkspaceContentIdentity(
                    $"test:{PhysicalPath(root.Path)}"),
                PathSemantics = new WorkspacePathSemantics(
                    CaseSensitive: OperatingSystem.IsLinux(),
                    Symlinks: SymlinkEvaluationPolicy.DenySymlinks),
            })
            .ToArray();
        WorkspaceRootBinding selected = roots.Single(root =>
            root.Id.Value == workspace.DefaultRootId);
        var mapper = new LocalWorkspacePathMapper(
            roots.ToDictionary(
                root => root.Id,
                    root => PhysicalPath(
                        workspace.Roots.Single(candidate =>
                            candidate.Id == root.Id.Value).Path)));
        return new RuntimeWorkspaceBinding
        {
            Files = new LocalWorkspaceFileStore(roots, mapper),
            Paths = mapper,
            WorkingDirectory = new WorkspacePath(
                selected.Id,
                string.Empty),
            Roots = roots,
            Content = selected.Content,
        };
    }

    public static RuntimeWorkspaceBinding Create(
        string rootPath,
        AccessMode access = AccessMode.ReadWrite,
        string rootId = "root",
        bool resolveLinks = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        string fullPath = resolveLinks
            ? PhysicalPath(rootPath)
            : Path.GetFullPath(rootPath);
        var root = new WorkspaceRootBinding
        {
            Id = new WorkspaceRootId(rootId),
            DisplayName = Path.GetFileName(fullPath),
            Access = access,
            DefaultExecutionPath =
                new WorkspacePath(
                    new WorkspaceRootId(rootId),
                    string.Empty),
            Content = new WorkspaceContentIdentity($"test:{fullPath}"),
            PathSemantics = new WorkspacePathSemantics(
                CaseSensitive: OperatingSystem.IsLinux(),
                Symlinks: SymlinkEvaluationPolicy.DenySymlinks),
        };
        var mapper = new LocalWorkspacePathMapper(
            new Dictionary<WorkspaceRootId, string>
            {
                [root.Id] = fullPath,
            },
            resolveLinks);
        return new RuntimeWorkspaceBinding
        {
            Files = new LocalWorkspaceFileStore([root], mapper),
            Paths = mapper,
            WorkingDirectory = new WorkspacePath(root.Id, string.Empty),
            Roots = [root],
            Content = root.Content,
        };
    }

    private sealed class LocalWorkspacePathMapper(
        IReadOnlyDictionary<WorkspaceRootId, string> roots,
        bool resolveLinks = true)
        : IWorkspacePathMapper
    {
        public WorkspaceExecutionPath ToExecutionPath(
            WorkspacePath path)
        {
            string root = roots.TryGetValue(path.RootId, out string? value)
                ? value
                : throw new UnauthorizedAccessException(
                    $"Unknown workspace root '{path.RootId.Value}'.");
            return new WorkspaceExecutionPath(
                path.RelativePath.Length == 0
                    ? root
                    : Path.Combine(
                        root,
                        path.RelativePath.Replace(
                            '/',
                            Path.DirectorySeparatorChar)));
        }

        public WorkspacePath? TryFromExecutionPath(
            WorkspaceExecutionPath path)
        {
            foreach ((WorkspaceRootId rootId, string rootPath) in roots)
            {
                string relative = Path.GetRelativePath(
                    rootPath,
                    resolveLinks
                        ? PhysicalPath(path.Value)
                        : Path.GetFullPath(path.Value));
                if (relative == "." ||
                    (!Path.IsPathRooted(relative) &&
                     relative != ".." &&
                     !relative.StartsWith(
                         $"..{Path.DirectorySeparatorChar}",
                         StringComparison.Ordinal)))
                {
                    return new WorkspacePath(
                        rootId,
                        relative == "."
                            ? string.Empty
                            : relative.Replace(
                                Path.DirectorySeparatorChar,
                                '/'));
                }
            }
            return null;
        }

        public Uri ToExecutionFileUri(WorkspacePath path) =>
            new(
                ToExecutionPath(path).Value,
                UriKind.Absolute);

        public WorkspacePath? TryFromExecutionFileUri(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);
            return uri.IsFile && uri.IsAbsoluteUri
                ? TryFromExecutionPath(
                    new WorkspaceExecutionPath(uri.LocalPath))
                : null;
        }
    }

    private static string PhysicalPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath)!;
        string current = root;
        foreach (string segment in fullPath[root.Length..].Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            if (info.LinkTarget is not null &&
                info.ResolveLinkTarget(
                    returnFinalTarget: true) is { } target)
            {
                current = target.FullName;
            }
        }
        return current;
    }

    private sealed class LocalWorkspaceFileStore(
        IReadOnlyList<WorkspaceRootBinding> roots,
        IWorkspacePathMapper paths)
        : IWorkspaceFileStore
    {
        public async ValueTask<WorkspaceFileInfo?> GetInfoAsync(
            WorkspacePath path,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fullPath = paths.ToExecutionPath(path).Value;
            FileSystemInfo info = Directory.Exists(fullPath)
                ? new DirectoryInfo(fullPath)
                : new FileInfo(fullPath);
            if (!info.Exists)
            {
                return null;
            }
            return new WorkspaceFileInfo
            {
                Path = path,
                Kind = info is DirectoryInfo
                    ? WorkspaceEntryKind.Directory
                    : WorkspaceEntryKind.File,
                Version = await VersionAsync(
                    info,
                    cancellationToken).ConfigureAwait(false),
            };
        }

        public async ValueTask<WorkspaceTextSnapshot?> ReadTextAsync(
            WorkspacePath path,
            WorkspaceTextReadOptions options,
            CancellationToken cancellationToken = default)
        {
            string fullPath = paths.ToExecutionPath(path).Value;
            if (!File.Exists(fullPath))
            {
                return null;
            }
            string allText = await File.ReadAllTextAsync(
                fullPath,
                cancellationToken).ConfigureAwait(false);
            IReadOnlyList<(int Start, int End)> lines =
                LineRanges(allText);
            int start = Math.Clamp(
                checked((int)options.StartLine - 1),
                0,
                lines.Count);
            int selectedCount = Math.Min(
                lines.Count - start,
                options.MaxLines ?? int.MaxValue);
            int endExclusive = start + selectedCount;
            string content = selectedCount == 0
                ? string.Empty
                : allText[
                    lines[start].Start..
                    lines[endExclusive - 1].End];
            int maximumBytes = Math.Max(1, options.MaxBytes);
            bool byteTruncated = Encoding.UTF8.GetByteCount(content) >
                maximumBytes;
            if (byteTruncated)
            {
                content = Encoding.UTF8.GetString(
                    Encoding.UTF8.GetBytes(content)
                        .AsSpan(0, maximumBytes)
                        .ToArray());
            }
            WorkspaceFileInfo info =
                (await GetInfoAsync(path, cancellationToken)
                    .ConfigureAwait(false))!;
            return new WorkspaceTextSnapshot
            {
                Path = path,
                Content = content,
                Version = info.Version,
                StartLine = options.StartLine,
                EndLine = lines.Count == 0 || selectedCount == 0
                    ? 0
                    : options.StartLine + selectedCount - 1,
                TotalLines = lines.Count,
                Truncated = byteTruncated,
            };
        }

        public async ValueTask<WorkspaceBinarySnapshot?> ReadBytesAsync(
            WorkspacePath path,
            WorkspaceByteReadOptions options,
            CancellationToken cancellationToken = default)
        {
            string fullPath = paths.ToExecutionPath(path).Value;
            if (!File.Exists(fullPath))
            {
                return null;
            }
            byte[] all = await File.ReadAllBytesAsync(
                fullPath,
                cancellationToken).ConfigureAwait(false);
            byte[] selected = all
                .Skip(checked((int)options.Offset))
                .Take(Math.Max(0, options.MaxBytes))
                .ToArray();
            WorkspaceFileInfo info =
                (await GetInfoAsync(path, cancellationToken)
                    .ConfigureAwait(false))!;
            return new WorkspaceBinarySnapshot
            {
                Path = path,
                Content = selected,
                Version = info.Version,
                Offset = options.Offset,
                Truncated = selected.Length <
                    all.Length - options.Offset,
            };
        }

        public async ValueTask<WorkspaceMutationResult> WriteTextAsync(
            WorkspacePath path,
            string content,
            WorkspaceWriteCondition condition,
            CancellationToken cancellationToken = default)
        {
            return await WriteBytesAsync(
                path,
                Encoding.UTF8.GetBytes(content),
                condition,
                cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<WorkspaceMutationResult> WriteBytesAsync(
            WorkspacePath path,
            ReadOnlyMemory<byte> content,
            WorkspaceWriteCondition condition,
            CancellationToken cancellationToken = default)
        {
            WorkspaceRootBinding root = roots.Single(candidate =>
                candidate.Id.Equals(path.RootId));
            if (root.Access != AccessMode.ReadWrite)
            {
                return new WorkspaceMutationResult
                {
                    Outcome = WorkspaceMutationOutcome.AccessDenied,
                    Path = path,
                };
            }
            string fullPath = paths.ToExecutionPath(path).Value;
            WorkspaceFileInfo? before =
                await GetInfoAsync(path, cancellationToken)
                    .ConfigureAwait(false);
            if (condition.CreateOnly && before is not null)
            {
                return new WorkspaceMutationResult
                {
                    Outcome = WorkspaceMutationOutcome.AlreadyExists,
                    Path = path,
                };
            }
            if (condition.ExpectedVersion is { } expected &&
                (before is null ||
                 before.Version.ContentDigest != expected.ContentDigest))
            {
                return new WorkspaceMutationResult
                {
                    Outcome = WorkspaceMutationOutcome.Stale,
                    Path = path,
                    ObservedVersion = before?.Version,
                };
            }
            Directory.CreateDirectory(
                Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(
                fullPath,
                content.ToArray(),
                cancellationToken).ConfigureAwait(false);
            WorkspaceFileInfo after =
                (await GetInfoAsync(path, cancellationToken)
                    .ConfigureAwait(false))!;
            return new WorkspaceMutationResult
            {
                Outcome = WorkspaceMutationOutcome.Succeeded,
                Path = path,
                Version = after.Version,
            };
        }

        public async ValueTask<WorkspaceMutationResult> DeleteAsync(
            WorkspacePath path,
            WorkspaceWriteCondition condition,
            CancellationToken cancellationToken = default)
        {
            WorkspaceRootBinding root = roots.Single(candidate =>
                candidate.Id.Equals(path.RootId));
            if (root.Access != AccessMode.ReadWrite)
            {
                return new WorkspaceMutationResult
                {
                    Outcome = WorkspaceMutationOutcome.AccessDenied,
                    Path = path,
                };
            }
            WorkspaceFileInfo? before =
                await GetInfoAsync(path, cancellationToken)
                    .ConfigureAwait(false);
            if (before is null)
            {
                return new WorkspaceMutationResult
                {
                    Outcome = WorkspaceMutationOutcome.NotFound,
                    Path = path,
                };
            }
            if (condition.ExpectedVersion is { } expected &&
                before.Version.ContentDigest != expected.ContentDigest)
            {
                return new WorkspaceMutationResult
                {
                    Outcome = WorkspaceMutationOutcome.Stale,
                    Path = path,
                    ObservedVersion = before.Version,
                };
            }
            string fullPath = paths.ToExecutionPath(path).Value;
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            else if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: false);
            }
            return new WorkspaceMutationResult
            {
                Outcome = WorkspaceMutationOutcome.Succeeded,
                Path = path,
            };
        }

        public ValueTask CreateDirectoryAsync(
            WorkspacePath path,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(
                paths.ToExecutionPath(path).Value);
            return ValueTask.CompletedTask;
        }

        public async ValueTask<WorkspaceListResultSet>
            ListAsync(
            WorkspacePath directory,
            WorkspaceListOptions options,
            CancellationToken cancellationToken = default)
        {
            string fullPath = paths.ToExecutionPath(directory).Value;
            if (!Directory.Exists(fullPath))
            {
                return new WorkspaceListResultSet();
            }
            var entries = new List<WorkspaceDirectoryEntry>();
            foreach (string candidate in Directory.EnumerateFileSystemEntries(
                         fullPath,
                         "*",
                         options.Recursive
                             ? SearchOption.AllDirectories
                             : SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relative = Path.GetRelativePath(
                    fullPath,
                    candidate);
                string normalized = relative.Replace(
                    Path.DirectorySeparatorChar,
                    '/');
                int depth = normalized.Count(character =>
                    character == '/') + 1;
                if (options.MaxDepth is { } maxDepth &&
                    depth > maxDepth ||
                    !options.IncludeHidden &&
                    normalized.Split('/').Any(part =>
                        part.StartsWith(".", StringComparison.Ordinal)))
                {
                    continue;
                }
                if (options.GlobPattern is { } pattern)
                {
                    var matcher = new Matcher();
                    matcher.AddInclude(pattern);
                    if (!matcher.Match(normalized).HasMatches)
                    {
                        continue;
                    }
                }
                WorkspacePath logical = new(
                    directory.RootId,
                    directory.RelativePath.Length == 0
                        ? relative.Replace(
                            Path.DirectorySeparatorChar,
                            '/')
                        : directory.RelativePath.TrimEnd('/') +
                            "/" +
                            relative.Replace(
                                Path.DirectorySeparatorChar,
                                '/'));
                WorkspaceFileInfo? info = await GetInfoAsync(
                    logical,
                    cancellationToken).ConfigureAwait(false);
                if (info is not null)
                {
                    entries.Add(new WorkspaceDirectoryEntry
                    {
                        Path = info.Path,
                        Kind = info.Kind,
                        Version = info.Version,
                    });
                }
                if (entries.Count >= options.MaxEntries)
                {
                    break;
                }
            }
            return new WorkspaceListResultSet
            {
                Entries = entries,
                Status = entries.Count >= options.MaxEntries
                    ? WorkspaceListStatus.Truncated
                    : WorkspaceListStatus.Completed,
                Message = entries.Count >= options.MaxEntries
                    ? "entry_limit"
                    : null,
            };
        }

        public async ValueTask<WorkspaceSearchResultSet>
            SearchTextAsync(
            WorkspaceSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            WorkspaceListResultSet listing =
                await ListAsync(
                    request.Root,
                    new WorkspaceListOptions
                    {
                        Recursive = true,
                        MaxEntries = 20_000,
                        GlobPattern = request.GlobPattern,
                        MaxDepth = request.MaxDepth,
                        IncludeHidden = request.IncludeHidden,
                        RespectIgnoreFiles = request.RespectIgnoreFiles,
                    },
                    cancellationToken).ConfigureAwait(false);
            var results = new List<WorkspaceSearchResult>();
            var files = new HashSet<WorkspacePath>();
            var counts = new Dictionary<WorkspacePath, long>();
            string query = request.FixedStrings
                ? Regex.Escape(request.Query)
                : request.Query;
            if (request.WordRegexp)
            {
                query = $@"\b(?:{query})\b";
            }
            bool insensitive = request.CaseMode ==
                WorkspaceSearchCaseMode.Insensitive ||
                request.CaseMode == WorkspaceSearchCaseMode.Smart &&
                !request.Query.Any(char.IsUpper);
            var expression = new Regex(
                query,
                RegexOptions.CultureInvariant |
                (insensitive ? RegexOptions.IgnoreCase : RegexOptions.None) |
                (request.Multiline
                    ? RegexOptions.Singleline
                    : RegexOptions.None),
                TimeSpan.FromSeconds(1));
            foreach (WorkspaceDirectoryEntry entry in listing.Entries)
            {
                if (entry.Kind != WorkspaceEntryKind.File)
                {
                    continue;
                }
                string searchRoot = paths.ToExecutionPath(
                    new WorkspacePath(
                        request.Root.RootId,
                        request.Root.RelativePath)).Value;
                string candidatePath =
                    paths.ToExecutionPath(entry.Path).Value;
                string relativeToSearch = Path.GetRelativePath(
                        searchRoot,
                        candidatePath)
                    .Replace(Path.DirectorySeparatorChar, '/');
                if (request.GlobPattern is { Length: > 0 } glob &&
                    !MatchesGlob(relativeToSearch, glob) ||
                    request.IncludeGlobs.Count > 0 &&
                    !request.IncludeGlobs.Any(include =>
                        MatchesGlob(relativeToSearch, include)) ||
                    request.ExcludeGlobs.Any(exclude =>
                        MatchesGlob(relativeToSearch, exclude)))
                {
                    continue;
                }
                WorkspaceTextSnapshot? snapshot =
                    await ReadTextAsync(
                        entry.Path,
                        new WorkspaceTextReadOptions
                        {
                            MaxLines = null,
                            MaxBytes = 1024 * 1024,
                        },
                        cancellationToken).ConfigureAwait(false);
                if (snapshot is null)
                {
                    continue;
                }
                string[] lines = snapshot.Content.Split('\n');
                if (request.Multiline)
                {
                    for (int offset = 0;
                         offset <= snapshot.Content.Length;)
                    {
                        Match match = expression.Match(
                            snapshot.Content,
                            offset);
                        if (!match.Success)
                        {
                            break;
                        }
                        long lineNumber = snapshot.Content
                            .AsSpan(0, match.Index)
                            .Count('\n') + 1L;
                        files.Add(entry.Path);
                        counts[entry.Path] =
                            counts.GetValueOrDefault(entry.Path) + 1;
                        results.Add(new WorkspaceSearchResult
                        {
                            Path = entry.Path,
                            Line = lineNumber,
                            Column = match.Index -
                                snapshot.Content.LastIndexOf(
                                    '\n',
                                    Math.Max(0, match.Index - 1)) + 1,
                            LineText = match.Value,
                        });
                        offset = match.Index +
                            Math.Max(match.Length, 1);
                    }
                    continue;
                }
                for (int line = 0; line < lines.Length; line++)
                {
                    Match match = expression.Match(lines[line]);
                    if (!match.Success)
                    {
                        continue;
                    }
                    files.Add(entry.Path);
                    counts[entry.Path] =
                        counts.GetValueOrDefault(entry.Path) + 1;
                    results.Add(new WorkspaceSearchResult
                    {
                        Path = entry.Path,
                        Line = line + 1,
                        Column = match.Index + 1,
                        LineText = lines[line],
                    });
                    if (results.Count >= request.MaxResults)
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
                            IgnoredFileCount = listing.IgnoredCount,
                            Message = "result_limit",
                        };
                    }
                }
            }
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
                Status = results.Count == 0
                    ? WorkspaceSearchStatus.NoMatches
                    : WorkspaceSearchStatus.Completed,
                IgnoredFileCount = listing.IgnoredCount,
            };
        }

        private static bool MatchesGlob(
            string path,
            string pattern)
        {
            var matcher = new Matcher(StringComparison.Ordinal);
            matcher.AddInclude(pattern);
            return matcher.Match(path).HasMatches;
        }

        private static async ValueTask<WorkspaceContentVersion>
            VersionAsync(
            FileSystemInfo info,
            CancellationToken cancellationToken)
        {
            if (info is FileInfo file)
            {
                byte[] bytes = await File.ReadAllBytesAsync(
                    file.FullName,
                    cancellationToken).ConfigureAwait(false);
                return new WorkspaceContentVersion(
                    null,
                    new Digest(
                        "sha256",
                        Convert.ToHexString(
                            SHA256.HashData(bytes))
                        .ToLowerInvariant()),
                    file.LastWriteTimeUtc,
                    new ByteSize(file.Length));
            }
            return new WorkspaceContentVersion(
                null,
                null,
                info.LastWriteTimeUtc,
                new ByteSize(0));
        }

        private static IReadOnlyList<(int Start, int End)> LineRanges(
            string text)
        {
            var ranges = new List<(int Start, int End)>();
            int start = 0;
            for (int index = 0; index < text.Length; index++)
            {
                if (text[index] != '\n')
                {
                    continue;
                }
                ranges.Add((start, index + 1));
                start = index + 1;
            }
            if (start < text.Length)
            {
                ranges.Add((start, text.Length));
            }
            return ranges;
        }
    }
}
