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
var xlaRoot = Path.GetFullPath(options.XlaRoot);
if (!Directory.Exists(xlaRoot))
    throw new DirectoryNotFoundException($"XLA root does not exist: {xlaRoot}");

var outputRoot = Path.GetFullPath(options.OutputRoot);
Directory.CreateDirectory(outputRoot);

if (options.Configure)
    await ConfigureAsync(xlaRoot, options.Backend);

var pluginPath = options.Build
    ? await BuildPluginAsync(xlaRoot, options.Backend)
    : FindBuiltPlugin(xlaRoot, options.Backend);

if (options.SmokeTest)
    await RunSmokeTestAsync(xlaRoot, options.Backend);

var rid = CurrentRuntimeIdentifier();
var destinationDirectory = Path.Combine(outputRoot, "runtimes", rid, "native");
Directory.CreateDirectory(destinationDirectory);

var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(pluginPath));
if (File.Exists(destinationPath))
    File.Delete(destinationPath);

File.Copy(pluginPath, destinationPath);

Console.WriteLine($"Prepared {options.Backend} PJRT plugin: {destinationPath}");
Console.WriteLine("Probe with:");
Console.WriteLine($"  dotnet run --file tools/probe-pjrt.cs -- --backend {options.Backend} --library \"{destinationPath}\" --matmul-milestone");

static async Task<string> BuildPluginAsync(string xlaRoot, string backend)
{
    await RunBazelAsync(xlaRoot, $"build {BazelConfig(backend)} {PluginTarget(backend)}".Trim());
    return FindBuiltPlugin(xlaRoot, backend);
}

static string PluginTarget(string backend)
{
    return NormalizeBackend(backend) switch
    {
        "cpu" => OperatingSystem.IsMacOS()
            ? "//xla/pjrt/c:pjrt_c_api_cpu_plugin.so"
            : "//build_tools/pjrt_wheels:xla_plugins/xla_cpu_pjrt/xla_cpu_pjrt.so",
        "cuda" or "rocm" => "//xla/pjrt/c:pjrt_c_api_gpu_plugin.so",
        var value => throw new ArgumentException($"Unsupported backend: {value}")
    };
}

static string BazelConfig(string backend)
    => NormalizeBackend(backend) switch
    {
        "cuda" => "--config=cuda",
        "rocm" => "--config=rocm",
        _ => string.Empty
    };

static async Task RunSmokeTestAsync(string xlaRoot, string backend)
{
    if (NormalizeBackend(backend) != "cpu")
    {
        Console.WriteLine("Skipping XLA wheel smoke test for GPU; use tools/probe-pjrt.cs against the prepared plugin on a GPU host.");
        return;
    }

    if (OperatingSystem.IsMacOS())
    {
        Console.WriteLine("Skipping XLA wheel smoke test on macOS; the wheel target uses Linux linker options.");
        return;
    }

    await RunBazelAsync(xlaRoot, "test //build_tools/pjrt_wheels:cpu_smoke_test");
}

static async Task ConfigureAsync(string xlaRoot, string backend)
{
    var configure = Path.Combine(xlaRoot, "configure.py");
    if (!File.Exists(configure))
        throw new FileNotFoundException($"XLA configure.py was not found: {configure}");

    await RunProcessAsync(xlaRoot, configure, $"--backend={NormalizeBackend(backend).ToUpperInvariant()}");
}

static async Task RunBazelAsync(string xlaRoot, string arguments)
{
    var bazel = FindOnPath("bazel") ?? FindOnPath("bazelisk")
        ?? throw new InvalidOperationException("Could not find bazel or bazelisk on PATH.");

    await RunProcessAsync(xlaRoot, bazel, arguments);
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

static string FindBuiltPlugin(string xlaRoot, string backend)
{
    string[] candidates = NormalizeBackend(backend) switch
    {
        "cpu" =>
        [
            Path.Combine(xlaRoot, "bazel-bin", "xla", "pjrt", "c", "pjrt_c_api_cpu_plugin.so"),
            Path.Combine(xlaRoot, "bazel-bin", "build_tools", "pjrt_wheels", "xla_plugins", "xla_cpu_pjrt", "xla_cpu_pjrt.so"),
            Path.Combine(xlaRoot, "xla", "pjrt", "c", "pjrt_c_api_cpu_plugin.so"),
            Path.Combine(xlaRoot, "build_tools", "pjrt_wheels", "xla_plugins", "xla_cpu_pjrt", "xla_cpu_pjrt.so")
        ],
        "cuda" or "rocm" =>
        [
            Path.Combine(xlaRoot, "bazel-bin", "xla", "pjrt", "c", "pjrt_c_api_gpu_plugin.so"),
            Path.Combine(xlaRoot, "xla", "pjrt", "c", "pjrt_c_api_gpu_plugin.so")
        ],
        var value => throw new ArgumentException($"Unsupported backend: {value}")
    };

    foreach (var candidate in candidates)
    {
        if (File.Exists(candidate))
            return Path.GetFullPath(candidate);
    }

    throw new FileNotFoundException(
        $"Could not find built {backend} PJRT plugin. Run this script with --build, or build " +
        $"{PluginTarget(backend)} manually.");
}

static string NormalizeBackend(string backend)
{
    var normalized = backend.Trim().ToLowerInvariant();
    return normalized == "gpu" ? "cuda" : normalized;
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

sealed record PrepareOptions(string XlaRoot, string OutputRoot, string Backend, bool Configure, bool Build, bool SmokeTest)
{
    public static PrepareOptions Parse(string[] args)
    {
        var xlaRoot = "/Users/ewoof/Desktop/HPD-Agent-InternalDocs/Helium/Reference/xla";
        var outputRoot = Path.Combine(Environment.CurrentDirectory, "artifacts", "pjrt");
        var backend = "cpu";
        var configure = false;
        var build = false;
        var smokeTest = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--xla-root" when i + 1 < args.Length:
                    xlaRoot = args[++i];
                    break;
                case "--output-root" when i + 1 < args.Length:
                    outputRoot = args[++i];
                    break;
                case "--backend" when i + 1 < args.Length:
                    backend = args[++i];
                    break;
                case "--configure":
                    configure = true;
                    break;
                case "--build":
                    build = true;
                    break;
                case "--smoke-test":
                    smokeTest = true;
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument: {args[i]}");
            }
        }

        return new PrepareOptions(xlaRoot, outputRoot, NormalizeBackendForOptions(backend), configure, build, smokeTest);
    }

    private static string NormalizeBackendForOptions(string backend)
    {
        var normalized = backend.Trim().ToLowerInvariant();
        return normalized == "gpu" ? "cuda" : normalized;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --file tools/prepare-pjrt-runtime.cs -- --backend cpu --configure --build --smoke-test");
        Console.WriteLine("  dotnet run --file tools/prepare-pjrt-runtime.cs -- --backend cuda --xla-root /path/to/xla --output-root ./artifacts/pjrt --configure --build");
        Console.WriteLine("  dotnet run --file tools/prepare-pjrt-runtime.cs -- --backend rocm --xla-root /path/to/xla --output-root ./artifacts/pjrt --configure --build");
    }
}
