using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Rhodium.Benchmarks;

internal sealed record SmokeReportEnvironment(
    DateTimeOffset GeneratedAtUtc,
    string MachineName,
    string OSDescription,
    string ProcessArchitecture,
    string FrameworkDescription,
    string RuntimeVersion,
    int LogicalProcessorCount,
    string? GitThread,
    string? GitCommit,
    bool? GitTrackedChanges)
{
    public static SmokeReportEnvironment Create()
        => new(
            DateTimeOffset.UtcNow,
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.FrameworkDescription,
            Environment.Version.ToString(),
            Environment.ProcessorCount,
            Git("rev-parse", "--abbrev-ref", "HEAD"),
            Git("rev-parse", "--short=12", "HEAD"),
            HasTrackedChanges());

    private static bool? HasTrackedChanges()
    {
        var status = Git("status", "--porcelain", "--untracked-files=no");
        return status is null ? null : status.Length > 0;
    }

    private static string? Git(params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
