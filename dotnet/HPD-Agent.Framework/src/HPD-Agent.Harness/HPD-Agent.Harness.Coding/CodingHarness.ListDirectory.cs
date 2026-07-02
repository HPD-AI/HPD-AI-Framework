using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Xml;
using HPD.Agent;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.Middleware;
using HPDOS.ToolHarnesses.Middleware;
using Microsoft.Extensions.AI;

public partial class CodingToolHarness
{
    private const int DefaultEntryLimit = 200;
    private const int MaxEntryLimit = 1000;
    private const int DefaultMaxDepth = 1;
    private const int MaxDepth = 25;
    private const int RecursiveTraversalTimeoutMilliseconds = 10_000;
    private const int MaxRecursiveTraversalEntries = 20_000;

    /// <summary>
    /// Lists files and folders under a directory as a bounded XML fragment.
    /// </summary>
    [AIFunction]
    [RequiresPermission]
    [Description("Lists files and folders under a directory. Use this to understand project structure before reading specific files. Directories end with '/'. Use typed parameters for common ls/find behavior: includeHidden for hidden entries, includeMetadata for size and timestamps, kind for files/directories, maxDepth for bounded recursive tree views, and sortBy/sortDirection for ordering. Prefer GlobSearch for filename patterns and Grep for content search.")]
    public async Task<string> ListDirectory(
        [Description("The directory path to list. Relative paths are resolved from the current working directory.")] string path,
        [Description("The 1-based entry number to start returning after filtering and sorting.")] int offset = 1,
        [Description("The maximum number of entries to return. Maximum: 1000.")] int limit = DefaultEntryLimit,
        [Description("Whether to recursively list descendants.")] bool recursive = false,
        [Description("The maximum directory depth to traverse when recursive is true.")] int? maxDepth = null,
        [Description("Whether to include hidden files and directories.")] bool includeHidden = false,
        [Description("Whether to respect ignore files such as .gitignore.")] bool respectIgnoreFiles = true,
        [Description("Filters entries by kind.")] DirectoryEntryKindFilter kind = DirectoryEntryKindFilter.All,
        [Description("Controls entry ordering.")] DirectorySortBy sortBy = DirectorySortBy.Name,
        [Description("Controls ascending or descending sort order.")] SortDirection sortDirection = SortDirection.Ascending,
        [Description("Whether to include size, last-write time, and symlink metadata.")] bool includeMetadata = false,
        FunctionExecutionContext context = null!)
    {
        try
        {
            var argumentError = ValidateListDirectoryArguments(path, offset, limit, recursive, maxDepth, kind, sortBy, sortDirection);
            if (argumentError != null)
                return FormatListDirectoryError(path ?? string.Empty, argumentError);

            if (Path.IsPathRooted(path) && IsBlockedDirectoryPath(Path.GetFullPath(path)))
                return FormatListDirectoryError(path, "Cannot list blocked system path.");

            var resolvedPath = ResolveDirectoryPath(path, context);
            if (IsBlockedDirectoryPath(resolvedPath.FullPath))
                return FormatListDirectoryError(resolvedPath.FullPath, "Cannot list blocked system path.");

            var request = CreateDirectoryListingRequest(
                resolvedPath.FullPath,
                offset,
                limit,
                recursive,
                maxDepth,
                includeHidden,
                respectIgnoreFiles,
                kind,
                sortBy,
                sortDirection,
                includeMetadata);

            var sourceResult = await TryListFromDirectorySourcesAsync(request, CancellationToken.None).ConfigureAwait(false);
            if (sourceResult != null)
                return FormatDirectoryResult(CreateResultFromSource(request, sourceResult));

            if (File.Exists(resolvedPath.FullPath))
                return FormatListDirectoryError(resolvedPath.FullPath, "Path is a file. Use ReadFile instead.");

            if (!Directory.Exists(resolvedPath.FullPath))
                return FormatListDirectoryError(resolvedPath.FullPath, BuildMissingDirectoryMessage(resolvedPath.FullPath));

            var result = await ListLocalDirectoryAsync(request, CancellationToken.None).ConfigureAwait(false);
            return FormatDirectoryResult(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return FormatListDirectoryError(path ?? string.Empty, $"Unable to list directory: {ex.Message}");
        }
        catch (IOException ex)
        {
            return FormatListDirectoryError(path ?? string.Empty, $"Unable to list directory: {ex.Message}");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return FormatListDirectoryError(path ?? string.Empty, $"Unable to list directory: {ex.Message}");
        }
    }

    private async ValueTask<DirectoryListingSourceResult?> TryListFromDirectorySourcesAsync(
        DirectoryListingRequest request,
        CancellationToken cancellationToken)
    {
        foreach (var source in _directoryListingSources)
        {
            var result = await source.TryListAsync(request, cancellationToken).ConfigureAwait(false);
            if (result != null)
                return result;
        }

        return null;
    }

    private static string? ValidateListDirectoryArguments(
        string? path,
        int offset,
        int limit,
        bool recursive,
        int? maxDepth,
        DirectoryEntryKindFilter kind,
        DirectorySortBy sortBy,
        SortDirection sortDirection)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Path is required.";
        if (offset < 1)
            return "Offset must be greater than or equal to 1.";
        if (limit < 1 || limit > MaxEntryLimit)
            return $"Limit must be between 1 and {MaxEntryLimit.ToString(CultureInfo.InvariantCulture)}.";
        if (maxDepth is < 0 or > MaxDepth)
            return $"MaxDepth must be between 0 and {MaxDepth.ToString(CultureInfo.InvariantCulture)}.";
        if (!recursive && maxDepth is not null and not DefaultMaxDepth)
            return "MaxDepth requires recursive mode.";
        if (!Enum.IsDefined(kind))
            return "Kind must be a valid DirectoryEntryKindFilter value.";
        if (!Enum.IsDefined(sortBy))
            return "SortBy must be a valid DirectorySortBy value.";
        if (!Enum.IsDefined(sortDirection))
            return "SortDirection must be a valid SortDirection value.";

        return null;
    }

