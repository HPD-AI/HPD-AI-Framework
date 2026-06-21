using System.Collections.Immutable;
using System.Reflection;
using FluentAssertions;
using HPD.Events;
using HPD.Graph.Abstractions.Artifacts;
using HPD.Graph.Abstractions.Handlers;
using HPD.Graph.Connectors.Abstractions.Attributes;
using HPD.Graph.Connectors.Abstractions.Connections;
using HPD.Graph.Connectors.Core.DependencyInjection;
using HPD.Graph.Connectors.OpenApi.Handlers;
using HPD.Graph.Connectors.SourceGenerator;
using HPD.Graph.Core.Builders;
using HPD.Graph.Core.Context;
using HPD.OpenApi.Core.Model;
using Microsoft.AspNetCore.Routing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Graph.Tests.V21;

public sealed class ConnectorSourceGeneratorTests
{
    [Fact]
    public void Generator_EmitsCompilableConnectorSurface()
    {
        var result = RunGenerator(ConnectorFixtureSource);

        result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty();

        var generated = result.RunResult.GeneratedTrees
            .Select(tree => tree.GetText().ToString())
            .ToArray();

        generated.Should().Contain(text => text.Contains("AddGithubConnector", StringComparison.Ordinal));
        generated.Should().Contain(text => text.Contains("MapGithubConnectorWebhooks", StringComparison.Ordinal));
        generated.Should().Contain(text => text.Contains("GithubCreateIssueActionHandler", StringComparison.Ordinal));
        generated.Should().Contain(text => text.Contains("GithubIssueOpenedSourceProvider", StringComparison.Ordinal));
        generated.Should().Contain(text => text.Contains("GithubRepositoriesOptionProvider", StringComparison.Ordinal));
        generated.Should().Contain(text => text.Contains("Fields =", StringComparison.Ordinal));
        generated.Should().Contain(text => text.Contains("ConnectionType = \"github.pat\"", StringComparison.Ordinal));
        generated.Should().Contain(text => text.Contains("OptionProviderName = \"github.repositories\"", StringComparison.Ordinal));
        generated.Should().Contain(text => text.Contains("HandleWebhookAsync(HPD.Graph.Connectors.Abstractions.Sources.WebhookEnvelope envelope, System.IServiceProvider services", StringComparison.Ordinal));
        generated.Should().Contain(text => text.Contains("VerifyWebhook(envelope, services, bodyBytes)", StringComparison.Ordinal));
        generated.Should().Contain(text => text.Contains("ExtractEventType(envelope, bodyBytes)", StringComparison.Ordinal));
        generated.Should().Contain(text => text.Contains("IConnectorClientFactory<global::Demo.GitHubClient>", StringComparison.Ordinal));
        generated.Should().Contain(text => text.Contains("IConnectionProvider", StringComparison.Ordinal));
    }

    [Fact]
    public void Generator_ReportsDuplicateIdsAndUnknownConfigReferences()
    {
        var result = RunGenerator(DiagnosticFixtureSource);

        result.Diagnostics
            .Select(d => d.Id)
            .Should()
            .Contain(["HPDC002", "HPDC004"]);
    }

    [Fact]
    public void Generator_EmitsOpenApiOperationWrappersFromAdditionalSpecFile()
    {
        var result = RunGenerator(OpenApiFixtureSource, [new InMemoryAdditionalText("Specs/widgets.openapi.json", OpenApiSpecJson)]);

        result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty();

        var generated = result.RunResult.GeneratedTrees
            .Select(tree => tree.GetText().ToString())
            .ToArray();

        generated.Should().Contain(text => text.Contains("WidgetsCreateWidgetOpenApiActionHandler", StringComparison.Ordinal));
        generated.Should().Contain(text => text.Contains("HandlerName => \"widgets.createWidget\"", StringComparison.Ordinal));
        generated.Should().Contain(text => text.Contains("AddWidgetsCreateWidgetNode", StringComparison.Ordinal));
        generated.Should().Contain(text => text.Contains("OpenApiOperationRegistration(\"widgets\"", StringComparison.Ordinal));
        generated.Should().Contain(text => text.Contains("openapi.operationId", StringComparison.Ordinal));
        generated.Should().Contain(text => text.Contains("Name = \"name\"", StringComparison.Ordinal));
        generated.Should().NotContain(text => text.Contains("deleteWidget", StringComparison.Ordinal));
    }

