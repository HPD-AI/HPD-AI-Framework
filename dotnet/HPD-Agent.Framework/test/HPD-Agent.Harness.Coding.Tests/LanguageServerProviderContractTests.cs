using System.Reflection;
using HPDOS.ToolHarnesses.Middleware;
using HPDOS.ToolHarnesses.Middleware.Generated;

namespace HPD.Agent.ToolHarness.Coding.Tests;

[Collection(CurrentDirectoryCollection.Name)]
public sealed class LanguageServerProviderContractTests
{
    public static TheoryData<string> ProviderIds
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var definition in Definitions)
                data.Add(definition.Id);
            return data;
        }
    }

    private static LanguageServerDefinition[] Definitions
        => new GeneratedLanguageServerRegistryProvider().GetAll().OrderBy(static item => item.Id).ToArray();

    [Theory]
    [MemberData(nameof(ProviderIds))]
    public async Task EveryProvider_ResolvesItsDeclaredRootMarker(string providerId)
    {
        var definition = Find(providerId);
        var markers = DeclaredType(providerId).GetCustomAttribute<LanguageServerRootMarkersAttribute>()!.Markers;
        var root = CreateTempRoot();
        var nested = Path.Combine(root, "src", "nested");
        Directory.CreateDirectory(nested);
        var path = Path.Combine(nested, "sample" + definition.Extensions[0]);
        await File.WriteAllTextAsync(path, "");
        CreateMarker(root, markers[0]);

        try
        {
            var resolved = await definition.Provider.ResolveRootAsync(Context(definition, path, root));
            resolved.Should().Be(providerId == "tlaplus" ? nested : root,
                $"{providerId} must activate from its nearest declared marker");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(ProviderIds))]
    public async Task EveryProvider_RejectsAWorkspaceWithoutDeclaredMarkers(string providerId)
    {
        var definition = Find(providerId);
        var root = CreateTempRoot();
        var path = Path.Combine(root, "sample" + definition.Extensions[0]);
        await File.WriteAllTextAsync(path, "");

        try
        {
            var resolved = await definition.Provider.ResolveRootAsync(Context(definition, path, root));
            if (providerId == "tlaplus")
                resolved.Should().Be(root, "the TLA+ source file itself satisfies the *.tla root marker");
            else
                resolved.Should().BeNull($"{providerId} declares activation markers");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(ProviderIds))]
    public async Task EveryStaticProvider_ResolvesItsDeclaredExecutableAndArguments(string providerId)
    {
        var definition = Find(providerId);
        var type = DeclaredType(providerId);
        var executable = type.GetCustomAttribute<LanguageServerExecutableAttribute>();
        if (executable is null)
            return;

        var expectedArguments = type.GetCustomAttribute<LanguageServerArgumentsAttribute>()?.Arguments ?? [];
        var resolver = new RecordingToolResolver("/tools/" + executable.Executable);
        var launch = await definition.Provider.ResolveLaunchAsync(new LanguageServerLaunchContext
        {
            Root = "/workspace",
            WorkspaceRoot = "/workspace",
            Definition = definition,
            Options = new LanguageServerOptions(),
            ToolResolver = resolver
        });

        launch.Should().NotBeNull();
        resolver.ExecutableNames.Should().ContainSingle().Which.Should().Be(executable.Executable);
        launch!.FileName.Should().Be("/tools/" + executable.Executable);
        launch.Arguments.Should().Equal(expectedArguments);
        launch.WorkingDirectory.Should().Be("/workspace");
    }

    [Fact]
    public async Task TypeScriptAndDeno_AreMutuallyExclusiveByNearestRoot()
    {
        var root = CreateTempRoot();
        var denoRoot = Path.Combine(root, "deno-app");
        Directory.CreateDirectory(denoRoot);
        await File.WriteAllTextAsync(Path.Combine(root, "package.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(denoRoot, "deno.json"), "{}");
        var nodeFile = Path.Combine(root, "node.ts");
        var denoFile = Path.Combine(denoRoot, "main.ts");
        await File.WriteAllTextAsync(nodeFile, "");
        await File.WriteAllTextAsync(denoFile, "");

        try
        {
            await using var service = new LanguageServerService(new LanguageServerOptions { WorkspaceFolders = [root] });
            var node = await service.ResolveDocumentAsync(nodeFile);
            var deno = await service.ResolveDocumentAsync(denoFile);

            node.Servers.Select(static server => server.ServerId).Should().Contain("typescript").And.NotContain("deno");
            deno.Servers.Select(static server => server.ServerId).Should().Contain("deno").And.NotContain("typescript");
            deno.Servers.Single(static server => server.ServerId == "deno").Root.Should().Be(denoRoot);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TypeScriptLaunch_RequiresLocalTypeScriptAndPinsTsserverPath()
    {
        var definition = Find("typescript");
        var resolver = new MappingToolResolver
        {
            Executables = { ["typescript-language-server"] = "/repo/node_modules/.bin/typescript-language-server" },
            NodeModules = { ["typescript/lib/tsserver.js"] = "/repo/node_modules/typescript/lib/tsserver.js" }
        };

        var launch = await definition.Provider.ResolveLaunchAsync(LaunchContext(definition, resolver));

        launch.Should().NotBeNull();
        launch!.Arguments.Should().Equal("--stdio");
        launch.InitializationOptions["tsserver"].Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>()
            .Which["path"].Should().Be("/repo/node_modules/typescript/lib/tsserver.js");

        resolver.NodeModules.Clear();
        (await definition.Provider.ResolveLaunchAsync(LaunchContext(definition, resolver))).Should().BeNull();
    }

    [Fact]
    public async Task DenoLaunch_UsesDenoLspCommand()
    {
        var definition = Find("deno");
        var resolver = new MappingToolResolver { Executables = { ["deno"] = "/tools/deno" } };

        var launch = await definition.Provider.ResolveLaunchAsync(LaunchContext(definition, resolver));

        launch.Should().NotBeNull();
        launch!.FileName.Should().Be("/tools/deno");
        launch.Arguments.Should().Equal("lsp");
        launch.WorkingDirectory.Should().Be("/workspace");
    }

    [Theory]
    [InlineData("go.work")]
    [InlineData("go.mod")]
    public async Task Gopls_RecognizesGoWorkspaceMarkers(string marker)
    {
        await AssertGeneratedResolutionAsync("gopls", "main.go", marker, "go");
    }

    [Theory]
    [InlineData("pyright", "pyrightconfig.json")]
    [InlineData("ruff", "ruff.toml")]
    [InlineData("pylsp", "setup.cfg")]
    public async Task PythonProviders_RecognizeProviderSpecificRoots(string providerId, string marker)
    {
        await AssertGeneratedResolutionAsync(providerId, "main.py", marker, "python", explicitlyEnable: providerId == "pylsp");
    }

    [Fact]
    public async Task RustAnalyzer_RequiresAWorkspaceMarkerForStandaloneFiles()
    {
        var definition = Find("rust-analyzer");
        var root = CreateTempRoot();
        var path = Path.Combine(root, "main.rs");
        await File.WriteAllTextAsync(path, "fn main() {}");
        try
        {
            (await definition.Provider.ResolveRootAsync(Context(definition, path, root))).Should().BeNull();
            await File.WriteAllTextAsync(Path.Combine(root, "Cargo.toml"), "[package]");
            (await definition.Provider.ResolveRootAsync(Context(definition, path, root))).Should().Be(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("Sample.sln")]
    [InlineData("Sample.slnx")]
    [InlineData("Sample.csproj")]
    [InlineData("Directory.Build.props")]
    [InlineData("global.json")]
    public async Task CSharp_RecognizesEveryDeclaredWorkspaceMarker(string marker)
    {
        await AssertGeneratedResolutionAsync("csharp", Path.Combine("src", "Program.cs"), marker, "csharp");
    }

    [Fact]
    public async Task CSharp_SelectsNearestNestedProjectRoot()
    {
        var definition = Find("csharp");
        var root = CreateTempRoot();
        var nested = Path.Combine(root, "src", "Nested");
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(Path.Combine(root, "Root.sln"), "");
        await File.WriteAllTextAsync(Path.Combine(nested, "Nested.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var path = Path.Combine(nested, "Program.cs");
        await File.WriteAllTextAsync(path, "class Program {}");

        try
        {
            (await definition.Provider.ResolveRootAsync(Context(definition, path, root))).Should().Be(nested);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Catalog_PreservesUnusualTlaPlusExtensionsAndLanguageIds()
    {
        var definition = Find("tlaplus");
        definition.Extensions.Should().Equal(".tla", ".tlaplus");
        definition.LanguageIds.Should().Contain(new KeyValuePair<string, string>(".tla", "tlaplus"));
        definition.LanguageIds.Should().Contain(new KeyValuePair<string, string>(".tlaplus", "tlaplus"));
    }

    [Fact]
    public async Task CallerCancelledStartup_IsNotRecordedAsUnavailable()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "main.canceltest");
        await File.WriteAllTextAsync(path, "");
        var provider = new CancellationAwareProvider();
        var definition = new LanguageServerDefinition
        {
            Id = "cancel-test",
            Extensions = [".canceltest"],
            LanguageIds = new Dictionary<string, string> { [".canceltest"] = "cancel-test" },
            Provider = provider
        };
        await using var service = new LanguageServerService(new LanguageServerOptions
        {
            WorkspaceFolders = [root],
            Servers = [definition]
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            var action = () => service.OpenDocumentAsync(new LanguageServerDocumentOpenRequest
            {
                Path = path,
                Uri = new Uri(path).AbsoluteUri,
                LanguageId = "cancel-test",
                Text = ""
            }, cancellation.Token).AsTask();

            await action.Should().ThrowAsync<OperationCanceledException>();
            (await service.GetStatusAsync()).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Status_ReportsStartingOnlyWhileProviderLaunchIsInFlight()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "main.starttest");
        await File.WriteAllTextAsync(path, "");
        var provider = new BlockingLaunchProvider();
        await using var service = new LanguageServerService(new LanguageServerOptions
        {
            WorkspaceFolders = [root],
            Servers =
            [
                new LanguageServerDefinition
                {
                    Id = "start-test",
                    Extensions = [".starttest"],
                    Provider = provider
                }
            ]
        });
        using var cancellation = new CancellationTokenSource();
        try
        {
            var opening = service.OpenDocumentAsync(new LanguageServerDocumentOpenRequest
            {
                Path = path,
                Uri = new Uri(path).AbsoluteUri,
                LanguageId = "start-test",
                Text = ""
            }, cancellation.Token).AsTask();
            await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

            (await service.GetStatusAsync()).Should().ContainSingle(status =>
                status.ServerId == "start-test" && status.Status == LanguageServerStatusKind.Starting);

            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await opening);
            (await service.GetStatusAsync()).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task AssertGeneratedResolutionAsync(
        string providerId,
        string fileName,
        string marker,
        string languageId,
        bool explicitlyEnable = false)
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "");
        CreateMarker(root, marker);
        try
        {
            var options = new LanguageServerOptions
            {
                WorkspaceFolders = [root],
                EnabledServers = explicitlyEnable
                    ? new HashSet<string>(StringComparer.Ordinal) { providerId }
                    : new HashSet<string>(StringComparer.Ordinal)
            };
            await using var service = new LanguageServerService(options);
            var resolution = await service.ResolveDocumentAsync(path);
            resolution.Servers.Should().Contain(server => server.ServerId == providerId && server.LanguageId == languageId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static LanguageServerDefinition Find(string id)
        => Definitions.Single(item => item.Id == id);

    private static Type DeclaredType(string id)
        => typeof(LanguageServerOptions).Assembly.GetTypes().Single(type =>
            type.GetCustomAttribute<HpdLanguageServerAttribute>()?.Id == id);

    private static LanguageServerRootContext Context(LanguageServerDefinition definition, string path, string root)
        => new() { Path = path, WorkspaceRoot = root, Definition = definition, Options = new LanguageServerOptions() };

    private static LanguageServerLaunchContext LaunchContext(LanguageServerDefinition definition, ILanguageServerToolResolver resolver)
        => new()
        {
            Root = "/workspace",
            WorkspaceRoot = "/workspace",
            Definition = definition,
            Options = new LanguageServerOptions(),
            ToolResolver = resolver
        };

    private static void CreateMarker(string root, string marker)
    {
        var concrete = marker.Replace("*", "project", StringComparison.Ordinal).Replace("?", "x", StringComparison.Ordinal);
        var path = Path.Combine(root, concrete);
        if (Path.HasExtension(concrete) || concrete.StartsWith(".", StringComparison.Ordinal))
            File.WriteAllText(path, "");
        else
            Directory.CreateDirectory(path);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpd-lsp-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class RecordingToolResolver(string resolved) : ILanguageServerToolResolver
    {
        public List<string> ExecutableNames { get; } = [];
        public ValueTask<string?> FindExecutableAsync(string name, string root, CancellationToken cancellationToken = default)
        {
            ExecutableNames.Add(name);
            return ValueTask.FromResult<string?>(resolved);
        }
        public ValueTask<string?> FindNodeModuleAsync(string modulePath, string root, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>(null);
        public ValueTask<string?> FindLocalBinAsync(string name, string root, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>(null);
    }

    private sealed class MappingToolResolver : ILanguageServerToolResolver
    {
        public Dictionary<string, string> Executables { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> NodeModules { get; } = new(StringComparer.Ordinal);
        public ValueTask<string?> FindExecutableAsync(string name, string root, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Executables.GetValueOrDefault(name));
        public ValueTask<string?> FindNodeModuleAsync(string modulePath, string root, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(NodeModules.GetValueOrDefault(modulePath));
        public ValueTask<string?> FindLocalBinAsync(string name, string root, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Executables.GetValueOrDefault(name));
    }

    private sealed class CancellationAwareProvider : ILanguageServerProvider
    {
        public string ConfigurationIdentity => "cancellation-aware:v1";
        public ValueTask<string?> ResolveRootAsync(LanguageServerRootContext context, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>(context.WorkspaceRoot);

        public ValueTask<LanguageServerLaunchDescriptor?> ResolveLaunchAsync(LanguageServerLaunchContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<LanguageServerLaunchDescriptor?>(null);
        }

        public ValueTask<LanguageServerInitialization> CreateInitializationAsync(LanguageServerInitializationContext context, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new LanguageServerInitialization());
    }

    private sealed class BlockingLaunchProvider : ILanguageServerProvider
    {
        public string ConfigurationIdentity => "blocking-launch:v1";
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<string?> ResolveRootAsync(LanguageServerRootContext context, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>(context.WorkspaceRoot);

        public async ValueTask<LanguageServerLaunchDescriptor?> ResolveLaunchAsync(LanguageServerLaunchContext context, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }

        public ValueTask<LanguageServerInitialization> CreateInitializationAsync(LanguageServerInitializationContext context, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new LanguageServerInitialization());
    }
}
