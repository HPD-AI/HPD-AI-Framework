#:property TargetFramework=net10.0
#:property PublishAot=false
#:property PackAsTool=false
#:property IsPackable=false
#:property GenerateDocumentationFile=false

using System.Diagnostics;

const string RhodiumRoot = "HPD-AI-Framework/dotnet/shared/src/Rhodium";

if (args.Contains("--help", StringComparer.OrdinalIgnoreCase)
    || args.Contains("-h", StringComparer.OrdinalIgnoreCase))
{
    PrintUsage();
    return 0;
}

try
{
    ValidateArguments(args);

    var specPath = GetOptionValue(args, "--spec")
        ?? throw new InvalidOperationException("--spec <PATH> is required.");
    var reportDirectory = GetOptionValue(args, "--report-dir")
        ?? throw new InvalidOperationException("--report-dir <PATH> is required.");

    var manifestPath = Path.Combine(reportDirectory, "external-parity-manifest.json");

    var buildExitCode = await DotnetAsync(
        [
            "run",
            $"{RhodiumRoot}/eng/ci/build-external-parity-manifest.cs",
            "--",
            "--spec",
            specPath,
            "--out",
            manifestPath
        ]);
    if (buildExitCode != 0)
        return buildExitCode;

    return await DotnetAsync(
        [
            "run",
            $"{RhodiumRoot}/eng/ci/verify-rhodium.cs",
            "--",
            "--keep-reports",
            "--require-clean-git",
            "--require-target-hardware",
            "--external-parity-manifest",
            manifestPath,
            "--require-external-parity",
            "--require-release-evidence",
            "--report-dir",
            reportDirectory
        ]);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

static async Task<int> DotnetAsync(IReadOnlyList<string> arguments)
{
    Console.WriteLine("$ dotnet " + string.Join(' ', arguments.Select(QuoteIfNeeded)));

    var startInfo = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

    foreach (var argument in arguments)
        startInfo.ArgumentList.Add(argument);

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Failed to start dotnet process.");

    var standardOutput = process.StandardOutput.ReadToEndAsync();
    var standardError = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    var combined = (await standardOutput) + await standardError;
    foreach (var line in ReadLines(combined))
    {
        if (ShouldSuppress(line))
            continue;

        Console.WriteLine(line);
    }

    return process.ExitCode;
}

static void ValidateArguments(IReadOnlyList<string> values)
{
    for (var i = 0; i < values.Count; i++)
    {
        var value = values[i];
        if (!value.StartsWith("--", StringComparison.Ordinal))
            continue;

        if (IsOption(value, "--spec") || IsOption(value, "--report-dir"))
        {
            var optionName = value.Contains('=', StringComparison.Ordinal) ? value[..value.IndexOf('=', StringComparison.Ordinal)] : value;
            var optionValue = GetOptionValue(values, optionName);
            if (string.IsNullOrWhiteSpace(optionValue) || optionValue.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"{optionName} requires a non-empty value.");

            if (!value.Contains('=', StringComparison.Ordinal))
                i++;
            continue;
        }

        if (string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        throw new ArgumentException($"Unknown release certification option: {value}. Run with -- --help for usage.");
    }
}

static bool IsOption(string value, string optionName)
    => string.Equals(value, optionName, StringComparison.OrdinalIgnoreCase)
        || value.StartsWith(optionName + "=", StringComparison.OrdinalIgnoreCase);

static string? GetOptionValue(IReadOnlyList<string> values, string name)
{
    var prefix = name + "=";
    for (var i = 0; i < values.Count - 1; i++)
    {
        if (values[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return values[i][prefix.Length..];

        if (string.Equals(values[i], name, StringComparison.OrdinalIgnoreCase))
            return values[i + 1];
    }

    if (values.Count > 0 && values[^1].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        return values[^1][prefix.Length..];

    return null;
}

static bool ShouldSuppress(string line)
{
    if (string.IsNullOrWhiteSpace(line))
        return true;

    return line.Contains("warning", StringComparison.OrdinalIgnoreCase)
        || line.Contains("Determining projects to restore", StringComparison.Ordinal)
        || line.Contains("All projects are up-to-date", StringComparison.Ordinal)
        || line.Contains("CSSM_ModuleLoad", StringComparison.Ordinal)
        || line.Contains("Restored ", StringComparison.Ordinal)
        || line.Contains(" -> ", StringComparison.Ordinal);
}

static IEnumerable<string> ReadLines(string text)
{
    using var reader = new StringReader(text);
    while (reader.ReadLine() is { } line)
        yield return line;
}

static string QuoteIfNeeded(string value)
    => value.Any(char.IsWhiteSpace) ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : value;

static void PrintUsage()
{
    Console.WriteLine(
        """
        Usage:
          dotnet run HPD-AI-Framework/dotnet/shared/src/Rhodium/eng/ci/certify-rhodium-release.cs -- --spec <PATH> --report-dir <PATH>

        Required options:
          --spec <PATH>        Hash-free external parity manifest spec.
          --report-dir <PATH>  Release evidence directory. The final external parity manifest and retained verifier reports are written here.

        Behavior:
          1. Builds <report-dir>/external-parity-manifest.json from the supplied spec.
          2. Runs verify-rhodium.cs with --keep-reports, --require-clean-git,
             --require-target-hardware, --require-external-parity, and
             --require-release-evidence.

        This wrapper is meant for the final target-machine release certification run.
        It intentionally does not provide skip flags.
        """);
}
