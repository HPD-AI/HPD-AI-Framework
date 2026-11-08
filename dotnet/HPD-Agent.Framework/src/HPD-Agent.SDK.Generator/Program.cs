using System.Text.Json;
using HPD.Agent.ClientGenerator;
using HPD.Agent.GoGenerator;
using HPD.Agent.PythonGenerator;
using HPD.Agent.TypeScriptGenerator;

try
{
    var options = GeneratorCommandOptions.Parse(args);
    object manifest = options.Target switch
    {
        "csharp" => await new CSharpSdkGenerator().GenerateAsync(
            new GeneratorOptions(
                options.Input,
                options.Output,
                options.NamespaceName,
                VerifyBuild: !options.SkipBuild,
                AllowPublicApiChange: options.AllowPublicApiChange,
                RuntimeCatalogFile: options.RuntimeCatalog)),
        "python" => await new PythonSdkGenerator().GenerateAsync(
            new PythonGeneratorOptions(
                options.Input,
                options.Output,
                options.PackageName)),
        "go" => await new GoSdkGenerator().GenerateAsync(
            new GoGeneratorOptions(
                options.Input,
                options.Output,
                options.ModulePath,
                options.GoPackageName)),
        "typescript" => await new TypeScriptSdkGenerator().GenerateAsync(
            new TypeScriptGeneratorOptions(
                options.Input,
                options.Output,
                options.NpmPackageName)),
        _ => throw new ArgumentException($"Unsupported target '{options.Target}'.")
    };
    Console.WriteLine(JsonSerializer.Serialize(
        manifest,
        new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    Console.Error.WriteLine(
        "Usage: hpd-sdk --target <csharp|python|go|typescript> --input <openapi.json> " +
        "--output <directory> [--namespace ...] [--package ...] [--module ...] " +
        "[--npm-package ...] " +
        "[--runtime-catalog ...] [--skip-build] [--allow-public-api-change]");
    return 1;
}

internal sealed record GeneratorCommandOptions(
    string Target,
    string Input,
    string Output,
    string NamespaceName,
    string PackageName,
    string GoPackageName,
    string ModulePath,
    string NpmPackageName,
    string? RuntimeCatalog,
    bool SkipBuild,
    bool AllowPublicApiChange)
{
    public static GeneratorCommandOptions Parse(string[] args)
    {
        string? target = null;
        string? input = null;
        string? output = null;
        var namespaceName = "HPD.Agent.Client";
        var packageName = "hpd_agent_client";
        var goPackageName = "hpdagent";
        var modulePath = "github.com/hpd-research/hpd-agent-sdk";
        var npmPackageName = "@hpd-research/hpd-agent-client";
        string? runtimeCatalog = null;
        var skipBuild = false;
        var allowPublicApiChange = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--target":
                    target = Next(args, ref index);
                    break;
                case "--input":
                    input = Next(args, ref index);
                    break;
                case "--output":
                    output = Next(args, ref index);
                    break;
                case "--namespace":
                    namespaceName = Next(args, ref index);
                    break;
                case "--package":
                    packageName = Next(args, ref index);
                    goPackageName = packageName.Replace('-', '_');
                    break;
                case "--go-package":
                    goPackageName = Next(args, ref index);
                    break;
                case "--module":
                    modulePath = Next(args, ref index);
                    break;
                case "--npm-package":
                    npmPackageName = Next(args, ref index);
                    break;
                case "--runtime-catalog":
                    runtimeCatalog = Next(args, ref index);
                    break;
                case "--skip-build":
                    skipBuild = true;
                    break;
                case "--allow-public-api-change":
                    allowPublicApiChange = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[index]}'.");
            }
        }
        if (target is not "csharp" and not "python" and not "go" and not "typescript")
            throw new ArgumentException("--target must be csharp, python, go, or typescript.");
        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output))
            throw new ArgumentException("Both --input and --output are required.");
        return new GeneratorCommandOptions(
            target,
            Path.GetFullPath(input),
            Path.GetFullPath(output),
            namespaceName,
            packageName,
            goPackageName,
            modulePath,
            npmPackageName,
            runtimeCatalog is null ? null : Path.GetFullPath(runtimeCatalog),
            skipBuild,
            allowPublicApiChange);
    }

    private static string Next(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"Argument '{args[index]}' requires a value.");
        return args[++index];
    }
}
