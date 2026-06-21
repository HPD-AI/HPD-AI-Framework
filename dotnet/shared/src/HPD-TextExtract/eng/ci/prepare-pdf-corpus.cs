#:property TargetFramework=net10.0
#:property PublishAot=false
#:property PackAsTool=false
#:property IsPackable=false
#:property GenerateDocumentationFile=false

using System.Security.Cryptography;

var options = PdfCorpusPrepOptions.Parse(args);
var repoRoot = FindRepoRoot(Directory.GetCurrentDirectory());
var referenceRoot = Path.GetFullPath(options.ReferenceRoot ?? FindDefaultReferenceRoot(repoRoot));
var outputRoot = Path.GetFullPath(options.OutputRoot ?? Path.Combine(
    repoRoot,
    "dotnet",
    "HPD-Agent.Framework",
    "test",
    "HPD-TextExtract.Tests",
    "Content",
    "Fixtures",
    "PdfCorpus"));

Console.WriteLine("=====================================");
Console.WriteLine(" HPD TextExtraction PDF Corpus Prep ");
Console.WriteLine("=====================================");
Console.WriteLine($"Reference: {referenceRoot}");
Console.WriteLine($"Output:    {outputRoot}");
Console.WriteLine();

if (!Directory.Exists(referenceRoot))
{
    throw new DirectoryNotFoundException(
        $"Reference root does not exist: {referenceRoot}. Pass --reference-root or set HPD_TEXT_EXTRACTION_REFERENCE_ROOT.");
}

var copied = 0;
var unchanged = 0;
var missing = new List<PdfCorpusFixture>();

