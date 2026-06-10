#:property TargetFramework=net10.0
#:property PublishAot=false
#:property PackAsTool=false
#:property IsPackable=false
#:property GenerateDocumentationFile=false

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

var options = AotSmokeOptions.Parse(args);
var repoRoot = FindRepoRoot(Directory.GetCurrentDirectory());
var dotnetRoot = Path.Combine(repoRoot, "HPD-AI-Framework", "dotnet");
var textExtractRoot = Path.Combine(dotnetRoot, "shared", "src", "HPD-TextExtract");
var logRoot = Path.Combine(textExtractRoot, ".tmp", "artifacts", "logs");
var projectPath = Path.GetFullPath(options.ProjectPath ?? Path.Combine(
    dotnetRoot,
    "HPD-Agent.Framework",
    "test",
    "HPD-Agent.TextExtraction.AotSmoke",
    "HPD-Agent.TextExtraction.AotSmoke.csproj"));
var targetFramework = options.TargetFramework ?? Environment.GetEnvironmentVariable("HPD_TEXT_EXTRACTION_AOT_TFM") ?? "net10.0";
var runtimeIdentifier = options.RuntimeIdentifier ?? Environment.GetEnvironmentVariable("HPD_TEXT_EXTRACTION_AOT_RID") ?? InferRuntimeIdentifier();
var publishDirectory = Path.Combine(
    Path.GetDirectoryName(projectPath)!,
    "bin",
    "Release",
    targetFramework,
    runtimeIdentifier,
    "publish");
var executableName = runtimeIdentifier.StartsWith("win-", StringComparison.OrdinalIgnoreCase)
    ? "HPD-Agent.TextExtraction.AotSmoke.exe"
    : "HPD-Agent.TextExtraction.AotSmoke";
var executablePath = Path.Combine(publishDirectory, executableName);
var nativeLibraryPath = Path.Combine(publishDirectory, GetPdfiumNativeLibraryName(runtimeIdentifier));

Console.WriteLine("==================================");
Console.WriteLine(" HPD TextExtraction AOT PDF Smoke ");
Console.WriteLine("==================================");
Console.WriteLine($"Project: {projectPath}");
Console.WriteLine($"TFM:     {targetFramework}");
Console.WriteLine($"RID:     {runtimeIdentifier}");
Console.WriteLine();

if (!File.Exists(projectPath))
    throw new FileNotFoundException($"AOT smoke project does not exist: {projectPath}");

if (Directory.Exists(publishDirectory))
    Directory.Delete(publishDirectory, recursive: true);

var publishExitCode = await DotnetAsync(
    [
        "publish",
        projectPath,
        "-c",
        "Release",
        "-f",
        targetFramework,
        "-r",
        runtimeIdentifier,
        "-v:q",
        "/p:WarningLevel=0"
    ],
    logPath: Path.Combine(logRoot, "text-extraction-aot-publish.log"));

if (publishExitCode != 0)
    return publishExitCode;

if (!File.Exists(executablePath))
    throw new FileNotFoundException($"Missing published Native AOT executable: {executablePath}");

if (!OperatingSystem.IsWindows() && !IsExecutable(executablePath))
    throw new InvalidOperationException($"Published Native AOT file is not executable: {executablePath}");

if (!File.Exists(nativeLibraryPath))
    throw new FileNotFoundException($"Missing PDFium native library in publish output: {nativeLibraryPath}");

return await RunProcessAsync(
    executablePath,
    [],
    logPath: Path.Combine(logRoot, "text-extraction-aot-smoke.log"));

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

static string InferRuntimeIdentifier()
{
    var os = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        ? "osx"
        : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? "linux"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "win"
                : throw new PlatformNotSupportedException($"Unsupported OS: {RuntimeInformation.OSDescription}");

    var architecture = RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        _ => throw new PlatformNotSupportedException($"Unsupported architecture: {RuntimeInformation.ProcessArchitecture}")
    };

    return $"{os}-{architecture}";
}

static string GetPdfiumNativeLibraryName(string runtimeIdentifier)
{
    if (runtimeIdentifier.StartsWith("osx-", StringComparison.OrdinalIgnoreCase))
        return "libpdfium.dylib";

    if (runtimeIdentifier.StartsWith("linux-", StringComparison.OrdinalIgnoreCase))
        return "libpdfium.so";

    if (runtimeIdentifier.StartsWith("win-", StringComparison.OrdinalIgnoreCase))
        return "pdfium.dll";

    throw new PlatformNotSupportedException($"Unsupported runtime identifier: {runtimeIdentifier}");
}

static bool IsExecutable(string path)
{
    try
    {
        var mode = File.GetUnixFileMode(path);
        return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
    }
    catch (PlatformNotSupportedException)
    {
        return true;
    }
}

static Task<int> DotnetAsync(IReadOnlyList<string> arguments, string logPath) =>
    RunProcessAsync("dotnet", arguments, logPath);

static async Task<int> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, string logPath)
{
    var startInfo = new ProcessStartInfo(fileName)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };

    foreach (var argument in arguments)
        startInfo.ArgumentList.Add(argument);

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Failed to start process: {fileName}");

    var standardOutput = process.StandardOutput.ReadToEndAsync();
    var standardError = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    var output = await standardOutput;
    var error = await standardError;
    var combined = new StringBuilder(output.Length + error.Length + 1)
        .Append(output)
        .Append(error)
        .ToString();

    Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
    await File.WriteAllTextAsync(logPath, combined, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    foreach (var line in ReadLines(combined))
    {
        if (line.Contains("warning", StringComparison.OrdinalIgnoreCase))
            continue;

        Console.WriteLine(line);
    }

    return process.ExitCode;
}

static IEnumerable<string> ReadLines(string text)
{
    using var reader = new StringReader(text);
    while (reader.ReadLine() is { } line)
        yield return line;
}

sealed record AotSmokeOptions(string? ProjectPath, string? TargetFramework, string? RuntimeIdentifier)
{
    public static AotSmokeOptions Parse(string[] args)
    {
        string? projectPath = null;
        string? targetFramework = null;
        string? runtimeIdentifier = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--project" when i + 1 < args.Length:
                    projectPath = args[++i];
                    break;
                case "--framework" when i + 1 < args.Length:
                    targetFramework = args[++i];
                    break;
                case "--rid" when i + 1 < args.Length:
                    runtimeIdentifier = args[++i];
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument: {args[i]}");
            }
        }

        return new AotSmokeOptions(projectPath, targetFramework, runtimeIdentifier);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --file run-text-extraction-aot-smoke.cs -- [--project <csproj>] [--framework <tfm>] [--rid <runtime-identifier>]");
        Console.WriteLine();
        Console.WriteLine("Environment:");
        Console.WriteLine("  HPD_TEXT_EXTRACTION_AOT_TFM overrides the default TFM.");
        Console.WriteLine("  HPD_TEXT_EXTRACTION_AOT_RID overrides runtime identifier inference.");
    }
}
