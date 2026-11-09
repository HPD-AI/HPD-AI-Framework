#:property TargetFramework=net10.0

using System.Diagnostics;

var frameworkRoot = FindFrameworkRoot();
var temporaryRoot = Path.Combine(
    frameworkRoot,
    "obj",
    "automatic-composition-smoke",
    Guid.NewGuid().ToString("N"));
var feed = Path.Combine(temporaryRoot, "feed");
var packages = Path.Combine(temporaryRoot, "packages");
var consumer = Path.Combine(temporaryRoot, "consumer");

Directory.CreateDirectory(feed);
Directory.CreateDirectory(packages);
Directory.CreateDirectory(consumer);

await PackAsync(
    frameworkRoot,
    "src/HPD-Agent/HPD-Agent.csproj",
    feed);
await PackAsync(
    frameworkRoot,
    "src/HPD-Agent.Hosting/HPD-Agent.Hosting.csproj",
    feed);
await PackAsync(
    frameworkRoot,
    "src/HPD-Agent.AspNetCore/HPD-Agent.AspNetCore.csproj",
    feed);
await PackAsync(
    frameworkRoot,
    "src/HPD-Agent.Providers/HPD-Agent.Providers.Anthropic/HPD-Agent.Providers.Anthropic.csproj",
    feed);

await CopyPublishedDependenciesAsync(feed);

await File.WriteAllTextAsync(
    Path.Combine(consumer, "AutomaticCompositionConsumer.csproj"),
    """
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="HPD-Agent.AspNetCore" Version="1.0.0" />
        <PackageReference Include="HPD-Agent.Providers.Anthropic" Version="1.0.0" />
        <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.13" />
      </ItemGroup>
    </Project>
    """);

await File.WriteAllTextAsync(
    Path.Combine(consumer, "Program.cs"),
    """
    using HPD.Agent;
    using HPD.Agent.AspNetCore;
    using Microsoft.Extensions.DependencyInjection;

    var services = new ServiceCollection();
    services.AddHPDAgentHost("automatic");
    using var provider = services.BuildServiceProvider();
    var composition = provider.GetRequiredService<HPDApplicationComposition>();
    return composition.Providers.Descriptors.TryGet("anthropic", out _) ? 0 : 1;
    """);

var restoreArguments = new[]
{
    "restore", "AutomaticCompositionConsumer.csproj",
    "--force",
    "--source", feed,
    "--source", "https://api.nuget.org/v3/index.json",
    $"-p:RestorePackagesPath={packages}",
    "--nologo"
};
await RunAsync(consumer, "dotnet", restoreArguments);
await RunAsync(
    consumer,
    "dotnet",
    [
        "run", "--project", "AutomaticCompositionConsumer.csproj",
        "--no-restore",
        $"-p:RestorePackagesPath={packages}"
    ]);

await RunAsync(
    consumer,
    "dotnet",
    [
        "build", "AutomaticCompositionConsumer.csproj",
        "--no-restore",
        "-t:Rebuild",
        $"-p:RestorePackagesPath={packages}",
        "-p:HpdAgentExcludeProviders=anthropic"
    ]);
await RunAsync(
    consumer,
    "dotnet",
    [
        "run", "--project", "AutomaticCompositionConsumer.csproj",
        "--no-build", "--no-restore",
        $"-p:RestorePackagesPath={packages}",
        "-p:HpdAgentExcludeProviders=anthropic"
    ],
    expectedExitCode: 1);

var diagnostics = await RunAndCaptureAsync(
    consumer,
    "dotnet",
    [
        "build", "AutomaticCompositionConsumer.csproj",
        "--no-restore",
        "-t:Rebuild",
        $"-p:RestorePackagesPath={packages}",
        "-p:HpdAgentExcludeProviders=missing-provider"
    ]);
if (diagnostics.ExitCode != 0 || !diagnostics.Output.Contains("HPDP016", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "Expected unused provider exclusion diagnostic HPDP016." +
        Environment.NewLine +
        diagnostics.Output);
}

