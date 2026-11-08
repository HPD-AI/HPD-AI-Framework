using HPD.Agent.GoGenerator;

if (args.Length == 0)
{
    Console.Error.WriteLine(
        "Usage: HPD-Agent.GoGenerator --input <openapi.json> --output <directory> " +
        "[--module <module>] [--package <package>]");
    return 1;
}

string? input = null;
string? output = null;
var module = "github.com/hpd-research/hpd-agent-sdk";
var package = "hpdagent";
for (var index = 0; index < args.Length; index++)
{
    switch (args[index])
    {
        case "--input":
            input = Next(args, ref index);
            break;
        case "--output":
            output = Next(args, ref index);
            break;
        case "--module":
            module = Next(args, ref index);
            break;
        case "--package":
            package = Next(args, ref index);
            break;
        default:
            throw new ArgumentException($"Unknown argument '{args[index]}'.");
    }
}
if (input is null || output is null)
    throw new ArgumentException("Both --input and --output are required.");
var manifest = await new GoSdkGenerator().GenerateAsync(
    new GoGeneratorOptions(
        Path.GetFullPath(input),
        Path.GetFullPath(output),
        module,
        package));
Console.WriteLine(
    $"Generated Go SDK: {manifest.OperationCount} operations, " +
    $"{manifest.ModelCount} models, {manifest.EventCount} events.");
Console.WriteLine($"Output: {Path.GetFullPath(output)}");
return 0;

static string Next(string[] values, ref int index)
{
    if (index + 1 >= values.Length)
        throw new ArgumentException($"Argument '{values[index]}' requires a value.");
    return values[++index];
}
