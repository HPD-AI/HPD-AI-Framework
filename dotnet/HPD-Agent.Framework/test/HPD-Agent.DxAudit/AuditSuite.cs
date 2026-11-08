using System.Reflection;
using System.Runtime.CompilerServices;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent;
using HPD.Agent.AspNetCore;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Middleware;
using HPD.Agent.Permissions;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;
using HPD.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HPD.Agent.DxAudit;

internal static class AuditSuite
{
    public static async Task<IReadOnlyList<ProbeResult>> RunAsync(CancellationToken cancellationToken)
    {
        var results = new List<ProbeResult>
        {
            await MinimalAgentRunAsync(cancellationToken),
            await WithChatClientRunAsync(cancellationToken),
            await DirectMicrosoftFunctionAsync(cancellationToken),
            await NormalizedToolAsync(cancellationToken),
            await InMemoryHistoryAsync(cancellationToken),
            await MissingSessionAsync(cancellationToken),
            await FileHistoryAsync(cancellationToken),
            StructuredResultSurface(),
            ConfigRoundTrip(),
            await EventObservationAsync(cancellationToken),
            await StructuredOutputObservationAsync(cancellationToken),
            await MiddlewareObservationAsync(cancellationToken),
            await CancellationObservationAsync(cancellationToken),
            await ThreadLifecycleObservationAsync(cancellationToken),
            await HostingCompositionObservationAsync(cancellationToken),
            await HostingEndpointObservationAsync(cancellationToken),
            ProviderPackageDiscovery(),
            await ToolFailureObservationAsync(cancellationToken),
            await ToolTimeoutObservationAsync(cancellationToken),
            await ToolRetryObservationAsync(cancellationToken),
            await PermissionDecisionObservationAsync(approve: true, cancellationToken),
            await PermissionDecisionObservationAsync(approve: false, cancellationToken),
            await AttachmentObservationAsync(cancellationToken),
            await ToolOutputOffloadObservationAsync(cancellationToken),
            PortableProviderSelection(),
            ExplicitSecretSerialization(),
            await StoreLifecycleObservationAsync(cancellationToken),
            HostingVocabularySurface(),
            ExplicitHostingCompositionSurface()
        };

        return results;
    }

    private static async Task<ProbeResult> MinimalAgentRunAsync(CancellationToken cancellationToken)
    {
        const string id = "DX-PROBE-001";
        const string area = "core";

        try
        {
            await using var agent = await CreateExplicitOverrideAgentAsync(
                new StaticChatClient("consumer reply"),
                cancellationToken);
            var result = await agent.RunAsync("Hello", cancellationToken: cancellationToken);
            Require(result.Text == "consumer reply", $"Expected 'consumer reply', observed '{result.Text}'.");
            Require(result.Events.Count > 0, "Expected turn events.");
            return Pass(id, area, "Explicit client override builds, runs, and returns text and events.");
        }
        catch (Exception exception)
        {
            return Failure(id, area, "Explicit client override should complete a turn.", exception);
        }
    }

    private static async Task<ProbeResult> WithChatClientRunAsync(CancellationToken cancellationToken)
    {
        const string id = "DX-PROBE-002";
        const string area = "composition";

        try
        {
            await using var agent = await new AgentBuilder(new AgentConfig())
                .WithChatClient(new StaticChatClient("with-chat-client reply"))
                .BuildAsync(cancellationToken);
            var result = await agent.RunAsync("Hello", cancellationToken: cancellationToken);
            Require(result.Text == "with-chat-client reply", "Unexpected response text.");
            return Pass(id, area, "WithChatClient builds and executes.");
        }
        catch (Exception exception)
        {
            return Failure(id, area, "WithChatClient build and first run should both succeed.", exception);
        }
    }

    private static async Task<ProbeResult> DirectMicrosoftFunctionAsync(CancellationToken cancellationToken)
    {
        const string id = "DX-PROBE-003";
        const string area = "tools";

        try
        {
            var config = CreateConfig(new ToolCallingChatClient());
            config.ServerConfiguredTools =
            [
                HPDAIFunctionFactory.Create(
                    (arguments, _, _) =>
                    {
                        var value = Convert.ToInt32(arguments["value"]);
                        return Task.FromResult<object?>(value * 2);
                    },
                    new HPDAIFunctionFactoryOptions
                    {
                        Name = "double",
                        Description = "Doubles one integer."
                    })
            ];

            await using var agent = await new AgentBuilder(config).BuildAsync(cancellationToken);
            var result = await agent.RunAsync("Double 21", cancellationToken: cancellationToken);
            Require(result.Text == "The value is 42.", "The normalized HPD function did not execute.");
            return Pass(id, area, "HPD factory output assigned to the existing tool collection executes.");
        }
        catch (Exception exception)
        {
            return Failure(
                id,
                area,
                "An HPD function registered through AgentConfig.ServerConfiguredTools should execute.",
                exception);
        }
    }

    private static async Task<ProbeResult> NormalizedToolAsync(CancellationToken cancellationToken)
    {
        const string id = "DX-PROBE-004";
        const string area = "tools";

        try
        {
            var config = CreateConfig(new ToolCallingChatClient());
            config.ServerConfiguredTools =
            [
                HPDAIFunctionFactory.Import(
                    AIFunctionFactory.Create((int value) => value * 2, "double"),
                    explicitlyUngoverned: true)
            ];

            await using var agent = await new AgentBuilder(config).BuildAsync(cancellationToken);
            var result = await agent.RunAsync("Double 21", cancellationToken: cancellationToken);
            Require(result.Text == "The value is 42.", "The normalized HPD function did not execute.");
            return Pass(id, area, "An explicitly normalized HPD function builds and executes.");
        }
        catch (Exception exception)
        {
            return Failure(id, area, "The explicitly normalized tool path should execute.", exception);
        }
    }

