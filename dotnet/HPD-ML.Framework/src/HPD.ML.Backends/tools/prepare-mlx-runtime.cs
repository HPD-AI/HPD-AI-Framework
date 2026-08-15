#:property TargetFramework=net11.0
#:property PublishAot=false
#:property PackAsTool=false
#:property IsPackable=false
#:property GenerateDocumentationFile=false
#:property LangVersion=preview
#:property Nullable=enable

using System.Diagnostics;
using System.Runtime.InteropServices;

var options = PrepareOptions.Parse(args);
var mlxCRoot = Path.GetFullPath(options.MlxCRoot);
if (!Directory.Exists(mlxCRoot))
    throw new DirectoryNotFoundException($"mlx-c root does not exist: {mlxCRoot}");
if (!File.Exists(Path.Combine(mlxCRoot, "CMakeLists.txt")))
    throw new FileNotFoundException($"mlx-c CMakeLists.txt was not found in {mlxCRoot}");

var outputRoot = Path.GetFullPath(options.OutputRoot);
var buildRoot = Path.GetFullPath(options.BuildRoot);
Directory.CreateDirectory(outputRoot);
Directory.CreateDirectory(buildRoot);

if (!string.IsNullOrWhiteSpace(options.DeveloperDir))
{
    Environment.SetEnvironmentVariable("DEVELOPER_DIR", options.DeveloperDir);
    Console.WriteLine($"DEVELOPER_DIR={options.DeveloperDir}");
}

if (options.BuildMetal)
    await VerifyMetalToolchainAsync(options);

if (options.Configure)
    await ConfigureAsync(mlxCRoot, buildRoot, options);

if (options.Build)
    await BuildAsync(buildRoot);

var libraryPath = FindBuiltLibrary(buildRoot);
var rid = CurrentRuntimeIdentifier();
var destinationDirectory = Path.Combine(outputRoot, "artifacts", "mlx", rid, "native");
Directory.CreateDirectory(destinationDirectory);

var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(libraryPath));
if (File.Exists(destinationPath))
    File.Delete(destinationPath);
File.Copy(libraryPath, destinationPath);

CopyMetalArtifacts(buildRoot, destinationDirectory);

Console.WriteLine($"Prepared MLX C runtime: {destinationPath}");
Console.WriteLine("Probe with:");
Console.WriteLine($"  dotnet run --file tools/probe-mlx.cs -- --library \"{destinationPath}\" --device gpu --matmul-smoke");

static async Task ConfigureAsync(string mlxCRoot, string buildRoot, PrepareOptions options)
{
    var args = new List<string>
    {
        "-S", Quote(mlxCRoot),
        "-B", Quote(buildRoot),
        "-DCMAKE_BUILD_TYPE=Release",
        "-DMLX_C_BUILD_EXAMPLES=OFF",
        $"-DBUILD_SHARED_LIBS={(options.Shared ? "ON" : "OFF")}",
        $"-DMLX_BUILD_METAL={(options.BuildMetal ? "ON" : "OFF")}"
    };

    if (!string.IsNullOrWhiteSpace(options.MacosDeploymentTarget))
        args.Add($"-DCMAKE_OSX_DEPLOYMENT_TARGET={options.MacosDeploymentTarget}");

    if (OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        args.Add("-DCMAKE_OSX_ARCHITECTURES=arm64");

    await RunProcessAsync(Environment.CurrentDirectory, FindOnPath("cmake") ?? "cmake", string.Join(' ', args));
}

static async Task BuildAsync(string buildRoot)
{
    var parallelism = Math.Max(1, Environment.ProcessorCount - 1);
    await RunProcessAsync(Environment.CurrentDirectory, FindOnPath("cmake") ?? "cmake", $"--build {Quote(buildRoot)} -j {parallelism}");
}

static async Task VerifyMetalToolchainAsync(PrepareOptions options)
{
    if (!OperatingSystem.IsMacOS())
        return;

    var metal = await TryRunProcessAsync(Environment.CurrentDirectory, "xcrun", "--find metal");
    var metallib = await TryRunProcessAsync(Environment.CurrentDirectory, "xcrun", "--find metallib");
    if (metal.ExitCode == 0 && metallib.ExitCode == 0)
        return;

    var details = string.Join(
        Environment.NewLine,
        new[]
        {
            metal.ExitCode == 0 ? $"metal: {metal.Stdout.Trim()}" : $"metal lookup failed: {metal.Stderr.Trim()}",
            metallib.ExitCode == 0 ? $"metallib: {metallib.Stdout.Trim()}" : $"metallib lookup failed: {metallib.Stderr.Trim()}"
        });

    throw new InvalidOperationException(
        "MLX Metal build requires Xcode's Metal Toolchain, but the toolchain is not available." +
        Environment.NewLine +
        details +
        Environment.NewLine +
        "Repair commands:" +
        Environment.NewLine +
        "  DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer xcodebuild -runFirstLaunch" +
        Environment.NewLine +
        "  DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer xcodebuild -downloadComponent MetalToolchain" +
        Environment.NewLine +
        "CPU-only fallback:" +
        Environment.NewLine +
        "  pass --no-metal to this script.");
}

static string FindBuiltLibrary(string buildRoot)
{
    var names = OperatingSystem.IsWindows()
        ? new[] { "mlxc.dll", "libmlxc.dll" }
        : OperatingSystem.IsMacOS()
            ? new[] { "libmlxc.dylib", "libmlxc.a" }
            : new[] { "libmlxc.so", "libmlxc.a" };

    foreach (var name in names)
    {
        var candidate = Directory.EnumerateFiles(buildRoot, name, SearchOption.AllDirectories).FirstOrDefault();
        if (candidate is not null)
            return Path.GetFullPath(candidate);
    }

    throw new FileNotFoundException($"Could not find built mlx-c library under {buildRoot}.");
}

static void CopyMetalArtifacts(string buildRoot, string destinationDirectory)
{
    foreach (var metallib in Directory.EnumerateFiles(buildRoot, "*.metallib", SearchOption.AllDirectories))
    {
        var destination = Path.Combine(destinationDirectory, Path.GetFileName(metallib));
        File.Copy(metallib, destination, overwrite: true);
        Console.WriteLine($"Copied MLX Metal artifact: {destination}");
    }
}

static async Task RunProcessAsync(string workingDirectory, string fileName, string arguments)
{
    Console.WriteLine($"{fileName} {arguments}");

    var startInfo = new ProcessStartInfo
    {
        FileName = fileName,
        Arguments = arguments,
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Failed to start process: {fileName}");

    var stdoutTask = PumpAsync(process.StandardOutput, Console.Out);
    var stderrTask = PumpAsync(process.StandardError, Console.Error);
    await process.WaitForExitAsync();
    await Task.WhenAll(stdoutTask, stderrTask);

    if (process.ExitCode != 0)
        throw new InvalidOperationException($"{Path.GetFileName(fileName)} failed with exit code {process.ExitCode}.");
}

static async Task<ProcessResult> TryRunProcessAsync(string workingDirectory, string fileName, string arguments)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = fileName,
        Arguments = arguments,
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Failed to start process: {fileName}");

    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
}

static async Task PumpAsync(TextReader reader, TextWriter writer)
{
    while (await reader.ReadLineAsync() is { } line)
        await writer.WriteLineAsync(line);
}

static string? FindOnPath(string executable)
{
    var path = Environment.GetEnvironmentVariable("PATH");
    if (string.IsNullOrWhiteSpace(path))
        return null;

    foreach (var directory in path.Split(Path.PathSeparator))
    {
        if (string.IsNullOrWhiteSpace(directory))
            continue;

        var candidate = Path.Combine(directory, executable);
        if (File.Exists(candidate))
            return candidate;
    }

    return null;
}

static string CurrentRuntimeIdentifier()
{
    var os = OperatingSystem.IsWindows()
        ? "win"
        : OperatingSystem.IsMacOS()
            ? "osx"
            : "linux";

    var arch = RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => "arm64",
        Architecture.X64 => "x64",
        Architecture.X86 => "x86",
        _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
    };

    return $"{os}-{arch}";
}