    private static GeneratorTestResult RunGenerator(
        string source,
        IReadOnlyList<AdditionalText>? additionalTexts = null)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create(
            "ConnectorGeneratorFixture",
            [syntaxTree],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ConnectorSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(
            [generator.AsSourceGenerator()],
            additionalTexts: additionalTexts ?? []);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();
        var emitDiagnostics = outputCompilation.GetDiagnostics();

        return new GeneratorTestResult(
            runResult,
            diagnostics.Concat(emitDiagnostics).ToImmutableArray());
    }

    private static IReadOnlyList<MetadataReference> References()
    {
        var trustedPlatformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray() ?? [];

        var explicitReferences = new[]
        {
            typeof(HpdConnectorAttribute).Assembly,
            typeof(ConnectionAuthKind).Assembly,
            typeof(GraphBuilder).Assembly,
            typeof(GraphContext).Assembly,
            typeof(IGraphNodeHandler<>).Assembly,
            typeof(ConnectorCoreServiceCollectionExtensions).Assembly,
            typeof(OpenApiCallOperationHandler).Assembly,
            typeof(RestApiOperation).Assembly,
            typeof(IEndpointRouteBuilder).Assembly,
            typeof(IServiceCollection).Assembly,
            typeof(Event).Assembly,
            typeof(ArtifactKey).Assembly,
        }
        .Select(assembly => MetadataReference.CreateFromFile(assembly.Location));

        return trustedPlatformAssemblies.Concat(explicitReferences).ToArray();
    }

    private sealed record GeneratorTestResult(
        GeneratorDriverRunResult RunResult,
        ImmutableArray<Diagnostic> Diagnostics);

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly SourceText _text;

