#:property TargetFramework=net10.0
#:property PublishAot=false
#:property PackAsTool=false
#:property IsPackable=false
#:property GenerateDocumentationFile=false

using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

const string RipgrepVersion = "15.1.0";

var options = PackPrepOptions.Parse(args);
var projectPath = Path.GetFullPath(options.ProjectPath);
if (!File.Exists(projectPath))
    throw new FileNotFoundException($"Project file does not exist: {projectPath}");

var projectDirectory = Path.GetDirectoryName(projectPath)
    ?? throw new InvalidOperationException($"Could not determine project directory for {projectPath}.");
var outputRoot = Path.GetFullPath(options.OutputRoot ?? Path.Combine(projectDirectory, "obj", "RipgrepPack"));
var downloadRoot = Path.Combine(outputRoot, "downloads", RipgrepVersion);
var extractRoot = Path.Combine(outputRoot, "extract", RipgrepVersion);
var runtimesRoot = Path.Combine(outputRoot, "runtimes");
var generatedRoot = Path.Combine(outputRoot, "Generated");

Directory.CreateDirectory(downloadRoot);
Directory.CreateDirectory(extractRoot);
Directory.CreateDirectory(runtimesRoot);
Directory.CreateDirectory(generatedRoot);

using var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("HPD-RipgrepPackPrep/1.0");

var manifestEntries = new List<ManifestEntry>();
foreach (var asset in RipgrepAsset.All)
{
    Console.WriteLine($"Preparing ripgrep {RipgrepVersion} for {asset.RuntimeIdentifier}...");

    var archivePath = Path.Combine(downloadRoot, asset.ArchiveName);
    await DownloadIfNeededAsync(httpClient, asset.ArchiveUrl, archivePath);

    var actualArchiveSha256 = await ComputeSha256Async(archivePath);
    if (!string.Equals(asset.ExpectedArchiveSha256, actualArchiveSha256, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"Archive SHA-256 mismatch for {asset.ArchiveName}. Expected {asset.ExpectedArchiveSha256}, got {actualArchiveSha256}.");
    }

    var assetExtractRoot = Path.Combine(extractRoot, asset.RuntimeIdentifier);
    if (Directory.Exists(assetExtractRoot))
        Directory.Delete(assetExtractRoot, recursive: true);
    Directory.CreateDirectory(assetExtractRoot);

    ExtractArchive(archivePath, assetExtractRoot);

    var extractedBinary = FindExtractedBinary(assetExtractRoot, asset.BinaryFileName);
    var destinationDirectory = Path.Combine(runtimesRoot, asset.RuntimeIdentifier, "native");
    Directory.CreateDirectory(destinationDirectory);

    var destinationPath = Path.Combine(destinationDirectory, asset.BinaryFileName);
    File.Copy(extractedBinary, destinationPath, overwrite: true);
    if (!OperatingSystem.IsWindows() && asset.BinaryFileName == "rg")
    {
        File.SetUnixFileMode(
            destinationPath,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute |
            UnixFileMode.GroupRead |
            UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead |
            UnixFileMode.OtherExecute);
    }

    var binarySha256 = await ComputeSha256Async(destinationPath);
    var relativePath = $"runtimes/{asset.RuntimeIdentifier}/native/{asset.BinaryFileName}";
    manifestEntries.Add(new ManifestEntry(
        asset.RuntimeIdentifier,
        relativePath,
        RipgrepVersion,
        binarySha256,
        asset.ArchiveUrl));
}

