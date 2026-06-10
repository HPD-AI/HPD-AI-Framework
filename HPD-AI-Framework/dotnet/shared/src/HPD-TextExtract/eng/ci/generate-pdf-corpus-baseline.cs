#:property TargetFramework=net10.0
#:property PublishAot=false
#:property PackAsTool=false
#:property IsPackable=false
#:property GenerateDocumentationFile=false
#:property Nullable=enable
#:property WarningLevel=0
#:project ../../HPD-TextExtract.csproj

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using HPD.TextExtract.Models;
using HPD.TextExtract.Pdf;

var options = BaselineOptions.Parse(args);
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = !options.WorkerMode,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};
if (options.WorkerMode)
{
    var entry = await ExtractOneAsync(options.WorkerPath!, options.WorkerRelativePath!, options.MaxPages, options.WorkerPassword);
    Console.WriteLine(JsonSerializer.Serialize(entry, jsonOptions));
    return;
}

var repoRoot = FindRepoRoot(Directory.GetCurrentDirectory());
var textExtractRoot = Path.Combine(repoRoot, "HPD-AI-Framework", "dotnet", "shared", "src", "HPD-TextExtract");
var artifactRoot = Path.Combine(textExtractRoot, ".tmp", "artifacts");
var corpusRoot = Path.GetFullPath(options.CorpusRoot ?? Path.Combine(artifactRoot, "pdf-corpus", "full-reference"));
var outputPath = Path.GetFullPath(options.OutputPath ?? Path.Combine(artifactRoot, "pdf-corpus", "pdf-corpus-baseline.json"));

Console.WriteLine("==========================================");
Console.WriteLine(" HPD TextExtraction PDF Corpus Baseline ");
Console.WriteLine("==========================================");
Console.WriteLine($"Corpus: {corpusRoot}");
Console.WriteLine($"Output: {outputPath}");
Console.WriteLine($"Max files: {options.MaxFiles}");
Console.WriteLine($"Max pages: {options.MaxPages}");
Console.WriteLine($"Timeout: {options.TimeoutSeconds}s");
Console.WriteLine();

if (!Directory.Exists(corpusRoot))
    throw new DirectoryNotFoundException($"PDF corpus root does not exist: {corpusRoot}");

var pdfs = Directory
    .EnumerateFiles(corpusRoot, "*.pdf", SearchOption.AllDirectories)
    .Order(StringComparer.Ordinal)
    .Take(options.MaxFiles)
    .ToArray();

if (pdfs.Length == 0)
    throw new InvalidOperationException($"No PDFs found under {corpusRoot}");

var entries = new List<PdfCorpusBaselineEntry>(pdfs.Length);
var processed = 0;
var failures = 0;

foreach (var path in pdfs)
{
    var relativePath = Path.GetRelativePath(corpusRoot, path).Replace('\\', '/');
    var password = KnownCorpusPasswords.GetPassword(relativePath);
    var entry = await ExtractOneInWorkerAsync(path, relativePath, options);
    entries.Add(entry);
    if (string.Equals(entry.Status, "ok", StringComparison.OrdinalIgnoreCase))
    {
        processed++;
        Console.WriteLine(password is null ? $"ok   {relativePath}" : $"ok   {relativePath} (password)");
    }
    else
    {
        failures++;
        Console.WriteLine($"fail {relativePath}: {entry.FailureType}: {entry.FailureMessage}");
    }
}

var manifest = new PdfCorpusBaselineManifest
{
    FormatVersion = 1,
    Engine = "HPD.TextExtract.Pdf.PdfExtractionEngine",
    Backend = "PDFium",
    MaxPages = options.MaxPages,
    Tolerance = PdfCorpusBaselineTolerance.Default,
    Entries = entries
};

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(manifest, jsonOptions));

Console.WriteLine();
Console.WriteLine($"Baseline complete: files={entries.Count}, processed={processed}, failures={failures}");
PrintFailureSummary(entries);

var unknownFailures = entries
    .Where(static entry => !string.Equals(entry.Status, "ok", StringComparison.OrdinalIgnoreCase))
    .Where(static entry => string.IsNullOrWhiteSpace(entry.FailureCategory) || string.IsNullOrWhiteSpace(entry.ReferenceExpectation))
    .ToArray();
