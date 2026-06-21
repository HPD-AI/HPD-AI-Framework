using System.Diagnostics;
using Xunit;

namespace HPD.RAG.IntegrationTests.Tests;

/// <summary>
/// Group 8: AOT CI build check.
///
/// This test creates a minimal .NET 9 app in a temp directory that references the MRAG
/// Core and Pipeline packages via project reference, then runs `dotnet publish` with AOT
/// enabled, and asserts that no IL2026/IL3050 warnings originate from HPD.RAG.*
/// assemblies.
///
/// The test is skipped when the AOT toolchain is not available (i.e., not on a developer
/// machine with the .NET 9 AOT workload installed, or in a CI environment without it).
/// </summary>
public sealed class MragAotTests
{
    [Fact(Skip = "requires AOT toolchain — run manually on machines with .NET 9 AOT workload")]
    public async Task AotBuild_ProducesZeroMragSourcedTrimWarnings()
    {
        // Create a minimal temp app referencing HPD.RAG.Pipeline
        var tempDir = Directory.CreateTempSubdirectory("mrag-aot-test-");
        try
        {
            var repoRoot = FindRepoRoot();
            var pipelineCsproj = Path.Combine(
                repoRoot,
                "dotnet", "src", "HPD-RAG", "HPD.RAG.Pipeline",
                "HPD.RAG.Pipeline.csproj");
            var extensionsCsproj = Path.Combine(
                repoRoot,
                "dotnet", "src", "HPD-RAG", "HPD.RAG.Extensions",
                "HPD.RAG.Extensions.csproj");

            var csproj = $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net9.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <PublishAot>true</PublishAot>
                    <TrimmerRootDescriptor>roots.xml</TrimmerRootDescriptor>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{pipelineCsproj}" />
                    <ProjectReference Include="{extensionsCsproj}" />
                  </ItemGroup>
                </Project>
                """;

            var program = """
                using HPD.RAG.Pipeline;

                var pipeline = await MragPipeline.Create()
                    .WithName("aot-test")
                    .AddHandler("read", MragHandlerNames.ReadMarkdown)
                    .BuildIngestionAsync();

                Console.WriteLine(pipeline.PipelineName);
                """;

            var roots = """
                <linker>
                  <assembly fullname="mrag-aot-app" preserve="all" />
                </linker>
                """;

            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "mrag-aot-app.csproj"), csproj);
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "Program.cs"), program);
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "roots.xml"), roots);

            // Run dotnet publish with AOT
            var psi = new ProcessStartInfo("dotnet",
                "publish -r osx-arm64 --self-contained /p:PublishAot=true -c Release")
            {
                WorkingDirectory = tempDir.FullName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi)!;
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var allOutput = stdout + "\n" + stderr;

            // Find any IL2026 or IL3050 warnings from HPD.RAG.* assemblies
            var mragWarnings = allOutput
                .Split('\n')
                .Where(line =>
                    (line.Contains("IL2026") || line.Contains("IL3050")) &&
                    line.Contains("HPD-RAG", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.True(mragWarnings.Count == 0,
                $"Found {mragWarnings.Count} AOT trim warnings from HPD-RAG assemblies:\n" +
                string.Join("\n", mragWarnings));
        }
        finally
        {
            try { tempDir.Delete(recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    private static string FindRepoRoot()
    {
        // Walk up from this assembly's location until we find the HPD-Agent.slnx file
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("HPD-Agent.slnx").Length > 0)
                return dir.FullName;
            // Also check parent/dotnet pattern
            if (dir.GetFiles("*.slnx").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate repo root (directory containing HPD-Agent.slnx). " +
            "This test must be run from within the HPD-Agent-Framework repository.");
    }
}