    private static ResolvedDirectoryPath ResolveDirectoryPath(string path, FunctionExecutionContext? context)
    {
        var trimmedPath = path.Trim();
        var fullPath = Path.GetFullPath(trimmedPath, Directory.GetCurrentDirectory());
        return new ResolvedDirectoryPath(trimmedPath, fullPath);
    }

    private static bool IsBlockedDirectoryPath(string fullPath)
    {
        var normalized = fullPath.Replace('\\', '/').TrimEnd('/');
        if (normalized is "/dev" or "/proc")
            return true;

        return normalized.StartsWith("/dev/", StringComparison.Ordinal) ||
               normalized.StartsWith("/proc/", StringComparison.Ordinal);
    }

    private static DirectoryListingRequest CreateDirectoryListingRequest(
        string fullPath,
        int offset,
        int limit,
        bool recursive,
        int? maxDepth,
        bool includeHidden,
        bool respectIgnoreFiles,
        DirectoryEntryKindFilter kind,
        DirectorySortBy sortBy,
        SortDirection sortDirection,
        bool includeMetadata)
        => new()
        {
            FullPath = fullPath,
            Offset = offset,
            Limit = limit,
            Recursive = recursive,
            MaxDepth = recursive ? maxDepth ?? MaxDepth : DefaultMaxDepth,
            IncludeHidden = includeHidden,
            RespectIgnoreFiles = respectIgnoreFiles,
            Kind = kind,
            SortBy = sortBy,
            SortDirection = sortDirection,
            IncludeMetadata = includeMetadata
        };

    private static async Task<ListDirectoryResult> ListLocalDirectoryAsync(
        DirectoryListingRequest request,
        CancellationToken cancellationToken)
    {
        var ignoreMatcher = request.RespectIgnoreFiles ? CreateIgnoreMatcher(request.FullPath) : null;
        var state = new LocalListingState(ignoreMatcher);
        var stopwatch = Stopwatch.StartNew();

        IReadOnlyList<DirectoryEntryInfo> collected;
        var truncated = false;
        string? truncationReason = null;

        if (request.Recursive)
        {
            collected = await EnumerateRecursiveAsync(request, state, stopwatch, cancellationToken).ConfigureAwait(false);
            truncated = state.TraversalStopped;
            truncationReason = state.TruncationReason;
        }
        else
        {
            collected = EnumerateNonRecursive(request, state);
        }

        var sorted = SortEntries(collected, request.SortBy, request.SortDirection);
        var totalEntries = sorted.Count.ToString(CultureInfo.InvariantCulture);
        var page = sorted.Skip(request.Offset - 1).Take(request.Limit).ToArray();
        var hasMore = request.Offset - 1 + page.Length < sorted.Count;

        if (hasMore)
        {
            truncated = true;
            truncationReason ??= "limit";
        }

        return new ListDirectoryResult
        {
            Path = request.FullPath,
            Entries = page,
            Offset = request.Offset,
            Limit = request.Limit,
            Recursive = request.Recursive,
            MaxDepth = request.MaxDepth,
            IncludeHidden = request.IncludeHidden,
            RespectIgnoreFiles = request.RespectIgnoreFiles,
            Kind = request.Kind,
            SortBy = request.SortBy,
            SortDirection = request.SortDirection,
            IncludeMetadata = request.IncludeMetadata,
            TotalEntries = state.TraversalTimedOut ? "unknown" : totalEntries,
            IgnoredCount = state.IgnoredCount,
            Truncated = truncated,
            TruncationReason = truncationReason,
            NextOffset = hasMore ? request.Offset + page.Length : null
        };
    }