    private static async Task<ProbeResult> InMemoryHistoryAsync(CancellationToken cancellationToken)
    {
        const string id = "DX-PROBE-005";
        const string area = "sessions";

        try
        {
            await using var agent = await CreateExplicitOverrideAgentAsync(
                new HistoryEchoChatClient(),
                cancellationToken,
                builder => builder.WithInMemorySessionStore());

            await agent.CreateSessionAsync("dx-memory", cancellationToken: cancellationToken);
            var first = await agent.RunAsync(
                "First", sessionId: "dx-memory", cancellationToken: cancellationToken);
            var second = await agent.RunAsync(
                "Second", sessionId: "dx-memory", cancellationToken: cancellationToken);

            Require(first.Text == "User messages: 1", $"First turn observed '{first.Text}'.");
            Require(second.Text == "User messages: 2", $"Second turn observed '{second.Text}'.");
            return Pass(id, area, "In-memory history retains the previous turn.");
        }
        catch (Exception exception)
        {
            return Failure(id, area, "In-memory history should retain sequential turns.", exception);
        }
    }

    private static async Task<ProbeResult> MissingSessionAsync(CancellationToken cancellationToken)
    {
        const string id = "DX-PROBE-006";
        const string area = "sessions";

        try
        {
            await using var agent = await CreateExplicitOverrideAgentAsync(
                new HistoryEchoChatClient(),
                cancellationToken,
                builder => builder.WithInMemorySessionStore());

            _ = await agent.RunAsync("Hello", sessionId: "missing", cancellationToken: cancellationToken);
            return Pass(id, area, "Running with an absent session auto-created the session.");
        }
        catch (SessionNotFoundException)
        {
            return Pass(id, area, "Running with an absent session returned SessionNotFoundException.");
        }
        catch (Exception exception)
        {
            return Failure(
                id,
                area,
                "Running with an absent session should return an actionable missing-session error.",
                exception);
        }
    }

    private static async Task<ProbeResult> FileHistoryAsync(CancellationToken cancellationToken)
    {
        const string id = "DX-PROBE-007";
        const string area = "sessions";
        var storagePath = Path.Combine(
            Path.GetTempPath(),
            $"hpd-agent-dx-audit-{Guid.NewGuid():N}");

        try
        {
            var firstAgent = await CreateExplicitOverrideAgentAsync(
                new HistoryEchoChatClient(),
                cancellationToken,
                builder => builder.WithFileSessionStore(storagePath));
            await firstAgent.CreateSessionAsync("dx-file", cancellationToken: cancellationToken);
            var first = await firstAgent.RunAsync(
                "First", sessionId: "dx-file", cancellationToken: cancellationToken);
            await firstAgent.DisposeAsync();

            await using var secondAgent = await CreateExplicitOverrideAgentAsync(
                new HistoryEchoChatClient(),
                cancellationToken,
                builder => builder.WithFileSessionStore(storagePath));
            var second = await secondAgent.RunAsync(
                "Second", sessionId: "dx-file", cancellationToken: cancellationToken);

            Require(first.Text == "User messages: 1", $"First turn observed '{first.Text}'.");
            Require(second.Text == "User messages: 2", $"Second turn observed '{second.Text}'.");
            return Pass(id, area, "File-backed history survives agent disposal and rebuild.");
        }
        catch (Exception exception)
        {
            return Failure(id, area, "File-backed history should survive agent rebuild.", exception);
        }
        finally
        {
            if (Directory.Exists(storagePath))
                Directory.Delete(storagePath, recursive: true);
        }
    }

    private static ProbeResult StructuredResultSurface()
    {
        const string id = "DX-PROBE-009";
        const string area = "structured-output";
        var methods = typeof(Agent)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method =>
                method.Name == "RunStructuredAsync" &&
                method.IsGenericMethodDefinition)
            .ToArray();

        if (methods.Length == 0)
            return Gap(id, area, "Agent exposes no generic RunStructuredAsync method.");

        var directResult = methods.FirstOrDefault(method =>
            method.ReturnType != typeof(Task) &&
            method.ReturnType != typeof(ValueTask));
        if (directResult is not null)
            return Pass(id, area, $"Structured output returns {directResult.ReturnType.Name}.");