static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

readonly record struct ProcessResult(int ExitCode, string Stdout, string Stderr);

sealed record PrepareOptions(
    string MlxCRoot,
    string OutputRoot,
    string BuildRoot,
    bool Configure,
    bool Build,
    bool Shared,
    bool BuildMetal,
    string? MacosDeploymentTarget,
    string? DeveloperDir)
{
    public static PrepareOptions Parse(string[] args)
    {
        var defaultRoot = "/Users/ewoof/Desktop/HPD-Agent-InternalDocs/Helium/Reference/mlx-c";
        if (!Directory.Exists(defaultRoot))
            defaultRoot = "/Users/ewoof/Desktop/HPD-Agent-InternalDocs/Helium/Reference/mlx-net/external/mlx-c";

        var mlxCRoot = defaultRoot;
        var outputRoot = Environment.CurrentDirectory;
        var buildRoot = Path.Combine(Environment.CurrentDirectory, "artifacts", "build", "mlx-c");
        var configure = false;
        var build = false;
        var shared = true;
        var buildMetal = true;
        string? macosDeploymentTarget = null;
        string? developerDir = DefaultDeveloperDir();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mlx-c-root" when i + 1 < args.Length:
                    mlxCRoot = args[++i];
                    break;
                case "--output-root" when i + 1 < args.Length:
                    outputRoot = args[++i];
                    break;
                case "--build-root" when i + 1 < args.Length:
                    buildRoot = args[++i];
                    break;
                case "--configure":
                    configure = true;
                    break;
                case "--build":
                    build = true;
                    break;
                case "--static":
                    shared = false;
                    break;
                case "--no-metal":
                    buildMetal = false;
                    break;
                case "--metal":
                    buildMetal = true;
                    break;
                case "--macos-deployment-target" when i + 1 < args.Length:
                    macosDeploymentTarget = args[++i];
                    break;
                case "--developer-dir" when i + 1 < args.Length:
                    developerDir = args[++i];
                    break;
                case "--no-developer-dir":
                    developerDir = null;
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument: {args[i]}");
            }
        }

        if (!configure && !build)
        {
            configure = true;
            build = true;
        }

        return new PrepareOptions(mlxCRoot, outputRoot, buildRoot, configure, build, shared, buildMetal, macosDeploymentTarget, developerDir);
    }

    private static string? DefaultDeveloperDir()
    {
        if (!OperatingSystem.IsMacOS())
            return null;

        var existing = Environment.GetEnvironmentVariable("DEVELOPER_DIR");
        if (!string.IsNullOrWhiteSpace(existing))
            return existing;

        const string xcodeDeveloper = "/Applications/Xcode.app/Contents/Developer";
        return Directory.Exists(xcodeDeveloper) ? xcodeDeveloper : null;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --file tools/prepare-mlx-runtime.cs -- --mlx-c-root /path/to/mlx-c --configure --build");
        Console.WriteLine("  dotnet run --file tools/prepare-mlx-runtime.cs -- --mlx-c-root /path/to/mlx-c --output-root . --macos-deployment-target 14.0 --developer-dir /Applications/Xcode.app/Contents/Developer");
        Console.WriteLine("  dotnet run --file tools/prepare-mlx-runtime.cs -- --mlx-c-root /path/to/mlx-c --no-metal");
    }
}
