using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Xml;
using HPD.Agent.Harness.Coding.Ripgrep;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

public partial class CodingHarness
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
    /// Searches file contents with ripgrep and returns bounded XML results.
    /// </summary>
    [AIFunction]
    [RequiresPermission]
    [Description("Searches file contents with ripgrep. Defaults to files_with_matches for broad discovery. Use outputMode=Content for matching lines and optional context, or outputMode=Count for per-file counts. Use includeGlobs/excludeGlobs, path, fixedStrings, wordRegexp, and multiline to narrow searches.")]
    public async Task<string> Grep(
        [Description("The regex pattern to search for. Use fixedStrings for literal text.")] string pattern,
        [Description("The file or directory path to search. Relative paths are resolved from the current working directory.")] string path = ".",
        [Description("Controls whether to return files, matching content, or per-file counts.")] GrepOutputMode outputMode = GrepOutputMode.FilesWithMatches,
        [Description("The 1-based result number to start returning after filtering.")] int offset = 1,
        [Description("The maximum number of results to return. Maximum: 1000.")] int limit = DefaultGrepLimit,
        [Description("Optional ripgrep glob patterns to include.")] string[]? includeGlobs = null,
        [Description("Optional ripgrep glob patterns to exclude.")] string[]? excludeGlobs = null,
        [Description("Controls case sensitivity.")] GrepCaseMode caseMode = GrepCaseMode.Smart,
        [Description("Whether to treat pattern as literal text.")] bool fixedStrings = false,
        [Description("Whether matches must be whole words.")] bool wordRegexp = false,
        [Description("Symmetric before/after context lines. Applies only to content output.")] int contextLines = 0,
        [Description("Before-context lines. Applies only to content output and overrides the before side of contextLines when greater than zero.")] int beforeContext = 0,
        [Description("After-context lines. Applies only to content output and overrides the after side of contextLines when greater than zero.")] int afterContext = 0,
        [Description("Optional per-file match cap. Maps to ripgrep --max-count.")] int? maxMatchesPerFile = null,
        [Description("Optional recursive directory depth cap. Maps to ripgrep --max-depth.")] int? maxDepth = null,
        [Description("Whether to enable multiline search with dot matching newlines.")] bool multiline = false,
        [Description("Whether to include hidden files and directories.")] bool includeHidden = false,
        [Description("Whether to respect ignore files such as .gitignore.")] bool respectIgnoreFiles = true)
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

            var resolved = ResolveGrepPath(path);
            if (IsBlockedSearchPath(resolved.FullPath))
                return FormatGrepError(resolved.FullPath, "Cannot search blocked system path.");

            if (!File.Exists(resolved.FullPath) && !Directory.Exists(resolved.FullPath))
                return FormatGrepError(resolved.FullPath, "Path does not exist.");

            var options = CreateRipgrepOptions(
                pattern.Trim(),
                resolved,
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
                respectIgnoreFiles);

            var normalizedIncludeGlobs = NormalizeGlobs(includeGlobs);
            var normalizedExcludeGlobs = NormalizeGlobs(excludeGlobs);
            var effectiveBeforeContext = CalculateEffectiveBeforeContext(contextLines, beforeContext);
            var effectiveAfterContext = CalculateEffectiveAfterContext(contextLines, afterContext);

            return outputMode switch
            {
                GrepOutputMode.FilesWithMatches => await ExecuteFilesWithMatchesGrepAsync(
                    pattern.Trim(),
                    resolved,
                    options,
                    offset,
                    limit,
                    caseMode,
                    fixedStrings,
                    wordRegexp,
                    contextLines,
                    effectiveBeforeContext,
                    effectiveAfterContext,
                    maxMatchesPerFile,
                    maxDepth,
                    normalizedIncludeGlobs,
                    normalizedExcludeGlobs,
                    multiline,
                    includeHidden,
                    respectIgnoreFiles,
                    CancellationToken.None).ConfigureAwait(false),

                GrepOutputMode.Content => await ExecuteContentGrepAsync(
                    pattern.Trim(),
                    resolved,
                    options,
                    offset,
                    limit,
                    caseMode,
                    fixedStrings,
                    wordRegexp,
                    contextLines,
                    effectiveBeforeContext,
                    effectiveAfterContext,
                    maxMatchesPerFile,
                    maxDepth,
                    normalizedIncludeGlobs,
                    normalizedExcludeGlobs,
                    multiline,
                    includeHidden,
                    respectIgnoreFiles,
                    CancellationToken.None).ConfigureAwait(false),

                GrepOutputMode.Count => await ExecuteCountGrepAsync(
                    pattern.Trim(),
                    resolved,
                    options,
                    offset,
                    limit,
                    caseMode,
                    fixedStrings,
                    wordRegexp,
                    contextLines,
                    effectiveBeforeContext,
                    effectiveAfterContext,
                    maxMatchesPerFile,
                    maxDepth,
                    normalizedIncludeGlobs,
                    normalizedExcludeGlobs,
                    multiline,
                    includeHidden,
                    respectIgnoreFiles,
                    CancellationToken.None).ConfigureAwait(false),

                _ => FormatGrepError(resolved.FullPath, "OutputMode must be a valid GrepOutputMode value.")
            };
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

    private async Task<string> ExecuteFilesWithMatchesGrepAsync(
        string pattern,
        ResolvedGrepPath resolved,
        RipgrepSearchOptions options,
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
        CancellationToken cancellationToken)
    {
        var result = await _ripgrepRunner.ListFilesWithMatchesAsync(options, cancellationToken).ConfigureAwait(false);
        var files = result.Files
            .Distinct(GetPathComparer())
            .OrderByDescending(path => TryGetLastWriteTimeUtc(resolved.WorkingDirectory, path))
            .ThenBy(path => path, GetPathComparer())
            .ToArray();

        var page = SlicePage(files, offset, limit);
        var state = CreateGrepResultState(page.HasMore, result.Completion);
        if (TryFormatGrepCompletionError(resolved.FullPath, result.Completion, page.Items.Count) is { } error)
            return error;

        return FormatGrepResult(
            pattern,
            resolved.FullPath,
            GrepOutputMode.FilesWithMatches,
            offset,
            limit,
            caseMode,
            fixedStrings,
            wordRegexp,
            contextLines,
            effectiveBeforeContext,
            effectiveAfterContext,
            maxMatchesPerFile,
            maxDepth,
            includeGlobs,
            excludeGlobs,
            multiline,
            includeHidden,
            respectIgnoreFiles,
            page.Items.Count,
            state.HasNextPage ? "unknown" : files.Length.ToString(CultureInfo.InvariantCulture),
            files.Length == 0 && result.Completion.Status == RipgrepCompletionStatus.NoMatches ? "0" : "unknown",
            state,
            result.Completion,
            writer =>
            {
                foreach (var file in page.Items)
                {
                    writer.WriteStartElement("file");
                    writer.WriteAttributeString("path", NormalizeResultPath(file));
                    writer.WriteEndElement();
                }
            });
    }

    private async Task<string> ExecuteCountGrepAsync(
        string pattern,
        ResolvedGrepPath resolved,
        RipgrepSearchOptions options,
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
        CancellationToken cancellationToken)
    {
        var result = await _ripgrepRunner.CountAsync(options, cancellationToken).ConfigureAwait(false);
        var page = SlicePage(result.Counts, offset, limit);
        var state = CreateGrepResultState(page.HasMore, result.Completion);
        if (TryFormatGrepCompletionError(resolved.FullPath, result.Completion, page.Items.Count) is { } error)
            return error;

        var totalMatches = state.HasNextPage
            ? "unknown"
            : result.Counts.Sum(count => count.Count).ToString(CultureInfo.InvariantCulture);

        return FormatGrepResult(
            pattern,
            resolved.FullPath,
            GrepOutputMode.Count,
            offset,
            limit,
            caseMode,
            fixedStrings,
            wordRegexp,
            contextLines,
            effectiveBeforeContext,
            effectiveAfterContext,
            maxMatchesPerFile,
            maxDepth,
            includeGlobs,
            excludeGlobs,
            multiline,
            includeHidden,
            respectIgnoreFiles,
            page.Items.Count,
            state.HasNextPage ? "unknown" : result.Counts.Count.ToString(CultureInfo.InvariantCulture),
            totalMatches,
            state,
            result.Completion,
            writer =>
            {
                foreach (var count in page.Items)
                {
                    writer.WriteStartElement("count");
                    writer.WriteAttributeString("path", NormalizeResultPath(count.Path));
                    writer.WriteAttributeString("matches", count.Count.ToString(CultureInfo.InvariantCulture));
                    writer.WriteEndElement();
                }
            });
    }

    private async Task<string> ExecuteContentGrepAsync(
        string pattern,
        ResolvedGrepPath resolved,
        RipgrepSearchOptions options,
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
        CancellationToken cancellationToken)
    {
        var matches = new List<GrepMatchInfo>();
        var pendingContext = new Dictionary<string, List<RipgrepContextEvent>>(GetPathComparer());
        var lastMatchByPath = new Dictionary<string, GrepMatchInfo>(GetPathComparer());
        RipgrepCompletionEvent? completion = null;
        var lineTruncated = false;
        var contextRequested = effectiveBeforeContext > 0 || effectiveAfterContext > 0;

        await foreach (var item in _ripgrepRunner.SearchAsync(options, cancellationToken).ConfigureAwait(false))
        {
            switch (item)
            {
                case RipgrepContextEvent context:
                    if (contextRequested)
                    {
                        var key = NormalizeResultPath(context.Path);
                        if (lastMatchByPath.TryGetValue(key, out var previousMatch) &&
                            context.LineNumber > previousMatch.LineNumber)
                        {
                            previousMatch.ContextLines.Add(new GrepContextLineInfo(
                                context.LineNumber,
                                ShortenLine(TrimLineEnding(context.Text), ref lineTruncated)));
                        }
                        else
                        {
                            if (!pendingContext.TryGetValue(key, out var contexts))
                            {
                                contexts = [];
                                pendingContext[key] = contexts;
                            }

                            contexts.Add(context);
                        }
                    }
                    break;

                case RipgrepMatchEvent match:
                    var matchPath = NormalizeResultPath(match.Path);
                    pendingContext.TryGetValue(matchPath, out var before);
                    var matchInfo = new GrepMatchInfo(
                        matchPath,
                        match.LineNumber,
                        ShortenLine(TrimLineEnding(match.Text), ref lineTruncated));

                    if (before != null)
                    {
                        foreach (var context in before)
                        {
                            matchInfo.ContextLines.Add(new GrepContextLineInfo(
                                context.LineNumber,
                                ShortenLine(TrimLineEnding(context.Text), ref lineTruncated)));
                        }
                    }

                    matches.Add(matchInfo);
                    lastMatchByPath[matchPath] = matchInfo;
                    pendingContext[matchPath] = [];
                    break;

                case RipgrepCompletionEvent completed:
                    completion = completed;
                    break;
            }
        }

        completion ??= new RipgrepCompletionEvent
        {
            Status = RipgrepCompletionStatus.Failed,
            ExitCode = null,
            Partial = matches.Count > 0,
            TimedOut = false,
            Cancelled = false,
            Truncated = false,
            MatchesEmitted = matches.Count,
            Stderr = null,
            Reason = "missing_completion"
        };

        var page = SlicePage(matches, offset, limit);
        var state = CreateGrepResultState(page.HasMore, completion, lineTruncated);
        if (TryFormatGrepCompletionError(resolved.FullPath, completion, page.Items.Count) is { } error)
            return error;

        return FormatGrepResult(
            pattern,
            resolved.FullPath,
            GrepOutputMode.Content,
            offset,
            limit,
            caseMode,
            fixedStrings,
            wordRegexp,
            contextLines,
            effectiveBeforeContext,
            effectiveAfterContext,
            maxMatchesPerFile,
            maxDepth,
            includeGlobs,
            excludeGlobs,
            multiline,
            includeHidden,
            respectIgnoreFiles,
            page.Items.Count,
            state.HasNextPage ? "unknown" : matches.Count.ToString(CultureInfo.InvariantCulture),
            state.HasNextPage ? "unknown" : matches.Count.ToString(CultureInfo.InvariantCulture),
            state,
            completion,
            writer =>
            {
                foreach (var match in page.Items)
                {
                    writer.WriteStartElement("match");
                    writer.WriteAttributeString("path", match.Path);
                    writer.WriteAttributeString("line", match.LineNumber.ToString(CultureInfo.InvariantCulture));

                    if (!contextRequested)
                    {
                        writer.WriteString($"{match.LineNumber.ToString(CultureInfo.InvariantCulture)}\t{match.Text}");
                    }
                    else
                    {
                        foreach (var context in match.ContextLines.OrderBy(item => item.LineNumber))
                        {
                            if (context.LineNumber < match.LineNumber)
                            {
                                writer.WriteStartElement("context");
                                writer.WriteAttributeString("line", context.LineNumber.ToString(CultureInfo.InvariantCulture));
                                writer.WriteString($"{context.LineNumber.ToString(CultureInfo.InvariantCulture)}\t{context.Text}");
                                writer.WriteEndElement();
                            }
                        }

                        writer.WriteStartElement("line");
                        writer.WriteString($"{match.LineNumber.ToString(CultureInfo.InvariantCulture)}\t{match.Text}");
                        writer.WriteEndElement();

                        foreach (var context in match.ContextLines.OrderBy(item => item.LineNumber))
                        {
                            if (context.LineNumber > match.LineNumber)
                            {
                                writer.WriteStartElement("context");
                                writer.WriteAttributeString("line", context.LineNumber.ToString(CultureInfo.InvariantCulture));
                                writer.WriteString($"{context.LineNumber.ToString(CultureInfo.InvariantCulture)}\t{context.Text}");
                                writer.WriteEndElement();
                            }
                        }
                    }

                    writer.WriteEndElement();
                }
            });
    }

    private static RipgrepSearchOptions CreateRipgrepOptions(
        string pattern,
        ResolvedGrepPath resolved,
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
        => new()
        {
            Pattern = pattern,
            WorkingDirectory = resolved.WorkingDirectory,
            SearchPaths = [resolved.SearchPath],
            IncludeGlobs = NormalizeGlobs(includeGlobs),
            ExcludeGlobs = NormalizeGlobs(excludeGlobs).Concat(GrepBuiltInExcludeGlobs).ToArray(),
            CaseMode = caseMode switch
            {
                GrepCaseMode.Sensitive => RipgrepCaseMode.Sensitive,
                GrepCaseMode.Insensitive => RipgrepCaseMode.Insensitive,
                _ => RipgrepCaseMode.Smart
            },
            FixedStrings = fixedStrings,
            WordRegexp = wordRegexp,
            Multiline = multiline,
            MultilineDotAll = multiline,
            IncludeHidden = includeHidden,
            RespectIgnoreFiles = respectIgnoreFiles,
            BeforeContext = outputMode == GrepOutputMode.Content
                ? NullIfZero(CalculateEffectiveBeforeContext(contextLines, beforeContext))
                : null,
            AfterContext = outputMode == GrepOutputMode.Content
                ? NullIfZero(CalculateEffectiveAfterContext(contextLines, afterContext))
                : null,
            MaxMatches = CalculateGrepMaxMatches(offset, limit),
            MaxMatchesPerFile = maxMatchesPerFile,
            MaxDepth = maxDepth,
            MaxColumns = MaxGrepColumns,
            Timeout = TimeSpan.FromMilliseconds(GrepTimeoutMilliseconds),
            StrictJsonParsing = false
        };

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

    private static ResolvedGrepPath ResolveGrepPath(string path)
    {
        var trimmed = path.Trim();
        var fullPath = Path.GetFullPath(trimmed, Directory.GetCurrentDirectory());
        if (File.Exists(fullPath))
        {
            return new ResolvedGrepPath(
                fullPath,
                Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory(),
                Path.GetFileName(fullPath));
        }

        return new ResolvedGrepPath(fullPath, fullPath, ".");
    }

    private static IReadOnlyList<string> NormalizeGlobs(string[]? globs)
        => globs?
            .Where(glob => !string.IsNullOrWhiteSpace(glob))
            .Select(glob => glob.Trim().Replace('\\', '/'))
            .ToArray() ?? [];

    private static int CalculateGrepMaxMatches(int offset, int limit)
    {
        var requested = (long)offset + limit;
        return requested > int.MaxValue ? int.MaxValue : (int)requested;
    }

    private static int CalculateEffectiveBeforeContext(int contextLines, int beforeContext)
        => beforeContext > 0 ? beforeContext : contextLines;

    private static int CalculateEffectiveAfterContext(int contextLines, int afterContext)
        => afterContext > 0 ? afterContext : contextLines;

    private static int? NullIfZero(int value)
        => value == 0 ? null : value;

    private static GrepPage<T> SlicePage<T>(IReadOnlyList<T> items, int offset, int limit)
    {
        var start = offset - 1;
        if (start >= items.Count)
            return new GrepPage<T>([], false);

        var requested = items.Skip(start).Take(limit + 1).ToArray();
        var hasMore = requested.Length > limit;
        return new GrepPage<T>(hasMore ? requested.Take(limit).ToArray() : requested, hasMore);
    }

    private static DateTime TryGetLastWriteTimeUtc(string workingDirectory, string relativePath)
    {
        try
        {
            var fullPath = Path.GetFullPath(relativePath, workingDirectory);
            return File.Exists(fullPath) ? File.GetLastWriteTimeUtc(fullPath) : DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
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
        RipgrepCompletionEvent completion,
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

        if (resultsRead == 0 && completion.Status is RipgrepCompletionStatus.NoMatches or RipgrepCompletionStatus.Success)
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

    private static string NormalizeResultPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized;
    }

    private static string? TryFormatGrepCompletionError(
        string path,
        RipgrepCompletionEvent completion,
        int resultsRead)
    {
        if (resultsRead > 0)
            return null;

        return completion.Status switch
        {
            RipgrepCompletionStatus.Failed => FormatGrepError(path, "Ripgrep search failed."),
            RipgrepCompletionStatus.TimedOut => FormatGrepError(path, "Ripgrep search timed out."),
            RipgrepCompletionStatus.Cancelled => FormatGrepError(path, "Ripgrep search cancelled."),
            _ => null
        };
    }

    private static GrepResultState CreateGrepResultState(
        bool pageHasMore,
        RipgrepCompletionEvent completion,
        bool lineTruncated = false)
    {
        var hasNextPage = pageHasMore ||
            completion.Truncated ||
            completion.TimedOut ||
            completion.Cancelled ||
            completion.Status == RipgrepCompletionStatus.Failed && completion.Partial;

        var reason = GetGrepTruncationReason(completion, lineTruncated, hasNextPage);
        return new GrepResultState(
            Truncated: hasNextPage || lineTruncated,
            TruncationReason: reason,
            HasNextPage: hasNextPage,
            NextReason: hasNextPage ? GetNextGrepReason(completion) : null);
    }

    private static string? GetGrepTruncationReason(
        RipgrepCompletionEvent completion,
        bool lineTruncated,
        bool hasNextPage)
    {
        if (completion.TimedOut || completion.Status == RipgrepCompletionStatus.TimedOut)
            return "timeout";
        if (completion.Cancelled || completion.Status == RipgrepCompletionStatus.Cancelled)
            return "cancelled";
        if (completion.Status == RipgrepCompletionStatus.Failed && completion.Partial)
            return "failed";
        if (hasNextPage)
            return "limit";
        return lineTruncated ? "line_length" : null;
    }

    private static string GetNextGrepReason(RipgrepCompletionEvent completion)
    {
        if (completion.TimedOut || completion.Status == RipgrepCompletionStatus.TimedOut)
            return "partial_results_timeout";
        if (completion.Cancelled || completion.Status == RipgrepCompletionStatus.Cancelled)
            return "partial_results_cancelled";
        if (completion.Status == RipgrepCompletionStatus.Failed && completion.Partial)
            return "partial_results_failed";
        return "more_matches_available";
    }

    private static string TrimLineEnding(string value)
        => value.TrimEnd('\r', '\n');

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

    private static string ToXmlValue(RipgrepCompletionStatus status)
        => status.ToString().ToLowerInvariant();

    private sealed record ResolvedGrepPath(string FullPath, string WorkingDirectory, string SearchPath);

    private sealed class GrepMatchInfo(string path, int lineNumber, string text)
    {
        public string Path { get; } = path;
        public int LineNumber { get; } = lineNumber;
        public string Text { get; } = text;
        public List<GrepContextLineInfo> ContextLines { get; } = [];
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
