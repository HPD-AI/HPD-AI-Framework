namespace HPD.Environment.Runtime.Workspaces;

using System.Globalization;
using System.Text;
using System.Text.Json;
using HPD.Environment.Contracts;

/// <summary>
/// Shared ripgrep protocol for Environment-owned workspace acceleration. The
/// Agent never invokes ripgrep directly; a provider executes these arguments
/// in the execution unit whose content it is exposing.
/// </summary>
internal static class WorkspaceRipgrep
{
    /// <summary>Builds deterministic JSON-output arguments for one search.</summary>
    internal static IReadOnlyList<string> BuildArguments(
        WorkspaceSearchRequest request,
        string searchPath)
    {
        var arguments = new List<string>
        {
            "--no-config",
            "--no-messages",
            "--color=never",
            "--threads=1",
            "--no-require-git",
        };
        switch (request.OutputMode)
        {
            case WorkspaceSearchOutputMode.Matches:
                arguments.Add("--json");
                break;
            case WorkspaceSearchOutputMode.FilesWithMatches:
                arguments.Add("--files-with-matches");
                break;
            case WorkspaceSearchOutputMode.Count:
                arguments.Add("--count");
                arguments.Add("--with-filename");
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request.OutputMode,
                    "The workspace search output mode is invalid.");
        }
        switch (request.CaseMode)
        {
            case WorkspaceSearchCaseMode.Sensitive:
                arguments.Add("--case-sensitive");
                break;
            case WorkspaceSearchCaseMode.Insensitive:
                arguments.Add("--ignore-case");
                break;
            case WorkspaceSearchCaseMode.Smart:
                arguments.Add("--smart-case");
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request.CaseMode,
                    "The workspace search case mode is invalid.");
        }

        AddFlag(arguments, request.FixedStrings, "--fixed-strings");
        AddFlag(arguments, request.WordRegexp, "--word-regexp");
        if (request.Multiline)
        {
            arguments.Add("--multiline");
            arguments.Add("--multiline-dotall");
        }
        AddFlag(arguments, request.IncludeHidden, "--hidden");
        AddFlag(
            arguments,
            !request.RespectIgnoreFiles,
            "--no-ignore");
        AddOption(
            arguments,
            "--before-context",
            (int?)request.BeforeContext);
        AddOption(
            arguments,
            "--after-context",
            (int?)request.AfterContext);
        AddOption(
            arguments,
            "--max-count",
            request.MaxMatchesPerFile);
        AddOption(arguments, "--max-depth", request.MaxDepth);
        AddOption(arguments, "--max-columns", request.MaxColumns);
        AddOption(
            arguments,
            "--max-filesize",
            request.MaxFileSizeBytes);

        if (request.GlobPattern is { Length: > 0 } glob)
        {
            arguments.Add("--glob");
            arguments.Add(glob);
        }
        foreach (string include in request.IncludeGlobs)
        {
            arguments.Add("--glob");
            arguments.Add(include);
        }
        foreach (string exclude in request.ExcludeGlobs)
        {
            arguments.Add("--glob");
            arguments.Add("!" + exclude);
        }
        if (!request.IncludeHidden)
        {
            arguments.Add("--glob");
            arguments.Add("!**/.*");
        }

        arguments.Add("--regexp");
        arguments.Add(request.Query);
        arguments.Add("--");
        arguments.Add(searchPath);
        return arguments;
    }

    /// <summary>
    /// Parses ripgrep JSON events into portable match and context records.
    /// Partial output is retained for timeout, cancellation, and failure.
    /// </summary>
    internal static WorkspaceRipgrepParseResult Parse(
        string standardOutput,
        WorkspaceSearchRequest request,
        int? exitCode,
        bool timedOut,
        bool cancelled,
        string? standardError,
        Func<string, WorkspacePath?> mapPath)
    {
        if (request.OutputMode != WorkspaceSearchOutputMode.Matches)
        {
            return ParseFileOutput(
                standardOutput,
                request,
                exitCode,
                timedOut,
                cancelled,
                standardError,
                mapPath);
        }

        var matches = new List<MutableWorkspaceMatch>();
        var pendingContext =
            new Dictionary<WorkspacePath, List<WorkspaceSearchContextLine>>();
        var lastMatch =
            new Dictionary<WorkspacePath, MutableWorkspaceMatch>();
        bool capReached = false;
        string? parseError = null;

        using var reader = new StringReader(standardOutput);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (!root.TryGetProperty("type", out JsonElement type))
                {
                    continue;
                }
                if (type.GetString() == "context")
                {
                    WorkspaceRipgrepLine? context =
                        ReadLine(root, mapPath);
                    if (context is null)
                    {
                        continue;
                    }
                    var contextLine = new WorkspaceSearchContextLine
                    {
                        Line = context.Line,
                        Text = TrimLineEnding(context.Text),
                    };
                    if (lastMatch.TryGetValue(
                            context.Path,
                            out MutableWorkspaceMatch? previous) &&
                        context.Line > previous.Line)
                    {
                        previous.ContextLines.Add(contextLine);
                    }
                    else
                    {
                        if (!pendingContext.TryGetValue(
                                context.Path,
                                out List<WorkspaceSearchContextLine>? pending))
                        {
                            pending = [];
                            pendingContext[context.Path] = pending;
                        }
                        pending.Add(contextLine);
                    }
                    continue;
                }
                if (type.GetString() != "match")
                {
                    continue;
                }

                WorkspaceRipgrepLine? match =
                    ReadLine(root, mapPath);
                if (match is null)
                {
                    continue;
                }
                if (matches.Count >= request.MaxResults)
                {
                    capReached = true;
                    break;
                }

                var result = new MutableWorkspaceMatch
                {
                    Path = match.Path,
                    Line = match.Line,
                    Column = match.Column,
                    LineText = TrimLineEnding(match.Text),
                };
                if (pendingContext.Remove(
                        match.Path,
                        out List<WorkspaceSearchContextLine>? before))
                {
                    result.ContextLines.AddRange(before);
                }
                matches.Add(result);
                lastMatch[match.Path] = result;
            }
            catch (JsonException exception)
            {
                parseError = exception.Message;
                break;
            }
        }

        WorkspaceSearchStatus status;
        if (cancelled)
        {
            status = WorkspaceSearchStatus.Cancelled;
        }
        else if (timedOut)
        {
            status = WorkspaceSearchStatus.TimedOut;
        }
        else if (parseError is not null)
        {
            status = WorkspaceSearchStatus.Failed;
        }
        else if (capReached)
        {
            status = WorkspaceSearchStatus.Truncated;
        }
        else if (exitCode == 0)
        {
            status = WorkspaceSearchStatus.Completed;
        }
        else if (exitCode == 1)
        {
            status = WorkspaceSearchStatus.NoMatches;
        }
        else
        {
            status = WorkspaceSearchStatus.Failed;
        }

        string? message = parseError ??
            (string.IsNullOrWhiteSpace(standardError)
                ? capReached
                    ? "result_limit"
                    : null
                : Bounded(standardError, 2_048));
        return new WorkspaceRipgrepParseResult(
            new WorkspaceSearchResultSet
            {
                Results = matches
                    .Select(match => new WorkspaceSearchResult
                    {
                        Path = match.Path,
                        Line = match.Line,
                        Column = match.Column,
                        LineText = match.LineText,
                        ContextLines = match.ContextLines,
                    })
                    .ToArray(),
                Status = status,
                Message = message,
            },
            parseError is null && !capReached &&
            !timedOut && !cancelled &&
            exitCode is 0 or 1);
    }

    private static WorkspaceRipgrepParseResult ParseFileOutput(
        string standardOutput,
        WorkspaceSearchRequest request,
        int? exitCode,
        bool timedOut,
        bool cancelled,
        string? standardError,
        Func<string, WorkspacePath?> mapPath)
    {
        var files = new List<WorkspacePath>();
        var counts = new List<WorkspaceSearchCount>();
        string? parseError = null;
        bool capReached = false;
        using var reader = new StringReader(standardOutput);
        while (reader.ReadLine() is { Length: > 0 } line)
        {
            if ((request.OutputMode == WorkspaceSearchOutputMode.FilesWithMatches
                    ? files.Count
                    : counts.Count) >= request.MaxResults)
            {
                capReached = true;
                break;
            }
            WorkspacePath? path;
            long count = 0;
            if (request.OutputMode ==
                WorkspaceSearchOutputMode.Count)
            {
                int separator = line.LastIndexOf(':');
                if (separator <= 0 ||
                    !long.TryParse(
                        line.AsSpan(separator + 1),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out count))
                {
                    parseError = "invalid_count_output";
                    break;
                }
                path = mapPath(line[..separator]);
            }
            else
            {
                path = mapPath(line);
            }
            if (path is null)
            {
                continue;
            }
            if (request.OutputMode ==
                WorkspaceSearchOutputMode.FilesWithMatches)
            {
                files.Add(path.Value);
            }
            else
            {
                counts.Add(new WorkspaceSearchCount
                {
                    Path = path.Value,
                    Count = count,
                });
            }
        }

        WorkspaceSearchStatus status = cancelled
            ? WorkspaceSearchStatus.Cancelled
            : timedOut
                ? WorkspaceSearchStatus.TimedOut
                : parseError is not null
                    ? WorkspaceSearchStatus.Failed
                    : capReached
                        ? WorkspaceSearchStatus.Truncated
                        : exitCode switch
                        {
                            0 => WorkspaceSearchStatus.Completed,
                            1 => WorkspaceSearchStatus.NoMatches,
                            _ => WorkspaceSearchStatus.Failed,
                        };
        return new WorkspaceRipgrepParseResult(
            new WorkspaceSearchResultSet
            {
                Files = files,
                Counts = counts,
                Status = status,
                Message = parseError ??
                    (string.IsNullOrWhiteSpace(standardError)
                        ? capReached ? "result_limit" : null
                        : Bounded(standardError, 2_048)),
            },
            parseError is null &&
            !capReached &&
            !timedOut &&
            !cancelled &&
            exitCode is 0 or 1);
    }

    private static WorkspaceRipgrepLine? ReadLine(
        JsonElement root,
        Func<string, WorkspacePath?> mapPath)
    {
        if (!root.TryGetProperty("data", out JsonElement data) ||
            !data.TryGetProperty("path", out JsonElement pathElement) ||
            !TryReadText(pathElement, out string? pathText) ||
            pathText is null ||
            mapPath(pathText) is not { } logical ||
            !data.TryGetProperty("lines", out JsonElement linesElement) ||
            !TryReadText(linesElement, out string? text) ||
            text is null ||
            !data.TryGetProperty(
                "line_number",
                out JsonElement lineNumberElement) ||
            !lineNumberElement.TryGetInt64(out long line))
        {
            return null;
        }

        long column = 1;
        if (data.TryGetProperty(
                "submatches",
                out JsonElement submatches) &&
            submatches.ValueKind == JsonValueKind.Array &&
            submatches.GetArrayLength() > 0 &&
            submatches[0].TryGetProperty(
                "start",
                out JsonElement startElement) &&
            startElement.TryGetInt32(out int byteStart) &&
            byteStart >= 0)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            int boundedStart = Math.Min(byteStart, bytes.Length);
            column = Encoding.UTF8
                .GetString(bytes, 0, boundedStart).Length + 1;
        }

        return new WorkspaceRipgrepLine(
            logical,
            line,
            column,
            text);
    }

    private static bool TryReadText(
        JsonElement container,
        out string? text)
    {
        text = null;
        if (container.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        if (container.TryGetProperty(
                "text",
                out JsonElement textElement) &&
            textElement.ValueKind == JsonValueKind.String)
        {
            text = textElement.GetString();
            return true;
        }
        if (container.TryGetProperty(
                "bytes",
                out JsonElement bytesElement) &&
            bytesElement.ValueKind == JsonValueKind.String)
        {
            try
            {
                text = Encoding.UTF8.GetString(
                    Convert.FromBase64String(
                        bytesElement.GetString() ?? string.Empty));
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
        return false;
    }

    private static string TrimLineEnding(string value) =>
        value.TrimEnd('\r', '\n');

    private static string Bounded(string value, int maxLength) =>
        value.Length <= maxLength
            ? value
            : value[..maxLength] + "[truncated]";

    private static void AddFlag(
        List<string> arguments,
        bool enabled,
        string flag)
    {
        if (enabled)
        {
            arguments.Add(flag);
        }
    }

    private static void AddOption<T>(
        List<string> arguments,
        string flag,
        T? value)
        where T : struct, IFormattable
    {
        if (value is not { } actual)
        {
            return;
        }
        arguments.Add(flag);
        arguments.Add(actual.ToString(
            null,
            CultureInfo.InvariantCulture));
    }

    private sealed record WorkspaceRipgrepLine(
        WorkspacePath Path,
        long Line,
        long Column,
        string Text);

    private sealed record MutableWorkspaceMatch
    {
        public required WorkspacePath Path { get; init; }
        public required long Line { get; init; }
        public required long Column { get; init; }
        public required string LineText { get; init; }
        public List<WorkspaceSearchContextLine> ContextLines { get; } = [];
    }
}

/// <summary>Portable results plus whether ripgrep completed normally.</summary>
internal sealed record WorkspaceRipgrepParseResult(
    WorkspaceSearchResultSet ResultSet,
    bool CompletedNormally);