foreach (var fixture in PdfCorpusFixture.Curated)
{
    var sourcePath = Path.Combine(referenceRoot, fixture.SourceRelativePath);
    var destinationPath = Path.Combine(outputRoot, fixture.DestinationRelativePath);
    if (!File.Exists(sourcePath))
    {
        missing.Add(fixture);
        continue;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
    if (File.Exists(destinationPath) && await FilesMatchAsync(sourcePath, destinationPath))
    {
        unchanged++;
        continue;
    }

    File.Copy(sourcePath, destinationPath, overwrite: true);
    copied++;
    Console.WriteLine($"Copied {fixture.SourceRelativePath} -> {fixture.DestinationRelativePath}");
}

if (missing.Count > 0)
{
    foreach (var fixture in missing)
        Console.WriteLine($"Missing {fixture.SourceRelativePath}");

    if (!options.AllowMissing)
        throw new InvalidOperationException($"Missing {missing.Count} curated PDF fixture(s).");
}

Console.WriteLine();
Console.WriteLine($"Curated corpus: copied={copied}, unchanged={unchanged}, missing={missing.Count}");

if (options.CopyFullCorpus)
{
    var fullOutputRoot = Path.GetFullPath(options.FullOutputRoot ?? Path.Combine(outputRoot, "full-reference"));
    var fullCopied = 0;
    var fullUnchanged = 0;
    foreach (var sourcePath in Directory.EnumerateFiles(referenceRoot, "*.pdf", SearchOption.AllDirectories).Order())
    {
        var relativePath = Path.GetRelativePath(referenceRoot, sourcePath);
        var destinationPath = Path.Combine(fullOutputRoot, NormalizePath(relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        if (File.Exists(destinationPath) && await FilesMatchAsync(sourcePath, destinationPath))
        {
            fullUnchanged++;
            continue;
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);
        fullCopied++;
    }

    Console.WriteLine($"Full corpus: copied={fullCopied}, unchanged={fullUnchanged}, output={fullOutputRoot}");
}

static string FindRepoRoot(string startDirectory)
{
    var current = new DirectoryInfo(startDirectory);
    while (current is not null)
    {
        if (Directory.Exists(Path.Combine(current.FullName, ".git")) &&
            Directory.Exists(Path.Combine(current.FullName, "dotnet")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new InvalidOperationException($"Could not find repository root from {startDirectory}.");
}

static string FindDefaultReferenceRoot(string repoRoot)
{
    var environmentRoot = Environment.GetEnvironmentVariable("HPD_TEXT_EXTRACTION_REFERENCE_ROOT");
    if (!string.IsNullOrWhiteSpace(environmentRoot))
        return environmentRoot;

    var siblingReferenceRoot = Path.Combine(
        repoRoot,
        "..",
        "HPD-Agent-InternalDocs",
        "HPD-TextExtraction",
        "Reference");
    if (Directory.Exists(siblingReferenceRoot))
        return siblingReferenceRoot;

    return Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Desktop",
        "HPD-Agent-InternalDocs",
        "HPD-TextExtraction",
        "Reference");
}

static async Task<bool> FilesMatchAsync(string leftPath, string rightPath)
{
    var leftInfo = new FileInfo(leftPath);
    var rightInfo = new FileInfo(rightPath);
    if (leftInfo.Length != rightInfo.Length)
        return false;

    var leftHash = await ComputeSha256Async(leftPath);
    var rightHash = await ComputeSha256Async(rightPath);
    return leftHash.SequenceEqual(rightHash);
}

static async Task<byte[]> ComputeSha256Async(string path)
{
    using var sha256 = SHA256.Create();
    await using var stream = File.OpenRead(path);
    return await sha256.ComputeHashAsync(stream);
}

static string NormalizePath(string relativePath) =>
    relativePath.Replace('\\', Path.DirectorySeparatorChar)
        .Replace('/', Path.DirectorySeparatorChar);

sealed record PdfCorpusFixture(string SourceRelativePath, string DestinationRelativePath)
{
    public static IReadOnlyList<PdfCorpusFixture> Curated { get; } =
    [
        new("pdfium/testing/resources/bigtable_mini.pdf", "native-capabilities/bigtable_mini.pdf"),
        new("pdfium/testing/resources/cropped_text.pdf", "native-capabilities/cropped_text.pdf"),
        new("pdfium/testing/resources/embedded_images.pdf", "native-capabilities/embedded_images.pdf"),
        new("pdfium/testing/resources/font_weight.pdf", "native-capabilities/font_weight.pdf"),
        new("pdfium/testing/resources/form_object_with_text.pdf", "native-capabilities/form_object_with_text.pdf"),
        new("pdfium/testing/resources/hello_world.pdf", "native-capabilities/hello_world.pdf"),
        new("pdfium/testing/resources/marked_content_id.pdf", "native-capabilities/marked_content_id.pdf"),
        new("pdfium/testing/resources/rotated_image.pdf", "native-capabilities/rotated_image.pdf"),
        new("pdfium/testing/resources/rotated_text.pdf", "native-capabilities/rotated_text.pdf"),
        new("pdfium/testing/resources/rotated_text_90.pdf", "native-capabilities/rotated_text_90.pdf"),
        new("pdfium/testing/resources/shared_form_xobject_matrix.pdf", "native-capabilities/shared_form_xobject_matrix.pdf"),
        new("pdfium/testing/resources/text_font.pdf", "native-capabilities/text_font.pdf"),
        new("pdfium/testing/resources/text_in_page_marked.pdf", "native-capabilities/text_in_page_marked.pdf"),
        new("pdfium/testing/resources/text_render_mode.pdf", "native-capabilities/text_render_mode.pdf"),
        new("pdfium/testing/resources/utf-8.pdf", "native-capabilities/utf-8.pdf"),
        new("pdfium/testing/resources/vertical_text.pdf", "native-capabilities/vertical_text.pdf"),

        new("PdfPig/src/UglyToad.PdfPig.Tests/Integration/Documents/cmap-parsing-exception.pdf", "layout-stress/cmap-parsing-exception.pdf"),
        new("PdfPig/src/UglyToad.PdfPig.Tests/Integration/Documents/cropped-and-rotated.pdf", "layout-stress/cropped-and-rotated.pdf"),
        new("PdfPig/src/UglyToad.PdfPig.Tests/Integration/Documents/Grapheme clusters emoji.pdf", "layout-stress/grapheme-clusters-emoji.pdf"),
        new("PdfPig/src/UglyToad.PdfPig.Tests/Dla/Documents/Random 2 Columns Lists Hyph - Justified.pdf", "layout-stress/random-2-columns-lists-hyph-justified.pdf"),
        new("PdfPig/src/UglyToad.PdfPig.Tests/Integration/Documents/SinglePage180ClockwiseRotation - from PdfPig.pdf", "layout-stress/single-page-180-rotation.pdf"),
        new("PdfPig/src/UglyToad.PdfPig.Tests/Integration/Documents/SinglePage270ClockwiseRotation - from PdfPig.pdf", "layout-stress/single-page-270-rotation.pdf"),
        new("PdfPig/src/UglyToad.PdfPig.Tests/Integration/Documents/SinglePage90ClockwiseRotation - from PdfPig.pdf", "layout-stress/single-page-90-rotation.pdf"),
        new("PdfPig/src/UglyToad.PdfPig.Tests/Integration/Documents/Single Page Images - from libre office.pdf", "layout-stress/single-page-images.pdf"),
        new("PdfPig/src/UglyToad.PdfPig.Tests/Integration/Documents/Type0_CJK_Font.pdf", "layout-stress/type0-cjk-font.pdf"),

        new("PdfPig/src/UglyToad.PdfPig.Tests/Integration/SpecificTestDocuments/encrypted-password-is-password.pdf", "security/encrypted-password-is-password.pdf"),
        new("PdfPig/src/UglyToad.PdfPig.Tests/Integration/SpecificTestDocuments/StackOverflow_Issue_1122.pdf", "security/circular-reference-issue-1122.pdf"),
        new("PdfPig/src/UglyToad.PdfPig.Tests/Integration/SpecificTestDocuments/stackoverflow_error.pdf", "security/stack-depth-error.pdf"),
        new("pdfium/testing/resources/bug_457855936.pdf", "security/pdfium-parser-death-test-bug-457855936.pdf"),

        new("liteparse/integration_tests_data/sample.pdf", "document-samples/sample.pdf")
    ];
}

sealed record PdfCorpusPrepOptions(
    string? ReferenceRoot,
    string? OutputRoot,
    bool CopyFullCorpus,
    string? FullOutputRoot,
    bool AllowMissing)
{
    public static PdfCorpusPrepOptions Parse(string[] args)
    {
        string? referenceRoot = null;
        string? outputRoot = null;
        string? fullOutputRoot = null;
        var copyFullCorpus = false;
        var allowMissing = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--reference-root" when i + 1 < args.Length:
                    referenceRoot = args[++i];
                    break;
                case "--output" when i + 1 < args.Length:
                    outputRoot = args[++i];
                    break;
                case "--copy-full":
                    copyFullCorpus = true;
                    break;
                case "--full-output" when i + 1 < args.Length:
                    fullOutputRoot = args[++i];
                    copyFullCorpus = true;
                    break;
                case "--allow-missing":
                    allowMissing = true;
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument: {args[i]}");
            }
        }

        return new PdfCorpusPrepOptions(referenceRoot, outputRoot, copyFullCorpus, fullOutputRoot, allowMissing);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --file prepare-pdf-corpus.cs -- [--reference-root <Reference>] [--output <PdfCorpus>] [--copy-full] [--full-output <path>] [--allow-missing]");
        Console.WriteLine();
        Console.WriteLine("Defaults:");
        Console.WriteLine("  --reference-root: HPD_TEXT_EXTRACTION_REFERENCE_ROOT, sibling HPD-Agent-InternalDocs path, or ~/Desktop/HPD-Agent-InternalDocs/HPD-TextExtraction/Reference");
        Console.WriteLine("  --output: HPD-TextExtract.Tests/Content/Fixtures/PdfCorpus");
    }
}