        public InMemoryAdditionalText(string path, string text)
        {
            Path = path;
            _text = SourceText.From(text);
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default)
            => _text;
    }

    private const string ConnectorFixtureSource = """
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HPD.Events;
using HPD.Graph.Abstractions.Artifacts;
using HPD.Graph.Connectors.Abstractions.Assets;
using HPD.Graph.Connectors.Abstractions.Attributes;
using HPD.Graph.Connectors.Abstractions.Configuration;
using HPD.Graph.Connectors.Abstractions.Connections;
using HPD.Graph.Connectors.Abstractions.Events;
using HPD.Graph.Connectors.Abstractions.IO;
using HPD.Graph.Connectors.Abstractions.Materialization;
using HPD.Graph.Connectors.Abstractions.Options;
using HPD.Graph.Connectors.Abstractions.Sources;

namespace Demo;

[HpdConnector("github", DisplayName = "GitHub")]
public sealed partial class GitHubConnector
{
    [HpdConnectorPreDispatch]
    private static Microsoft.AspNetCore.Http.IResult? VerifyWebhook(WebhookEnvelope envelope, IServiceProvider services, byte[] bodyBytes)
        => null;

    [HpdConnectorBodyExtractor]
    private static (string? EventType, byte[] DispatchBytes) ExtractEventType(WebhookEnvelope envelope, byte[] bodyBytes)
        => ("issues", bodyBytes);
}

[HpdConnection("github.pat", AppId = "github", AuthKind = ConnectionAuthKind.BearerToken)]
public sealed partial record GitHubPatConnection;

[HpdActionConfig("github.create_issue", DisplayName = "Create Issue")]
public sealed partial record GitHubCreateIssueConfig : IConnectorConfig
{
    [ConnectorConnection("github.pat")]
    public string ConnectionId { get; init; } = "";

    [ConnectorOption("github.repositories")]
    public string Repository { get; init; } = "";

    public string Title { get; init; } = "";
}

[HpdConnectorAction("github.create_issue", ConfigType = typeof(GitHubCreateIssueConfig))]
public static partial class GitHubIssueActions
{
    public static Task<GitHubIssueResult> RunAsync(GitHubClient client, GitHubCreateIssueConfig config, CancellationToken ct)
        => Task.FromResult(new GitHubIssueResult(client.Name + config.Title));
}

public sealed record GitHubIssueResult(string Title);

public sealed class GitHubClient
{
    public string Name => "github";
}

[HpdWebhookSource("github.issue.opened", DisplayName = "Issue Opened")]
public sealed partial class GitHubIssueOpenedSource
{
    public sealed record Config : IConnectorConfig
    {
        public string Repository { get; init; } = "";
    }

    public static WorkflowSourceEvent? FromWebhook(WebhookEnvelope envelope, Config config)
        => new(JsonSerializer.SerializeToElement(new { ok = true }), EventId: "evt-1");
}

[HpdPollingSource("github.issue.updated", DisplayName = "Issue Updated")]
public sealed partial class GitHubIssueUpdatedSource
{
    public sealed record Config : IConnectorConfig
    {
        public string Repository { get; init; } = "";
    }

    public static async IAsyncEnumerable<WorkflowSourceEvent> PollAsync(
        Config config,
        [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new WorkflowSourceEvent(JsonSerializer.SerializeToElement(new { ok = true }), EventId: "evt-2");
        await Task.CompletedTask;
    }
}

public static partial class GitHubOptions
{
    [HpdConnectorOption("github.repositories")]
    public static ValueTask<ConnectorOptionPage> GetRepositoriesAsync(
        ConnectorOptionRequest request,
        CancellationToken ct)
        => ValueTask.FromResult(new ConnectorOptionPage
        {
            Options =
            [
                new ConnectorOption { Value = "HPD/repo", Label = "HPD/repo" }
            ]
        });
}

[HpdConnectorAssetCatalog("dbt.manifest")]
public sealed partial class DbtCatalog
{
    public sealed record Config : IConnectorConfig;

    public static Task<IReadOnlyList<ConnectorAssetDescriptor>> LoadAssetsAsync(
        Config config,
        CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ConnectorAssetDescriptor>>(
        [
            new ConnectorAssetDescriptor
            {
                AssetType = "dbt.model",
                AppId = "dbt",
                ArtifactKey = ArtifactKey.FromPath("warehouse", "orders")
            }
        ]);
}

public sealed partial record DbtRunConfig : IConnectorConfig;

[HpdConnectorMaterialization("dbt.run", ConfigType = typeof(DbtRunConfig))]
public static partial class DbtMaterialization
{
    public static async IAsyncEnumerable<Event> RunAsync(
        DbtRunConfig config,
        ConnectorMaterializationContext context,
        [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new ExternalArtifactMaterializedEvent
        {
            ArtifactKey = context.ArtifactKey,
            Version = "v1",
            MaterializedAt = DateTimeOffset.UnixEpoch
        };
        await Task.CompletedTask;
    }
}

[HpdConnectorAssetCheck("warehouse.row_count_positive")]
public static partial class WarehouseChecks
{
    public static ValueTask<ArtifactCheckCompletedEvent> RunAsync(
        ArtifactKey artifactKey,
        CancellationToken ct)
        => ValueTask.FromResult(new ArtifactCheckCompletedEvent
        {
            ArtifactKey = artifactKey,
            CheckName = "warehouse.row_count_positive",
            Passed = true
        });
}

[HpdArtifactIOManager("memory")]
public sealed partial class MemoryIOManager : IArtifactIOManager
{
    public string Name => "memory";
    public ValueTask StoreAsync(ArtifactWriteContext context, object? value, CancellationToken ct = default) => ValueTask.CompletedTask;
    public ValueTask<object?> LoadAsync(ArtifactReadContext context, CancellationToken ct = default) => ValueTask.FromResult<object?>(null);
}
""";

    private const string OpenApiFixtureSource = """
using HPD.Graph.Connectors.Abstractions.Attributes;

namespace Demo;

[HpdConnector("widgets", DisplayName = "Widgets")]
[HpdOpenApiSpec("Specs/widgets.openapi.json", IncludeOperations = ["createWidget"])]
public sealed partial class WidgetsConnector
{
}
""";

    private const string OpenApiSpecJson = """
{
  "openapi": "3.0.1",
  "info": { "title": "Widgets", "version": "1.0.0" },
  "servers": [{ "url": "https://api.example.test" }],
  "paths": {
    "/widgets": {
      "post": {
        "operationId": "createWidget",
        "summary": "Create widget",
        "requestBody": {
          "content": {
            "application/json": {
              "schema": {
                "type": "object",
                "required": ["name"],
                "properties": {
                  "name": { "type": "string" }
                }
              }
            }
          }
        },
        "responses": { "201": { "description": "Created" } }
      },
      "delete": {
        "operationId": "deleteWidget",
        "responses": { "204": { "description": "Deleted" } }
      }
    }
  }
}
""";

    private const string DiagnosticFixtureSource = """
using HPD.Graph.Connectors.Abstractions.Attributes;
using HPD.Graph.Connectors.Abstractions.Configuration;

namespace Demo;

[HpdConnector("github")]
public sealed partial class GitHubConnectorA
{
}

[HpdConnector("github")]
public sealed partial class GitHubConnectorB
{
}

[HpdActionConfig("github.create_issue")]
public sealed partial record GitHubCreateIssueConfig : IConnectorConfig
{
    [ConnectorConnection("missing.connection")]
    [ConnectorOption("missing.option")]
    public string ConnectionId { get; init; } = "";
}
""";
}
