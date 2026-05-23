#:property TargetFramework=net10.0
#:property PublishAot=false
#:property PackAsTool=false
#:property IsPackable=false
#:property GenerateDocumentationFile=false
#:property LangVersion=preview
#:property Nullable=enable
#:property AllowUnsafeBlocks=true
#:project ../HPD.ML.Backends.csproj

using HPD.ML.Backends.Mlx;

var options = ProbeOptions.Parse(args);
var runtimeOptions = new MlxRuntimeOptions
{
    NativeLibraryPath = options.LibraryPath,
    SearchRoot = options.SearchRoot,
    Device = options.Device,
    AllowCpuFallback = options.AllowCpuFallback
};

var resolution = MlxRuntimeResolver.Resolve(runtimeOptions);
if (!resolution.IsAvailable || resolution.LibraryPath is null)
{
    Console.Error.WriteLine(resolution.ReasonUnavailable);
    foreach (var path in resolution.SearchedPaths)
        Console.Error.WriteLine($"  searched: {path}");
    Environment.ExitCode = 2;
    return;
}

Console.WriteLine($"Resolved MLX runtime source: {resolution.Source}");
Console.WriteLine($"Library: {resolution.LibraryPath}");

using var backend = MlxFloatBackend.Create(runtimeOptions);
Console.WriteLine($"Device: {backend.DeviceKind}");

var passed = true;
if (options.RunMatMulSmokeTest)
    passed &= RunMatMulSmoke(backend);
if (options.RunShapeSmokeTest)
    passed &= RunShapeSmoke(backend);
if (options.RunUnarySmokeTest)
    passed &= RunUnarySmoke(backend);

Environment.ExitCode = passed ? 0 : 3;

static bool NearlyEqual(float actual, float expected)
    => MathF.Abs(actual - expected) <= 1e-5f;

static bool RunMatMulSmoke(MlxFloatBackend backend)
{
    using var a = backend.CreateMatrix(2, 2, [1, 2, 3, 4]);
    using var b = backend.CreateMatrix(2, 2, [5, 6, 7, 8]);
    using var c = backend.MatMul(a, b);

    var result = c.ToArray();
    var matches = Matches(result, [19, 22, 43, 50]);
    Console.WriteLine($"MatMul2x2 output: [{string.Join(", ", result)}]");
    Console.WriteLine($"MatMul2x2 matched: {matches}");
    return matches;
}

static bool RunShapeSmoke(MlxFloatBackend backend)
{
    using var value = backend.CreateMatrix(2, 3, [1, 2, 3, 4, 5, 6]);
    using var reshaped = backend.Reshape(value, 3, 2);
    using var sliced = backend.Slice(value, 0, 1, 2, 2);
    using var scalar = backend.CreateMatrix(1, 1, [7]);
    using var broadcast = backend.Broadcast(scalar, 2, 3);
    using var concatenated = backend.Concatenate(value, value, axis: 1);

    var matches = Matches(reshaped.ToArray(), [1, 2, 3, 4, 5, 6]) &&
                  Matches(sliced.ToArray(), [2, 3, 5, 6]) &&
                  Matches(broadcast.ToArray(), [7, 7, 7, 7, 7, 7]) &&
                  Matches(concatenated.ToArray(), [1, 2, 3, 1, 2, 3, 4, 5, 6, 4, 5, 6]);

    Console.WriteLine($"Shape ops matched: {matches}");
    return matches;
}

static bool RunUnarySmoke(MlxFloatBackend backend)
{
    using var signed = backend.CreateMatrix(1, 3, [-1, 0, 1]);
    using var exp = backend.Exp(signed);
    using var tanh = backend.Tanh(signed);
    using var sigmoid = backend.Sigmoid(signed);
    using var logits = backend.CreateMatrix(1, 3, [1, 2, 3]);
    using var softmax = backend.Softmax(logits, axis: 1);

    var e1 = MathF.Exp(1);
    var e2 = MathF.Exp(2);
    var e3 = MathF.Exp(3);
    var denominator = e1 + e2 + e3;
    var matches = Matches(exp.ToArray(), [MathF.Exp(-1), 1, MathF.Exp(1)]) &&
                  Matches(tanh.ToArray(), [MathF.Tanh(-1), 0, MathF.Tanh(1)]) &&
                  Matches(sigmoid.ToArray(), [1 / (1 + MathF.Exp(1)), 0.5f, 1 / (1 + MathF.Exp(-1))]) &&
                  Matches(softmax.ToArray(), [e1 / denominator, e2 / denominator, e3 / denominator]);

    Console.WriteLine($"Unary/model ops matched: {matches}");
    return matches;
}

static bool Matches(float[] actual, float[] expected)
{
    if (actual.Length != expected.Length)
        return false;

    for (var i = 0; i < actual.Length; i++)
    {
        if (!NearlyEqual(actual[i], expected[i]))
            return false;
    }

    return true;
}

sealed record ProbeOptions(
    string? LibraryPath,
    string? SearchRoot,
    MlxDeviceKind Device,
    bool AllowCpuFallback,
    bool RunMatMulSmokeTest,
    bool RunShapeSmokeTest,
    bool RunUnarySmokeTest)
{
    public static ProbeOptions Parse(string[] args)
    {
        string? library = null;
        string? searchRoot = null;
        var device = MlxDeviceKind.Gpu;
        var allowCpuFallback = true;
        var runMatMulSmokeTest = false;
        var runShapeSmokeTest = false;
        var runUnarySmokeTest = false;

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
                case "--device" when i + 1 < args.Length:
                    device = ParseDevice(args[++i]);
                    break;
                case "--no-cpu-fallback":
                    allowCpuFallback = false;
                    break;
                case "--matmul-smoke" or "--matmul-milestone":
                    runMatMulSmokeTest = true;
                    break;
                case "--shape-smoke":
                    runShapeSmokeTest = true;
                    break;
                case "--unary-smoke" or "--model-smoke":
                    runUnarySmokeTest = true;
                    break;
                case "--all-smoke":
                    runMatMulSmokeTest = true;
                    runShapeSmokeTest = true;
                    runUnarySmokeTest = true;
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument: {args[i]}");
            }
        }

        return new ProbeOptions(library, searchRoot, device, allowCpuFallback, runMatMulSmokeTest, runShapeSmokeTest, runUnarySmokeTest);
    }

    private static MlxDeviceKind ParseDevice(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "cpu" => MlxDeviceKind.Cpu,
            "gpu" => MlxDeviceKind.Gpu,
            _ => throw new ArgumentException($"Unsupported MLX device: {value}")
        };

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --file tools/probe-mlx.cs -- --library /path/to/libmlxc.dylib --device gpu --matmul-smoke");
        Console.WriteLine("  dotnet run --file tools/probe-mlx.cs -- --library /path/to/libmlxc.dylib --device cpu --all-smoke --no-cpu-fallback");
        Console.WriteLine("  dotnet run --file tools/probe-mlx.cs -- --search-root . --device cpu");
    }
}