        return Gap(
            id,
            area,
            "Every public generic RunStructuredAsync overload returns only task completion.",
            string.Join(
                "; ",
                methods.Select(method =>
                    $"{method.Name}<{string.Join(",", method.GetGenericArguments().Select(argument => argument.Name))}> " +
                    $"returns {method.ReturnType.Name}")));
    }

    private static ProbeResult ConfigRoundTrip()
    {
        const string id = "DX-PROBE-010";
        const string area = "configuration";

        try
        {
            var config = new AgentConfig
            {
                Name = "DX Config",
                SystemInstructions = "Be useful.",
                MaxAgenticIterations = 7,
                CoalesceDeltas = true
            };

            foreach (var format in new[] { HpdConfigFormat.Json, HpdConfigFormat.Yaml })
            {
                var document = HpdAgentConfigSerializer.Serialize(config, format);
                var restored = HpdAgentConfigSerializer.Deserialize(document, format)
                    ?? throw new AuditRequirementException($"{format} deserialization returned null.");
                Require(restored.Name == config.Name, $"{format} did not retain Name.");
                Require(
                    restored.SystemInstructions == config.SystemInstructions,
                    $"{format} did not retain SystemInstructions.");
                Require(
                    restored.MaxAgenticIterations == config.MaxAgenticIterations,
                    $"{format} did not retain MaxAgenticIterations.");
                Require(
                    restored.CoalesceDeltas == config.CoalesceDeltas,
                    $"{format} did not retain CoalesceDeltas.");
            }

            return Pass(id, area, "Portable AgentConfig round-trips through JSON and YAML.");
        }
        catch (Exception exception)
        {
            return Failure(id, area, "Portable AgentConfig should round-trip through JSON and YAML.", exception);
        }
    }

    private static async Task<ProbeResult> EventObservationAsync(CancellationToken cancellationToken)
    {
        const string id = "DX-PROBE-011";
        const string area = "events";
        var received = new TaskCompletionSource<TextDeltaEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await using var agent = await CreateExplicitOverrideAgentAsync(
                new StaticChatClient("observed delta"),
                cancellationToken);
            using var subscription = agent.Subscribe<TextDeltaEvent>(
                evt => received.TrySetResult(evt));

            _ = await agent.RunAsync("Hello", cancellationToken: cancellationToken);
            var observed = await received.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);
            Require(observed.Text == "observed delta", $"Observed delta text '{observed.Text}'.");
            return Pass(id, area, "A public TextDeltaEvent subscription observes streamed text.");
        }
        catch (Exception exception)
        {
            return Failure(id, area, "A public TextDeltaEvent subscription should observe streamed text.", exception);
        }
    }

    private static async Task<ProbeResult> StructuredOutputObservationAsync(
        CancellationToken cancellationToken)
    {
        const string id = "DX-PROBE-012";
        const string area = "structured-output";
        var received = new TaskCompletionSource<StructuredResultEvent<AuditPerson>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await using var agent = await CreateExplicitOverrideAgentAsync(
                new StaticChatClient("""{"name":"Ada","age":37}"""),
                cancellationToken);
            using var subscription = agent.Subscribe<StructuredResultEvent<AuditPerson>>(evt =>
            {
                if (!evt.IsPartial)
                    received.TrySetResult(evt);
            });

            var value = await agent.RunStructuredAsync<AuditPerson>(
                "Return Ada.",
                cancellationToken: cancellationToken);
            Require(value.Name == "Ada", "The returned structured value did not contain the expected name.");
            Require(value.Age == 37, "The returned structured value did not contain the expected age.");
            var result = await received.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);

            Require(result.Value.Name == "Ada", "The structured result did not contain the expected name.");
            Require(result.Value.Age == 37, "The structured result did not contain the expected age.");
            return Pass(
                id,
                area,
                "Typed JSON output is returned and observable through StructuredResultEvent<T>.");
        }
        catch (Exception exception)
        {
            return Failure(
                id,
                area,
                "RunStructuredAsync<T> should publish a completed StructuredResultEvent<T>.",
                exception);
        }
    }

    private static async Task<ProbeResult> MiddlewareObservationAsync(CancellationToken cancellationToken)
    {
        const string id = "DX-PROBE-013";
        const string area = "middleware";
        var middleware = new CountingMiddleware();

        try
        {
            await using var agent = await CreateExplicitOverrideAgentAsync(
                new StaticChatClient("middleware reply"),
                cancellationToken,
                builder => builder.WithMiddleware(middleware));
            _ = await agent.RunAsync("Hello", cancellationToken: cancellationToken);

            Require(
                middleware.BeforeMessageTurnCalls == 1,
                $"Observed {middleware.BeforeMessageTurnCalls} BeforeMessageTurn calls.");
            return Pass(id, area, "A public IAgentMiddleware receives the message-turn hook.");
        }
        catch (Exception exception)
        {
            return Failure(id, area, "A registered IAgentMiddleware should participate in a run.", exception);
        }
    }

    private static async Task<ProbeResult> CancellationObservationAsync(CancellationToken cancellationToken)
    {
        const string id = "DX-PROBE-014";
        const string area = "cancellation";

        try
        {
            await using var agent = await CreateExplicitOverrideAgentAsync(
                new SlowChatClient(),
                cancellationToken);
            using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            runCancellation.CancelAfter(TimeSpan.FromMilliseconds(100));

            try
            {
                _ = await agent.RunAsync(
                    "Wait indefinitely",
                    cancellationToken: runCancellation.Token);
                return Failure(
                    id,
                    area,
                    "A cancelled run should end with OperationCanceledException.",
                    new AuditRequirementException("The run completed instead of being cancelled."));
            }
            catch (OperationCanceledException)
            {
                return Pass(id, area, "Caller cancellation reaches the active public run.");
            }
        }
        catch (Exception exception)
        {
            return Failure(id, area, "Caller cancellation should propagate through RunAsync.", exception);
        }
    }

    private static async Task<ProbeResult> ThreadLifecycleObservationAsync(
        CancellationToken cancellationToken)
    {
        const string id = "DX-PROBE-015";
        const string area = "sessions";

        try
        {
            await using var agent = await CreateExplicitOverrideAgentAsync(
                new HistoryEchoChatClient(),
                cancellationToken,
                builder => builder.WithInMemorySessionStore());

            await agent.CreateSessionAsync("thread-lifecycle", cancellationToken: cancellationToken);
            var sessions = await agent.ListSessionsAsync(cancellationToken);
            Require(
                sessions.Contains("thread-lifecycle", StringComparer.Ordinal),
                "Created session was absent from ListSessionsAsync.");

            var branch = await agent.CreateThreadAsync(
                "thread-lifecycle",
                threadId: "branch",
                name: "Branch",
                cancellationToken);
            Require(branch == "branch", $"CreateThreadAsync returned '{branch}'.");
            _ = await agent.RunAsync(
                "Branch seed",
                sessionId: "thread-lifecycle",
                threadId: "branch",
                cancellationToken: cancellationToken);

            var fork = await agent.ForkThreadAsync(
                "thread-lifecycle",
                "branch",
                "fork",
                new Dictionary<string, object> { ["source"] = "dx-audit" },
                cancellationToken);
            Require(
                fork.Target.ThreadId == "fork",
                $"Fork returned target '{fork.Target.ThreadId}'.");

            await agent.RestoreThreadAsync("thread-lifecycle", "fork", cancellationToken);
            await agent.DeleteThreadAsync("thread-lifecycle", "fork", cancellationToken);
            await agent.DeleteThreadAsync("thread-lifecycle", "branch", cancellationToken);
            return Pass(
                id,
                area,
                "Session listing, thread creation, fork, runtime restore, and delete complete.");
        }
        catch (Exception exception)
        {
            return Failure(
                id,
                area,
                "Public session/thread management methods should complete one lifecycle.",
                exception);
        }
    }

    private static async Task<ProbeResult> StoreLifecycleObservationAsync(
        CancellationToken cancellationToken)
    {
        const string id = "DX-PROBE-028";
        const string area = "sessions";

        try
        {
            Require(
                typeof(Agent).Assembly.GetExportedTypes().All(type => type.Name != "ThreadProjector"),
                "ThreadProjector should remain an internal event-replay implementation.");

            if (!AgentEventCompositionHost.TryComposeRegisteredModules(out var composition))
            {
                return Gap(
                    id,
                    area,
                    "No generated event modules were available for a standalone session store.");
            }

            var store = new InMemorySessionStore(composition.Codec);
            var session = await store.CreateSessionAsync(
                "store-only-session",
                new Dictionary<string, object> { ["source"] = "dx-audit" },
                cancellationToken);
            var thread = await store.CreateThreadAsync(
                session.Id,
                defaultAgentId: "standalone-agent",
                threadId: "main",
                name: "Main",
                cancellationToken: cancellationToken);
            var projected = await store.ProjectThreadAsync(
                session.Id,
                thread.Id,
                ThreadProjectionPurpose.ThreadHistory,
                cancellationToken)
                ?? throw new AuditRequirementException("The store-created thread did not project.");

            Require(
                projected.DefaultAgentId == "standalone-agent",
                $"Projected default agent was '{projected.DefaultAgentId}'.");
            Require(projected.Name == "Main", $"Projected thread name was '{projected.Name}'.");

            await AssertThrowsInvalidOperationAsync(
                () => store.CreateSessionAsync("store-only-session").AsTask());
            await AssertThrowsInvalidOperationAsync(
                () => store.CreateThreadAsync(
                        session.Id,
                        "standalone-agent",
                        threadId: "main",
                        cancellationToken: cancellationToken)
                    .AsTask());

            return Pass(
                id,
                area,
                "Session and thread lifecycle works from ISessionStore without constructing an Agent.");
        }
        catch (Exception exception)
        {
            return Failure(
                id,
                area,
                "Store-only session/thread creation should be atomic, composable, and projection-ready.",
                exception);
        }
    }

    private static async Task<ProbeResult> HostingCompositionObservationAsync(
        CancellationToken cancellationToken)
    {
        const string id = "DX-PROBE-016";
        const string area = "hosting";

        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddHPD().AddHPDAgentHost("dx-host-auto");

            await using var app = builder.Build();
            app.MapHPDAgentHostApi("dx-host-auto", options =>
            {
                options.RoutePrefix = "/api/agent";
            });
            await app.StartAsync(cancellationToken);
            await app.StopAsync(cancellationToken);
            return Pass(
                id,
                area,
                "AddHPDAgentHost and MapHPDAgentHostApi start from the explicit AddHPD composition.");
        }
        catch (Exception exception)
        {
            return Failure(
                id,
                area,
                "The explicit AddHPD composition should allow the hosted API to start.",
                exception);
        }
    }

    private static ProbeResult HostingVocabularySurface()
    {
        const string id = "DX-PROBE-029";
        const string area = "hosting";

        try
        {
            var assembly = typeof(HPDAgentHostServiceCollectionExtensions).Assembly;
            var serviceMethods = typeof(HPDAgentHostServiceCollectionExtensions)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Select(method => method.Name)
                .ToArray();
            var endpointMethods = typeof(HPDAgentHostEndpointRouteBuilderExtensions)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Select(method => method.Name)
                .ToArray();
            var hostingTypeNames = typeof(HPDAgentHostOptions).Assembly.GetExportedTypes()
                .Select(type => type.Name)
                .ToHashSet(StringComparer.Ordinal);

            Require(
                serviceMethods.Contains("AddHPDAgentHost", StringComparer.Ordinal),
                "AddHPDAgentHost is missing.");
            Require(
                !serviceMethods.Contains("AddHPDAgent", StringComparer.Ordinal),
                "The ambiguous AddHPDAgent name is still exported.");
            Require(
                endpointMethods.Contains("MapHPDAgentHostApi", StringComparer.Ordinal),
                "MapHPDAgentHostApi is missing.");
            Require(
                !endpointMethods.Contains("MapHPDAgentApi", StringComparer.Ordinal),
                "The ambiguous MapHPDAgentApi name is still exported.");
            Require(
                hostingTypeNames.Contains(nameof(HPDAgentHostOptions)),
                "HPDAgentHostOptions is missing.");
            Require(
                !hostingTypeNames.Contains("HPDAgentConfig"),
                "The overloaded HPDAgentConfig type is still exported.");
            Require(
                assembly.GetReferencedAssemblies().All(reference =>
                    !string.Equals(reference.Name, "HPD-Agent.Evaluations", StringComparison.Ordinal)),
                "The base ASP.NET host still references HPD-Agent.Evaluations.");

            return Pass(
                id,
                area,
                "ASP.NET hosting exports host-profile vocabulary without legacy or evaluation coupling.");
        }
        catch (Exception exception)
        {
            return Failure(
                id,
                area,
                "ASP.NET hosting should expose unambiguous host-profile vocabulary.",
                exception);
        }
    }

    private static ProbeResult ExplicitHostingCompositionSurface()
    {
        const string id = "DX-PROBE-030";
        const string area = "hosting";

        try
        {
            var services = new ServiceCollection();
            services.AddHPD(hpd => hpd.AddOpenAI());
            using var serviceProvider = services.BuildServiceProvider();
            var composition = serviceProvider.GetRequiredService<HPDApplicationComposition>();
            var providerComposition = serviceProvider.GetRequiredService<ProviderComposition>();

            Require(
                ReferenceEquals(composition.Providers, providerComposition),
                "DI provider composition is not the finalized application composition.");
            Require(
                composition.Providers.Descriptors.TryGet("openai", out _),
                "The explicit application composition does not contain OpenAI.");

            return Pass(
                id,
                area,
                "Provider packages contribute through AddHPD without assembly-load workarounds.");
        }
        catch (Exception exception)
        {
            return Failure(
                id,
                area,
                "AddHPD should finalize explicit provider contributions without type-reference workarounds.",
                exception);
        }
    }

    private static async Task<ProbeResult> HostingEndpointObservationAsync(
        CancellationToken cancellationToken)
    {
        const string id = "DX-PROBE-017";
        const string area = "hosting";

        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddHPD(hpd =>
                hpd.UseProviderComposition(ProviderComposition.Create([])));
            builder.Services.AddHPDAgentHost("dx-host-endpoints");

            await using var app = builder.Build();
            app.MapHPDAgentHostApi("dx-host-endpoints", options =>
            {
                options.RoutePrefix = "/api/agent";
            });
            await app.StartAsync(cancellationToken);

            using var client = app.GetTestClient();
            using var eventCatalog = await client.GetAsync(
                "/api/agent/event-catalog",
                cancellationToken);
            using var inputCatalog = await client.GetAsync(
                "/api/agent/input-catalog",
                cancellationToken);
            using var sessions = await client.GetAsync(
                "/api/agent/sessions",
                cancellationToken);

            eventCatalog.EnsureSuccessStatusCode();
            inputCatalog.EnsureSuccessStatusCode();
            sessions.EnsureSuccessStatusCode();
            Require(
                (await eventCatalog.Content.ReadAsStringAsync(cancellationToken)).Contains(
                    "\"events\"",
                    StringComparison.Ordinal),
                "Event catalog response did not contain events.");
            Require(
                (await inputCatalog.Content.ReadAsStringAsync(cancellationToken)).Contains(
                    "\"inputs\"",
                    StringComparison.Ordinal),
                "Input catalog response did not contain inputs.");
            Require(
                (await sessions.Content.ReadAsStringAsync(cancellationToken)).Trim() == "[]",
                "A new hosted session list was not empty.");

            using var created = await client.PostAsJsonAsync(
                "/api/agent/sessions",
                new { sessionId = "hosted-session" },
                cancellationToken);
            Require(created.StatusCode == System.Net.HttpStatusCode.Created,
                $"Hosted session creation returned {created.StatusCode}.");
            Require(
                (await created.Content.ReadAsStringAsync(cancellationToken)).Contains(
                    "hosted-session",
                    StringComparison.Ordinal),
                "Hosted session response did not contain the requested ID.");

            using var populatedSessions = await client.GetAsync(
                "/api/agent/sessions",
                cancellationToken);
            populatedSessions.EnsureSuccessStatusCode();
            Require(
                (await populatedSessions.Content.ReadAsStringAsync(cancellationToken)).Contains(
                    "hosted-session",
                    StringComparison.Ordinal),
                "Created hosted session was absent from the session list.");

            await app.StopAsync(cancellationToken);
            return Pass(
                id,
                area,
                "Catalogs and hosted session creation/listing serve successfully.");
        }
        catch (Exception exception)
        {
            return Failure(
                id,
                area,
                "Mapped hosted discovery and session endpoints should serve successfully.",
                exception);
        }
    }

    private static ProbeResult ProviderPackageDiscovery()
    {
        const string id = "DX-PROBE-018";
        const string area = "providers";

        try
        {
            _ = typeof(HPD.Agent.Providers.OpenAI.OpenAIProviderConfig).Assembly;
            var builder = new AgentBuilder();
            var providers = builder.GetAvailableProviders();
            Require(
                providers.Contains("openai", StringComparer.Ordinal),
                $"Registered providers: {string.Join(", ", providers)}.");
            return Pass(id, area, "The OpenAI provider package is discoverable from AgentBuilder.");
        }
        catch (Exception exception)
        {
            return Failure(
                id,
                area,
                "Referencing a provider package should make its provider discoverable.",
                exception);
        }
    }

    private static async Task<ProbeResult> ToolFailureObservationAsync(
        CancellationToken cancellationToken)
    {
        const string id = "DX-PROBE-019";
        const string area = "tools";

        try
        {
            var config = CreateConfig(new ToolCallingChatClient(
                "explode",
                new Dictionary<string, object?>(),
                "The tool failed cleanly."));
            config.ServerConfiguredTools =
            [
                HPDAIFunctionFactory.Create(
                    (_, _, _) => throw new InvalidOperationException("probe explosion"),
                    new HPDAIFunctionFactoryOptions
                    {
                        Name = "explode",
                        Description = "Always throws."
                    })
            ];

            await using var agent = await new AgentBuilder(config).BuildAsync(cancellationToken);
            var result = await agent.RunAsync(
                "Call explode",
                cancellationToken: cancellationToken).WaitAsync(
                    TimeSpan.FromSeconds(5),
                    cancellationToken);
            Require(result.ToolCalls.Count == 1, "The failed tool call was not reported.");
            return Pass(
                id,
                area,
                "A throwing tool completes the run and reports one tool call.");
        }
        catch (Exception exception)
        {
            return Failure(
                id,
                area,
                "A throwing tool should surface through the tool result path.",
                exception);
        }
    }

    private static async Task<ProbeResult> ToolTimeoutObservationAsync(
        CancellationToken cancellationToken)
    {
        const string id = "DX-PROBE-020";
        const string area = "tools";

        try
        {
            var config = CreateConfig(new ToolCallingChatClient(
                "slow",
                new Dictionary<string, object?>(),
                "The slow tool was stopped."));
            config.ServerConfiguredTools =
            [
                HPDAIFunctionFactory.Create(
                    async (_, _, token) =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10), token);
                        return "too late";
                    },
                    new HPDAIFunctionFactoryOptions
                    {
                        Name = "slow",
                        Description = "Exceeds the configured timeout."
                    })
            ];

            await using var agent = await new AgentBuilder(config)
                .WithFunctionTimeout(TimeSpan.FromMilliseconds(100))
                .BuildAsync(cancellationToken);
            try
            {
                _ = await agent.RunAsync(
                    "Call slow",
                    cancellationToken: cancellationToken).WaitAsync(
                        TimeSpan.FromSeconds(5),
                        cancellationToken);
                return Pass(id, area, "A timed-out tool completed through the tool error path.");
            }
            catch (TimeoutException)
            {
                return Pass(id, area, "A timed-out tool propagated TimeoutException.");
            }
        }
        catch (Exception exception)
        {
            return Failure(
                id,
                area,
                "A tool timeout should terminate or report within the configured bound.",
                exception);
        }
    }

    private static async Task<ProbeResult> ToolRetryObservationAsync(
        CancellationToken cancellationToken)
    {
        const string id = "DX-PROBE-021";
        const string area = "tools";
        var calls = 0;

        try
        {
            var config = CreateConfig(new ToolCallingChatClient(
                "flaky",
                new Dictionary<string, object?>(),
                "The retry completed."));
            config.ServerConfiguredTools =
            [
                HPDAIFunctionFactory.Create(
                    (_, _, _) =>
                    {
                        if (Interlocked.Increment(ref calls) == 1)
                            throw new InvalidOperationException("transient probe failure");
                        return Task.FromResult<object?>("recovered");
                    },
                    new HPDAIFunctionFactoryOptions
                    {
                        Name = "flaky",
                        Description = "Fails once, then succeeds.",
                        Replay = ReplayPolicy.Idempotent
                    })
            ];

            await using var agent = await new AgentBuilder(config)
                .WithRetry(configure =>
                {
                    configure.FunctionRetries.MaxRetries = 1;
                    configure.FunctionRetries.RetryDelay = TimeSpan.Zero;
                    configure.FunctionRetries.MaxRetryDelay = TimeSpan.Zero;
                    configure.FunctionRetries.UseProviderRetryDelays = false;
                })
                .BuildAsync(cancellationToken);
            _ = await agent.RunAsync(
                "Call flaky",
                cancellationToken: cancellationToken).WaitAsync(
                    TimeSpan.FromSeconds(5),
                    cancellationToken);
            Require(calls == 2, $"Observed {calls} flaky-tool calls.");
            return Pass(id, area, "A replay-eligible function retries once after failure.");
        }
        catch (Exception exception)
        {
            return Failure(
                id,
                area,
                "A replay-eligible function should retry according to FunctionRetries.",
                exception);
        }
    }

    private static async Task<ProbeResult> PermissionDecisionObservationAsync(
        bool approve,
        CancellationToken cancellationToken)
    {
        var id = approve ? "DX-PROBE-022" : "DX-PROBE-023";
        const string area = "permissions";
        var executions = 0;
        var requests = 0;

        try
        {
            var config = CreateConfig(new ToolCallingChatClient(
                "sensitive",
                new Dictionary<string, object?>(),
                approve ? "The protected tool ran." : "The protected tool was denied."));
            config.ServerConfiguredTools =
            [
                HPDAIFunctionFactory.Create(
                    (_, _, _) =>
                    {
                        Interlocked.Increment(ref executions);
                        return Task.FromResult<object?>("sensitive result");
                    },
                    new HPDAIFunctionFactoryOptions
                    {
                        Name = "sensitive",
                        Description = "Requires an explicit permission decision.",
                        FunctionPermission = AIFunctionPermissionDeclaration.Required("sensitive")
                    })
            ];

            await using var agent = await new AgentBuilder(config)
                .WithInMemorySessionStore()
                .WithPermissions()
                .BuildAsync(cancellationToken);
            using var subscription = agent.Subscribe<PermissionRequestEvent>(
                (Func<PermissionRequestEvent, Task>)(async request =>
                {
                    Interlocked.Increment(ref requests);
                    _ = await agent.AnswerRequestAsync(
                        request.Answer(approve),
                        cancellationToken);
                }));

            var sessionId = approve ? "permission-allow" : "permission-deny";
            await agent.CreateSessionAsync(sessionId, cancellationToken: cancellationToken);
            _ = await agent.RunAsync(
                "Call sensitive",
                sessionId: sessionId,
                cancellationToken: cancellationToken).WaitAsync(
                    TimeSpan.FromSeconds(5),
                    cancellationToken);

            Require(requests == 1, $"Observed {requests} permission requests.");
            Require(
                executions == (approve ? 1 : 0),
                $"Observed {executions} protected-tool executions after answering {approve}.");
            return Pass(
                id,
                area,
                approve
                    ? "Permission approval executes the protected tool."
                    : "Permission denial stops the protected tool.");
        }
        catch (Exception exception)
        {
            return Failure(
                id,
                area,
                approve
                    ? "Approving a PermissionRequestEvent should execute the protected tool."
                    : "Denying a PermissionRequestEvent should block the protected tool.",
                exception);
        }
    }

    private static async Task<ProbeResult> AttachmentObservationAsync(
        CancellationToken cancellationToken)
    {
        const string id = "DX-PROBE-024";
        const string area = "content";
        var storagePath = Path.Combine(
            Path.GetTempPath(),
            $"hpd-agent-dx-content-{Guid.NewGuid():N}");
        var store = new LocalFileContentStore(storagePath);
        var uploaded = new TaskCompletionSource<ContentUploadedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        try
        {
            var agent = await CreateExplicitOverrideAgentAsync(
                new StaticChatClient("attachment received"),
                cancellationToken,
                builder => builder
                    .WithInMemorySessionStore()
                    .WithContentStore(store));
            using var subscription = agent.Subscribe<ContentUploadedEvent>(evt =>
            {
                uploaded.TrySetResult(evt);
            });

            await agent.CreateSessionAsync(
                "dx-attachment",
                cancellationToken: cancellationToken);
            _ = await agent.RunAsync(
                new UserMessagesInputEvent
                {
                    SessionId = "dx-attachment",
                    ThreadId = "main",
                    Messages =
                    [
                        new ChatMessage(
                            ChatRole.User,
                            [new DataContent(bytes, "image/png")])
                    ]
                },
                cancellationToken).WaitAsync(
                    TimeSpan.FromSeconds(5),
                    cancellationToken);

            var upload = await uploaded.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);
            Require(upload.MediaType == "image/png", $"Observed media type '{upload.MediaType}'.");
            Require(upload.SizeBytes == bytes.Length, $"Observed size {upload.SizeBytes}.");

            var stored = await store.QueryAsync(
                ContentScope.Create("dx-attachment"),
                cancellationToken: cancellationToken);
            Require(stored.Count == 1, $"Observed {stored.Count} stored content items.");

            await using var read = await store.OpenReadAsync(
                stored[0].Address,
                cancellationToken);
            var opened = read
                ?? throw new AuditRequirementException("Stored attachment could not be reopened.");
            using var memory = new MemoryStream();
            await opened.Content.CopyToAsync(memory, cancellationToken);
            Require(
                memory.ToArray().SequenceEqual(bytes),
                "Stored attachment bytes differ from the input.");

            await agent.DisposeAsync();
            return Pass(
                id,
                area,
                "A DataContent attachment is stored, observable, and readable.");
        }
        catch (Exception exception)
        {
            return Failure(
                id,
                area,
                "A DataContent attachment should be stored and readable in session scope.",
                exception);
        }
        finally
        {
            if (Directory.Exists(storagePath))
                Directory.Delete(storagePath, recursive: true);
        }
    }

    private static async Task<ProbeResult> ToolOutputOffloadObservationAsync(
        CancellationToken cancellationToken)
    {
        const string id = "DX-PROBE-025";
        const string area = "content";
        var storagePath = Path.Combine(
            Path.GetTempPath(),
            $"hpd-agent-dx-offload-{Guid.NewGuid():N}");
        var store = new LocalFileContentStore(storagePath);
        var projected = new TaskCompletionSource<ToolOutputProjectedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        const string evidence = "offloaded evidence body";

        try
        {
            var serializerOptions = new JsonSerializerOptions(DxAuditJsonContext.Default.Options)
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            serializerOptions.MakeReadOnly();
            var tool = HPDAIFunctionFactory.Create(
                (_, _, _) => Task.FromResult<object?>(
                    new OffloadProbeResult("complete", evidence)),
                new HPDAIFunctionFactoryOptions
                {
                    Name = "OffloadProbe",
                    Description = "Returns one offloaded result part.",
                    ResultType = typeof(OffloadProbeResult),
                    SerializerOptions = serializerOptions,
                    ContentProjectionPlan = new ContentProjectionPlan(
                        typeof(OffloadProbeResult),
                        [
                            new ContentProjectionPath(
                                "/evidence",
                                "evidence",
                                ContentSensitivity.Internal)
                        ])
                });

            var chatClient = new ToolCallingChatClient(
                "OffloadProbe",
                new Dictionary<string, object?>(),
                "offload complete");
            var config = CreateConfig(chatClient);
            config.ServerConfiguredTools = [tool];

            await using var agent = await new AgentBuilder(config)
                .WithInMemorySessionStore()
                .WithContentStore(store)
                .BuildAsync(cancellationToken);
            using var subscription = agent.Subscribe<ToolOutputProjectedEvent>(evt =>
            {
                projected.TrySetResult(evt);
            });

            await agent.CreateSessionAsync(
                "dx-offload",
                cancellationToken: cancellationToken);
            _ = await agent.RunAsync(
                "Call OffloadProbe",
                sessionId: "dx-offload",
                cancellationToken: cancellationToken).WaitAsync(
                    TimeSpan.FromSeconds(5),
                    cancellationToken);

            var stored = await store.QueryAsync(
                ContentScope.Create("dx-offload"),
                cancellationToken: cancellationToken);
            if (stored.Count == 0)
            {
                var resultShape = string.Join(
                    " | ",
                    chatClient.Requests
                        .SelectMany(message => message)
                        .SelectMany(message => message.Contents)
                        .OfType<FunctionResultContent>()
                        .Where(content => content.CallId == "call-1")
                        .Select(content => content.Result?.ToString() ?? "<null>"));
                throw new AuditRequirementException(
                    "The run completed but no tool-output bundle was stored. " +
                    $"Observed function result: {resultShape}");
            }
            Require(stored.Count == 1, $"Observed {stored.Count} stored tool-output bundles.");
            await using var read = await store.OpenReadAsync(
                stored[0].Address,
                cancellationToken);
            var opened = read
                ?? throw new AuditRequirementException("Offloaded content could not be reopened.");
            using var memory = new MemoryStream();
            await opened.Content.CopyToAsync(memory, cancellationToken);
            var text = System.Text.Encoding.UTF8.GetString(memory.ToArray());
            Require(
                text.Contains(evidence, StringComparison.Ordinal),
                "Offloaded evidence body was not retained.");

            var projectionObserved = projected.Task.IsCompletedSuccessfully;
            if (projectionObserved)
            {
                var projection = await projected.Task;
                Require(
                    projection.FunctionName == "OffloadProbe",
                    "Projection named the wrong function.");
                Require(
                    projection.PartCount == 1,
                    $"Observed {projection.PartCount} offloaded parts.");
            }

            return Pass(
                id,
                area,
                projectionObserved
                    ? "A marked tool result is projected, stored, observable, and readable."
                    : "A marked tool result is stored and readable; its event was not observed.");
        }
        catch (Exception exception)
        {
            return Failure(
                id,
                area,
                "A marked tool result should be replaced by a retrievable content reference.",
                exception);
        }
        finally
        {
            if (Directory.Exists(storagePath))
                Directory.Delete(storagePath, recursive: true);
        }
    }

    private static ProbeResult PortableProviderSelection()
    {
        const string id = "DX-PROBE-026";
        const string area = "providers";

        try
        {
            var builder = new AgentBuilder();
            builder.WithProvider(
                "openai",
                "gpt-dx",
                new ApiKeyProviderAuthentication
                {
                    SecretKey = "openai:ApiKey"
                });

            var json = HpdAgentConfigSerializer.Serialize(builder.Config);
            Require(
                !json.Contains("super-secret", StringComparison.Ordinal),
                "A runtime secret appeared in portable configuration.");
            var restored = HpdAgentConfigSerializer.Deserialize(json)
                ?? throw new AuditRequirementException("Portable provider configuration returned null.");
            var chat = restored.ResolveClientConfig(ProviderClientFamily.Chat)
                ?? throw new AuditRequirementException("Chat provider configuration was absent after round-trip.");
            var provider = chat.Provider
                ?? throw new AuditRequirementException("Provider reference was absent after round-trip.");
            Require(provider.Key == "openai", "Provider key did not round-trip.");
            Require(chat.ModelName == "gpt-dx", "Provider model did not round-trip.");
            Require(
                provider.Authentication is ApiKeyProviderAuthentication
                {
                    SecretKey: "openai:ApiKey"
                },
                "Portable API-key reference did not round-trip.");

            return Pass(
                id,
                area,
                "Portable provider/backend/model/auth selection round-trips without credential material.");
        }
        catch (Exception exception)
        {
            return Failure(
                id,
                area,
                "Portable provider configuration should round-trip without credential material.",
                exception);
        }
    }

    private static ProbeResult ExplicitSecretSerialization()
    {
        const string id = "DX-PROBE-027";
        const string area = "providers";
        const string secret = "super-secret";

        try
        {
            var builder = new AgentBuilder();
            builder.WithProvider("openai", "gpt-dx", secret.AsSpan());
            try
            {
                _ = HpdAgentConfigSerializer.Serialize(builder.Config);
            }
            catch (AgentRunConfigurationException exception)
                when (exception.Code == "RuntimeOnlyProviderConfiguration")
            {
                Require(
                    !exception.ToString().Contains(secret, StringComparison.Ordinal),
                    "The explicit API key appeared in the refusal.");
                return Pass(
                    id,
                    area,
                    "Explicit runtime API-key registration is explicitly refused for portable serialization.");
            }

            throw new AuditRequirementException(
                "A process-local explicit API key was serialized instead of being refused.");
        }
        catch (Exception exception)
        {
            return Failure(
                id,
                area,
                "Explicit runtime API-key registration should not prevent portable configuration or leak the secret.",
                exception);
        }
    }

    private static async Task<Agent> CreateExplicitOverrideAgentAsync(
        IChatClient client,
        CancellationToken cancellationToken,
        Func<AgentBuilder, AgentBuilder>? configure = null)
    {
        var config = CreateConfig(client);
        var builder = new AgentBuilder(config);
        builder = configure?.Invoke(builder) ?? builder;
        return await builder.BuildAsync(cancellationToken);
    }

    private static AgentConfig CreateConfig(IChatClient client)
    {
        var config = new AgentConfig
        {
            Name = "DX Audit",
            SystemInstructions = "Be concise."
        };
        config.EnsureChatClientConfig().Override = ClientOverride<IChatClient>.Borrow(
            client,
            "dx-audit",
            "in-memory",
            "dx-audit/chat");
        return config;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new AuditRequirementException(message);
    }

    private static async Task AssertThrowsInvalidOperationAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new AuditRequirementException("Expected InvalidOperationException.");
    }

    private static ProbeResult Pass(string id, string area, string summary) =>
        new(id, area, AuditStatus.Pass, summary);

    private static ProbeResult Gap(string id, string area, string summary, string? detail = null) =>
        new(id, area, AuditStatus.Gap, summary, detail);

    private static ProbeResult Failure(
        string id,
        string area,
        string summary,
        Exception exception)
    {
        var detail = $"{exception.GetType().Name}: {exception.Message}";
        if (exception is AgentRunConfigurationException runConfigurationException)
            detail = $"Code={runConfigurationException.Code}; {detail}";

        return new ProbeResult(id, area, AuditStatus.Failure, summary, detail);
    }
}