if (unknownFailures.Length > 0)
{
    throw new InvalidOperationException(
        "PDF corpus baseline has unclassified failures. Add an explicit reference expectation before accepting them:"
        + Environment.NewLine
        + string.Join(Environment.NewLine, unknownFailures.Select(static entry => $"  {entry.Path}: {entry.FailureType}: {entry.FailureMessage}")));
}

static async Task<PdfCorpusBaselineEntry> ExtractOneAsync(string path, string relativePath, int maxPages, string? password)
{
    try
    {
        var engine = new PdfExtractionEngine();
        var result = await engine.ExtractAsync(ContentInput.FromPath(path, MimeTypes.Pdf), new PdfExtractionOptions
        {
            OcrEnabled = false,
            IncludeTextItems = true,
            IncludeEmbeddedImages = true,
            MaxPages = maxPages,
            Password = password
        });

        return PdfCorpusBaselineEntry.FromResult(relativePath, result, password is not null ? "password" : null);
    }
    catch (Exception error)
    {
        return PdfCorpusBaselineEntry.FromFailure(relativePath, error);
    }
}

static async Task<PdfCorpusBaselineEntry> ExtractOneInWorkerAsync(string path, string relativePath, BaselineOptions options)
{
    var processPath = Environment.ProcessPath
        ?? throw new InvalidOperationException("Cannot locate current process path for corpus worker.");
    var startInfo = new ProcessStartInfo(processPath)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    startInfo.ArgumentList.Add("--worker-file");
    startInfo.ArgumentList.Add(path);
    startInfo.ArgumentList.Add("--worker-relative-path");
    startInfo.ArgumentList.Add(relativePath);
    startInfo.ArgumentList.Add("--max-pages");
    startInfo.ArgumentList.Add(options.MaxPages.ToString(System.Globalization.CultureInfo.InvariantCulture));
    var password = KnownCorpusPasswords.GetPassword(relativePath);
    if (password is not null)
    {
        startInfo.ArgumentList.Add("--worker-password");
        startInfo.ArgumentList.Add(password);
    }

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Failed to start PDF corpus worker for {relativePath}.");
    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
    try
    {
        await process.WaitForExitAsync(timeout.Token);
    }
    catch (OperationCanceledException)
    {
        TryKill(process);
        return PdfCorpusBaselineEntry.FromFailure(relativePath, "TimeoutException", $"Extraction exceeded {options.TimeoutSeconds} seconds.");
    }

    var output = await outputTask;
    var error = await errorTask;
    if (process.ExitCode != 0)
    {
        return PdfCorpusBaselineEntry.FromFailure(relativePath, "WorkerExitException", $"Worker exited {process.ExitCode}: {TrimForManifest(error)}");
    }

    try
    {
        var entry = JsonSerializer.Deserialize<PdfCorpusBaselineEntry>(output, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return entry ?? PdfCorpusBaselineEntry.FromFailure(relativePath, "WorkerOutputException", "Worker returned empty JSON.");
    }
    catch (JsonException jsonError)
    {
        return PdfCorpusBaselineEntry.FromFailure(relativePath, jsonError.GetType().Name, $"Could not parse worker output: {TrimForManifest(output)}");
    }
}

static void TryKill(Process process)
{
    try
    {
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);
    }
    catch (InvalidOperationException)
    {
    }
}

static string TrimForManifest(string value)
{
    value = value.Trim();
    return value.Length <= 500 ? value : value[..500];
}

static void PrintFailureSummary(IReadOnlyList<PdfCorpusBaselineEntry> entries)
{
    var failedEntries = entries
        .Where(static entry => !string.Equals(entry.Status, "ok", StringComparison.OrdinalIgnoreCase))
        .ToArray();
    if (failedEntries.Length == 0)
        return;

    Console.WriteLine();
    Console.WriteLine("Failure categories:");
    foreach (var group in failedEntries
        .GroupBy(static entry => (Category: entry.FailureCategory ?? "Unclassified", Type: entry.FailureType ?? "Unknown"))
        .OrderBy(static group => group.Key.Category, StringComparer.Ordinal)
        .ThenBy(static group => group.Key.Type, StringComparer.Ordinal))
    {
        Console.WriteLine($"  {group.Key.Category}/{group.Key.Type}: {group.Count()}");
    }
}

