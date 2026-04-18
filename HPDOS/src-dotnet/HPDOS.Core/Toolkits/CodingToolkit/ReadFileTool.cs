using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using HPD.Agent;
using Ude;

/// <summary>
/// ReadFile implementation for CodingToolkit (partial class).
/// Pipeline: ResolvePath → PreValidate → Dedup Check → Read (with byte/line caps) → PostRead (cache update).
/// </summary>
public partial class CodingToolkit
{
    // ═══════════════════════════════════════════════════════════════════
    // CONSTANTS
    // ═══════════════════════════════════════════════════════════════════

    private const int MaxOutputBytes = 256 * 1024;  // 256KB total output cap
    private const int MaxLineLength = 2000;          // Per-line char cap
    private const string LineTruncationSuffix = "... (line truncated)";
    private const int ReadTimeoutMs = 10_000;         // 10s read deadline

    // ═══════════════════════════════════════════════════════════════════
    // READ SESSION CACHE - Deduplication across reads within a session
    // ═══════════════════════════════════════════════════════════════════

    private record ReadCacheEntry(long MtimeUtcTicks, int Offset, int Limit);

    private readonly ConcurrentDictionary<string, ReadCacheEntry> _readCache = new(StringComparer.OrdinalIgnoreCase);

    private const string FileUnchangedStub =
        "File unchanged since last read. The content from the earlier ReadFile result in this conversation is still current.";

