#:property TargetFramework=net11.0
#:property PublishAot=false
#:property PackAsTool=false
#:property IsPackable=false
#:property GenerateDocumentationFile=false
#:property LangVersion=preview
#:property Nullable=enable
#:property AllowUnsafeBlocks=true
#:project ../HPD.ML.Backends.csproj

using HPD.ML.Backends.Pjrt;

var options = ProbeOptions.Parse(args);
var resolverOptions = new PjrtPluginResolverOptions
{
    ExplicitPath = options.LibraryPath,
    SearchRoot = options.SearchRoot,
    Backend = options.Backend,
    ClientOptions = options.ToClientCreateOptions()
};

if (options.RunMatMulSmokeTest)
{
    if (!PjrtSmokeTest.TryRunLocalMatMulMilestone(
            resolverOptions,
            out var result,
            out var reasonUnavailable))
    {
        Console.Error.WriteLine(reasonUnavailable);
        Environment.ExitCode = 2;
        return;
    }

    var milestoneResult = result ?? throw new InvalidOperationException("PJRT milestone returned success without a result.");
    PrintMilestoneResult(milestoneResult);
    Environment.ExitCode = milestoneResult.OutputMatchesExpected ? 0 : 3;
    return;
}

var resolution = PjrtPluginResolver.Resolve(resolverOptions);

if (!resolution.IsAvailable || resolution.LibraryPath is null)
{
    Console.Error.WriteLine(resolution.ReasonUnavailable);
    Environment.ExitCode = 2;
    return;
}

Console.WriteLine($"Resolved plugin source: {resolution.Source}");
using var backend = PjrtFloatBackend.Create(new PjrtPluginResolverOptions
{
    ExplicitPath = resolution.LibraryPath,
    Backend = options.Backend,
    ClientOptions = options.ToClientCreateOptions()
});
var info = backend.PluginInfo;

Console.WriteLine($"Library: {info.LibraryPath}");
Console.WriteLine($"PJRT API: {info.ApiVersion}");
Console.WriteLine($"PJRT_Api struct size: {info.ApiStructSize}");

if (options.CreateClient)
{
    var clientInfo = backend.ClientInfo;
    Console.WriteLine($"Platform: {clientInfo.PlatformName}");
    Console.WriteLine($"Platform version: {clientInfo.PlatformVersion}");
    Console.WriteLine($"Devices: {clientInfo.DeviceCount}");
}

static void PrintMilestoneResult(PjrtMatMulSmokeResult result)
{
    Console.WriteLine($"Resolved plugin source: {result.Resolution.Source}");
    Console.WriteLine($"Library: {result.PluginInfo.LibraryPath}");
    Console.WriteLine($"PJRT API: {result.PluginInfo.ApiVersion}");
    Console.WriteLine($"PJRT_Api struct size: {result.PluginInfo.ApiStructSize}");
    Console.WriteLine($"Platform: {result.ClientInfo.PlatformName}");
    Console.WriteLine($"Platform version: {result.ClientInfo.PlatformVersion}");
    Console.WriteLine($"Devices: {result.ClientInfo.DeviceCount}");
    Console.WriteLine($"MatMul2x2 expected: [{string.Join(", ", result.Expected)}]");
    Console.WriteLine($"MatMul2x2 output: [{string.Join(", ", result.Output)}]");
    Console.WriteLine($"MatMul2x2 matched: {result.OutputMatchesExpected}");
    Console.WriteLine($"Cached executables: {result.CachedExecutableCount}");
    Console.WriteLine($"Backend disposed: {result.BackendDisposed}");
}

sealed record ProbeOptions(
    string? LibraryPath,
    string? SearchRoot,
    string Backend,
    bool CreateClient,
    bool RunMatMulSmokeTest,
    IReadOnlyList<long>? VisibleDevices,
    string? Allocator,
    float? MemoryFraction,
    bool? Preallocate)
{
    public PjrtClientCreateOptions? ToClientCreateOptions()
    {
        if (VisibleDevices is null &&
            Allocator is null &&
            MemoryFraction is null &&
            Preallocate is null &&
            !Backend.Equals("rocm", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new PjrtClientCreateOptions
        {
            PlatformName = Backend.Equals("rocm", StringComparison.OrdinalIgnoreCase) ? "ROCM" : null,
            VisibleDevices = VisibleDevices,
            Allocator = Allocator,
            MemoryFraction = MemoryFraction,
            Preallocate = Preallocate
        };
    }

    public static ProbeOptions Parse(string[] args)
    {
        string? library = null;
        string? searchRoot = null;
        var backend = "cpu";
        var createClient = false;
        var runMatMulSmokeTest = false;
        IReadOnlyList<long>? visibleDevices = null;
        string? allocator = null;
        float? memoryFraction = null;
        bool? preallocate = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--library" when i + 1 < args.Length:
                    library = args[++i];
                    break;
                case "--search-root" when i + 1 < args.Length:
                    searchRoot = args[++i];
                    break;
                case "--backend" when i + 1 < args.Length:
                    backend = args[++i];
                    break;
                case "--visible-devices" when i + 1 < args.Length:
                    visibleDevices = ParseVisibleDevices(args[++i]);
                    break;
                case "--allocator" when i + 1 < args.Length:
                    allocator = args[++i];
                    break;
                case "--memory-fraction" when i + 1 < args.Length:
                    memoryFraction = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--preallocate" when i + 1 < args.Length:
                    preallocate = bool.Parse(args[++i]);
                    break;
                case "--client":
                    createClient = true;
                    break;
                case "--matmul-smoke":
                    createClient = true;
                    runMatMulSmokeTest = true;
                    break;
                case "--matmul-milestone":
                    createClient = true;
                    runMatMulSmokeTest = true;
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument: {args[i]}");
            }
        }

        return new ProbeOptions(library, searchRoot, backend, createClient, runMatMulSmokeTest, visibleDevices, allocator, memoryFraction, preallocate);
    }

    private static IReadOnlyList<long> ParseVisibleDevices(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(long.Parse)
            .ToArray();

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --file tools/probe-pjrt.cs -- --library /path/to/pjrt_plugin");
        Console.WriteLine("  dotnet run --file tools/probe-pjrt.cs -- --search-root ./artifacts/pjrt --backend cpu");
        Console.WriteLine("  dotnet run --file tools/probe-pjrt.cs -- --search-root ./artifacts/pjrt --backend cuda --visible-devices 0 --client");
        Console.WriteLine("  dotnet run --file tools/probe-pjrt.cs -- --library /path/to/pjrt_plugin --client");
        Console.WriteLine("  dotnet run --file tools/probe-pjrt.cs -- --search-root ./artifacts/pjrt --backend cpu --matmul-smoke");
        Console.WriteLine("  dotnet run --file tools/probe-pjrt.cs -- --backend cuda --library /path/to/pjrt_plugin --visible-devices 0 --matmul-milestone");
    }
}
