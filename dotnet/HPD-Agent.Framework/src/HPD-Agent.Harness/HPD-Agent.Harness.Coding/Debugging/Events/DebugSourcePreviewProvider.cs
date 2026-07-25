using System.Security.Cryptography;
using System.Text;
using HPD.Agent.ToolHarness.Coding;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal sealed record DebugSourcePreviewRequest(
    AgentWorkspace Workspace,
    string Path,
    IReadOnlyList<long> Lines,
    string? Language = null,
    string? OwnedAdapterContent = null,
    string? SourceVersion = null);

internal interface IDebugSourcePreviewProvider
{
    ValueTask<DebugSourcePreview> CaptureAsync(
        DebugSourcePreviewRequest request,
        CancellationToken cancellationToken);
}

internal sealed record DebugSourcePreviewOptions
{
    public int ContextLines { get; init; } = 3;
    public int MaximumHunks { get; init; } = 8;
    public int MaximumLines { get; init; } = 80;
    public int MaximumUtf8Bytes { get; init; } = 32 * 1024;
}

/// <summary>
/// Captures replay-safe source excerpts only from registered live text sources
/// or files authorized by the invocation's multi-root workspace.
/// </summary>
internal sealed class DebugSourcePreviewProvider(
    IEnumerable<IReadFileTextSource> textSources,
    DebugSourcePreviewOptions options) : IDebugSourcePreviewProvider
{
    private readonly IReadOnlyList<IReadFileTextSource> _textSources =
        textSources?.ToArray() ?? [];
    private readonly DebugSourcePreviewOptions _options =
        options ?? throw new ArgumentNullException(nameof(options));

    public async ValueTask<DebugSourcePreview> CaptureAsync(
        DebugSourcePreviewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.OwnedAdapterContent is not null)
        {
            var bounded = Bound(request.OwnedAdapterContent);
            return BuildPreview(
                Path.GetFileName(request.Path),
                request.Language,
                bounded.Text,
                request.Lines,
                request.SourceVersion,
                bounded.Truncated);
        }
        string fullPath;
        try
        {
            fullPath = request.Workspace.ResolveWorkspacePath(request.Path);
        }
        catch (AgentWorkspaceException)
        {
            return Unavailable(SafeDisplayPath(request.Workspace, request.Path), "outside_workspace");
        }

        try
        {
            var snapshot = await ReadTrustedTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
                return Unavailable(DisplayPath(request.Workspace, fullPath), "source_unavailable");

            var preview = BuildPreview(
                DisplayPath(request.Workspace, fullPath),
                request.Language,
                snapshot.Value.Text,
                request.Lines,
                snapshot.Value.Version,
                snapshot.Value.Truncated);
            return preview;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            DecoderFallbackException)
        {
            return Unavailable(DisplayPath(request.Workspace, fullPath), "source_unavailable");
        }
    }

    private async ValueTask<(string Text, string? Version, bool Truncated)?> ReadTrustedTextAsync(
        string fullPath,
        CancellationToken cancellationToken)
    {
        foreach (var source in _textSources)
        {
            var result = await source.TryReadTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
            if (result is null) continue;
            using var sourceReader = result.Reader;
            var bounded = await ReadBoundedAsync(sourceReader, cancellationToken).ConfigureAwait(false);
            return (bounded.Text, result.Version, bounded.Truncated);
        }

        if (!File.Exists(fullPath)) return null;
        await using var stream = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        var fileText = await ReadBoundedAsync(reader, cancellationToken).ConfigureAwait(false);
        return (fileText.Text,
            File.GetLastWriteTimeUtc(fullPath).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            fileText.Truncated);
    }

    private async ValueTask<(string Text, bool Truncated)> ReadBoundedAsync(
        TextReader reader,
        CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var raw = new StringBuilder();
        while (raw.Length <= _options.MaximumUtf8Bytes)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            raw.Append(buffer, 0, count);
        }

        return Bound(raw.ToString());
    }

    private (string Text, bool Truncated) Bound(string value)
    {
        var builder = new StringBuilder(Math.Min(value.Length, _options.MaximumUtf8Bytes));
        var utf8Bytes = 0;
        var truncated = false;
        foreach (var rune in value.EnumerateRunes())
        {
            if (utf8Bytes + rune.Utf8SequenceLength > _options.MaximumUtf8Bytes)
            {
                truncated = true;
                break;
            }
            builder.Append(rune);
            utf8Bytes += rune.Utf8SequenceLength;
        }
        truncated |= value.Length > builder.Length;
        return (builder.ToString(), truncated);
    }

    private DebugSourcePreview BuildPreview(
        string displayPath,
        string? language,
        string text,
        IReadOnlyList<long> selectedLines,
        string? version,
        bool contentTruncated)
    {
        var allLines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var ranges = selectedLines
            .Where(line => line > 0)
            .Select(line => (
                Start: Math.Max(1, checked((int)Math.Min(int.MaxValue, line)) - _options.ContextLines),
                End: Math.Min(allLines.Length, checked((int)Math.Min(int.MaxValue, line)) + _options.ContextLines)))
            .OrderBy(range => range.Start)
            .ToArray();
        var merged = new List<(int Start, int End)>();
        foreach (var range in ranges)
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End + 1)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, range.End));
            else
                merged.Add(range);
        }

        var hunks = new List<DebugSourcePreviewHunk>();
        var retainedLines = 0;
        var truncated = false;
        foreach (var range in merged)
        {
            if (hunks.Count == _options.MaximumHunks || retainedLines == _options.MaximumLines)
            {
                truncated = true;
                break;
            }
            var count = Math.Min(range.End - range.Start + 1, _options.MaximumLines - retainedLines);
            hunks.Add(new DebugSourcePreviewHunk(
                range.Start,
                allLines.Skip(range.Start - 1).Take(count).ToArray()));
            retainedLines += count;
            truncated |= count < range.End - range.Start + 1;
        }

        var retainedText = string.Join('\n', hunks.SelectMany(hunk => hunk.Lines));
        return new DebugSourcePreview
        {
            DisplayPath = displayPath,
            Language = NormalizeLanguage(language) ?? LanguageFromPath(displayPath),
            ContentHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(retainedText))).ToLowerInvariant(),
            SourceVersion = version,
            Hunks = hunks,
            Truncated = truncated || contentTruncated
        };
    }

    private static string DisplayPath(AgentWorkspace workspace, string fullPath)
    {
        var owner = workspace.GetOwningRoot(fullPath);
        var relative = Path.GetRelativePath(owner.Path, fullPath);
        var ambiguous = workspace.Roots.Count(root =>
            File.Exists(Path.Combine(root.Path, relative))) > 1;
        return ambiguous ? $"@{owner.Id}/{relative}" : relative;
    }

    private static string SafeDisplayPath(AgentWorkspace workspace, string path)
        => Path.IsPathRooted(path) ? Path.GetFileName(path) : path;

    private static DebugSourcePreview Unavailable(string displayPath, string reason)
        => new()
        {
            DisplayPath = displayPath,
            Hunks = [],
            Truncated = false,
            UnavailableReason = reason
        };

    private static string? LanguageFromPath(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".fs" => "fsharp",
            ".vb" => "visualbasic",
            ".py" => "python",
            ".js" or ".mjs" or ".cjs" => "javascript",
            ".ts" or ".tsx" => "typescript",
            ".go" => "go",
            ".rs" => "rust",
            ".rb" => "ruby",
            ".java" => "java",
            ".c" or ".h" or ".cpp" or ".hpp" => "cpp",
            _ => null
        };

    private static string? NormalizeLanguage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.ToLowerInvariant();
        return normalized switch
        {
            "text/x-csharp" or "text/csharp" => "csharp",
            "text/x-python" or "text/python" => "python",
            "text/javascript" or "application/javascript" => "javascript",
            "text/typescript" or "application/typescript" => "typescript",
            "text/x-go" => "go",
            "text/x-rust" => "rust",
            _ when !normalized.Contains('/') => normalized,
            _ => null
        };
    }
}