static string FindRepoRoot(string startDirectory)
{
    var current = new DirectoryInfo(startDirectory);
    while (current is not null)
    {
        if (Directory.Exists(Path.Combine(current.FullName, ".git")) &&
            Directory.Exists(Path.Combine(current.FullName, "HPD-AI-Framework")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new InvalidOperationException($"Could not find repository root from {startDirectory}.");
}

sealed record BaselineOptions(
    string? CorpusRoot,
    string? OutputPath,
    int MaxFiles,
    int MaxPages,
    int TimeoutSeconds,
    string? WorkerPath,
    string? WorkerRelativePath,
    string? WorkerPassword)
{
    public bool WorkerMode => WorkerPath is not null;

    public static BaselineOptions Parse(string[] args)
    {
        string? corpusRoot = null;
        string? outputPath = null;
        string? workerPath = null;
        string? workerRelativePath = null;
        string? workerPassword = null;
        var maxFiles = int.MaxValue;
        var maxPages = 3;
        var timeoutSeconds = 45;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--corpus-root" when i + 1 < args.Length:
                    corpusRoot = args[++i];
                    break;
                case "--output" when i + 1 < args.Length:
                    outputPath = args[++i];
                    break;
                case "--max-files" when i + 1 < args.Length:
                    maxFiles = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--max-pages" when i + 1 < args.Length:
                    maxPages = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--timeout-seconds" when i + 1 < args.Length:
                    timeoutSeconds = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--worker-file" when i + 1 < args.Length:
                    workerPath = args[++i];
                    break;
                case "--worker-relative-path" when i + 1 < args.Length:
                    workerRelativePath = args[++i];
                    break;
                case "--worker-password" when i + 1 < args.Length:
                    workerPassword = args[++i];
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument: {args[i]}");
            }
        }

        if (maxFiles <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxFiles), "--max-files must be positive.");
        if (maxPages <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPages), "--max-pages must be positive.");
        if (timeoutSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "--timeout-seconds must be positive.");
        if (workerPath is not null && string.IsNullOrWhiteSpace(workerRelativePath))
            throw new ArgumentException("--worker-relative-path is required with --worker-file.");

        return new BaselineOptions(corpusRoot, outputPath, maxFiles, maxPages, timeoutSeconds, workerPath, workerRelativePath, workerPassword);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --file generate-pdf-corpus-baseline.cs -- [--corpus-root <path>] [--output <path>] [--max-files <n>] [--max-pages <n>] [--timeout-seconds <n>]");
    }
}

sealed class PdfCorpusBaselineManifest
{
    public int FormatVersion { get; init; }
    public string Engine { get; init; } = string.Empty;
    public string Backend { get; init; } = string.Empty;
    public int MaxPages { get; init; }
    public PdfCorpusBaselineTolerance Tolerance { get; init; } = PdfCorpusBaselineTolerance.Default;
    public IReadOnlyList<PdfCorpusBaselineEntry> Entries { get; init; } = [];
}

sealed record PdfCorpusBaselineTolerance(
    int TextLengthPad,
    int TextItemCountPad,
    int AssetCountPad,
    int WarningCountPad,
    int ProjectionLineCountPad)
{
    public static PdfCorpusBaselineTolerance Default { get; } = new(
        TextLengthPad: 32,
        TextItemCountPad: 4,
        AssetCountPad: 1,
        WarningCountPad: 0,
        ProjectionLineCountPad: 3);
}

sealed class PdfCorpusBaselineEntry
{
    public string Path { get; init; } = string.Empty;
    public string Status { get; init; } = "ok";
    public PdfCorpusBaselineRange PageCount { get; init; } = new(0, 0);
    public PdfCorpusBaselineRange TextLength { get; init; } = new(0, 0);
    public PdfCorpusBaselineRange TextItemCount { get; init; } = new(0, 0);
    public PdfCorpusBaselineRange AssetCount { get; init; } = new(0, 0);
    public PdfCorpusBaselineRange WarningCount { get; init; } = new(0, 0);
    public PdfCorpusBaselineRange OcrCandidatePageCount { get; init; } = new(0, 0);
    public PdfCorpusBaselineRange ProjectionLineCount { get; init; } = new(0, 0);
    public string? AccessMode { get; init; }
    public string? FailureType { get; init; }
    public string? FailureCategory { get; init; }
    public string? ReferenceExpectation { get; init; }
    public string? FailureMessage { get; init; }
    public string? FailureStackTrace { get; init; }