internal sealed class AuditRequirementException(string message) : Exception(message);

internal sealed record AuditPerson(string Name, int Age);

internal sealed record OffloadProbeResult(
    string Summary,
    [property: Offload(Sensitivity = ContentSensitivity.Internal)] string Evidence);

[JsonSerializable(typeof(OffloadProbeResult))]
internal partial class DxAuditJsonContext : JsonSerializerContext;

internal sealed class CountingMiddleware : IAgentMiddleware
{
    private int _beforeMessageTurnCalls;

    public int BeforeMessageTurnCalls => Volatile.Read(ref _beforeMessageTurnCalls);

    public Task BeforeMessageTurnAsync(
        BeforeMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _beforeMessageTurnCalls);
        return Task.CompletedTask;
    }
}

internal sealed class StaticChatClient(string response) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, response);
        await Task.CompletedTask;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

internal sealed class SlowChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This probe exercises streaming.");

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, "too late");
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

internal sealed class HistoryEchoChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This probe exercises streaming.");

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var userMessages = messages.Count(message => message.Role == ChatRole.User);
        yield return new ChatResponseUpdate(ChatRole.Assistant, $"User messages: {userMessages}");
        await Task.CompletedTask;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

internal sealed class ToolCallingChatClient(
    string functionName = "double",
    Dictionary<string, object?>? arguments = null,
    string finalResponse = "The value is 42.") : IChatClient
{
    private int _callCount;

    public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This probe exercises streaming.");

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Requests.Add(messages.ToArray());
        if (Interlocked.Increment(ref _callCount) == 1)
        {
            yield return new ChatResponseUpdate(
                ChatRole.Assistant,
                [
                    new FunctionCallContent(
                        "call-1",
                        functionName,
                        arguments ?? new Dictionary<string, object?> { ["value"] = 21 })
                ]);
        }
        else
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, finalResponse);
        }

        await Task.CompletedTask;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