    // ═══════════════════════════════════════════════════════════════════
    // PATH RESOLUTION - Normalize before validation
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves relative paths to absolute, normalizes separators.
    /// Runs before any validation or I/O.
    /// </summary>
    private static string ResolvePath(string filePath)
    {
        filePath = filePath.Trim();

        // Resolve relative paths against CWD (models frequently produce these)
        if (!Path.IsPathFullyQualified(filePath))
            filePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), filePath));

        return Path.GetFullPath(filePath);
    }

    // ═══════════════════════════════════════════════════════════════════
    // PRE-READ VALIDATION - All checks before any I/O
    // ═══════════════════════════════════════════════════════════════════

    private static readonly HashSet<string> BlockedDevicePaths = new(StringComparer.Ordinal)
    {
        "/dev/zero", "/dev/random", "/dev/urandom", "/dev/full",
        "/dev/stdin", "/dev/tty", "/dev/console",
        "/dev/stdout", "/dev/stderr",
        "/dev/fd/0", "/dev/fd/1", "/dev/fd/2"
    };

    /// <summary>
    /// Validates a resolved file path before reading. Returns null if OK, error message if blocked.
    /// Checks: device files, UNC paths, directory redirect, binary (extension + content sniff), existence + suggestions.
    /// </summary>
    private string? PreValidateRead(string filePath)
    {
        // 1. Device file blocking (Linux/macOS) — would hang the process
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (BlockedDevicePaths.Contains(filePath))
                return $"Error: Cannot read '{filePath}': this device file would block or produce infinite output.";

            // /proc/self/fd/0-2 and /proc/<pid>/fd/0-2 are Linux stdio aliases
            if (filePath.StartsWith("/proc/") &&
                (filePath.EndsWith("/fd/0") || filePath.EndsWith("/fd/1") || filePath.EndsWith("/fd/2")))
                return $"Error: Cannot read '{filePath}': this is a stdio device alias.";
        }

        // 2. UNC path security check (Windows) — reading triggers NTLM authentication
        if (filePath.StartsWith(@"\\") || filePath.StartsWith("//"))
            return $"Error: Cannot read UNC path '{filePath}': network paths may leak credentials.";

        // 3. Directory redirect — helpful message instead of generic error
        if (Directory.Exists(filePath))
            return $"Error: '{filePath}' is a directory, not a file. Use ListDirectory to view its contents.";

        // 4. File existence + suggestions
        if (!File.Exists(filePath))
        {
            var suggestion = SuggestSimilarFile(filePath);
            return suggestion != null
                ? $"Error: File not found: {filePath}. Did you mean: {suggestion}?"
                : $"Error: File not found: {filePath}";
        }

        // 5. Gitignore check — block secrets/build artifacts the model shouldn't see
        var cwd = Directory.GetCurrentDirectory();
        if (!FilterIgnoredFiles([filePath], cwd).Any())
            return $"Error: '{filePath}' is excluded by .gitignore. It may contain secrets or build artifacts.";

        // 6. File size pre-gate — prevent ReadAllLines from loading huge files into RAM
        const long MaxFileSizeBytes = 50L * 1024 * 1024; // 50MB
        var fileSize = new FileInfo(filePath).Length;
        if (fileSize > MaxFileSizeBytes)
            return $"Error: File is {fileSize / 1024 / 1024}MB — too large to load in full. Use offset/limit to read a specific range, or Grep to search within it.";

        // 7. Binary check — extension first (cheap), then content sniff (for extensionless or unknown files)
        var ext = Path.GetExtension(filePath);
        if (BinaryExtensions.Contains(ext))
            return $"Error: Cannot read binary file ({ext}). Use appropriate tool for binary files.";

        if (IsBinaryFile(filePath))
            return $"Error: File appears to be binary (null bytes or high non-printable ratio). Use appropriate tool for binary files.";

        return null; // All checks passed
    }

    // ═══════════════════════════════════════════════════════════════════
    // FILE SUGGESTION - Levenshtein-based "did you mean?" on ENOENT
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Finds the most similar filename in the same directory, or checks if the path exists under CWD.
    /// </summary>
    private static string? SuggestSimilarFile(string filePath)
    {
        // Try interpreting as relative to CWD
        var cwd = Directory.GetCurrentDirectory();
        var fileName = Path.GetFileName(filePath);
        var asRelative = Path.Combine(cwd, fileName);
        if (File.Exists(asRelative))
            return asRelative;

        // Look for similar filenames in the same directory
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return null;

        var targetName = Path.GetFileName(filePath);
        string? bestMatch = null;
        var bestDistance = int.MaxValue;
        const int maxDistance = 3; // Only suggest if within 3 edits

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            var candidateName = Path.GetFileName(file);
            var distance = LevenshteinDistance(targetName, candidateName);
            if (distance < bestDistance && distance <= maxDistance)
            {
                bestDistance = distance;
                bestMatch = file;
            }
        }

        return bestMatch;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a.Length;

        var costs = new int[b.Length + 1];
        for (var i = 0; i <= b.Length; i++) costs[i] = i;

        for (var i = 1; i <= a.Length; i++)
        {
            costs[0] = i;
            var prev = i - 1;
            for (var j = 1; j <= b.Length; j++)
            {
                var current = costs[j];
                costs[j] = Math.Min(
                    Math.Min(costs[j] + 1, costs[j - 1] + 1),
                    prev + (char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1));
                prev = current;
            }
        }

        return costs[b.Length];
    }

    // ═══════════════════════════════════════════════════════════════════
    // TIMEOUT-AWARE LINE READER — stream lines until deadline or EOF
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads lines from a file using a StreamReader, stopping at EOF or when the
    /// cancellation token fires (timeout). Returns the lines collected so far and
    /// whether the read was interrupted by the timeout.
    /// </summary>
    internal static (string[] Lines, bool TimedOut) ReadLinesWithTimeout(
        string filePath, Encoding encoding, CancellationToken ct)
    {
        var lines = new List<string>();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);

        while (!reader.EndOfStream)
        {
            if (ct.IsCancellationRequested)
                return (lines.ToArray(), true);

            // ReadLine is synchronous but bounded per-line; the CT check
            // between lines keeps worst-case latency ≈ one line's I/O time.
            var line = reader.ReadLine();
            if (line != null)
                lines.Add(line);
        }

        return (lines.ToArray(), false);
    }

    // ═══════════════════════════════════════════════════════════════════
    // READ FILE - Main entry point
    // ═══════════════════════════════════════════════════════════════════

    [AIFunction]
    [AIDescription("Read file contents with optional line offset and limit. Returns file content with line numbers. Automatically detects file encoding. Supports read deduplication — returns a stub if a file hasn't changed since last read.")]
    [RequiresPermission]
    public string ReadFile(
        [AIDescription("Absolute or relative path to the file to read.")] string filePath,
        [AIDescription("Line number to start reading from (1-based). Default: 1")] int offset = 1,
        [AIDescription("Maximum number of lines to read. Default: 2000 (0 = all lines)")] int limit = 2000)
    {
        // ── 1. Resolve path (relative → absolute, normalize) ──
        var fullPath = ResolvePath(filePath);

        // ── 2. Pre-validate (device, UNC, directory, binary, existence) ──
        var validationError = PreValidateRead(fullPath);
        if (validationError != null)
            return validationError;

        try
        {
            // ── 3. Dedup check: same file+range+mtime → return stub ──
            var currentMtime = new FileInfo(fullPath).LastWriteTimeUtc.Ticks;
            if (_readCache.TryGetValue(fullPath, out var cached) &&
                cached.Offset == offset &&
                cached.Limit == limit &&
                cached.MtimeUtcTicks == currentMtime)
            {
                return FileUnchangedStub;
            }

            // ── 4. Read with encoding detection + timeout ──
            var ext = Path.GetExtension(fullPath);
            var encoding = DetectEncoding(fullPath) ?? Encoding.UTF8;

            using var cts = new CancellationTokenSource(ReadTimeoutMs);
            var (lines, timedOut) = ReadLinesWithTimeout(fullPath, encoding, cts.Token);
            var totalLines = lines.Length;

            // ── Empty / whitespace-only early exit ──
            if (totalLines == 0)
                return $"(The file '{Path.GetFileName(fullPath)}' exists but is empty.)";

            // Whitespace scan is O(n) — skip it for large files (already pre-gated at 50MB,
            // but a 49MB file of spaces would still be slow). Sample the first 512 lines instead.
            const int WhitespaceSampleLines = 512;
            var sampleSize = Math.Min(totalLines, WhitespaceSampleLines);
            var sampleAllWhitespace = lines.Take(sampleSize).All(l => string.IsNullOrWhiteSpace(l));
            if (sampleAllWhitespace && (totalLines <= WhitespaceSampleLines || lines.All(l => string.IsNullOrWhiteSpace(l))))
                return $"(The file '{Path.GetFileName(fullPath)}' exists but contains only whitespace.)";

            if (offset < 1) offset = 1;
            if (offset > totalLines)
                return $"Error: Offset {offset} exceeds file length ({totalLines} lines).";

            var startIndex = offset - 1;
            var endIndex = limit > 0
                ? Math.Min(startIndex + limit, totalLines)
                : totalLines;

            // ── 5. Build output with byte cap + per-line truncation ──
            var sb = new StringBuilder();
            var mimeType = GetMimeType(ext);

            sb.AppendLine($"File: {Path.GetFileName(fullPath)}");
            sb.AppendLine($"Path: {fullPath}");
            sb.AppendLine($"Type: {mimeType}");
            sb.AppendLine("---");

            var accumulatedBytes = Encoding.UTF8.GetByteCount(sb.ToString());
            var byteCapped = false;
            var actualEnd = startIndex;

            for (var i = startIndex; i < endIndex; i++)
            {
                // Per-line truncation
                var line = lines[i].Length > MaxLineLength
                    ? string.Concat(lines[i].AsSpan(0, MaxLineLength), LineTruncationSuffix)
                    : lines[i];

                var formatted = $"{i + 1,4}│ {line}\n";
                var lineBytes = Encoding.UTF8.GetByteCount(formatted);

                // Byte cap check — break before appending if we'd exceed
                if (accumulatedBytes + lineBytes > MaxOutputBytes)
                {
                    byteCapped = true;
                    break;
                }

                sb.Append(formatted);
                accumulatedBytes += lineBytes;
                actualEnd = i + 1;
            }

            var linesEmitted = actualEnd - startIndex;

            // Insert line range into header (after "---")
            sb.Insert(
                sb.ToString().IndexOf("---") + 4,
                $"Lines: {offset}-{offset + linesEmitted - 1} of {totalLines}\n");

            // ── Truncation notices ──
            if (timedOut)
            {
                sb.AppendLine("---");
                sb.AppendLine($"READ TIMED OUT after {ReadTimeoutMs / 1000}s — returned {totalLines} lines collected before the deadline.");
                sb.AppendLine("The file may be on a slow filesystem. The partial content above is still valid.");
                if (actualEnd < totalLines)
                    sb.AppendLine($"To read more of the collected portion, use offset: {actualEnd + 1}");
            }
            else if (byteCapped)
            {
                sb.AppendLine("---");
                sb.AppendLine($"OUTPUT CAPPED: Hit {MaxOutputBytes / 1024}KB byte limit after {linesEmitted} lines.");
                sb.AppendLine($"To continue reading, use offset: {actualEnd + 1}");
            }
            else if (actualEnd < totalLines)
            {
                sb.AppendLine("---");
                sb.AppendLine($"TRUNCATED: Showing {linesEmitted} of {totalLines} lines.");
                sb.AppendLine($"To read more, use offset: {actualEnd + 1}");
            }

            // ── 6. Post-read: update session cache ──
            _readCache[fullPath] = new ReadCacheEntry(currentMtime, offset, limit);

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error reading file: {ex.Message}";
        }
    }
}