    private static ListDirectoryResult CreateResultFromSource(
        DirectoryListingRequest request,
        DirectoryListingSourceResult sourceResult)
    {
        var sorted = SortEntries(FilterEntries(sourceResult.Entries, request), request.SortBy, request.SortDirection);
        var page = sorted.Skip(request.Offset - 1).Take(request.Limit).ToArray();
        var hasMore = request.Offset - 1 + page.Length < sorted.Count;

        return new ListDirectoryResult
        {
            Path = sourceResult.FullPath,
            Entries = page,
            Offset = request.Offset,
            Limit = request.Limit,
            Recursive = request.Recursive,
            MaxDepth = request.MaxDepth,
            IncludeHidden = request.IncludeHidden,
            RespectIgnoreFiles = request.RespectIgnoreFiles,
            Kind = request.Kind,
            SortBy = request.SortBy,
            SortDirection = request.SortDirection,
            IncludeMetadata = request.IncludeMetadata,
            TotalEntries = sourceResult.TotalEntries,
            IgnoredCount = sourceResult.IgnoredCount,
            Truncated = sourceResult.Truncated || hasMore,
            TruncationReason = sourceResult.TruncationReason ?? (hasMore ? "limit" : null),
            NextOffset = hasMore ? request.Offset + page.Length : null
        };
    }

    private static IReadOnlyList<DirectoryEntryInfo> FilterEntries(
        IReadOnlyList<DirectoryEntryInfo> entries,
        DirectoryListingRequest request)
        => entries
            .Where(entry => request.IncludeHidden || !IsHiddenPath(entry.RelativePath))
            .Where(entry => EntryMatchesKind(entry, request.Kind))
            .ToArray();

