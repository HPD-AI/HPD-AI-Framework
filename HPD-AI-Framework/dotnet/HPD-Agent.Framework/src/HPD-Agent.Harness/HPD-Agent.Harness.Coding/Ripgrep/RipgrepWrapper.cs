using System.Buffers;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Agent.ToolHarness.Coding.Ripgrep;

/// <summary>
/// Resolves the ripgrep binary used by HPD's ripgrep wrapper.
/// </summary>
public interface IRipgrepBinaryProvider
{
    /// <summary>
    /// Resolves an available ripgrep binary, or returns an unavailable result.
    /// </summary>
    ValueTask<RipgrepBinaryResolution> ResolveAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs ripgrep searches with typed options and typed events.
/// </summary>
public interface IRipgrepRunner
{
    /// <summary>
    /// Runs a ripgrep search and streams parsed ripgrep events followed by one completion event.
    /// </summary>
    IAsyncEnumerable<RipgrepEvent> SearchAsync(
        RipgrepSearchOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a ripgrep search that returns only files containing at least one match.
    /// </summary>
    Task<RipgrepFilesWithMatchesResult> ListFilesWithMatchesAsync(
        RipgrepSearchOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a ripgrep search that returns per-file match counts.
    /// </summary>
    Task<RipgrepCountResult> CountAsync(
        RipgrepSearchOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Parses one line of ripgrep JSON output.
/// </summary>
public interface IRipgrepJsonParser
{
    /// <summary>
    /// Attempts to parse a single UTF-8 encoded ripgrep JSON event line.
    /// </summary>
    bool TryParse(
        ReadOnlySpan<byte> utf8JsonLine,
        out RipgrepEvent? parsedEvent,
        out RipgrepJsonParseError? error);
}

/// <summary>
/// Options for the default ripgrep binary provider.
/// </summary>
public sealed record RipgrepBinaryProviderOptions
{
    /// <summary>
    /// An explicit absolute path to an rg binary.
    /// </summary>
    public string? ConfiguredPath { get; init; }

    /// <summary>
    /// Optional bundled binary manifest entries.
    /// </summary>
    public IReadOnlyList<RipgrepBundledBinaryManifest> BundledBinaries { get; init; } = [];

    /// <summary>
    /// Version policy applied during binary resolution.
    /// </summary>
    public RipgrepVersionPolicy VersionPolicy { get; init; } = RipgrepVersionPolicy.Any;

    /// <summary>
    /// Required version for exact version policy.
    /// </summary>
    public string? RequiredVersion { get; init; }

    /// <summary>
    /// Minimum version for minimum version policy.
    /// </summary>
    public string? MinimumVersion { get; init; }

    /// <summary>
    /// Whether to run rg --version when possible.
    /// </summary>
    public bool CaptureVersion { get; init; } = true;
}

/// <summary>
/// Manifest entry for a bundled ripgrep binary.
/// </summary>
public sealed record RipgrepBundledBinaryManifest
{
    public required string RuntimeIdentifier { get; init; }
    public required string RelativePath { get; init; }
    public required string Version { get; init; }
    public required string Sha256 { get; init; }
    public string? SourceUrl { get; init; }
}

/// <summary>
/// Resolved ripgrep binary metadata.
/// </summary>
public sealed record RipgrepBinaryResolution
{
    public required bool IsAvailable { get; init; }
    public string? Path { get; init; }
    public RipgrepBinarySource Source { get; init; }
    public string? DetectedVersion { get; init; }
    public string? ExpectedVersion { get; init; }
    public string? RuntimeIdentifier { get; init; }
    public string? Sha256 { get; init; }
    public bool VersionSatisfied { get; init; }
    public string? ReasonUnavailable { get; init; }
}

/// <summary>
/// Source used to resolve a ripgrep binary.
/// </summary>
public enum RipgrepBinarySource
{
    None,
    ConfiguredPath,
    BundledPath,
    SystemPath
}

/// <summary>
/// Version validation policy for resolved ripgrep binaries.
/// </summary>
public enum RipgrepVersionPolicy
{
    Any,
    Exact,
    Minimum
}

/// <summary>
/// Ripgrep search options.
/// </summary>
public sealed record RipgrepSearchOptions
{
    public required string Pattern { get; init; }
    public required string WorkingDirectory { get; init; }
    public IReadOnlyList<string> SearchPaths { get; init; } = ["."];
    public IReadOnlyList<string> IncludeGlobs { get; init; } = [];
    public IReadOnlyList<string> ExcludeGlobs { get; init; } = [];
    public RipgrepCaseMode CaseMode { get; init; } = RipgrepCaseMode.Smart;
    public bool FixedStrings { get; init; }
    public bool WordRegexp { get; init; }
    public bool Multiline { get; init; }
    public bool MultilineDotAll { get; init; }
    public bool IncludeHidden { get; init; }
    public bool RespectIgnoreFiles { get; init; } = true;
    public bool FollowSymlinks { get; init; }
    public int? BeforeContext { get; init; }
    public int? AfterContext { get; init; }
    public int? MaxMatches { get; init; }
    public int? MaxMatchesPerFile { get; init; }
    public int? MaxDepth { get; init; }
    public int? MaxColumns { get; init; }
    public long? MaxFileSizeBytes { get; init; }
    public int? Threads { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);
    public bool StrictJsonParsing { get; init; }
}

public sealed record RipgrepFilesWithMatchesResult
{
    public required IReadOnlyList<string> Files { get; init; }
    public required RipgrepCompletionEvent Completion { get; init; }
}

public sealed record RipgrepCountResult
{
    public required IReadOnlyList<RipgrepCountEntry> Counts { get; init; }
    public required RipgrepCompletionEvent Completion { get; init; }
}

public sealed record RipgrepCountEntry
{
    public required string Path { get; init; }
    public required int Count { get; init; }
}

public enum RipgrepCaseMode
{
    Sensitive,
    Insensitive,
    Smart
}

/// <summary>
/// Base type for ripgrep wrapper events.
/// </summary>
public abstract record RipgrepEvent;

public sealed record RipgrepBeginEvent : RipgrepEvent
{
    public required string Path { get; init; }
}

public sealed record RipgrepMatchEvent : RipgrepEvent
{
    public required string Path { get; init; }
    public required string Text { get; init; }
    public required int LineNumber { get; init; }
    public required long AbsoluteOffset { get; init; }
    public required IReadOnlyList<RipgrepSubmatch> Submatches { get; init; }
}

public sealed record RipgrepContextEvent : RipgrepEvent
{
    public required string Path { get; init; }
    public required string Text { get; init; }
    public required int LineNumber { get; init; }
    public required long AbsoluteOffset { get; init; }
}

public sealed record RipgrepEndEvent : RipgrepEvent
{
    public required string Path { get; init; }
    public long? BinaryOffset { get; init; }
    public RipgrepStats? Stats { get; init; }
}

public sealed record RipgrepSummaryEvent : RipgrepEvent
{
    public RipgrepStats? Stats { get; init; }
}

public sealed record RipgrepCompletionEvent : RipgrepEvent
{
    public required RipgrepCompletionStatus Status { get; init; }
    public required int? ExitCode { get; init; }
    public required bool Partial { get; init; }
    public required bool TimedOut { get; init; }
    public required bool Cancelled { get; init; }
    public required bool Truncated { get; init; }
    public required int MatchesEmitted { get; init; }
    public string? Stderr { get; init; }
    public string? Reason { get; init; }
}

public enum RipgrepCompletionStatus
{
    Success,
    NoMatches,
    Truncated,
    TimedOut,
    Cancelled,
    Failed
}

public sealed record RipgrepSubmatch
{
    public required string Text { get; init; }
    public required int Start { get; init; }
    public required int End { get; init; }
}

public sealed record RipgrepStats
{
    public long Searches { get; init; }
    public long SearchesWithMatch { get; init; }
    public long BytesSearched { get; init; }
    public long BytesPrinted { get; init; }
    public long MatchedLines { get; init; }
    public long Matches { get; init; }
    public TimeSpan? Elapsed { get; init; }
}

public sealed record RipgrepJsonParseError
{
    public required string Message { get; init; }
    public string? EventType { get; init; }
}

/// <summary>
/// Default network-free ripgrep binary provider.
/// </summary>
public sealed class DefaultRipgrepBinaryProvider : IRipgrepBinaryProvider
{
    private readonly RipgrepBinaryProviderOptions _options;
    private readonly string? _pathEnvironment;
    private readonly string _baseDirectory;

    public DefaultRipgrepBinaryProvider(RipgrepBinaryProviderOptions? options = null)
        : this(options, System.Environment.GetEnvironmentVariable("PATH"), AppContext.BaseDirectory)
    {
    }

    internal DefaultRipgrepBinaryProvider(
        RipgrepBinaryProviderOptions? options,
        string? pathEnvironment,
        string baseDirectory)
    {
        _options = options ?? new RipgrepBinaryProviderOptions
        {
            BundledBinaries = RipgrepBundledBinaries.Manifest
        };
        _pathEnvironment = pathEnvironment;
        _baseDirectory = baseDirectory;
    }

    public async ValueTask<RipgrepBinaryResolution> ResolveAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_options.ConfiguredPath))
        {
            var configured = _options.ConfiguredPath.Trim();
            if (!System.IO.Path.IsPathFullyQualified(configured))
                return Unavailable($"Configured ripgrep path must be absolute: {configured}");
            if (!File.Exists(configured))
                return Unavailable($"Configured ripgrep path does not exist: {configured}");

            var resolution = await CreateResolutionAsync(
                configured,
                RipgrepBinarySource.ConfiguredPath,
                expectedVersion: null,
                runtimeIdentifier: null,
                sha256: null,
                cancellationToken).ConfigureAwait(false);

            return resolution.IsAvailable ? resolution : resolution with { Source = RipgrepBinarySource.ConfiguredPath };
        }

        foreach (var candidate in EnumerateSystemPathCandidates(_pathEnvironment))
        {
            if (!File.Exists(candidate))
                continue;

            var resolution = await CreateResolutionAsync(
                candidate,
                RipgrepBinarySource.SystemPath,
                expectedVersion: null,
                runtimeIdentifier: null,
                sha256: null,
                cancellationToken).ConfigureAwait(false);

            if (resolution.IsAvailable)
                return resolution;
        }

        var runtimeIdentifier = GetCurrentRuntimeIdentifier();
        foreach (var manifest in _options.BundledBinaries)
        {
            if (!string.Equals(manifest.RuntimeIdentifier, runtimeIdentifier, StringComparison.OrdinalIgnoreCase))
                continue;

            var fullPath = System.IO.Path.GetFullPath(manifest.RelativePath, _baseDirectory);
            if (!File.Exists(fullPath))
                continue;

            var actualSha256 = ComputeSha256(fullPath);
            if (!string.Equals(actualSha256, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return Unavailable($"Bundled ripgrep SHA-256 mismatch for {fullPath}.");
            }

            var resolution = await CreateResolutionAsync(
                fullPath,
                RipgrepBinarySource.BundledPath,
                manifest.Version,
                manifest.RuntimeIdentifier,
                actualSha256,
                cancellationToken).ConfigureAwait(false);

            return resolution;
        }

        return Unavailable("No ripgrep binary was found.");
    }

    private async ValueTask<RipgrepBinaryResolution> CreateResolutionAsync(
        string path,
        RipgrepBinarySource source,
        string? expectedVersion,
        string? runtimeIdentifier,
        string? sha256,
        CancellationToken cancellationToken)
    {
        var detectedVersion = expectedVersion;
        var mustProbeVersion = _options.CaptureVersion ||
            _options.VersionPolicy is RipgrepVersionPolicy.Exact or RipgrepVersionPolicy.Minimum;

        if (mustProbeVersion)
        {
            var versionLine = await TryCaptureVersionLineAsync(path, cancellationToken).ConfigureAwait(false);
            var parsedVersion = versionLine == null ? null : TryParseRipgrepVersion(versionLine);
            detectedVersion = parsedVersion ?? expectedVersion;

            if (detectedVersion == null &&
                _options.VersionPolicy is RipgrepVersionPolicy.Exact or RipgrepVersionPolicy.Minimum)
            {
                return Unavailable($"Could not determine ripgrep version for {path}.") with
                {
                    Source = source,
                    Path = path,
                    ExpectedVersion = GetExpectedPolicyVersion(),
                    RuntimeIdentifier = runtimeIdentifier,
                    Sha256 = sha256
                };
            }
        }

        var versionSatisfied = IsVersionSatisfied(detectedVersion, expectedVersion, out var versionReason);
        if (!versionSatisfied)
        {
            return Unavailable(versionReason ?? "Ripgrep version did not satisfy policy.") with
            {
                Source = source,
                Path = path,
                DetectedVersion = detectedVersion,
                ExpectedVersion = GetExpectedPolicyVersion() ?? expectedVersion,
                RuntimeIdentifier = runtimeIdentifier,
                Sha256 = sha256
            };
        }

        return new RipgrepBinaryResolution
        {
            IsAvailable = true,
            Path = path,
            Source = source,
            DetectedVersion = detectedVersion,
            ExpectedVersion = GetExpectedPolicyVersion() ?? expectedVersion,
            RuntimeIdentifier = runtimeIdentifier,
            Sha256 = sha256,
            VersionSatisfied = true
        };
    }

    private bool IsVersionSatisfied(string? detectedVersion, string? expectedVersion, out string? reason)
    {
        reason = null;
        return _options.VersionPolicy switch
        {
            RipgrepVersionPolicy.Any => true,
            RipgrepVersionPolicy.Exact => IsExactVersionSatisfied(detectedVersion, expectedVersion, out reason),
            RipgrepVersionPolicy.Minimum => IsMinimumVersionSatisfied(detectedVersion, expectedVersion, out reason),
            _ => false
        };
    }

    private bool IsExactVersionSatisfied(string? detectedVersion, string? manifestVersion, out string? reason)
    {
        var required = _options.RequiredVersion;
        if (string.IsNullOrWhiteSpace(required))
        {
            reason = "RequiredVersion must be set when version policy is Exact.";
            return false;
        }

        var actual = detectedVersion ?? manifestVersion;
        if (string.Equals(actual, required, StringComparison.OrdinalIgnoreCase))
        {
            reason = null;
            return true;
        }

        reason = $"Ripgrep version {actual ?? "unknown"} does not match required version {required}.";
        return false;
    }

    private bool IsMinimumVersionSatisfied(string? detectedVersion, string? manifestVersion, out string? reason)
    {
        var minimum = _options.MinimumVersion;
        if (string.IsNullOrWhiteSpace(minimum))
        {
            reason = "MinimumVersion must be set when version policy is Minimum.";
            return false;
        }

        var actual = detectedVersion ?? manifestVersion;
        if (actual == null)
        {
            reason = $"Ripgrep version is unknown and cannot be compared to minimum version {minimum}.";
            return false;
        }

        if (!TryParseSemanticVersion(actual, out var actualVersion) ||
            !TryParseSemanticVersion(minimum, out var minimumVersion))
        {
            reason = $"Ripgrep version {actual} cannot be safely compared to minimum version {minimum}.";
            return false;
        }

        if (actualVersion.CompareTo(minimumVersion) >= 0)
        {
            reason = null;
            return true;
        }

        reason = $"Ripgrep version {actual} is lower than minimum version {minimum}.";
        return false;
    }

    private string? GetExpectedPolicyVersion()
        => _options.VersionPolicy switch
        {
            RipgrepVersionPolicy.Exact => _options.RequiredVersion,
            RipgrepVersionPolicy.Minimum => _options.MinimumVersion,
            _ => null
        };

    private static RipgrepBinaryResolution Unavailable(string reason)
        => new()
        {
            IsAvailable = false,
            Source = RipgrepBinarySource.None,
            ReasonUnavailable = reason,
            VersionSatisfied = false
        };

    private static async ValueTask<string?> TryCaptureVersionLineAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            process.StartInfo.ArgumentList.Add("--version");

            if (!process.Start())
                return null;

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
            var line = await process.StandardOutput.ReadLineAsync(linked.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            return line;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateSystemPathCandidates(string? pathEnvironment)
    {
        if (string.IsNullOrWhiteSpace(pathEnvironment))
            yield break;

        var executableNames = OperatingSystem.IsWindows()
            ? EnumerateWindowsExecutableNames()
            : ["rg"];

        foreach (var directory in pathEnvironment.Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!System.IO.Path.IsPathFullyQualified(directory))
                continue;

            foreach (var executableName in executableNames)
            {
                yield return System.IO.Path.Combine(directory, executableName);
            }
        }
    }

    private static IReadOnlyList<string> EnumerateWindowsExecutableNames()
    {
        var names = new List<string> { "rg.exe" };
        var pathExt = System.Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrWhiteSpace(pathExt))
            return names;

        foreach (var extension in pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (extension.Equals(".EXE", StringComparison.OrdinalIgnoreCase))
                continue;

            names.Add("rg" + extension.ToLowerInvariant());
        }

        return names;
    }

    private static string ComputeSha256(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GetCurrentRuntimeIdentifier()
    {
        var os = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsMacOS()
                ? "osx"
                : "linux";

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
        };

        return $"{os}-{arch}";
    }

    internal static string? TryParseRipgrepVersion(string versionLine)
    {
        const string prefix = "ripgrep ";
        if (!versionLine.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var remainder = versionLine[prefix.Length..].Trim();
        var end = remainder.IndexOfAny([' ', '(']);
        return end < 0 ? remainder : remainder[..end];
    }

    private static bool TryParseSemanticVersion(string value, out SemanticVersion version)
    {
        version = default;
        var parts = value.Split('.');
        if (parts.Length is < 1 or > 4)
            return false;

        Span<int> numbers = stackalloc int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return false;
        }

        version = new SemanticVersion(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    private readonly record struct SemanticVersion(int Major, int Minor, int Patch, int Revision)
        : IComparable<SemanticVersion>
    {
        public int CompareTo(SemanticVersion other)
        {
            var major = Major.CompareTo(other.Major);
            if (major != 0) return major;
            var minor = Minor.CompareTo(other.Minor);
            if (minor != 0) return minor;
            var patch = Patch.CompareTo(other.Patch);
            if (patch != 0) return patch;
            return Revision.CompareTo(other.Revision);
        }
    }
}

/// <summary>
/// AOT-safe parser for ripgrep JSON lines.
/// </summary>
public sealed class RipgrepJsonParser : IRipgrepJsonParser
{
    public bool TryParse(
        ReadOnlySpan<byte> utf8JsonLine,
        out RipgrepEvent? parsedEvent,
        out RipgrepJsonParseError? error)
    {
        parsedEvent = null;
        error = null;

        if (IsWhiteSpace(utf8JsonLine))
            return true;

        try
        {
            var reader = new Utf8JsonReader(utf8JsonLine);
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                error = new RipgrepJsonParseError { Message = "Ripgrep JSON event is missing a string type." };
                return false;
            }

            var type = typeElement.GetString();
            if (!root.TryGetProperty("data", out var data))
            {
                error = new RipgrepJsonParseError { Message = "Ripgrep JSON event is missing data.", EventType = type };
                return false;
            }

            parsedEvent = type switch
            {
                "begin" => ParseBegin(data),
                "match" => ParseMatch(data),
                "context" => ParseContext(data),
                "end" => ParseEnd(data),
                "summary" => ParseSummary(data),
                _ => null
            };

            return true;
        }
        catch (JsonException ex)
        {
            error = new RipgrepJsonParseError { Message = ex.Message };
            return false;
        }
        catch (InvalidOperationException ex)
        {
            error = new RipgrepJsonParseError { Message = ex.Message };
            return false;
        }
    }

    private static RipgrepBeginEvent ParseBegin(JsonElement data)
        => new() { Path = ReadPath(data) };

    private static bool IsWhiteSpace(ReadOnlySpan<byte> value)
    {
        foreach (var item in value)
        {
            if (item is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
                return false;
        }

        return true;
    }

    private static RipgrepMatchEvent ParseMatch(JsonElement data)
        => new()
        {
            Path = ReadPath(data),
            Text = ReadTextObject(data, "lines"),
            LineNumber = ReadInt32(data, "line_number"),
            AbsoluteOffset = ReadInt64(data, "absolute_offset"),
            Submatches = ReadSubmatches(data)
        };

    private static RipgrepContextEvent ParseContext(JsonElement data)
        => new()
        {
            Path = ReadPath(data),
            Text = ReadTextObject(data, "lines"),
            LineNumber = ReadInt32(data, "line_number"),
            AbsoluteOffset = ReadInt64(data, "absolute_offset")
        };

    private static RipgrepEndEvent ParseEnd(JsonElement data)
        => new()
        {
            Path = ReadPath(data),
            BinaryOffset = data.TryGetProperty("binary_offset", out var binaryOffset) &&
                binaryOffset.ValueKind == JsonValueKind.Number &&
                binaryOffset.TryGetInt64(out var value)
                    ? value
                    : null,
            Stats = data.TryGetProperty("stats", out var stats)
                ? ReadStats(stats)
                : null
        };

    private static RipgrepSummaryEvent ParseSummary(JsonElement data)
        => new()
        {
            Stats = data.TryGetProperty("stats", out var stats)
                ? ReadStats(stats)
                : ReadStats(data)
        };

    private static string ReadPath(JsonElement data)
        => ReadTextObject(data, "path");

    private static string ReadTextObject(JsonElement data, string propertyName)
    {
        if (!data.TryGetProperty(propertyName, out var value))
            throw new InvalidOperationException($"Ripgrep JSON data is missing {propertyName}.");

        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                return text.GetString() ?? string.Empty;
            if (value.TryGetProperty("bytes", out var bytes) && bytes.ValueKind == JsonValueKind.String)
                return DecodeBase64Text(bytes.GetString());
        }

        if (value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? string.Empty;

        throw new InvalidOperationException($"Ripgrep JSON {propertyName} value is not text.");
    }

    private static string DecodeBase64Text(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return string.Empty;

        var bytes = Convert.FromBase64String(encoded);
        return Encoding.UTF8.GetString(bytes);
    }

    private static int ReadInt32(JsonElement data, string propertyName)
    {
        if (!data.TryGetProperty(propertyName, out var value) || !value.TryGetInt32(out var number))
            throw new InvalidOperationException($"Ripgrep JSON data is missing numeric {propertyName}.");

        return number;
    }

    private static long ReadInt64(JsonElement data, string propertyName)
    {
        if (!data.TryGetProperty(propertyName, out var value) || !value.TryGetInt64(out var number))
            throw new InvalidOperationException($"Ripgrep JSON data is missing numeric {propertyName}.");

        return number;
    }

    private static IReadOnlyList<RipgrepSubmatch> ReadSubmatches(JsonElement data)
    {
        if (!data.TryGetProperty("submatches", out var submatches) ||
            submatches.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<RipgrepSubmatch>();
        foreach (var submatch in submatches.EnumerateArray())
        {
            result.Add(new RipgrepSubmatch
            {
                Text = ReadTextObject(submatch, "match"),
                Start = ReadInt32(submatch, "start"),
                End = ReadInt32(submatch, "end")
            });
        }

        return result;
    }

    private static RipgrepStats ReadStats(JsonElement stats)
        => new()
        {
            Searches = ReadOptionalInt64(stats, "searches"),
            SearchesWithMatch = ReadOptionalInt64(stats, "searches_with_match"),
            BytesSearched = ReadOptionalInt64(stats, "bytes_searched"),
            BytesPrinted = ReadOptionalInt64(stats, "bytes_printed"),
            MatchedLines = ReadOptionalInt64(stats, "matched_lines"),
            Matches = ReadOptionalInt64(stats, "matches"),
            Elapsed = ReadElapsed(stats)
        };

    private static long ReadOptionalInt64(JsonElement data, string propertyName)
        => data.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var number)
            ? number
            : 0;

    private static TimeSpan? ReadElapsed(JsonElement stats)
    {
        if (!stats.TryGetProperty("elapsed", out var elapsed))
            return null;

        if (elapsed.ValueKind == JsonValueKind.Object)
        {
            var seconds = ReadOptionalInt64(elapsed, "secs");
            var nanos = ReadOptionalInt64(elapsed, "nanos");
            return TimeSpan.FromSeconds(seconds) + TimeSpan.FromTicks(nanos / 100);
        }

        return null;
    }
}

/// <summary>
/// Default ripgrep runner.
/// </summary>
public sealed class RipgrepRunner : IRipgrepRunner
{
    private const int MaxStderrBytes = 64 * 1024;

    private readonly IRipgrepBinaryProvider _binaryProvider;
    private readonly IRipgrepJsonParser _parser;
    private readonly IRipgrepProcessExecutor _processExecutor;

    public RipgrepRunner(IRipgrepBinaryProvider? binaryProvider = null, IRipgrepJsonParser? parser = null)
        : this(
            binaryProvider ?? new DefaultRipgrepBinaryProvider(),
            parser ?? new RipgrepJsonParser(),
            new RealRipgrepProcessExecutor())
    {
    }

    internal RipgrepRunner(
        IRipgrepBinaryProvider binaryProvider,
        IRipgrepJsonParser parser,
        IRipgrepProcessExecutor processExecutor)
    {
        _binaryProvider = binaryProvider;
        _parser = parser;
        _processExecutor = processExecutor;
    }

    public async IAsyncEnumerable<RipgrepEvent> SearchAsync(
        RipgrepSearchOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);

        var binary = await _binaryProvider.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (!binary.IsAvailable || string.IsNullOrWhiteSpace(binary.Path))
            throw new InvalidOperationException(binary.ReasonUnavailable ?? "Ripgrep binary is unavailable.");

        var arguments = BuildArguments(options);
        var request = new RipgrepProcessRequest(
            binary.Path,
            arguments,
            options.WorkingDirectory,
            options.Timeout,
            MaxStderrBytes);

        var matchesEmitted = 0;
        var sawOutput = false;

        await using var enumerator = _processExecutor
            .ExecuteAsync(request, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            RipgrepProcessEvent? processEvent = null;
            RipgrepCompletionEvent? completionFromEnumerator = null;
            try
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    completionFromEnumerator = CreateCompletion(
                        RipgrepCompletionStatus.Failed,
                        exitCode: null,
                        partial: sawOutput,
                        timedOut: false,
                        cancelled: false,
                        truncated: false,
                        matchesEmitted,
                        stderr: null,
                        reason: "process_ended_without_completion");
                }
                else
                {
                    processEvent = enumerator.Current;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                completionFromEnumerator = CreateCompletion(
                    RipgrepCompletionStatus.Cancelled,
                    exitCode: null,
                    partial: sawOutput,
                    timedOut: false,
                    cancelled: true,
                    truncated: false,
                    matchesEmitted,
                    stderr: null,
                    reason: "cancelled");
            }

            if (completionFromEnumerator != null)
            {
                yield return completionFromEnumerator;
                yield break;
            }

            if (processEvent == null)
                yield break;

            if (processEvent is RipgrepStdoutLine stdout)
            {
                sawOutput = true;
                var bytes = Encoding.UTF8.GetBytes(stdout.Line);
                if (!_parser.TryParse(bytes, out var parsedEvent, out var parseError))
                {
                    if (options.StrictJsonParsing)
                    {
                        yield return CreateCompletion(
                            RipgrepCompletionStatus.Failed,
                            exitCode: null,
                            partial: true,
                            timedOut: false,
                            cancelled: false,
                            truncated: false,
                            matchesEmitted,
                            stderr: null,
                            reason: parseError?.Message ?? "invalid_json");
                        yield break;
                    }

                    continue;
                }

                if (parsedEvent == null)
                    continue;

                yield return parsedEvent;

                if (parsedEvent is RipgrepMatchEvent)
                {
                    matchesEmitted++;
                    if (options.MaxMatches is { } maxMatches && matchesEmitted >= maxMatches)
                    {
                        yield return CreateCompletion(
                            RipgrepCompletionStatus.Truncated,
                            exitCode: null,
                            partial: true,
                            timedOut: false,
                            cancelled: false,
                            truncated: true,
                            matchesEmitted,
                            stderr: null,
                            reason: "max_matches_reached");
                        yield break;
                    }
                }

                continue;
            }

            if (processEvent is RipgrepProcessCompleted completed)
            {
                var status = GetCompletionStatus(completed);
                var partial = completed.TimedOut ||
                    completed.Cancelled ||
                    status == RipgrepCompletionStatus.Failed && sawOutput;

                yield return CreateCompletion(
                    status,
                    completed.ExitCode,
                    partial,
                    completed.TimedOut,
                    completed.Cancelled,
                    truncated: false,
                    matchesEmitted,
                    completed.Stderr,
                    completed.Reason);
                yield break;
            }
        }
    }

    internal static IReadOnlyList<string> BuildArguments(RipgrepSearchOptions options)
        => BuildArguments(options, RipgrepOutputMode.Json);

    internal static IReadOnlyList<string> BuildFilesWithMatchesArguments(RipgrepSearchOptions options)
        => BuildArguments(options, RipgrepOutputMode.FilesWithMatches);

    internal static IReadOnlyList<string> BuildCountArguments(RipgrepSearchOptions options)
        => BuildArguments(options, RipgrepOutputMode.Count);

    public async Task<RipgrepFilesWithMatchesResult> ListFilesWithMatchesAsync(
        RipgrepSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        var lines = await RunLineOutputModeAsync(options, RipgrepOutputMode.FilesWithMatches, cancellationToken)
            .ConfigureAwait(false);

        return new RipgrepFilesWithMatchesResult
        {
            Files = lines.Lines,
            Completion = lines.Completion
        };
    }

    public async Task<RipgrepCountResult> CountAsync(
        RipgrepSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        var lines = await RunLineOutputModeAsync(options, RipgrepOutputMode.Count, cancellationToken)
            .ConfigureAwait(false);

        var counts = new List<RipgrepCountEntry>(lines.Lines.Count);
        foreach (var line in lines.Lines)
        {
            var separator = line.LastIndexOf(':');
            if (separator <= 0 ||
                separator == line.Length - 1 ||
                !int.TryParse(line.AsSpan(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
            {
                return new RipgrepCountResult
                {
                    Counts = counts,
                    Completion = CreateCompletion(
                        RipgrepCompletionStatus.Failed,
                        exitCode: null,
                        partial: counts.Count > 0,
                        timedOut: false,
                        cancelled: false,
                        truncated: false,
                        matchesEmitted: counts.Count,
                        stderr: null,
                        reason: "invalid_count_output")
                };
            }

            counts.Add(new RipgrepCountEntry
            {
                Path = line[..separator],
                Count = count
            });
        }

        return new RipgrepCountResult
        {
            Counts = counts,
            Completion = lines.Completion with { MatchesEmitted = counts.Count }
        };
    }

    private async Task<RipgrepLineOutputResult> RunLineOutputModeAsync(
        RipgrepSearchOptions options,
        RipgrepOutputMode mode,
        CancellationToken cancellationToken)
    {
        ValidateOptions(options);

        var binary = await _binaryProvider.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (!binary.IsAvailable || string.IsNullOrWhiteSpace(binary.Path))
            throw new InvalidOperationException(binary.ReasonUnavailable ?? "Ripgrep binary is unavailable.");

        var arguments = BuildArguments(options, mode);
        var request = new RipgrepProcessRequest(
            binary.Path,
            arguments,
            options.WorkingDirectory,
            options.Timeout,
            MaxStderrBytes);

        var lines = new List<string>();
        var truncated = false;
        var maxLines = options.MaxMatches;

        await using var enumerator = _processExecutor
            .ExecuteAsync(request, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            RipgrepProcessEvent? processEvent = null;
            RipgrepCompletionEvent? completionFromEnumerator = null;
            try
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    completionFromEnumerator = CreateCompletion(
                        RipgrepCompletionStatus.Failed,
                        exitCode: null,
                        partial: lines.Count > 0,
                        timedOut: false,
                        cancelled: false,
                        truncated,
                        lines.Count,
                        stderr: null,
                        reason: "process_ended_without_completion");
                }
                else
                {
                    processEvent = enumerator.Current;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                completionFromEnumerator = CreateCompletion(
                    RipgrepCompletionStatus.Cancelled,
                    exitCode: null,
                    partial: lines.Count > 0,
                    timedOut: false,
                    cancelled: true,
                    truncated,
                    lines.Count,
                    stderr: null,
                    reason: "cancelled");
            }

            if (completionFromEnumerator != null)
                return new RipgrepLineOutputResult(lines, completionFromEnumerator);

            if (processEvent == null)
            {
                return new RipgrepLineOutputResult(
                    lines,
                    CreateCompletion(
                        RipgrepCompletionStatus.Failed,
                        exitCode: null,
                        partial: lines.Count > 0,
                        timedOut: false,
                        cancelled: false,
                        truncated,
                        lines.Count,
                        stderr: null,
                        reason: "missing_process_event"));
            }

            if (processEvent is RipgrepStdoutLine stdout)
            {
                if (!string.IsNullOrEmpty(stdout.Line))
                    lines.Add(stdout.Line);

                if (maxLines is { } max && lines.Count >= max)
                {
                    truncated = true;
                    return new RipgrepLineOutputResult(
                        lines,
                        CreateCompletion(
                            RipgrepCompletionStatus.Truncated,
                            exitCode: null,
                            partial: true,
                            timedOut: false,
                            cancelled: false,
                            truncated: true,
                            lines.Count,
                            stderr: null,
                            reason: "max_matches_reached"));
                }

                continue;
            }

            if (processEvent is RipgrepProcessCompleted completed)
            {
                var status = GetCompletionStatus(completed);
                var partial = completed.TimedOut ||
                    completed.Cancelled ||
                    status == RipgrepCompletionStatus.Failed && lines.Count > 0;

                return new RipgrepLineOutputResult(
                    lines,
                    CreateCompletion(
                        status,
                        completed.ExitCode,
                        partial,
                        completed.TimedOut,
                        completed.Cancelled,
                        truncated,
                        lines.Count,
                        completed.Stderr,
                        completed.Reason));
            }
        }
    }

    private static IReadOnlyList<string> BuildArguments(RipgrepSearchOptions options, RipgrepOutputMode outputMode)
    {
        var args = new List<string>
        {
            "--no-config"
        };

        switch (outputMode)
        {
            case RipgrepOutputMode.Json:
                args.Add("--json");
                break;
            case RipgrepOutputMode.FilesWithMatches:
                args.Add("--files-with-matches");
                break;
            case RipgrepOutputMode.Count:
                args.Add("--count");
                args.Add("--with-filename");
                break;
        }

        args.Add("--no-messages");

        switch (options.CaseMode)
        {
            case RipgrepCaseMode.Insensitive:
                args.Add("--ignore-case");
                break;
            case RipgrepCaseMode.Smart:
                args.Add("--smart-case");
                break;
        }

        AddFlag(args, options.FixedStrings, "--fixed-strings");
        AddFlag(args, options.WordRegexp, "--word-regexp");
        AddFlag(args, options.Multiline, "--multiline");
        AddFlag(args, options.MultilineDotAll, "--multiline-dotall");
        AddFlag(args, options.IncludeHidden, "--hidden");
        AddFlag(args, !options.RespectIgnoreFiles, "--no-ignore");
        AddFlag(args, options.FollowSymlinks, "--follow");

        foreach (var glob in options.IncludeGlobs)
        {
            args.Add("--glob");
            args.Add(glob);
        }

        foreach (var glob in options.ExcludeGlobs)
        {
            args.Add("--glob");
            args.Add("!" + glob);
        }

        AddOption(args, "--before-context", options.BeforeContext);
        AddOption(args, "--after-context", options.AfterContext);
        AddOption(args, "--max-count", options.MaxMatchesPerFile);
        AddOption(args, "--max-depth", options.MaxDepth);
        AddOption(args, "--max-columns", options.MaxColumns);
        AddOption(args, "--max-filesize", options.MaxFileSizeBytes);
        AddOption(args, "--threads", options.Threads);

        args.Add("--regexp");
        args.Add(options.Pattern);
        args.Add("--");
        args.AddRange(options.SearchPaths);
        return args;
    }

    private static void ValidateOptions(RipgrepSearchOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Pattern))
            throw new ArgumentException("Pattern is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.WorkingDirectory))
            throw new ArgumentException("WorkingDirectory is required.", nameof(options));
        if (!Directory.Exists(options.WorkingDirectory))
            throw new DirectoryNotFoundException($"WorkingDirectory does not exist: {options.WorkingDirectory}");
        if (!Enum.IsDefined(options.CaseMode))
            throw new ArgumentException("CaseMode must be valid.", nameof(options));
        if (options.SearchPaths.Count == 0)
            throw new ArgumentException("At least one search path is required.", nameof(options));
        if (options.Timeout <= TimeSpan.Zero)
            throw new ArgumentException("Timeout must be greater than zero.", nameof(options));
        if (options.MaxMatches is <= 0)
            throw new ArgumentException("MaxMatches must be greater than zero.", nameof(options));
        if (options.MaxMatchesPerFile is <= 0)
            throw new ArgumentException("MaxMatchesPerFile must be greater than zero.", nameof(options));
        if (options.MaxDepth is < 0)
            throw new ArgumentException("MaxDepth must be greater than or equal to zero.", nameof(options));
        if (options.MaxColumns is <= 0)
            throw new ArgumentException("MaxColumns must be greater than zero.", nameof(options));
        if (options.MaxFileSizeBytes is <= 0)
            throw new ArgumentException("MaxFileSizeBytes must be greater than zero.", nameof(options));
        if (options.Threads is <= 0)
            throw new ArgumentException("Threads must be greater than zero.", nameof(options));
    }

    private static RipgrepCompletionStatus GetCompletionStatus(RipgrepProcessCompleted completed)
    {
        if (completed.Cancelled)
            return RipgrepCompletionStatus.Cancelled;
        if (completed.TimedOut)
            return RipgrepCompletionStatus.TimedOut;
        return completed.ExitCode switch
        {
            0 => RipgrepCompletionStatus.Success,
            1 => RipgrepCompletionStatus.NoMatches,
            _ => RipgrepCompletionStatus.Failed
        };
    }

    private static RipgrepCompletionEvent CreateCompletion(
        RipgrepCompletionStatus status,
        int? exitCode,
        bool partial,
        bool timedOut,
        bool cancelled,
        bool truncated,
        int matchesEmitted,
        string? stderr,
        string? reason)
        => new()
        {
            Status = status,
            ExitCode = exitCode,
            Partial = partial,
            TimedOut = timedOut,
            Cancelled = cancelled,
            Truncated = truncated,
            MatchesEmitted = matchesEmitted,
            Stderr = stderr,
            Reason = reason
        };

    private static void AddFlag(List<string> args, bool value, string flag)
    {
        if (value)
            args.Add(flag);
    }

    private static void AddOption<T>(List<string> args, string flag, T? value)
        where T : struct, IFormattable
    {
        if (value is not { } actualValue)
            return;

        args.Add(flag);
        args.Add(actualValue.ToString(null, CultureInfo.InvariantCulture));
    }
}

internal enum RipgrepOutputMode
{
    Json,
    FilesWithMatches,
    Count
}

internal sealed record RipgrepLineOutputResult(
    IReadOnlyList<string> Lines,
    RipgrepCompletionEvent Completion);

internal sealed record RipgrepProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    TimeSpan Timeout,
    int MaxStderrBytes);

internal abstract record RipgrepProcessEvent;

internal sealed record RipgrepStdoutLine(string Line) : RipgrepProcessEvent;

internal sealed record RipgrepProcessCompleted(
    int? ExitCode,
    string? Stderr,
    bool TimedOut,
    bool Cancelled,
    string? Reason) : RipgrepProcessEvent;

internal interface IRipgrepProcessExecutor
{
    IAsyncEnumerable<RipgrepProcessEvent> ExecuteAsync(
        RipgrepProcessRequest request,
        CancellationToken cancellationToken);
}

internal sealed class RealRipgrepProcessExecutor : IRipgrepProcessExecutor
{
    public async IAsyncEnumerable<RipgrepProcessEvent> ExecuteAsync(
        RipgrepProcessRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(request.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        process.StartInfo.Environment.Remove("RIPGREP_CONFIG_PATH");

        foreach (var argument in request.Arguments)
            process.StartInfo.ArgumentList.Add(argument);

        bool started;
        string? startFailure = null;
        try
        {
            started = process.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            started = false;
            startFailure = ex.Message;
        }

        if (!started)
        {
            yield return new RipgrepProcessCompleted(null, startFailure, false, false, "process_start_failed");
            yield break;
        }

        var stderrTask = ReadBoundedStderrAsync(process.StandardError, request.MaxStderrBytes, linked.Token);

        RipgrepProcessCompleted? completion = null;

        try
        {
            while (true)
            {
                string? line;
                try
                {
                    line = await process.StandardOutput.ReadLineAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (line == null)
                    break;

                yield return new RipgrepStdoutLine(line);
            }

            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Completion is handled after cancellation state is inspected.
            }

            var stderr = await CompleteStderrAsync(stderrTask).ConfigureAwait(false);
            var timedOut = timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            var cancelled = cancellationToken.IsCancellationRequested;
            if (timedOut || cancelled)
                TryKill(process);

            completion = new RipgrepProcessCompleted(
                process.HasExited ? process.ExitCode : null,
                stderr,
                timedOut,
                cancelled,
                timedOut ? "timeout" : cancelled ? "cancelled" : null);
        }
        finally
        {
            if (!process.HasExited)
                TryKill(process);
        }

        yield return completion ?? new RipgrepProcessCompleted(null, null, false, false, "process_ended_without_completion");
    }

    private static async Task<string?> ReadBoundedStderrAsync(
        StreamReader stderr,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var bytes = 0;
        var buffer = ArrayPool<char>.Shared.Rent(4096);
        try
        {
            while (true)
            {
                var read = await stderr.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;

                var text = new string(buffer, 0, read);
                var textBytes = Encoding.UTF8.GetByteCount(text);
                if (bytes + textBytes <= maxBytes)
                {
                    builder.Append(text);
                    bytes += textBytes;
                }
                else
                {
                    var remaining = maxBytes - bytes;
                    foreach (var ch in text)
                    {
                        var charBytes = Encoding.UTF8.GetByteCount([ch]);
                        if (remaining - charBytes < 0)
                            break;

                        builder.Append(ch);
                        remaining -= charBytes;
                    }

                    builder.Append("[stderr truncated]");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Completion status carries cancellation/timeout.
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static async Task<string?> CompleteStderrAsync(Task<string?> stderrTask)
    {
        try
        {
            return await stderrTask.ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort process cleanup.
        }
    }
}
