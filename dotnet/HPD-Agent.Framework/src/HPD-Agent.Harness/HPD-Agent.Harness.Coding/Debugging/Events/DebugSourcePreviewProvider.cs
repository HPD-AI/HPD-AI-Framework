using System.Security.Cryptography;
using System.Text;
using HPD.Agent.Security;
using HPD.Agent.Sandbox;
using HPD.Agent.ToolHarness.Coding;
using HPD.Environment.Contracts;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal sealed record DebugSourcePreviewRequest(
    RuntimeWorkspaceBinding Workspace,
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
/// Captures replay-safe source excerpts from the session workspace or content
/// explicitly returned by the debug adapter.
/// </summary>
internal sealed class DebugSourcePreviewProvider(
    DebugSourcePreviewOptions options) : IDebugSourcePreviewProvider
{
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
        RuntimeWorkspaceBinding workspace = request.Workspace;
        try
        {
            WorkspacePath logical =
                DebugWorkspaceAccess.Resolve(
                    workspace,
                    request.Path);
            WorkspaceTextSnapshot? snapshot =
                await workspace.Files.ReadTextAsync(
                    logical,
                    new WorkspaceTextReadOptions
                    {
                        StartLine = 1,
                        MaxLines = null,
                        MaxBytes =
                            _options.MaximumUtf8Bytes,
                    },
                    cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                return Unavailable(
                    logical.ToString(),
                    "source_unavailable");
            }
            return BuildPreview(
                logical.ToString(),
                request.Language,
                snapshot.Content,
                request.Lines,
                snapshot.Version.ContentDigest?.Value ??
                    snapshot.Version.BackendRevision?.ToString(
                        System.Globalization.CultureInfo
                            .InvariantCulture),
                snapshot.Truncated);
        }
        catch (Exception exception)
            when (exception is IOException or
                UnauthorizedAccessException or
                DecoderFallbackException or
                ArgumentException)
        {
            return Unavailable(
                "source",
                "source_unavailable");
        }
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
