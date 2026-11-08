namespace HPD.Agent.ClientGenerator;

/// <summary>Inputs and output settings for one deterministic C# SDK generation.</summary>
public sealed record GeneratorOptions(
    string InputFile,
    string OutputDirectory,
    string Namespace = "HPD.Agent.Client",
    bool VerifyBuild = true,
    bool AllowPublicApiChange = false,
    string? RuntimeCatalogFile = null)
{
    /// <summary>Parses the command-line contract used by the SDK build pipeline.</summary>
    public static GeneratorOptions Parse(string[] args)
    {
        if (args.Length == 0)
            throw new ArgumentException(
                "Usage: HPD-Agent.ClientGenerator --input <openapi.json> --output <directory> " +
                "[--namespace <namespace>] [--runtime-catalog <catalog.json>] " +
                "[--skip-build] [--allow-public-api-change]");

        string? input = null;
        string? output = null;
        var namespaceName = "HPD.Agent.Client";
        var verifyBuild = true;
        var allowPublicApiChange = false;
        string? runtimeCatalog = null;
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--input":
                    input = Next(args, ref index, argument);
                    break;
                case "--output":
                    output = Next(args, ref index, argument);
                    break;
                case "--namespace":
                    namespaceName = Next(args, ref index, argument);
                    break;
                case "--skip-build":
                    verifyBuild = false;
                    break;
                case "--allow-public-api-change":
                    allowPublicApiChange = true;
                    break;
                case "--runtime-catalog":
                    runtimeCatalog = Next(args, ref index, argument);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{argument}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output))
            throw new ArgumentException("Both --input and --output are required.");
        if (namespaceName.Split('.').Any(static segment =>
                string.IsNullOrWhiteSpace(segment) ||
                !char.IsLetter(segment[0]) && segment[0] != '_' ||
                segment.Skip(1).Any(static value => !char.IsLetterOrDigit(value) && value != '_')))
        {
            throw new ArgumentException($"Namespace '{namespaceName}' is invalid.");
        }

        return new GeneratorOptions(
            Path.GetFullPath(input),
            Path.GetFullPath(output),
            namespaceName,
            verifyBuild,
            allowPublicApiChange,
            string.IsNullOrWhiteSpace(runtimeCatalog) ? null : Path.GetFullPath(runtimeCatalog));
    }

    private static string Next(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Argument '{name}' requires a value.");
        return args[++index];
    }
}
