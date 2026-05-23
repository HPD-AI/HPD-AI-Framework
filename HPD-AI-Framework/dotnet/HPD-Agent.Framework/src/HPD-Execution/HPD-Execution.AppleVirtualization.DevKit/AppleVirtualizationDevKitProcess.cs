namespace HPD.Execution.AppleVirtualization.DevKit;

using System.Diagnostics;
using System.Text;

public sealed record AppleVirtualizationDevKitProcessCommand
{
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string?> Environment { get; init; } = EmptyNullableStringDictionary;

    private static IReadOnlyDictionary<string, string?> EmptyNullableStringDictionary { get; } =
        new Dictionary<string, string?>(0, StringComparer.Ordinal);
}

public sealed record AppleVirtualizationDevKitProcessResult
{
    public required int ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public bool Succeeded => ExitCode == 0;
}

public interface IAppleVirtualizationDevKitProcessRunner
{
    ValueTask<AppleVirtualizationDevKitProcessResult> RunAsync(
        AppleVirtualizationDevKitProcessCommand command,
        CancellationToken cancellationToken = default);
}

public sealed class AppleVirtualizationDevKitProcessRunner : IAppleVirtualizationDevKitProcessRunner
{
    public async ValueTask<AppleVirtualizationDevKitProcessResult> RunAsync(
        AppleVirtualizationDevKitProcessCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.FileName);

        ProcessStartInfo startInfo = new()
        {
            FileName = command.FileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        if (!string.IsNullOrWhiteSpace(command.WorkingDirectory))
        {
            startInfo.WorkingDirectory = command.WorkingDirectory;
        }

        for (int i = 0; i < command.Arguments.Count; i++)
        {
            startInfo.ArgumentList.Add(command.Arguments[i]);
        }

        foreach (KeyValuePair<string, string?> item in command.Environment)
        {
            startInfo.Environment[item.Key] = item.Value;
        }

        using Process process = new() { StartInfo = startInfo };
        StringBuilder stdout = new();
        StringBuilder stderr = new();
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stdout.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stderr.AppendLine(args.Data);
            }
        };

        if (!process.Start())
        {
            return new()
            {
                ExitCode = 127,
                StandardError = "Failed to start process: " + command.FileName,
            };
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new()
        {
            ExitCode = process.ExitCode,
            StandardOutput = stdout.ToString(),
            StandardError = stderr.ToString(),
        };
    }
}