    public static PdfCorpusBaselineEntry FromResult(string path, PdfExtractionResult result, string? accessMode = null)
    {
        var tolerance = PdfCorpusBaselineTolerance.Default;
        return new PdfCorpusBaselineEntry
        {
            Path = path,
            PageCount = Range(result.Pages.Count),
            TextLength = Range(result.Text.Length, tolerance.TextLengthPad),
            TextItemCount = Range(result.Pages.Sum(static page => page.TextItems.Count), tolerance.TextItemCountPad),
            AssetCount = Range(result.Assets.Count, tolerance.AssetCountPad),
            WarningCount = Range(result.Diagnostics.Warnings.Count, tolerance.WarningCountPad),
            OcrCandidatePageCount = Range(result.Diagnostics.OcrCandidatePageCount),
            ProjectionLineCount = Range(GetProjectionLineCount(result), tolerance.ProjectionLineCountPad),
            AccessMode = accessMode
        };
    }

    public static PdfCorpusBaselineEntry FromFailure(string path, Exception error)
    {
        var failureType = error is PdfBackendException backendException
            ? backendException.Kind.ToString()
            : error.GetType().Name;
        return new PdfCorpusBaselineEntry
        {
            Path = path,
            Status = "failed",
            FailureType = failureType,
            FailureCategory = KnownCorpusFailureExpectations.GetCategory(path),
            ReferenceExpectation = KnownCorpusFailureExpectations.GetExpectation(path),
            FailureMessage = error.Message,
            FailureStackTrace = error.StackTrace
        };
    }

    public static PdfCorpusBaselineEntry FromFailure(string path, string failureType, string failureMessage) => new()
    {
        Path = path,
        Status = "failed",
        FailureType = failureType,
        FailureCategory = KnownCorpusFailureExpectations.GetCategory(path),
        ReferenceExpectation = KnownCorpusFailureExpectations.GetExpectation(path),
        FailureMessage = failureMessage
    };

    private static int GetProjectionLineCount(PdfExtractionResult result) =>
        result.Pages.Sum(static page =>
        {
            if (!page.Metadata.TryGetValue("projection", out var projectionObject) ||
                projectionObject is not IReadOnlyDictionary<string, object?> projection ||
                !projection.TryGetValue("lineCount", out var value))
            {
                return 0;
            }

            return value switch
            {
                int intValue => intValue,
                long longValue => checked((int)longValue),
                float floatValue => (int)floatValue,
                double doubleValue => (int)doubleValue,
                _ => 0
            };
        });

    private static PdfCorpusBaselineRange Range(int exact, int pad = 0) =>
        new(Math.Max(0, exact - pad), exact + pad);
}

readonly record struct PdfCorpusBaselineRange(int Min, int Max);

static class KnownCorpusPasswords
{
    private static readonly IReadOnlyDictionary<string, string> s_exactPaths = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PdfPig/src/UglyToad.PdfPig.Tests/Integration/SpecificTestDocuments/encrypted-password-is-password.pdf"] = "password",
        ["pdfium/testing/resources/bug_1124998.pdf"] = "test",
        ["pdfium/testing/resources/bug_644.pdf"] = "a",
        ["pdfium/testing/resources/encrypted.pdf"] = "1234",
        ["pdfium/testing/resources/encrypted_hello_world_r2.pdf"] = "h\u00f4tel",
        ["pdfium/testing/resources/encrypted_hello_world_r3.pdf"] = "h\u00f4tel",
        ["pdfium/testing/resources/encrypted_hello_world_r5.pdf"] = "h\u00f4tel",
        ["pdfium/testing/resources/encrypted_hello_world_r6.pdf"] = "h\u00f4tel"
    };

    private static readonly IReadOnlyDictionary<string, string> s_fileNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["encrypted-password-is-password.pdf"] = "password"
    };

    public static string? GetPassword(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        if (s_exactPaths.TryGetValue(normalized, out var password))
            return password;

        var fileName = Path.GetFileName(normalized);
        return s_fileNames.TryGetValue(fileName, out password) ? password : null;
    }
}

