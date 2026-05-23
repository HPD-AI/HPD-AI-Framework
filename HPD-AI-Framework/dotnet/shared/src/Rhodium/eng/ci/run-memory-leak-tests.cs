#:property TargetFramework=net10.0
#:property PublishAot=false
#:property PackAsTool=false
#:property IsPackable=false
#:property GenerateDocumentationFile=false

using System.Diagnostics;
using System.Text;

const string ProjectPath = "HPD-AI-Framework/dotnet/shared/src/Rhodium/test/Rhodium.Kernel.Tests/Rhodium.Kernel.Tests.csproj";
const string LogPath = "memory-results.log";

var exitCode = await DotnetAsync(
    [
        "test",
        ProjectPath,
        "--filter",
        "Category=MemoryLeak",
        "--logger",
        "trx;LogFileName=memory-results.trx"
    ],
    LogPath);

return exitCode;

static async Task<int> DotnetAsync(IReadOnlyList<string> arguments, string logPath)
{
    var startInfo = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };

    foreach (var argument in arguments)
        startInfo.ArgumentList.Add(argument);

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Failed to start dotnet process.");

    var standardOutput = process.StandardOutput.ReadToEndAsync();
    var standardError = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    var output = await standardOutput;
    var error = await standardError;
    var combined = new StringBuilder(output.Length + error.Length + 1)
        .Append(output)
        .Append(error)
        .ToString();

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
