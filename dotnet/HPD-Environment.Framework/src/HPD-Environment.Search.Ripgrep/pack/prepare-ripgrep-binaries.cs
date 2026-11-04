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
const string AlpineRipgrepVersion = "14.1.1-r0";

string? outputRootArgument =
    args.SkipWhile(argument => argument != "--output-root")
        .Skip(1)
        .FirstOrDefault();
if (string.IsNullOrWhiteSpace(outputRootArgument))
{
    throw new ArgumentException(
        "--output-root is required so MSBuild can embed the generated bundles.");
}
var outputRoot = Path.GetFullPath(outputRootArgument);
string bundlesRoot = Path.Combine(outputRoot, "bundles");
string generatedRoot = Path.Combine(outputRoot, "Generated");
string downloadRoot = Path.Combine(outputRoot, "downloads");
Directory.CreateDirectory(bundlesRoot);
Directory.CreateDirectory(generatedRoot);
Directory.CreateDirectory(downloadRoot);

using var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
    "HPD-Environment-RipgrepPack/1.0");

var manifests = new List<ManifestEntry>();
foreach (RipgrepAsset asset in RipgrepAsset.Official)
{
    string archive = await DownloadAsync(
        asset.Url,
        Path.Combine(downloadRoot, asset.ArchiveName),
        asset.ArchiveSha256);
    string binary = ExtractOfficialBinary(
        archive,
        asset.BinaryName,
        Path.Combine(outputRoot, "extract", asset.Target));
    string bundle = CreateBundle(
        bundlesRoot,
        asset.Target,
        new Dictionary<string, string>
        {
            ["rg"] = binary,
        });
    manifests.Add(await CreateManifestAsync(
        asset.Target,
        RipgrepVersion,
        bundle,
        executablePath: "rg",
        loaderPath: null));
}

var alpineFiles = new Dictionary<string, string>();
foreach (AlpinePackage package in AlpinePackage.All)
{
    string archive = await DownloadAsync(
        package.Url,
        Path.Combine(downloadRoot, package.FileName),
        package.Sha256);
    ExtractAlpineFiles(
        archive,
        package,
        alpineFiles,
        Path.Combine(outputRoot, "extract", "linux-arm64-musl"));
}
string alpineBundle = CreateBundle(
    bundlesRoot,
    "linux-arm64-musl",
    alpineFiles);
manifests.Add(await CreateManifestAsync(
    "linux-arm64-musl",
    AlpineRipgrepVersion,
    alpineBundle,
    executablePath: "rg",
    loaderPath: "lib/ld-musl-aarch64.so.1"));

string generatedPath = Path.Combine(
    generatedRoot,
    "RipgrepBundledBinaries.g.cs");
await File.WriteAllTextAsync(
    generatedPath,
    GenerateManifest(manifests),
    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
Console.WriteLine(generatedPath);

static async Task<ManifestEntry> CreateManifestAsync(
    string target,
    string version,
    string bundle,
    string executablePath,
    string? loaderPath)
{
    string digest = await Sha256Async(bundle);
    return new ManifestEntry(
        target,
        $"HPD.Environment.Search.Ripgrep.Bundles.{target}.tar",
        version,
        digest,
        executablePath,
        loaderPath);
}

static async Task<string> DownloadAsync(
    string url,
    string destination,
    string expectedSha256)
{
    if (!File.Exists(destination) ||
        !string.Equals(
            await Sha256Async(destination),
            expectedSha256,
            StringComparison.Ordinal))
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using Stream input =
            await new HttpClient().GetStreamAsync(url);
        await using FileStream output = File.Create(destination);
        await input.CopyToAsync(output);
    }
    string digest = await Sha256Async(destination);
    if (!string.Equals(
            digest,
            expectedSha256,
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"Archive SHA-256 mismatch for '{url}'.");
    }
    return destination;
}

static async Task<string> Sha256Async(string path)
{
    await using FileStream stream = File.OpenRead(path);
    byte[] digest = await SHA256.HashDataAsync(stream);
    return Convert.ToHexString(digest).ToLowerInvariant();
}

static string ExtractOfficialBinary(
    string archive,
    string binaryName,
    string extractRoot)
{
    if (Directory.Exists(extractRoot))
    {
        Directory.Delete(extractRoot, recursive: true);
    }
    Directory.CreateDirectory(extractRoot);
    if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
    {
        ZipFile.ExtractToDirectory(archive, extractRoot);
    }
    else
    {
        ExtractTarGzip(archive, extractRoot);
    }
    string[] matches = Directory.GetFiles(
        extractRoot,
        binaryName,
        SearchOption.AllDirectories);
    return matches.Length == 1
        ? matches[0]
        : throw new InvalidOperationException(
            $"Expected one '{binaryName}' in '{archive}', found {matches.Length}.");
}

static void ExtractTarGzip(string archive, string destination)
{
    using FileStream file = File.OpenRead(archive);
    using var gzip = new GZipStream(file, CompressionMode.Decompress);
    TarFile.ExtractToDirectory(gzip, destination, overwriteFiles: true);
}

static void ExtractAlpineFiles(
    string archive,
    AlpinePackage package,
    IDictionary<string, string> destinationFiles,
    string extractRoot)
{
    using FileStream file = File.OpenRead(archive);
    using var gzip = new GZipStream(file, CompressionMode.Decompress);
    using var reader = new TarReader(gzip);
    while (reader.GetNextEntry() is { } entry)
    {
        string? relative = package.Files
            .FirstOrDefault(pair => entry.Name == pair.Key)
            .Value;
        if (relative is null ||
            entry.DataStream is null)
        {
            continue;
        }
        string destination = Path.Combine(extractRoot, relative);
        Directory.CreateDirectory(
            Path.GetDirectoryName(destination)!);
        using FileStream output = File.Create(destination);
        entry.DataStream.CopyTo(output);
        destinationFiles[relative] = destination;
    }
}