static class KnownCorpusFailureExpectations
{
    private static readonly IReadOnlyDictionary<string, (string Category, string Expectation)> s_known = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
    {
        ["PdfPig/src/UglyToad.PdfPig.Tests/Integration/SpecificTestDocuments/StackOverflow_Issue_1122.pdf"] = (
            "ParserGuardrail",
            "Reference tests expect a circular-reference format exception; the native backend rejects the document at open."),
        ["PdfPig/src/UglyToad.PdfPig.Tests/Integration/SpecificTestDocuments/pages-indirect-to-null.pdf"] = (
            "LenientParserSalvageGap",
            "A lenient reference parser can synthesize one page from a null Pages entry; the native backend rejects the document at open."),
        ["PdfPig/src/UglyToad.PdfPig.Tests/Integration/SpecificTestDocuments/stackoverflow_error.pdf"] = (
            "ParserGuardrail",
            "Reference tests expect a bounded stack-depth exception; the native backend rejects the document at open."),
        ["pdfium/testing/resources/bug_1324189.pdf"] = (
            "ExpectedMalformedReject",
            "Reference data-availability test expects the malformed document to remain unavailable without crashing."),
        ["pdfium/testing/resources/bug_1324503.pdf"] = (
            "ExpectedMalformedReject",
            "Reference data-availability test expects the malformed document to remain unavailable without crashing."),
        ["pdfium/testing/resources/bug_298.pdf"] = (
            "ExpectedMalformedReject",
            "Reference regression test expects this hang-risk document not to open."),
        ["pdfium/testing/resources/bug_325_a.pdf"] = (
            "ExpectedMalformedReject",
            "Reference parser regression test expects this damaged document not to open."),
        ["pdfium/testing/resources/bug_325_b.pdf"] = (
            "ExpectedMalformedReject",
            "Reference parser regression test expects this damaged document not to open."),
        ["pdfium/testing/resources/bug_343.pdf"] = (
            "ExpectedMalformedReject",
            "Reference regression test expects this circular-reference hang-risk document not to open."),
        ["pdfium/testing/resources/bug_344.pdf"] = (
            "ExpectedMalformedReject",
            "Reference regression test expects this malformed signature dictionary not to open."),
        ["pdfium/testing/resources/bug_355.pdf"] = (
            "ExpectedMalformedReject",
            "Reference regression test expects this recursive string parser case not to open."),
        ["pdfium/testing/resources/bug_360.pdf"] = (
            "ExpectedMalformedReject",
            "Reference regression test expects this circular-pages document not to open."),
        ["pdfium/testing/resources/bug_424613308.pdf"] = (
            "ExpectedSecurityReject",
            "Reference security-handler regression test expects no-password open to fail safely."),
        ["pdfium/testing/resources/bug_451830.pdf"] = (
            "ExpectedMalformedReject",
            "Reference view regression test says the document is damaged and cannot be opened."),
        ["pdfium/testing/resources/bug_454695.pdf"] = (
            "ExpectedMalformedReject",
            "Reference parser/view regression tests expect this defective dictionary document not to open."),
        ["pdfium/testing/resources/bug_457855936.pdf"] = (
            "HostileParserTimeout",
            "Reference tests currently treat this parser file as process-hostile; HPD must keep it process-isolated."),
        ["pdfium/testing/resources/encrypted_hello_world_r2_bad_okey.pdf"] = (
            "BadEncryptionMetadata",
            "Reference security-handler regression test expects this bad owner-key file not to open even with a password."),
        ["pdfium/testing/resources/encrypted_hello_world_r3_bad_okey.pdf"] = (
            "BadEncryptionMetadata",
            "Reference security-handler regression test expects this bad owner-key file not to open even with a password."),
        ["pdfium/testing/resources/parser_rebuildxref_error_notrailer.pdf"] = (
            "ExpectedMalformedReject",
            "Reference parser unit test expects cross-reference rebuild to fail because the trailer is missing."),
        ["pdfium/testing/resources/trailer_as_hexstring.pdf"] = (
            "ExpectedMalformedReject",
            "Reference data-availability test expects this malformed trailer document not to open."),
        ["pdfium/testing/resources/trailer_unterminated.pdf"] = (
            "ExpectedMalformedReject",
            "Reference data-availability test expects this unterminated trailer document not to open.")
    };

    public static string? GetCategory(string relativePath) =>
        s_known.TryGetValue(Normalize(relativePath), out var value) ? value.Category : null;

    public static string? GetExpectation(string relativePath) =>
        s_known.TryGetValue(Normalize(relativePath), out var value) ? value.Expectation : null;

    private static string Normalize(string relativePath) => relativePath.Replace('\\', '/');
}