    private static IReadOnlyList<DirectoryEntryInfo> EnumerateNonRecursive(
        DirectoryListingRequest request,
        LocalListingState state)
    {
        var entries = new List<DirectoryEntryInfo>();

        foreach (var fullPath in EnumerateFileSystemEntries(request.FullPath, throwOnFailure: true))
        {
            if (TryCreateEntryInfo(fullPath, request.FullPath, out var entry) &&
                ShouldIncludeEntry(entry, request, state, recursiveChild: false))
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private static async Task<IReadOnlyList<DirectoryEntryInfo>> EnumerateRecursiveAsync(
        DirectoryListingRequest request,
        LocalListingState state,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var entries = new List<DirectoryEntryInfo>();
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((request.FullPath, 1));

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (stopwatch.ElapsedMilliseconds > RecursiveTraversalTimeoutMilliseconds)
            {
                state.Stop("timeout");
                break;
            }

            var (directory, depth) = queue.Dequeue();
            if (depth > request.MaxDepth)
                continue;

            foreach (var fullPath in EnumerateFileSystemEntries(directory, throwOnFailure: directory == request.FullPath))
            {
                if (entries.Count >= MaxRecursiveTraversalEntries)
                {
                    state.Stop("traversal_cap");
                    return entries;
                }

                if (!TryCreateEntryInfo(fullPath, request.FullPath, out var entry))
                    continue;

                if (!ShouldIncludeEntry(entry, request, state, recursiveChild: true))
                    continue;

                entries.Add(entry);

                if (entry.Kind == DirectoryEntryKind.Directory &&
                    !entry.IsSymlink &&
                    depth < request.MaxDepth)
                {
                    queue.Enqueue((fullPath, depth + 1));
                }
            }

            await Task.Yield();
        }

        return entries;
    }

    private static bool TryCreateEntryInfo(string fullPath, string rootPath, out DirectoryEntryInfo entry)
    {
        entry = default!;

        try
        {
            var attributes = File.GetAttributes(fullPath);
            var isDirectory = attributes.HasFlag(FileAttributes.Directory);
            var isSymlink = attributes.HasFlag(FileAttributes.ReparsePoint);
            var kind = isDirectory ? DirectoryEntryKind.Directory : DirectoryEntryKind.File;
            var relativePath = Path.GetRelativePath(rootPath, fullPath).Replace(Path.DirectorySeparatorChar, '/');
            if (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar)
                relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, '/');

            long? size = null;
            if (!isDirectory)
                size = new FileInfo(fullPath).Length;

            entry = new DirectoryEntryInfo
            {
                RelativePath = EnsureDirectorySuffix(relativePath, kind),
                Kind = kind,
                Size = size,
                LastWriteTimeUtc = File.GetLastWriteTimeUtc(fullPath),
                IsSymlink = isSymlink
            };

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ShouldIncludeEntry(
        DirectoryEntryInfo entry,
        DirectoryListingRequest request,
        LocalListingState state,
        bool recursiveChild)
    {
        if (!request.IncludeHidden && IsHiddenPath(entry.RelativePath))
        {
            state.IgnoredCount++;
            return false;
        }

        var normalizedName = entry.RelativePath.TrimEnd('/');
        var leafName = Path.GetFileName(normalizedName);
        if (recursiveChild && entry.Kind == DirectoryEntryKind.Directory && BuiltInRecursiveSkips.Contains(leafName))
        {
            state.IgnoredCount++;
            return false;
        }

        if (request.RespectIgnoreFiles && state.IsIgnored(entry))
        {
            state.IgnoredCount++;
            return false;
        }

        return EntryMatchesKind(entry, request.Kind);
    }

    private static bool EntryMatchesKind(DirectoryEntryInfo entry, DirectoryEntryKindFilter kind)
        => kind switch
        {
            DirectoryEntryKindFilter.All => true,
            DirectoryEntryKindFilter.Files => entry.Kind == DirectoryEntryKind.File,
            DirectoryEntryKindFilter.Directories => entry.Kind == DirectoryEntryKind.Directory,
            _ => false
        };

    private static IReadOnlyList<DirectoryEntryInfo> SortEntries(
        IReadOnlyList<DirectoryEntryInfo> entries,
        DirectorySortBy sortBy,
        SortDirection sortDirection)
    {
        IOrderedEnumerable<DirectoryEntryInfo> ordered = sortBy switch
        {
            DirectorySortBy.ModifiedTime => sortDirection == SortDirection.Descending
                ? entries.OrderByDescending(entry => entry.LastWriteTimeUtc)
                : entries.OrderBy(entry => entry.LastWriteTimeUtc),
            DirectorySortBy.Size => sortDirection == SortDirection.Descending
                ? entries.OrderByDescending(entry => entry.Size ?? 0)
                : entries.OrderBy(entry => entry.Size ?? 0),
            DirectorySortBy.Kind => sortDirection == SortDirection.Descending
                ? entries.OrderByDescending(entry => KindSortRank(entry.Kind))
                : entries.OrderBy(entry => KindSortRank(entry.Kind)),
            _ => sortDirection == SortDirection.Descending
                ? entries.OrderByDescending(entry => entry.RelativePath, GetPathComparer())
                : entries.OrderBy(entry => entry.Kind == DirectoryEntryKind.Directory ? 0 : 1)
                    .ThenBy(entry => entry.RelativePath, GetPathComparer())
        };

        if (sortBy != DirectorySortBy.Name)
            ordered = ordered.ThenBy(entry => entry.RelativePath, GetPathComparer());

        return ordered.ToArray();
    }

    private static int KindSortRank(DirectoryEntryKind kind)
        => kind switch
        {
            DirectoryEntryKind.Directory => 0,
            DirectoryEntryKind.File => 1,
            _ => 2
        };

    private static string EnsureDirectorySuffix(string relativePath, DirectoryEntryKind kind)
        => kind == DirectoryEntryKind.Directory && !relativePath.EndsWith("/", StringComparison.Ordinal)
            ? relativePath + "/"
            : relativePath;

    private static string FormatDirectoryResult(ListDirectoryResult result)
    {
        var builder = new StringBuilder();
        using var writer = CreateCodingToolHarnessXmlWriter(builder);

        writer.WriteStartElement("directory");
        writer.WriteAttributeString("path", result.Path);
        writer.WriteAttributeString("recursive", FormatBool(result.Recursive));
        if (result.Recursive)
            writer.WriteAttributeString("max_depth", result.MaxDepth?.ToString(CultureInfo.InvariantCulture) ?? MaxDepth.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("offset", result.Offset.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("entries_read", result.Entries.Count.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("total_entries", result.TotalEntries);
        writer.WriteAttributeString("ignored_count", result.IgnoredCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("include_hidden", FormatBool(result.IncludeHidden));
        writer.WriteAttributeString("respect_ignore_files", FormatBool(result.RespectIgnoreFiles));
        writer.WriteAttributeString("kind", FormatEnum(result.Kind));
        writer.WriteAttributeString("sort_by", FormatEnum(result.SortBy));
        writer.WriteAttributeString("sort_direction", FormatEnum(result.SortDirection));
        writer.WriteAttributeString("include_metadata", FormatBool(result.IncludeMetadata));
        writer.WriteAttributeString("truncated", FormatBool(result.Truncated));
        if (!string.IsNullOrEmpty(result.TruncationReason))
            writer.WriteAttributeString("truncation_reason", result.TruncationReason);

        if (result.Entries.Count == 0 && result.TotalEntries == "0")
        {
            writer.WriteStartElement("empty_directory");
            writer.WriteEndElement();
        }
        else if (result.Entries.Count == 0)
        {
            writer.WriteStartElement("no_content");
            writer.WriteAttributeString("reason", "offset_beyond_end");
            writer.WriteEndElement();
        }
        else
        {
            foreach (var entry in result.Entries)
                WriteEntry(writer, entry, result.IncludeMetadata);
        }

        if (result.NextOffset.HasValue)
        {
            writer.WriteStartElement("next_list");
            writer.WriteAttributeString("offset", result.NextOffset.Value.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("limit", result.Limit.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("recursive", FormatBool(result.Recursive));
            if (result.Recursive)
                writer.WriteAttributeString("max_depth", result.MaxDepth?.ToString(CultureInfo.InvariantCulture) ?? MaxDepth.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("include_hidden", FormatBool(result.IncludeHidden));
            writer.WriteAttributeString("respect_ignore_files", FormatBool(result.RespectIgnoreFiles));
            writer.WriteAttributeString("kind", FormatEnum(result.Kind));
            writer.WriteAttributeString("sort_by", FormatEnum(result.SortBy));
            writer.WriteAttributeString("sort_direction", FormatEnum(result.SortDirection));
            writer.WriteAttributeString("include_metadata", FormatBool(result.IncludeMetadata));
            writer.WriteAttributeString("reason", result.TruncationReason == "timeout" ? "partial_results_timeout" : "more_entries_available");
            writer.WriteEndElement();
        }

        if (result.Recursive && result.Truncated)
        {
            writer.WriteStartElement("truncation_hint");
            writer.WriteString(result.TruncationReason == "timeout"
                ? "Traversal timed out. Prefer ListDirectory on a specific subdirectory or rerun with a smaller maxDepth."
                : "For large recursive listings, prefer ListDirectory on a specific subdirectory or rerun with a smaller maxDepth.");
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.Flush();
        return builder.ToString();
    }

    private static void WriteEntry(XmlWriter writer, DirectoryEntryInfo entry, bool includeMetadata)
    {
        writer.WriteStartElement("entry");
        writer.WriteAttributeString("kind", FormatEnum(entry.Kind));
        writer.WriteAttributeString("path", entry.RelativePath);

        if (includeMetadata)
        {
            if (entry.Size.HasValue)
                writer.WriteAttributeString("size", entry.Size.Value.ToString(CultureInfo.InvariantCulture));
            if (entry.LastWriteTimeUtc.HasValue)
                writer.WriteAttributeString("last_write_time_utc", entry.LastWriteTimeUtc.Value.ToString("O", CultureInfo.InvariantCulture));
            if (entry.IsSymlink)
                writer.WriteAttributeString("symlink", "true");
        }

        writer.WriteEndElement();
    }

    private static string FormatListDirectoryError(string path, string message)
    {
        var builder = new StringBuilder();
        using var writer = CreateCodingToolHarnessXmlWriter(builder);

        writer.WriteStartElement("error");
        writer.WriteAttributeString("tool", "ListDirectory");
        if (!string.IsNullOrEmpty(path))
            writer.WriteAttributeString("path", path);
        writer.WriteString(message);
        writer.WriteEndElement();
        writer.Flush();
        return builder.ToString();
    }

    private sealed class LocalListingState(HpdIgnoreMatcher? ignoreMatcher)
    {
        private readonly HpdIgnoreMatcher? _ignoreMatcher = ignoreMatcher;

        public int IgnoredCount { get; set; }

        public bool TraversalStopped { get; private set; }

        public bool TraversalTimedOut => TruncationReason == "timeout";

        public string? TruncationReason { get; private set; }

        public void Stop(string reason)
        {
            TraversalStopped = true;
            TruncationReason = reason;
        }

        public bool IsIgnored(DirectoryEntryInfo entry)
        {
            if (_ignoreMatcher == null)
                return false;

            var relativePath = entry.RelativePath.TrimEnd('/');
            return _ignoreMatcher.IsIgnored(relativePath, entry.Kind == DirectoryEntryKind.Directory);
        }
    }
}

namespace HPDOS.ToolHarnesses.Middleware
{
/// <summary>
/// Provides directory entries for a path before `ListDirectory` falls back to disk.
/// </summary>
public interface IDirectoryListingSource
{
    /// <summary>
    /// Returns a structured directory listing for a resolved path, or null when the source does not own it.
    /// </summary>
    ValueTask<DirectoryListingSourceResult?> TryListAsync(
        DirectoryListingRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Request sent to host-provided directory listing sources.
/// </summary>
public sealed record DirectoryListingRequest
{
    public required string FullPath { get; init; }

    public required int Offset { get; init; }

    public required int Limit { get; init; }

    public required bool Recursive { get; init; }

    public int? MaxDepth { get; init; }

    public required bool IncludeHidden { get; init; }

    public required bool RespectIgnoreFiles { get; init; }

    public required DirectoryEntryKindFilter Kind { get; init; }

    public required DirectorySortBy SortBy { get; init; }

    public required SortDirection SortDirection { get; init; }

    public required bool IncludeMetadata { get; init; }
}

/// <summary>
/// Directory entries returned by a host-provided listing source.
/// </summary>
public sealed record DirectoryListingSourceResult
{
    public required string FullPath { get; init; }

    public required IReadOnlyList<DirectoryEntryInfo> Entries { get; init; }

    public required string TotalEntries { get; init; }

    public required int IgnoredCount { get; init; }

    public required bool Truncated { get; init; }

    public string? TruncationReason { get; init; }
}

public enum DirectoryEntryKind
{
    Directory,
    File,
    Other
}

public sealed record DirectoryEntryInfo
{
    public required string RelativePath { get; init; }

    public required DirectoryEntryKind Kind { get; init; }

    public long? Size { get; init; }

    public DateTimeOffset? LastWriteTimeUtc { get; init; }

    public bool IsSymlink { get; init; }
}

internal sealed record ResolvedDirectoryPath(string InputPath, string FullPath);

internal sealed record ListDirectoryResult
{
    public required string Path { get; init; }

    public required IReadOnlyList<DirectoryEntryInfo> Entries { get; init; }

    public required int Offset { get; init; }

    public required int Limit { get; init; }

    public required bool Recursive { get; init; }

    public int? MaxDepth { get; init; }

    public required bool IncludeHidden { get; init; }

    public required bool RespectIgnoreFiles { get; init; }

    public required DirectoryEntryKindFilter Kind { get; init; }

    public required DirectorySortBy SortBy { get; init; }

    public required SortDirection SortDirection { get; init; }

    public required bool IncludeMetadata { get; init; }

    public required string TotalEntries { get; init; }

    public required int IgnoredCount { get; init; }

    public required bool Truncated { get; init; }

    public string? TruncationReason { get; init; }

    public int? NextOffset { get; init; }
}
}

public enum DirectoryEntryKindFilter
{
    All,
    Files,
    Directories
}

public enum DirectorySortBy
{
    Name,
    ModifiedTime,
    Size,
    Kind
}

public enum SortDirection
{
    Ascending,
    Descending
}
