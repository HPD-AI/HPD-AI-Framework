using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Xml;
using HPD.Agent.Sandbox;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.ToolHarness.Coding.Security;
using HPD.Agent.Middleware;
using HPD.Environment.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileSystemGlobbing;

public partial class CodingToolHarness
{
    private const int DefaultGrepLimit = 200;
    private const int MaxGrepLimit = 1000;
    private const int MaxGrepContextLines = 20;
    private const int MaxGrepMatchesPerFile = 1000;
    private const int MaxGrepDepth = 100;
    private const int GrepTimeoutMilliseconds = 20_000;
    private const int MaxGrepColumns = 500;
    private const int MaxGrepLineLength = 2000;

    private static readonly string[] GrepBuiltInExcludeGlobs =
    [
        ".git/**",
        "**/.git/**",
        ".svn/**",
        "**/.svn/**",
        ".hg/**",
        "**/.hg/**",
        ".bzr/**",
        "**/.bzr/**",
        ".jj/**",
        "**/.jj/**",
        ".sl/**",
        "**/.sl/**"
    ];

    /// <summary>
    /// Searches the Environment workspace and returns bounded XML results.
    /// Backend implementations may accelerate the operation with ripgrep.
    /// </summary>
    [AIFunction]
    [RequiresPermission]
    [Description("Searches file contents through the active Environment workspace. Defaults to files_with_matches for broad discovery. Use outputMode=Content for matching lines and optional context, or outputMode=Count for per-file counts. Use includeGlobs/excludeGlobs, path, fixedStrings, wordRegexp, and multiline to narrow searches.")]
    public async Task<string> Grep(
        [Description("The regex pattern to search for. Use fixedStrings for literal text.")] string pattern,
        [Description("The file or directory path to search. Relative paths are resolved from the current working directory.")] string path = ".",
        [Description("Controls whether to return files, matching content, or per-file counts.")] GrepOutputMode outputMode = GrepOutputMode.FilesWithMatches,
        [Description("The 1-based result number to start returning after filtering.")] int offset = 1,
        [Description("The maximum number of results to return. Maximum: 1000.")] int limit = DefaultGrepLimit,
        [Description("Optional workspace glob patterns to include.")] string[]? includeGlobs = null,
        [Description("Optional workspace glob patterns to exclude.")] string[]? excludeGlobs = null,
        [Description("Controls case sensitivity.")] GrepCaseMode caseMode = GrepCaseMode.Smart,
        [Description("Whether to treat pattern as literal text.")] bool fixedStrings = false,
        [Description("Whether matches must be whole words.")] bool wordRegexp = false,
        [Description("Symmetric before/after context lines. Applies only to content output.")] int contextLines = 0,
        [Description("Before-context lines. Applies only to content output and overrides the before side of contextLines when greater than zero.")] int beforeContext = 0,
        [Description("After-context lines. Applies only to content output and overrides the after side of contextLines when greater than zero.")] int afterContext = 0,
        [Description("Optional per-file match cap.")] int? maxMatchesPerFile = null,
        [Description("Optional recursive directory depth cap.")] int? maxDepth = null,
        [Description("Whether to enable multiline search with dot matching newlines.")] bool multiline = false,
        [Description("Whether to include hidden files and directories.")] bool includeHidden = false,
        [Description("Whether to respect ignore files such as .gitignore.")] bool respectIgnoreFiles = true,
        FunctionExecutionContext context = null!)
    {
        try
        {
            var argumentError = ValidateGrepArguments(
                pattern,
                path,
                outputMode,
                offset,
                limit,
                caseMode,
                contextLines,
                beforeContext,
                afterContext,
                maxMatchesPerFile,
                maxDepth);
            if (argumentError != null)
                return FormatGrepError(path ?? string.Empty, argumentError);

            if (!CodingWorkspaceAccess.TryGet(context, out var workspace))
            {
                return FormatGrepError(
                    path ?? string.Empty,
                    "No Environment workspace binding is available for Grep.");
            }
            return await GrepWorkspaceAsync(
                workspace,
                pattern,
                path,
                outputMode,
                offset,
                limit,
                includeGlobs,
                excludeGlobs,
                caseMode,
                fixedStrings,
                wordRegexp,
                contextLines,
                beforeContext,
                afterContext,
                maxMatchesPerFile,
                maxDepth,
                multiline,
                includeHidden,
                respectIgnoreFiles).ConfigureAwait(false);
        }
        catch (AgentCapabilityDeniedException ex)
        {
            return FormatGrepError(path ?? string.Empty, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return FormatGrepError(path ?? string.Empty, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return FormatGrepError(path ?? string.Empty, $"Unable to search files: {ex.Message}");
        }
        catch (IOException ex)
        {
            return FormatGrepError(path ?? string.Empty, $"Unable to search files: {ex.Message}");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return FormatGrepError(path ?? string.Empty, $"Unable to search files: {ex.Message}");
        }
    }

    private static async Task<string> GrepWorkspaceAsync(
        RuntimeWorkspaceBinding workspace,
        string pattern,
        string path,
        GrepOutputMode outputMode,
        int offset,
        int limit,
        string[]? includeGlobs,
        string[]? excludeGlobs,
        GrepCaseMode caseMode,
        bool fixedStrings,
        bool wordRegexp,
        int contextLines,
        int beforeContext,
        int afterContext,
        int? maxMatchesPerFile,
        int? maxDepth,
        bool multiline,
        bool includeHidden,
        bool respectIgnoreFiles)
    {
        WorkspacePath searchRoot =
            CodingWorkspaceAccess.Resolve(workspace, path);
        string displayPath =
            CodingWorkspaceAccess.Display(workspace, searchRoot);
        WorkspaceFileInfo? rootInfo =
            await workspace.Files.GetInfoAsync(searchRoot)
                .ConfigureAwait(false);
        if (rootInfo is null)
        {
            return FormatGrepError(
                displayPath,
                "Path does not exist.");
        }

        int searchLimit = Math.Min(
            10_000,
            offset + limit + 1);
        string[] includes = NormalizeGlobs(includeGlobs).ToArray();
        string[] exclusions = NormalizeGlobs(excludeGlobs)
            .Concat(GrepBuiltInExcludeGlobs)
            .ToArray();
        int effectiveBefore =
            CalculateEffectiveBeforeContext(
                contextLines,
                beforeContext);
        int effectiveAfter =
            CalculateEffectiveAfterContext(
                contextLines,
                afterContext);
        WorkspaceSearchResultSet search =
            await workspace.Files.SearchTextAsync(
                new WorkspaceSearchRequest
                {
                    Root = searchRoot,
                    Query = pattern.Trim(),
                    OutputMode = outputMode switch
                    {
                        GrepOutputMode.FilesWithMatches =>
                            WorkspaceSearchOutputMode.FilesWithMatches,
                        GrepOutputMode.Count =>
                            WorkspaceSearchOutputMode.Count,
                        _ => WorkspaceSearchOutputMode.Matches,
                    },
                    IncludeGlobs = includes,
                    ExcludeGlobs = exclusions,
                    CaseMode = caseMode switch
                    {
                        GrepCaseMode.Sensitive =>
                            WorkspaceSearchCaseMode.Sensitive,
                        GrepCaseMode.Insensitive =>
                            WorkspaceSearchCaseMode.Insensitive,
                        _ => WorkspaceSearchCaseMode.Smart,
                    },
                    FixedStrings = fixedStrings,
                    WordRegexp = wordRegexp,
                    Multiline = multiline,
                    IncludeHidden = includeHidden,
                    RespectIgnoreFiles = respectIgnoreFiles,
                    MaxDepth = maxDepth,
                    MaxMatchesPerFile = maxMatchesPerFile,
                    BeforeContext = outputMode == GrepOutputMode.Content
                        ? effectiveBefore
                        : 0,
                    AfterContext = outputMode == GrepOutputMode.Content
                        ? effectiveAfter
                        : 0,
                    MaxColumns = MaxGrepColumns,
                    Timeout = TimeSpan.FromMilliseconds(
                        GrepTimeoutMilliseconds),
                    MaxResults = searchLimit,
                }).ConfigureAwait(false);
        bool providerTruncated =
            search.Status == WorkspaceSearchStatus.Truncated;
        WorkspaceSearchResult[] filtered = search.Results
            .DistinctBy(result => (
                result.Path.RootId,
                result.Path.RelativePath,
                result.Line,
                result.Column))
            .ToArray();
        GrepMatchInfo[] matches = filtered
            .Select(result => new GrepMatchInfo(
                CodingWorkspaceAccess.Display(workspace, result.Path),
                checked((int)result.Line),
                ShortenLine(result.LineText),
                result.ContextLines
                    .Select(context => new GrepContextLineInfo(
                        checked((int)context.Line),
                        ShortenLine(context.Text)))
                    .ToArray()))
            .ToArray();

        int resultsRead;
        int totalResults;
        int totalMatches;
        Action<XmlWriter> writeBody;
        switch (outputMode)
        {
            case GrepOutputMode.FilesWithMatches:
            {
                var fileCandidates =
                    new List<(string Path, DateTimeOffset ModifiedAt)>();
                foreach (WorkspacePath file in search.Files)
                {
                    WorkspaceFileInfo? info =
                        await workspace.Files.GetInfoAsync(
                            file).ConfigureAwait(false);
                    fileCandidates.Add((
                        CodingWorkspaceAccess.Display(workspace, file),
                        info?.Version.ModifiedAt ??
                            DateTimeOffset.MinValue));
                }
                string[] files = fileCandidates
                    .OrderByDescending(item => item.ModifiedAt)
                    .ThenBy(
                        item => item.Path,
                        StringComparer.Ordinal)
                    .Select(item => item.Path)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                GrepPage<string> page = SlicePage(
                    files,
                    offset,
                    limit);
                resultsRead = page.Items.Count;
                totalResults = files.Length;
                totalMatches = (int)Math.Min(
                    int.MaxValue,
                    files.Length);
                writeBody = writer =>
                {
                    foreach (string file in page.Items)
                    {
                        writer.WriteStartElement("file");
                        writer.WriteAttributeString(
                            "path",
                            file);
                        writer.WriteEndElement();
                    }
                };
                return FormatWorkspaceGrep(
                    page.HasMore,
                    providerTruncated);
            }
            case GrepOutputMode.Count:
            {
                (string Path, int Count)[] counts = search.Counts
                    .Select(count => (
                        Path:
                        CodingWorkspaceAccess.Display(
                            workspace,
                            count.Path),
                        Count: checked((int)count.Count)))
                    .OrderBy(item => item.Path, StringComparer.Ordinal)
                    .ToArray();
                GrepPage<(string Path, int Count)> page =
                    SlicePage<(string Path, int Count)>(
                        counts,
                        offset,
                        limit);
                resultsRead = page.Items.Count;
                totalResults = counts.Length;
                totalMatches = counts.Sum(count => count.Count);
                writeBody = writer =>
                {
                    foreach (var count in page.Items)
                    {
                        writer.WriteStartElement("count");
                        writer.WriteAttributeString(
                            "path",
                            count.Path);
                        writer.WriteAttributeString(
                            "matches",
                            count.Count.ToString(
                                CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }
                };
                return FormatWorkspaceGrep(
                    page.HasMore,
                    providerTruncated);
            }
            default:
            {
                GrepPage<GrepMatchInfo> page = SlicePage(
                    matches,
                    offset,
                    limit);
                resultsRead = page.Items.Count;
                totalResults = matches.Length;
                totalMatches = matches.Length;
                writeBody = writer =>
                {
                    foreach (GrepMatchInfo match in page.Items)
                    {
                        writer.WriteStartElement("match");
                        writer.WriteAttributeString(
                            "path",
                            match.Path);
                        writer.WriteAttributeString(
                            "line",
                            match.LineNumber.ToString(
                                CultureInfo.InvariantCulture));
                        if (effectiveBefore == 0 &&
                            effectiveAfter == 0)
                        {
                            writer.WriteString(
                                $"{match.LineNumber.ToString(CultureInfo.InvariantCulture)}\t{match.Text}");
                        }
                        else
                        {
                            foreach (GrepContextLineInfo context in
                                     match.ContextLines
                                         .Where(context =>
                                             context.LineNumber <
                                             match.LineNumber)
                                         .OrderBy(context =>
                                             context.LineNumber))
                            {
                                writer.WriteStartElement("context");
                                writer.WriteAttributeString(
                                    "line",
                                    context.LineNumber.ToString(
                                        CultureInfo.InvariantCulture));
                                writer.WriteString(
                                    $"{context.LineNumber.ToString(CultureInfo.InvariantCulture)}\t{context.Text}");
                                writer.WriteEndElement();
                            }
                            writer.WriteStartElement("line");
                            writer.WriteString(
                                $"{match.LineNumber.ToString(CultureInfo.InvariantCulture)}\t{match.Text}");
                            writer.WriteEndElement();
                            foreach (GrepContextLineInfo context in
                                     match.ContextLines
                                         .Where(context =>
                                             context.LineNumber >
                                             match.LineNumber)
                                         .OrderBy(context =>
                                             context.LineNumber))
                            {
                                writer.WriteStartElement("context");
                                writer.WriteAttributeString(
                                    "line",
                                    context.LineNumber.ToString(
                                        CultureInfo.InvariantCulture));
                                writer.WriteString(
                                    $"{context.LineNumber.ToString(CultureInfo.InvariantCulture)}\t{context.Text}");
                                writer.WriteEndElement();
                            }
                        }
                        writer.WriteEndElement();
                    }
                };
                return FormatWorkspaceGrep(
                    page.HasMore,
                    providerTruncated);
            }
        }

        string FormatWorkspaceGrep(
            bool pageHasMore,
            bool providerTruncated)
        {
            GrepCompletionStatus completionStatus =
                search.Status switch
                {
                    WorkspaceSearchStatus.Completed
                        when resultsRead == 0 =>
                        GrepCompletionStatus.NoMatches,
                    WorkspaceSearchStatus.Completed =>
                        pageHasMore
                            ? GrepCompletionStatus.Truncated
                            : GrepCompletionStatus.Success,
                    WorkspaceSearchStatus.NoMatches =>
                        GrepCompletionStatus.NoMatches,
                    WorkspaceSearchStatus.Truncated =>
                        GrepCompletionStatus.Truncated,
                    WorkspaceSearchStatus.TimedOut =>
                        GrepCompletionStatus.TimedOut,
                    WorkspaceSearchStatus.Cancelled =>
                        GrepCompletionStatus.Cancelled,
                    _ => GrepCompletionStatus.Failed,
                };
            bool partial =
                pageHasMore ||
                providerTruncated ||
                search.Status is WorkspaceSearchStatus.TimedOut or
                    WorkspaceSearchStatus.Cancelled or
                    WorkspaceSearchStatus.Failed;
            var completion = new GrepCompletionEvent
            {
                Status = completionStatus,
                ExitCode = completionStatus switch
                {
                    GrepCompletionStatus.NoMatches => 1,
                    GrepCompletionStatus.Failed => 2,
                    _ => 0,
                },
                Partial = partial,
                TimedOut =
                    search.Status == WorkspaceSearchStatus.TimedOut,
                Cancelled =
                    search.Status == WorkspaceSearchStatus.Cancelled,
                Truncated =
                    pageHasMore || providerTruncated,
                MatchesEmitted = resultsRead,
                Stderr = search.Message,
                Reason = search.Message,
            };
            if (TryFormatGrepCompletionError(
                    displayPath,
                    completion,
                    resultsRead) is { } error)
            {
                return error;
            }
            GrepResultState state =
                CreateGrepResultState(
                    pageHasMore,
                    completion);
            return FormatGrepResult(
                pattern,
                displayPath,
                outputMode,
                offset,
                limit,
                caseMode,
                fixedStrings,
                wordRegexp,
                contextLines,
                effectiveBefore,
                effectiveAfter,
                maxMatchesPerFile,
                maxDepth,
                includes,
                exclusions,
                multiline,
                includeHidden,
                respectIgnoreFiles,
                resultsRead,
                state.HasNextPage || providerTruncated
                    ? "unknown"
                    : totalResults.ToString(
                        CultureInfo.InvariantCulture),
                providerTruncated
                    ? "unknown"
                    : totalMatches.ToString(
                        CultureInfo.InvariantCulture),
                state,
                completion,
                writeBody);
        }
    }

    private static string? ValidateGrepArguments(
        string? pattern,
        string? path,
        GrepOutputMode outputMode,
        int offset,
        int limit,
        GrepCaseMode caseMode,
        int contextLines,
        int beforeContext,
        int afterContext,
        int? maxMatchesPerFile,
        int? maxDepth)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return "Pattern is required.";
        if (string.IsNullOrWhiteSpace(path))
            return "Path is required.";
        if (!Enum.IsDefined(outputMode))
            return "OutputMode must be a valid GrepOutputMode value.";
        if (!Enum.IsDefined(caseMode))
            return "CaseMode must be a valid GrepCaseMode value.";
        if (offset < 1)
            return "Offset must be greater than or equal to 1.";
        if (limit < 1 || limit > MaxGrepLimit)
            return $"Limit must be between 1 and {MaxGrepLimit.ToString(CultureInfo.InvariantCulture)}.";
        if (contextLines < 0 || contextLines > MaxGrepContextLines)
            return $"ContextLines must be between 0 and {MaxGrepContextLines.ToString(CultureInfo.InvariantCulture)}.";
        if (beforeContext < 0 || beforeContext > MaxGrepContextLines)
            return $"BeforeContext must be between 0 and {MaxGrepContextLines.ToString(CultureInfo.InvariantCulture)}.";
        if (afterContext < 0 || afterContext > MaxGrepContextLines)
            return $"AfterContext must be between 0 and {MaxGrepContextLines.ToString(CultureInfo.InvariantCulture)}.";
        if (maxMatchesPerFile is < 1 or > MaxGrepMatchesPerFile)
            return $"MaxMatchesPerFile must be between 1 and {MaxGrepMatchesPerFile.ToString(CultureInfo.InvariantCulture)}.";
        if (maxDepth is < 1 or > MaxGrepDepth)
            return $"MaxDepth must be between 1 and {MaxGrepDepth.ToString(CultureInfo.InvariantCulture)}.";
        if ((contextLines > 0 || beforeContext > 0 || afterContext > 0) && outputMode != GrepOutputMode.Content)
            return "Context parameters require outputMode Content.";

        return null;
    }

    private static IReadOnlyList<string> NormalizeGlobs(string[]? globs)
        => globs?
            .Where(glob => !string.IsNullOrWhiteSpace(glob))
            .Select(glob => glob.Trim().Replace('\\', '/'))
            .ToArray() ?? [];

    private static int CalculateEffectiveBeforeContext(int contextLines, int beforeContext)
        => beforeContext > 0 ? beforeContext : contextLines;

    private static int CalculateEffectiveAfterContext(int contextLines, int afterContext)
        => afterContext > 0 ? afterContext : contextLines;

    private static GrepPage<T> SlicePage<T>(IReadOnlyList<T> items, int offset, int limit)
    {
        var start = offset - 1;
        if (start >= items.Count)
            return new GrepPage<T>([], false);

        var requested = items.Skip(start).Take(limit + 1).ToArray();
        var hasMore = requested.Length > limit;
        return new GrepPage<T>(hasMore ? requested.Take(limit).ToArray() : requested, hasMore);
    }

    private static string FormatGrepResult(
        string pattern,
        string path,
        GrepOutputMode outputMode,
        int offset,
        int limit,
        GrepCaseMode caseMode,
        bool fixedStrings,
        bool wordRegexp,
        int contextLines,
        int effectiveBeforeContext,
        int effectiveAfterContext,
        int? maxMatchesPerFile,
        int? maxDepth,
        IReadOnlyList<string> includeGlobs,
        IReadOnlyList<string> excludeGlobs,
        bool multiline,
        bool includeHidden,
        bool respectIgnoreFiles,
        int resultsRead,
        string totalResults,
        string totalMatches,
        GrepResultState state,
        GrepCompletionEvent completion,
        Action<XmlWriter> writeBody)
    {
        var builder = new StringBuilder();
        using var writer = XmlWriter.Create(builder, new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            ConformanceLevel = ConformanceLevel.Fragment
        });

        writer.WriteStartElement("grep");
        writer.WriteAttributeString("path", path);
        writer.WriteAttributeString("pattern", pattern);
        writer.WriteAttributeString("output_mode", ToXmlValue(outputMode));
        writer.WriteAttributeString("offset", offset.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("limit", limit.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("results_read", resultsRead.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("total_results", totalResults);
        writer.WriteAttributeString("total_matches", totalMatches);
        writer.WriteAttributeString("truncated", XmlConvert.ToString(state.Truncated));
        if (state.TruncationReason != null)
            writer.WriteAttributeString("truncation_reason", state.TruncationReason);
        writer.WriteAttributeString("case_mode", ToXmlValue(caseMode));
        writer.WriteAttributeString("fixed_strings", XmlConvert.ToString(fixedStrings));
        writer.WriteAttributeString("word_regexp", XmlConvert.ToString(wordRegexp));
        writer.WriteAttributeString("context_lines", contextLines.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("before_context", effectiveBeforeContext.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("after_context", effectiveAfterContext.ToString(CultureInfo.InvariantCulture));
        if (maxMatchesPerFile.HasValue)
            writer.WriteAttributeString("max_matches_per_file", maxMatchesPerFile.Value.ToString(CultureInfo.InvariantCulture));
        if (maxDepth.HasValue)
            writer.WriteAttributeString("max_depth", maxDepth.Value.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("multiline", XmlConvert.ToString(multiline));
        writer.WriteAttributeString("include_hidden", XmlConvert.ToString(includeHidden));
        writer.WriteAttributeString("respect_ignore_files", XmlConvert.ToString(respectIgnoreFiles));
        writer.WriteAttributeString("status", ToXmlValue(completion.Status));

        foreach (var glob in includeGlobs)
        {
            writer.WriteStartElement("include_glob");
            writer.WriteAttributeString("pattern", glob);
            writer.WriteEndElement();
        }

        foreach (var glob in excludeGlobs)
        {
            writer.WriteStartElement("exclude_glob");
            writer.WriteAttributeString("pattern", glob);
            writer.WriteEndElement();
        }

        if (resultsRead == 0 && completion.Status is GrepCompletionStatus.NoMatches or GrepCompletionStatus.Success)
        {
            writer.WriteStartElement("no_matches");
            writer.WriteEndElement();

            if (respectIgnoreFiles || !includeHidden)
            {
                writer.WriteStartElement("search_hint");
                writer.WriteString("No matches found. If relevant files may be hidden or ignored, retry with respectIgnoreFiles=false, includeHidden=true, a narrower path, or adjusted includeGlobs/excludeGlobs.");
                writer.WriteEndElement();
            }
        }
        else
        {
            writeBody(writer);
        }

        if (state.HasNextPage)
        {
            writer.WriteStartElement("next_grep");
            writer.WriteAttributeString("offset", (offset + resultsRead).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("limit", limit.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("reason", state.NextReason ?? "more_matches_available");
            writer.WriteEndElement();
        }

        if (state.Truncated)
        {
            writer.WriteStartElement("truncation_hint");
            writer.WriteString(state.TruncationReason == "timeout"
                ? "Search timed out. Use a more specific path, pattern, includeGlobs, or excludeGlobs."
                : "Use a more specific path, pattern, includeGlobs, or excludeGlobs.");
            writer.WriteEndElement();
        }

        if (!string.IsNullOrWhiteSpace(completion.Stderr))
        {
            writer.WriteStartElement("diagnostic");
            writer.WriteString(completion.Stderr);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.Flush();
        return builder.ToString();
    }

    private static string FormatGrepError(string path, string message)
    {
        var builder = new StringBuilder();
        using var writer = XmlWriter.Create(builder, new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            ConformanceLevel = ConformanceLevel.Fragment
        });

        writer.WriteStartElement("error");
        writer.WriteAttributeString("tool", "Grep");
        writer.WriteAttributeString("path", path);
        writer.WriteString(message);
        writer.WriteEndElement();
        writer.Flush();
        return builder.ToString();
    }

    private static string? TryFormatGrepCompletionError(
        string path,
        GrepCompletionEvent completion,
        int resultsRead)
    {
        if (resultsRead > 0)
            return null;

        return completion.Status switch
        {
            GrepCompletionStatus.Failed => FormatGrepError(path, "Workspace search failed."),
            GrepCompletionStatus.TimedOut => FormatGrepError(path, "Workspace search timed out."),
            GrepCompletionStatus.Cancelled => FormatGrepError(path, "Workspace search cancelled."),
            _ => null
        };
    }

    private static GrepResultState CreateGrepResultState(
        bool pageHasMore,
        GrepCompletionEvent completion,
        bool lineTruncated = false)
    {
        var hasNextPage = pageHasMore ||
            completion.Truncated ||
            completion.TimedOut ||
            completion.Cancelled ||
            completion.Status == GrepCompletionStatus.Failed && completion.Partial;

        var reason = GetGrepTruncationReason(completion, lineTruncated, hasNextPage);
        return new GrepResultState(
            Truncated: hasNextPage || lineTruncated,
            TruncationReason: reason,
            HasNextPage: hasNextPage,
            NextReason: hasNextPage ? GetNextGrepReason(completion) : null);
    }

    private static string? GetGrepTruncationReason(
        GrepCompletionEvent completion,
        bool lineTruncated,
        bool hasNextPage)
    {
        if (completion.TimedOut || completion.Status == GrepCompletionStatus.TimedOut)
            return "timeout";
        if (completion.Cancelled || completion.Status == GrepCompletionStatus.Cancelled)
            return "cancelled";
        if (completion.Status == GrepCompletionStatus.Failed && completion.Partial)
            return "failed";
        if (hasNextPage)
            return "limit";
        return lineTruncated ? "line_length" : null;
    }

    private static string GetNextGrepReason(GrepCompletionEvent completion)
    {
        if (completion.TimedOut || completion.Status == GrepCompletionStatus.TimedOut)
            return "partial_results_timeout";
        if (completion.Cancelled || completion.Status == GrepCompletionStatus.Cancelled)
            return "partial_results_cancelled";
        if (completion.Status == GrepCompletionStatus.Failed && completion.Partial)
            return "partial_results_failed";
        return "more_matches_available";
    }

    private static string ShortenLine(string value)
    {
        var truncated = false;
        return ShortenLine(value, ref truncated);
    }

    private static string ShortenLine(string value, ref bool truncated)
    {
        if (value.Length <= MaxGrepLineLength)
            return value;

        truncated = true;
        return value[..MaxGrepLineLength] + "... [line truncated]";
    }

    private static string ToXmlValue(GrepOutputMode mode)
        => mode switch
        {
            GrepOutputMode.FilesWithMatches => "files_with_matches",
            GrepOutputMode.Content => "content",
            GrepOutputMode.Count => "count",
            _ => mode.ToString()
        };

    private static string ToXmlValue(GrepCaseMode mode)
        => mode.ToString().ToLowerInvariant();

    private static string ToXmlValue(GrepCompletionStatus status)
        => status.ToString().ToLowerInvariant();

    private sealed class GrepMatchInfo(
        string path,
        int lineNumber,
        string text,
        IEnumerable<GrepContextLineInfo>? contextLines = null)
    {
        public string Path { get; } = path;
        public int LineNumber { get; } = lineNumber;
        public string Text { get; } = text;
        public List<GrepContextLineInfo> ContextLines { get; } =
            contextLines?.ToList() ?? [];
    }

    private sealed record GrepContextLineInfo(int LineNumber, string Text);

    private sealed record GrepPage<T>(IReadOnlyList<T> Items, bool HasMore);

    private sealed record GrepResultState(
        bool Truncated,
        string? TruncationReason,
        bool HasNextPage,
        string? NextReason);
}

public enum GrepOutputMode
{
    FilesWithMatches,
    Content,
    Count
}

public enum GrepCaseMode
{
    Sensitive,
    Insensitive,
    Smart
}