var generatedSourcePath = Path.Combine(generatedRoot, "RipgrepBundledBinaries.g.cs");
await File.WriteAllTextAsync(generatedSourcePath, GenerateManifestSource(manifestEntries), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

Console.WriteLine($"Generated manifest: {generatedSourcePath}");
Console.WriteLine($"Generated native assets: {runtimesRoot}");

static async Task DownloadIfNeededAsync(HttpClient httpClient, string url, string destinationPath)
{
    if (File.Exists(destinationPath))
    {
        Console.WriteLine($"  Reusing {Path.GetFileName(destinationPath)}");
        return;
    }

    Console.WriteLine($"  Downloading {url}");
    using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
    response.EnsureSuccessStatusCode();

    await using var source = await response.Content.ReadAsStreamAsync();
    await using var destination = File.Create(destinationPath);
    await source.CopyToAsync(destination);
}

static async Task<string> ComputeSha256Async(string path)
{
    using var sha256 = SHA256.Create();
    await using var stream = File.OpenRead(path);
    var hash = await sha256.ComputeHashAsync(stream);
    return Convert.ToHexString(hash).ToLowerInvariant();
}

static void ExtractArchive(string archivePath, string destinationDirectory)
{
    if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
    {
        ZipFile.ExtractToDirectory(archivePath, destinationDirectory);
        return;
    }

    if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
        archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
    {
        using var file = File.OpenRead(archivePath);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(gzip, destinationDirectory, overwriteFiles: true);
        return;
    }

    throw new InvalidOperationException($"Unsupported archive type: {archivePath}");
}

static string FindExtractedBinary(string root, string binaryFileName)
{
    var matches = Directory
        .EnumerateFiles(root, binaryFileName, SearchOption.AllDirectories)
        .ToArray();

    return matches.Length switch
    {
        1 => matches[0],
        0 => throw new FileNotFoundException($"Could not find {binaryFileName} under {root}."),
        _ => throw new InvalidOperationException($"Found multiple {binaryFileName} binaries under {root}.")
    };
}

static string GenerateManifestSource(IReadOnlyList<ManifestEntry> entries)
{
    var builder = new StringBuilder();
    builder.AppendLine("// <auto-generated />");
    builder.AppendLine("namespace HPD.Agent.ToolHarness.Coding.Ripgrep;");
    builder.AppendLine();
    builder.AppendLine("internal static partial class RipgrepBundledBinaries");
    builder.AppendLine("{");
    builder.AppendLine("    static partial void AddBundledBinaries(List<RipgrepBundledBinaryManifest> binaries)");
    builder.AppendLine("    {");

    foreach (var entry in entries)
    {
        builder.AppendLine("        binaries.Add(new RipgrepBundledBinaryManifest");
        builder.AppendLine("        {");
        builder.AppendLine($"            RuntimeIdentifier = \"{Escape(entry.RuntimeIdentifier)}\",");
        builder.AppendLine($"            RelativePath = \"{Escape(entry.RelativePath)}\",");
        builder.AppendLine($"            Version = \"{Escape(entry.Version)}\",");
        builder.AppendLine($"            Sha256 = \"{Escape(entry.Sha256)}\",");
        builder.AppendLine($"            SourceUrl = \"{Escape(entry.SourceUrl)}\"");
        builder.AppendLine("        });");
        builder.AppendLine();
    }

    builder.AppendLine("    }");
    builder.AppendLine("}");
    return builder.ToString();
}

static string Escape(string value)
    => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);

sealed record ManifestEntry(
    string RuntimeIdentifier,
    string RelativePath,
    string Version,
    string Sha256,
    string SourceUrl);

sealed record RipgrepAsset(
    string RuntimeIdentifier,
    string ArchiveName,
    string BinaryFileName,
    string ExpectedArchiveSha256)
{
    private const string AssetRipgrepVersion = "15.1.0";

    private const string AssetSourceBaseUrl = "https://github.com/BurntSushi/ripgrep/releases/download";

    public string ArchiveUrl => $"{AssetSourceBaseUrl}/{AssetRipgrepVersion}/{ArchiveName}";

    public static IReadOnlyList<RipgrepAsset> All { get; } =
    [
        new("osx-arm64", $"ripgrep-{AssetRipgrepVersion}-aarch64-apple-darwin.tar.gz", "rg", "378e973289176ca0c6054054ee7f631a065874a352bf43f0fa60ef079b6ba715"),
        new("osx-x64", $"ripgrep-{AssetRipgrepVersion}-x86_64-apple-darwin.tar.gz", "rg", "64811cb24e77cac3057d6c40b63ac9becf9082eedd54ca411b475b755d334882"),
        new("linux-x64", $"ripgrep-{AssetRipgrepVersion}-x86_64-unknown-linux-musl.tar.gz", "rg", "1c9297be4a084eea7ecaedf93eb03d058d6faae29bbc57ecdaf5063921491599"),
        new("linux-arm64", $"ripgrep-{AssetRipgrepVersion}-aarch64-unknown-linux-gnu.tar.gz", "rg", "2b661c6ef508e902f388e9098d9c4c5aca72c87b55922d94abdba830b4dc885e"),
        new("win-x64", $"ripgrep-{AssetRipgrepVersion}-x86_64-pc-windows-msvc.zip", "rg.exe", "124510b94b6baa3380d051fdf4650eaa80a302c876d611e9dba0b2e18d87493a")
    ];
}

sealed record PackPrepOptions(string ProjectPath, string? OutputRoot)
{
    public static PackPrepOptions Parse(string[] args)
    {
        string? projectPath = null;
        string? outputRoot = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--project" when i + 1 < args.Length:
                    projectPath = args[++i];
                    break;
                case "--output-root" when i + 1 < args.Length:
                    outputRoot = args[++i];
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument: {args[i]}");
            }
        }

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            PrintUsage();
            throw new ArgumentException("--project is required.");
        }

        return new PackPrepOptions(projectPath, outputRoot);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --file prepare-ripgrep-binaries.cs -- --project <HPD-Agent.ToolHarness.Coding.csproj> [--output-root <path>]");
    }
}