static string CreateBundle(
    string bundlesRoot,
    string target,
    IReadOnlyDictionary<string, string> files)
{
    string destination = Path.Combine(
        bundlesRoot,
        $"{target}.tar");
    using FileStream output = File.Create(destination);
    using var writer = new TarWriter(
        output,
        TarEntryFormat.Ustar,
        leaveOpen: false);
    foreach ((string relative, string source) in
             files.OrderBy(
                 pair => pair.Key,
                 StringComparer.Ordinal))
    {
        using FileStream data = File.OpenRead(source);
        var entry = new UstarTarEntry(
            TarEntryType.RegularFile,
            relative)
        {
            DataStream = data,
            Mode = UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute |
                UnixFileMode.GroupRead |
                UnixFileMode.GroupExecute,
            ModificationTime = DateTimeOffset.UnixEpoch,
            Uid = 0,
            Gid = 0,
            UserName = string.Empty,
            GroupName = string.Empty,
        };
        writer.WriteEntry(entry);
    }
    return destination;
}

static string GenerateManifest(
    IReadOnlyList<ManifestEntry> entries)
{
    var source = new StringBuilder(
        """
        // <auto-generated />
        namespace HPD.Environment.Search.Ripgrep;

        internal static partial class RipgrepBundledBinaries
        {
            static partial void AddBundledBinaries(
                List<BundledRipgrepBinaryManifest> binaries)
            {

        """);
    foreach (ManifestEntry entry in entries)
    {
        source.AppendLine("        binaries.Add(new BundledRipgrepBinaryManifest");
        source.AppendLine("        {");
        source.AppendLine($"            Target = \"{entry.Target}\",");
        source.AppendLine($"            ResourceName = \"{entry.ResourceName}\",");
        source.AppendLine($"            Version = \"{entry.Version}\",");
        source.AppendLine($"            Sha256 = \"{entry.Sha256}\",");
        source.AppendLine($"            ExecutablePath = \"{entry.ExecutablePath}\",");
        if (entry.LoaderPath is null)
        {
            source.AppendLine("            LoaderPath = null,");
        }
        else
        {
            source.AppendLine($"            LoaderPath = \"{entry.LoaderPath}\",");
        }
        source.AppendLine("        });");
    }
    source.AppendLine("    }");
    source.AppendLine("}");
    return source.ToString();
}

sealed record ManifestEntry(
    string Target,
    string ResourceName,
    string Version,
    string Sha256,
    string ExecutablePath,
    string? LoaderPath);

sealed record RipgrepAsset(
    string Target,
    string ArchiveName,
    string BinaryName,
    string ArchiveSha256)
{
    private const string ReleaseRoot =
        "https://github.com/BurntSushi/ripgrep/releases/download/15.1.0/";

    public string Url => ReleaseRoot + ArchiveName;

    public static IReadOnlyList<RipgrepAsset> Official { get; } =
    [
        new(
            "osx-arm64",
            "ripgrep-15.1.0-aarch64-apple-darwin.tar.gz",
            "rg",
            "378e973289176ca0c6054054ee7f631a065874a352bf43f0fa60ef079b6ba715"),
        new(
            "osx-x64",
            "ripgrep-15.1.0-x86_64-apple-darwin.tar.gz",
            "rg",
            "64811cb24e77cac3057d6c40b63ac9becf9082eedd54ca411b475b755d334882"),
        new(
            "linux-x64",
            "ripgrep-15.1.0-x86_64-unknown-linux-musl.tar.gz",
            "rg",
            "1c9297be4a084eea7ecaedf93eb03d058d6faae29bbc57ecdaf5063921491599"),
    ];
}

sealed record AlpinePackage(
    string FileName,
    string Sha256,
    IReadOnlyDictionary<string, string> Files)
{
    private const string Main =
        "https://dl-cdn.alpinelinux.org/alpine/v3.22/main/aarch64/";

    private const string Community =
        "https://dl-cdn.alpinelinux.org/alpine/v3.22/community/aarch64/";

    public string Url =>
        FileName.StartsWith("ripgrep-", StringComparison.Ordinal)
            ? Community + FileName
            : Main + FileName;

    public static IReadOnlyList<AlpinePackage> All { get; } =
    [
        new(
            "musl-1.2.5-r13.apk",
            "d616c430bcbe0bf452b7f6ea2669f2cad14e85644a7599f4f495115d021742ef",
            new Dictionary<string, string>
            {
                ["lib/ld-musl-aarch64.so.1"] =
                    "lib/ld-musl-aarch64.so.1",
            }),
        new(
            "libgcc-14.2.0-r6.apk",
            "ba1835eec3ad8a120efd3d5020e561d53553a0513763a08f509e3ce6d4baa9ca",
            new Dictionary<string, string>
            {
                ["usr/lib/libgcc_s.so.1"] = "lib/libgcc_s.so.1",
            }),
        new(
            "pcre2-10.46-r1.apk",
            "7c24699bbfc009e02c23577071a09c80f687c051138f3863eb732eeb45da1f36",
            new Dictionary<string, string>
            {
                ["usr/lib/libpcre2-8.so.0.14.0"] =
                    "lib/libpcre2-8.so.0",
            }),
        new(
            "ripgrep-14.1.1-r0.apk",
            "f9c145aca9868a3a90d57d4eb89a4c1c92bc4f06870311d230856f68cf6e58bd",
            new Dictionary<string, string>
            {
                ["usr/bin/rg"] = "rg",
            }),
    ];
}
