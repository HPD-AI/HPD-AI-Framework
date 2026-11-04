namespace HPD.Environment.HostContainers;

using System.Diagnostics;
using System.Text;

internal static class HostContainersDockerCli
{
    public static string ResolvePath(
        HostContainersProviderOptions options)
    {
        string[] candidates = string.IsNullOrWhiteSpace(
            options.DockerCliPath)
            ?
            [
                "/usr/local/bin/docker",
                "/opt/homebrew/bin/docker",
                "/usr/bin/docker",
            ]
            : [Path.GetFullPath(options.DockerCliPath)];
        return candidates.FirstOrDefault(File.Exists) ??
            throw new InvalidOperationException(
                "HostContainers.DockerCliUnavailable: configure DockerCliPath or install the Docker CLI in a supported location.");
    }

    public static string? ResolveEngineSocket(
        HostContainersProviderOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.EngineSocketPath))
        {
            return null;
        }
        string socket = Path.GetFullPath(options.EngineSocketPath);
        if (!File.Exists(socket))
        {
            throw new InvalidOperationException(
                $"HostContainers.EngineSocketUnavailable: '{socket}' does not exist.");
        }
        return socket;
    }

    public static IReadOnlyList<string> EngineArguments(
        string? socketPath,
        IEnumerable<string> arguments)
    {
        var result = new List<string>();
        if (socketPath is not null)
        {
            result.Add("--host");
            result.Add($"unix://{socketPath}");
        }
        result.AddRange(arguments);
        return result;
    }

    public static async ValueTask<string> RunAsync(
        HostContainersProviderOptions options,
        string? socketPath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        string docker = ResolvePath(options);
        var start = new ProcessStartInfo
        {
            FileName = docker,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = options.WorkloadStateRoot ??
                Path.GetTempPath(),
        };
        start.Environment.Clear();
        start.Environment["PATH"] =
            "/usr/local/bin:/opt/homebrew/bin:/usr/bin:/bin";
        start.Environment["HOME"] =
            options.WorkloadStateRoot ?? Path.GetTempPath();
        if (socketPath is not null)
        {
            start.Environment["DOCKER_HOST"] =
                $"unix://{socketPath}";
        }
        foreach (string argument in
                 EngineArguments(socketPath, arguments))
        {
            start.ArgumentList.Add(argument);
        }
        Directory.CreateDirectory(start.WorkingDirectory);

        using var process = new Process
        {
            StartInfo = start,
        };
        if (!process.Start())
        {
            throw new InvalidOperationException(
                "HostContainers.DockerCliStartFailed: the Docker CLI process did not start.");
        }
        Task<string> stdout =
            process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr =
            process.StandardError.ReadToEndAsync(cancellationToken);
        using var deadline =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(deadline.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
            throw;
        }
        string output = await stdout.ConfigureAwait(false);
        string error = await stderr.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            if (error.Length > 1024)
            {
                error = error[..1024];
            }
            throw new InvalidOperationException(
                $"HostContainers.DockerEngineOperationFailed: Docker exited with code {process.ExitCode}: {error.Trim()}");
        }
        return output.Trim();
    }

    public static string ContainerName(
        string executionUnitId)
    {
        string identity = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    Encoding.UTF8.GetBytes(executionUnitId)))
            .ToLowerInvariant()[..24];
        return $"hpd-sandbox-{identity}";
    }
}
