#:property TargetFramework=net11.0
#:property PublishAot=true
#:property PackAsTool=false
#:property IsPackable=false
#:property GenerateDocumentationFile=false
#:property LangVersion=preview
#:property Nullable=enable
#:property AllowUnsafeBlocks=true
#:project ../HPD.ML.Backends.csproj

using HPD.ML.Backends.Mlx;
using HPD.ML.Backends.Mlx.Training;

var options = new MlxRuntimeOptions
{
    SearchRoot = FindRepoRoot(),
    Device = MlxDeviceKind.Cpu,
    AllowCpuFallback = false
};

using var backend = MlxFloatBackend.Create(options);
using var inputs = backend.CreateMatrix(4, 1, [1.0f, 2.0f, 3.0f, 4.0f]);
using var targets = backend.CreateMatrix(4, 1, [2.0f, 4.0f, 6.0f, 8.0f]);
using var weight = backend.CreateMatrix(1, 1, [0.0f]);
var optimizer = new MlxSgdOptimizer(backend, learningRate: 0.03f);

var initialLoss = Step(update: false);
for (var i = 0; i < 10; i++)
    _ = Step(update: true);
var finalLoss = Step(update: false);

Console.WriteLine($"InitialLoss={initialLoss}");
Console.WriteLine($"FinalLoss={finalLoss}");
Console.WriteLine($"Weight={weight.ToArray()[0]}");

if (!float.IsFinite(finalLoss) || finalLoss >= initialLoss)
    Environment.ExitCode = 3;

float Step(bool update)
{
    using var tape = new MlxTensorTape(backend);
    var x = tape.Watch(inputs);
    var y = tape.Watch(targets);
    var w = tape.Watch(weight);
    var loss = MlxLosses.MeanSquaredError(tape, tape.MatMul(x, w), y);
    var lossValue = loss.Value.ToArray()[0];
    if (update)
    {
        using var gradient = tape.Gradient(loss, w);
        optimizer.Step(weight, gradient);
    }

    return lossValue;
}

static string FindRepoRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, "artifacts", "mlx")) ||
            File.Exists(Path.Combine(directory.FullName, "dotnet", "shared", "src", "Helium", "Helium.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return Environment.CurrentDirectory;
}