var fileBasedPath = Path.Combine(temporaryRoot, "FileBasedComposition.cs");
await File.WriteAllTextAsync(
    fileBasedPath,
    $$"""
    #:sdk Microsoft.NET.Sdk.Web
    #:property TargetFramework=net10.0
    #:property CustomAfterMicrosoftCommonTargets={{Path.Combine(frameworkRoot, "src/HPD-Agent/buildTransitive/HPD-Agent.FileBased.targets")}}
    #:project {{Path.Combine(frameworkRoot, "src/HPD-Agent/HPD-Agent.csproj")}}
    #:project {{Path.Combine(frameworkRoot, "src/HPD-Agent.AspNetCore/HPD-Agent.AspNetCore.csproj")}}
    #:project {{Path.Combine(frameworkRoot, "src/HPD-Agent.SourceGenerator/HPD-Agent.SourceGenerator.csproj")}}
    #:project {{Path.Combine(frameworkRoot, "src/HPD-Agent.Providers/HPD-Agent.Providers.OpenAI/HPD-Agent.Providers.OpenAI.csproj")}}

    using HPD.Agent;
    using Microsoft.Extensions.DependencyInjection;

    var services = new ServiceCollection();
    services.AddHPD();
    using var provider = services.BuildServiceProvider();
    var composition = provider.GetRequiredService<HPDApplicationComposition>();
    return composition.Providers.Descriptors.TryGet("openai", out _) ? 0 : 1;
    """);
await RunAsync(temporaryRoot, "dotnet", ["run", fileBasedPath]);

Console.WriteLine($"automatic composition package smoke passed: {temporaryRoot}");
return;

static string FindFrameworkRoot()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "HPD-Agent.slnx")))
            return directory.FullName;
        directory = directory.Parent;
    }

    throw new InvalidOperationException(
        "Run this smoke test from the HPD-Agent.Framework source tree.");
}

static async Task PackAsync(
    string workingDirectory,
    string project,
    string feed)
{
    // Restore every target framework explicitly. A preceding "-f net10.0"
    // test/build can leave the shared assets file with only that framework.
    await RunAsync(
        workingDirectory,
        "dotnet",
        [
            "restore", project,
            "--force-evaluate",
            "--nologo"
        ]);
    await RunAsync(
        workingDirectory,
        "dotnet",
        [
            "pack", project,
            "-c", "Release",
            "--no-restore",
            "--nologo",
            "-o", feed
        ]);
}

static async Task CopyPublishedDependenciesAsync(string feed)
{
    var result = await RunAndCaptureAsync(
        Directory.GetCurrentDirectory(),
        "dotnet",
        ["nuget", "locals", "global-packages", "--list"]);
    var line = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(static value => value.Trim())
        .FirstOrDefault(static value =>
            value.StartsWith("global-packages:", StringComparison.Ordinal));
    if (line is null)
        throw new InvalidOperationException("Unable to locate the NuGet global packages folder.");

    var packages = line[(line.IndexOf(':') + 1)..].Trim().TrimEnd('/');
    foreach (var package in new[]
    {
        ("hpd-environment.contracts", "1.0.0"),
        ("hpd-environment.runtime", "1.0.0"),
        ("hpd-events", "1.0.0"),
        ("hpd-serialization", "1.0.0")
    })
    {
        var source = Path.Combine(
            packages,
            package.Item1,
            package.Item2,
            $"{package.Item1}.{package.Item2}.nupkg");
        if (File.Exists(source))
            File.Copy(source, Path.Combine(feed, Path.GetFileName(source)), overwrite: true);
    }
}

static async Task RunAsync(
    string workingDirectory,
    string fileName,
    IReadOnlyList<string> arguments,
    int expectedExitCode = 0)
{
    var result = await RunAndCaptureAsync(workingDirectory, fileName, arguments);
    if (result.ExitCode != expectedExitCode)
    {
        throw new InvalidOperationException(
            $"Command failed ({result.ExitCode}, expected {expectedExitCode}): " +
            $"{fileName} {string.Join(' ', arguments)}{Environment.NewLine}{result.Output}");
    }
}

static async Task<(int ExitCode, string Output)> RunAndCaptureAsync(
    string workingDirectory,
    string fileName,
    IReadOnlyList<string> arguments)
{
    var startInfo = new ProcessStartInfo(fileName)
    {
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    foreach (var argument in arguments)
        startInfo.ArgumentList.Add(argument);

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Unable to start '{fileName}'.");
    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    return (process.ExitCode, $"{await outputTask}{await errorTask}");
}
