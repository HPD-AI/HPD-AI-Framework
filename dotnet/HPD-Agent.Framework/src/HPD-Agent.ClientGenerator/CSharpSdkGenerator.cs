using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Agent.ClientGenerator.Generation;
using HPD.Agent.ClientGenerator.RuntimeCatalog;
using HPD.Agent.SDK.Contract;

namespace HPD.Agent.ClientGenerator;

/// <summary>Deterministically renders, verifies, and atomically publishes an HPD C# SDK.</summary>
public sealed class CSharpSdkGenerator
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Generates one standalone SDK project from an OpenAPI 3.1 contract.</summary>
    public async Task<GenerationManifest> GenerateAsync(
        GeneratorOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var contract = await OpenApiContractReader.ReadAsync(options.InputFile, cancellationToken)
            .ConfigureAwait(false);
        var sdkIr = HpdSdkIrBuilder.Build(contract, GeneratorMetadata.Version);
        HpdSdkIrValidator.Validate(sdkIr);
        var sdkIrJson = HpdSdkIrWriter.Write(sdkIr);
        var sdkIrSha256 = "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(sdkIrJson))).ToLowerInvariant();
        var names = new CSharpNames(sdkIr);
        var modelRenderer = new ModelRenderer(sdkIr, names, options.Namespace);
        var runtimeRenderer = new RuntimeRenderer(sdkIr, names, options.Namespace);
        var clientRenderer = new ClientRenderer(sdkIr, names, options.Namespace);
        var generatedSources = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["Models.g.cs"] = modelRenderer.RenderModels(),
            ["JsonContext.g.cs"] = modelRenderer.RenderJsonContext(),
            ["Runtime.g.cs"] = runtimeRenderer.RenderRuntime(),
            ["Services.g.cs"] = clientRenderer.RenderServices()
        };
        var runtimeCatalog = options.RuntimeCatalogFile is null
            ? null
            : await RuntimeCatalogReader.ReadAsync(options.RuntimeCatalogFile, cancellationToken)
                .ConfigureAwait(false);
        if (runtimeCatalog is not null)
        {
            generatedSources["RuntimePayloads.g.cs"] =
                new RuntimePayloadRenderer(runtimeCatalog, options.Namespace).Render();
        }
        var publicApi = RenderPublicApi(generatedSources.Values);
        var publicApiSha256 = "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(publicApi))).ToLowerInvariant();
        var destination = Path.GetFullPath(options.OutputDirectory);
        var existingPublicApi = Path.Combine(destination, "public-api.txt");
        if (!options.AllowPublicApiChange && File.Exists(existingPublicApi))
        {
            var existing = await File.ReadAllTextAsync(existingPublicApi, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(existing, publicApi, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The generated public API differs from the existing snapshot. " +
                    "Review public-api.txt, then rerun with --allow-public-api-change for an intentional change.");
            }
        }
        var manifest = new GenerationManifest(
            GeneratorMetadata.Version,
            sdkIr.IrVersion,
            contract.OpenApiVersion,
            contract.OpenApiSha256,
            contract.EventDigest,
            sdkIr.InputDigest,
            sdkIrSha256,
            publicApiSha256,
            contract.Operations.Count,
            names.ModelNames.Count,
            contract.Events.Count,
            contract.PublicInputs.Count,
            sdkIr.Diagnostics.Count,
            runtimeCatalog?.Schemas.Count ?? 0);

        var files = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            [".gitignore"] = "bin/\nobj/\n",
            ["hpd-client.generated.json"] = JsonSerializer.Serialize(manifest, ManifestJsonOptions) + "\n",
            ["sdk-ir.json"] = sdkIrJson,
            ["public-api.txt"] = publicApi,
            ["README.md"] = RenderReadme(contract, manifest)
        };
        files["generated/HpdAgentSdk.csproj"] = RenderProjectFile();
        foreach (var source in generatedSources)
            files["generated/" + source.Key] = source.Value;
        files["runtime/HPD-Agent.Client.csproj"] = RenderRuntimeProjectFile();
        foreach (var runtimeSource in await ReadRuntimeSourcesAsync(cancellationToken).ConfigureAwait(false))
            files["runtime/" + runtimeSource.Key] = runtimeSource.Value;

        var parent = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("The SDK output directory has no parent.");
        Directory.CreateDirectory(parent);
        var staging = destination + $".staging-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var previous = destination + $".previous-{Environment.ProcessId}-{Guid.NewGuid():N}";
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var file in files)
            {
                var path = Path.Combine(staging, file.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, file.Value, cancellationToken).ConfigureAwait(false);
            }

            if (options.VerifyBuild)
            {
                await BuildAsync(
                        Path.Combine(staging, "generated", "HpdAgentSdk.csproj"),
                        cancellationToken)
                    .ConfigureAwait(false);
                DeleteDirectory(Path.Combine(staging, "bin"));
                DeleteDirectory(Path.Combine(staging, "obj"));
                DeleteDirectory(Path.Combine(staging, "generated", "bin"));
                DeleteDirectory(Path.Combine(staging, "generated", "obj"));
                DeleteDirectory(Path.Combine(staging, "runtime", "bin"));
                DeleteDirectory(Path.Combine(staging, "runtime", "obj"));
            }

            var movedPrevious = false;
            if (Directory.Exists(destination))
            {
                Directory.Move(destination, previous);
                movedPrevious = true;
            }
            try
            {
                Directory.Move(staging, destination);
            }
            catch
            {
                if (movedPrevious && !Directory.Exists(destination))
                    Directory.Move(previous, destination);
                throw;
            }
            DeleteDirectory(previous);
        }
        catch
        {
            DeleteDirectory(staging);
            throw;
        }

        return manifest;
    }

    private static string RenderProjectFile() =>
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
            <IsAotCompatible>true</IsAotCompatible>
            <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
            <EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>
            <GenerateDocumentationFile>false</GenerateDocumentationFile>
            <NoWarn>$(NoWarn);CA2255</NoWarn>
          </PropertyGroup>
          <ItemGroup>
            <Compile Remove="examples/**/*.cs" />
            <ProjectReference Include="../runtime/HPD-Agent.Client.csproj" />
          </ItemGroup>
        </Project>
        """;

    private static string RenderRuntimeProjectFile() =>
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
            <IsAotCompatible>true</IsAotCompatible>
            <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
            <EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>
            <IsPackable>true</IsPackable>
            <PackageId>HPD-Agent.Client</PackageId>
          </PropertyGroup>
        </Project>
        """;

    private static string RenderReadme(
        OpenApiContract contract,
        GenerationManifest manifest) =>
        $"""
        # Generated HPD Agent C# Client

        This project was generated from `{contract.Title}` version `{contract.ApiVersion}`.

        - OpenAPI: `{contract.OpenApiVersion}`
        - HPD Agent SDK IR: `{manifest.IrVersion}`
        - OpenAPI SHA-256: `{manifest.OpenApiSha256}`
        - Event digest: `{manifest.EventDigest ?? "<not declared>"}`
        - Input digest: `{manifest.InputDigest}`
        - IR SHA-256: `{manifest.IrSha256}`
        - Public API SHA-256: `{manifest.PublicApiSha256}`
        - Operations: {manifest.OperationCount}
        - Models: {manifest.ModelCount}
        - Public inputs: {manifest.InputCount}
        - IR diagnostics: {manifest.DiagnosticCount}
        - Output events: {manifest.EventCount}
        - Typed runtime schemas: {manifest.RuntimeSchemaCount}

        The package contains the invariant runtime project under `runtime/` and
        application-specific generated code under `generated/`. Do not edit either
        generated surface manually. Regenerate from the application's OpenAPI
        document and review `sdk-ir.json`, `hpd-client.generated.json`, and
        `public-api.txt` as part of the contract change. Regeneration fails when
        the public API snapshot changes; rerun with `--allow-public-api-change`
        only for an intentional surface change.

        Use `client.Live.On<EventType>(...)`, `OnAny`, `OnError`, and
        `ObserveAsync` for ordered typed event delivery. Committed cursors are
        validated, replayed events are suppressed, and journal rebase control
        messages raise `HpdAgentJournalRebasedException`. The lower-level
        `IAsyncEnumerable<HpdAgentSseMessage>` remains available for raw access
        and unknown future events.

        Client-tool handlers registered through `client.Live.Tools` are invoked
        automatically for matching requests. `client.Chat` provides a scoped
        starter facade for sending text, goals, compactions, steering, and
        cancelling or observing a turn. Chat also supports session discovery,
        state-hydrated live subscriptions, execution-scoped submissions, and
        generated permission prompt/response helpers.

        `HpdAgentThreadMessages.Project(...)` coalesces typed journal events into
        display-oriented message, reasoning, and tool-call read models.
        """
        + "\n";

    private static async Task<IReadOnlyDictionary<string, string>> ReadRuntimeSourcesAsync(
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "RuntimeTemplate");
        if (!Directory.Exists(directory))
            throw new InvalidOperationException("The fixed client runtime template was not copied to the generator output.");
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(directory, "*.cs").OrderBy(static value => value, StringComparer.Ordinal))
            result[Path.GetFileName(path)] = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (result.Count == 0)
            throw new InvalidOperationException("The fixed client runtime template is empty.");
        return result;
    }

    private static string RenderPublicApi(IEnumerable<string> sources)
    {
        var builder = new StringBuilder("# HPD Agent generated public API\n");
        foreach (var source in sources)
        {
            var internalDepth = 0;
            var internalPending = false;
            using var reader = new StringReader(source);
            while (reader.ReadLine() is { } line)
            {
                var trimmed = line.Trim();
                if (internalPending)
                {
                    internalDepth += Braces(trimmed);
                    if (internalDepth > 0)
                        internalPending = false;
                    continue;
                }
                if (internalDepth > 0)
                {
                    internalDepth += Braces(trimmed);
                    continue;
                }
                if (trimmed.StartsWith("internal ", StringComparison.Ordinal))
                {
                    internalDepth = Math.Max(0, Braces(trimmed));
                    internalPending = internalDepth == 0;
                    continue;
                }
                if (trimmed.StartsWith("public ", StringComparison.Ordinal) ||
                    trimmed.StartsWith("public static class ", StringComparison.Ordinal) ||
                    trimmed.StartsWith("public sealed class ", StringComparison.Ordinal))
                {
                    builder.AppendLine(trimmed);
                }
            }
        }
        return builder.ToString();
    }

    private static int Braces(string line) =>
        line.Count(static character => character == '{') -
        line.Count(static character => character == '}');

    private static async Task BuildAsync(
        string project,
        CancellationToken cancellationToken)
    {
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = dotnet,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("build");
        process.StartInfo.ArgumentList.Add(project);
        process.StartInfo.ArgumentList.Add("--nologo");
        process.StartInfo.ArgumentList.Add("--configuration");
        process.StartInfo.ArgumentList.Add("Release");
        if (!process.Start())
            throw new InvalidOperationException("Failed to start dotnet for generated SDK verification.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await standardOutput.ConfigureAwait(false);
        var error = await standardError.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Generated SDK compilation failed.{Environment.NewLine}{output}{Environment.NewLine}{error}");
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
