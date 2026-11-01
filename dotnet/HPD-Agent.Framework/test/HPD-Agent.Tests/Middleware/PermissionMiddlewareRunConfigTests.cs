using FluentAssertions;
using HPD.Agent.ClientTools;
using HPD.Agent.Middleware;
using HPD.Agent.Permissions;
using HPD.Agent.Tests.Middleware.V2;
using HPD.Events.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace HPD.Agent.Tests.Middleware;

public class PermissionMiddlewareRunConfigTests
{
    [Fact]
    public async Task BeforeFunction_AutoApprove_BypassesRequiredPermission()
    {
        var middleware = new PermissionMiddleware();
        var function = CreateFunction("SensitiveTool", requiresPermission: true);
        var context = CreateBeforeFunctionContext(
            function,
            new AgentRunConfig
            {
                Security = new AgentSecurityRunConfig
                {
                    Approval = AgentApprovalPolicy.AutoApprove
                }
            });

        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        context.BlockExecution.Should().BeFalse();
        context.OverrideResult.Should().BeNull();
    }

    [Fact]
    public async Task BeforeFunction_RunConfigFalseOverride_AllowsFunctionWithRequiresPermission()
    {
        var middleware = new PermissionMiddleware();
        var function = CreateFunction("SensitiveTool", requiresPermission: true);
        var context = CreateBeforeFunctionContext(
            function,
            new AgentRunConfig
            {
                Security = new AgentSecurityRunConfig { PermissionOverrides =
                    [new(new("SensitiveTool"), RequiresPermission: false)] }
            });

        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        context.BlockExecution.Should().BeFalse();
        context.OverrideResult.Should().BeNull();
    }

    [Fact]
    public async Task BeforeFunction_RunConfigOverride_WinsOverBuilderOverride()
    {
        var registry = new PermissionOverrideRegistry();
        registry.RequirePermission("NormallyRequiredByBuilder");
        var middleware = new PermissionMiddleware(overrideRegistry: registry);
        var function = CreateFunction("NormallyRequiredByBuilder", requiresPermission: false);
        var context = CreateBeforeFunctionContext(
            function,
            new AgentRunConfig
            {
                Security = new AgentSecurityRunConfig { PermissionOverrides =
                    [new(new("NormallyRequiredByBuilder"), RequiresPermission: false)] }
            });

        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        context.BlockExecution.Should().BeFalse();
        context.OverrideResult.Should().BeNull();
    }

    [Fact]
    public async Task BeforeFunction_RunConfigUnknownFunctionOverride_IsIgnored()
    {
        var middleware = new PermissionMiddleware();
        var function = CreateFunction("ActualTool", requiresPermission: false);
        var context = CreateBeforeFunctionContext(
            function,
            new AgentRunConfig
            {
                Security = new AgentSecurityRunConfig { PermissionOverrides =
                    [new(new("MissingTool"), RequiresPermission: true)] }
            });

        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        context.BlockExecution.Should().BeFalse();
        context.OverrideResult.Should().BeNull();
    }

    [Fact]
    public async Task BeforeFunction_ReturnToModelDenial_DoesNotInterruptTurn()
    {
        var middleware = new PermissionMiddleware();
        var function = HPDAIFunctionFactory.Create(
            async (_, _, _) => "ok",
            new HPDAIFunctionFactoryOptions
            {
                Name = "SensitiveTool",
                FunctionPermission = AIFunctionPermissionDeclaration.Required("SensitiveTool") with
                {
                    PolicyDescriptorId = "test.return-to-model"
                }
            });
        using var services = new ServiceCollection().BuildServiceProvider();
        var coordinator = new EventCoordinator();
        var interruptions = new List<InterruptionHandledEvent>();
        using var interruptionSubscription = coordinator.Subscribe<InterruptionHandledEvent>(handled =>
        {
            interruptions.Add(handled);
            return ValueTask.CompletedTask;
        });
        using var subscription = coordinator.Subscribe<PermissionRequestEvent>(request =>
        {
            coordinator.Respond(new PermissionResponseEvent(
                request.PermissionId,
                request.SourceName,
                ChoiceId: "deny_once",
                Feedback: "Use the read-only status tool instead."));
            return ValueTask.CompletedTask;
        });
        var agentContext = CreateAgentContext(coordinator, services);
        var context = agentContext.AsBeforeFunction(
            function,
            "call-1",
            new Dictionary<string, object?>(),
            new AgentRunConfig());

        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        context.BlockExecution.Should().BeTrue();
        var result = context.OverrideResult.Should().BeOfType<string>().Subject;
        result.Should().Contain("<tool_permission");
        result.Should().Contain("outcome=\"denied\"");
        result.Should().Contain("executed=\"false\"");
        result.Should().Contain("Use the read-only status tool instead.");
        interruptions.Should().BeEmpty();
    }

    [Fact]
    public async Task BeforeFunction_UnknownCompoundAction_ReturnsValidationRejection()
    {
        var definition = new ClientToolDefinition
        {
            Name = "penpot",
            Description = "Penpot operations.",
            ParametersSchema = JsonDocument.Parse(
                """
                {
                  "type": "object",
                  "oneOf": [{
                    "type": "object",
                    "properties": { "action": { "const": "inspect" } },
                    "required": ["action"],
                    "additionalProperties": false
                  }]
                }
                """).RootElement.Clone(),
            OperationContract = new ClientToolOperationContract
            {
                Discriminator = "action",
                Actions = new Dictionary<string, ClientToolPolicy>
                {
                    ["inspect"] = new()
                }
            }
        };
        var function = HPDAIFunctionFactory.Create(
            async (_, _, _) => "ok",
            new HPDAIFunctionFactoryOptions
            {
                Name = "penpot_design_penpot",
                Description = "Penpot operations.",
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["ClientToolDefinition"] = definition
                }
            });
        var context = MiddlewareTestHelpers.CreateAgentContext().AsBeforeFunction(
            function,
            "call-1",
            new Dictionary<string, object?> { ["action"] = "unknown" },
            new AgentRunConfig());

        await new PermissionMiddleware().BeforeFunctionAsync(
            context,
            CancellationToken.None);

        context.BlockExecution.Should().BeTrue();
        context.OverrideResult.Should().BeOfType<string>().Which.Should()
            .Contain("Client tool request rejected: Unknown compound tool action 'unknown'.");
    }

    [Fact]
    public async Task BeforeFunction_ImmediatePolicyAllow_CompletesWithoutInteraction()
    {
        var middleware = new PermissionMiddleware();
        var function = CreateImmediatePolicyFunction();
        using var services = new ServiceCollection().BuildServiceProvider();
        var coordinator = new EventCoordinator();
        var requests = 0;
        using var subscription = coordinator.Subscribe<PermissionRequestEvent>(_ =>
        {
            requests++;
            return ValueTask.CompletedTask;
        });
        var context = CreateAgentContext(coordinator, services).AsBeforeFunction(
            function,
            "call-immediate",
            new Dictionary<string, object?>(),
            new AgentRunConfig());

        await middleware.BeforeFunctionAsync(context, CancellationToken.None);

        context.BlockExecution.Should().BeFalse();
        context.OverrideResult.Should().BeNull();
        context.PermissionGrant.Should().NotBeNull();
        context.PermissionGrant!.Source.Should().Be(PermissionGrantSource.PolicyDecision);
        requests.Should().Be(0);
    }

    [Fact]
    public async Task BeforeFunction_StoredDeny_OutranksImmediatePolicyAllow()
    {
        var store = new InMemorySessionStore(
            HPD.Agent.Tests.Infrastructure.TestEventComposition.Current.Codec);
        var key = new PermissionKey(
            "SensitiveTool",
            null,
            "function/SensitiveTool",
            ImmediatePermissionPolicy.Id,
            "1");
        var record = new PermissionPreferenceRecord
        {
            PreferenceId = "stored-deny",
            Key = key,
            Decision = PermissionDecisionKind.Deny,
            Kind = PermissionPersistenceKind.SessionKey,
            CreatedAt = DateTimeOffset.UtcNow
        };
        (await store.CommitAsync(new PermissionPreferenceCommit
        {
            SessionId = "stored-session",
            AuditThread = new ThreadKey("stored-session", "stored-thread"),
            ExpectedVersion = 0,
            Replacement = new PermissionPreferenceSnapshot
            {
                Version = 1,
                SchemaVersion = PermissionPreferenceSchema.CurrentVersion,
                Records = [record]
            },
            Event = new PermissionPreferenceChangedEvent(
                record.PreferenceId,
                key,
                record.Decision,
                record.Kind),
            IdempotencyKey = "stored-deny-commit",
            PublisherClaimantId = "test"
        }, CancellationToken.None)).Status.Should().Be(PermissionPreferenceCommitStatus.Committed);

        using var services = new ServiceCollection().BuildServiceProvider();
        var coordinator = new EventCoordinator();
        var context = CreateAgentContext(
                coordinator,
                services,
                store,
                "stored-session",
                "stored-thread")
            .AsBeforeFunction(
                CreateImmediatePolicyFunction(),
                "call-stored-deny",
                new Dictionary<string, object?>(),
                new AgentRunConfig());

        await new PermissionMiddleware().BeforeFunctionAsync(context, CancellationToken.None);

        context.BlockExecution.Should().BeTrue();
        context.PermissionGrant.Should().BeNull();
        context.OverrideResult.Should().BeOfType<string>().Which.Should()
            .Contain("stored user preference");
    }

    [Fact]
    public async Task BeforeFunction_LegacyPreferenceSchema_IsUnavailable()
    {
        var store = new InMemorySessionStore(
            HPD.Agent.Tests.Infrastructure.TestEventComposition.Current.Codec);
        var key = new PermissionKey(
            "SensitiveTool",
            null,
            "function/SensitiveTool",
            ImmediatePermissionPolicy.Id,
            "1");
        var record = new PermissionPreferenceRecord
        {
            PreferenceId = "legacy-stored-deny",
            Key = key,
            Decision = PermissionDecisionKind.Deny,
            Kind = PermissionPersistenceKind.SessionKey,
            CreatedAt = DateTimeOffset.UtcNow
        };
        (await store.CommitAsync(new PermissionPreferenceCommit
        {
            SessionId = "legacy-session",
            AuditThread = new ThreadKey("legacy-session", "legacy-thread"),
            ExpectedVersion = 0,
            Replacement = new PermissionPreferenceSnapshot
            {
                Version = 1,
                SchemaVersion = PermissionPreferenceSchema.CurrentVersion - 1,
                Records = [record]
            },
            Event = new PermissionPreferenceChangedEvent(
                record.PreferenceId,
                key,
                record.Decision,
                record.Kind),
            IdempotencyKey = "legacy-stored-deny-commit",
            PublisherClaimantId = "test"
        }, CancellationToken.None)).Status.Should().Be(PermissionPreferenceCommitStatus.Committed);

        using var services = new ServiceCollection().BuildServiceProvider();
        var context = CreateAgentContext(
                new EventCoordinator(),
                services,
                store,
                "legacy-session",
                "legacy-thread")
            .AsBeforeFunction(
                CreateImmediatePolicyFunction(),
                "call-legacy-preference",
                new Dictionary<string, object?>(),
                new AgentRunConfig());

        await new PermissionMiddleware().BeforeFunctionAsync(context, CancellationToken.None);

        context.BlockExecution.Should().BeFalse();
        context.PermissionGrant.Should().NotBeNull();
        context.PermissionGrant!.Source.Should().Be(PermissionGrantSource.PolicyDecision);
    }

    [PermissionPolicyDefinition("test.return-to-model")]
    internal sealed class ReturnToModelPolicy : IPermissionPolicy
    {
        public ValueTask<PermissionEvaluation> EvaluateAsync(
            PermissionEvaluationContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PermissionEvaluation
            {
                PolicyId = "test.return-to-model",
                PolicyRevision = "1",
                Authority = context.Authority,
                Title = "Allow SensitiveTool?",
                Risk = PermissionRisk.Medium,
                Choices = new PermissionChoiceSet
                {
                    Items = [new PermissionChoiceDescriptor
                    {
                        Id = "deny_once",
                        Label = "Deny",
                        Decision = PermissionDecisionKind.Deny,
                        DeniedBehavior = PermissionDeniedBehavior.ReturnToModel
                    }]
                }
            });
    }

    [PermissionPresentation("test.immediate-presentation", typeof(ImmediatePermissionJsonContext))]
    public sealed record ImmediatePermissionPresentation(string Message);

    [PermissionPolicyDefinition(ImmediatePermissionPolicy.Id)]
    internal sealed class ImmediatePermissionPolicy :
        PermissionPolicy<ImmediatePermissionPresentation>
    {
        internal const string Id = "test.immediate-policy";

        public override ValueTask<PermissionEvaluation> EvaluateAsync(
            ValidatedPermissionInput input,
            PermissionEvaluationContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PermissionEvaluation
            {
                PolicyId = Id,
                PolicyRevision = "1",
                Authority = context.Authority,
                Title = "Read-only operation",
                Risk = PermissionRisk.Low,
                Choices = new PermissionChoiceSet
                {
                    Items =
                    [
                        new PermissionChoiceDescriptor
                        {
                            Id = "allow",
                            Label = "Allow",
                            Decision = PermissionDecisionKind.Allow
                        },
                        new PermissionChoiceDescriptor
                        {
                            Id = "deny",
                            Label = "Deny",
                            Decision = PermissionDecisionKind.Deny
                        }
                    ]
                },
                ImmediateDecision = new PermissionDecision
                {
                    Kind = PermissionDecisionKind.Allow,
                    ChoiceId = "allow"
                }
            });
    }

    private static AIFunction CreateImmediatePolicyFunction() =>
        HPDAIFunctionFactory.Create(
            async (_, _, _) => "ok",
            new HPDAIFunctionFactoryOptions
            {
                Name = "SensitiveTool",
                Description = "Immediate policy test function",
                FunctionPermission = AIFunctionPermissionDeclaration.Required("SensitiveTool") with
                {
                    PolicyDescriptorId = ImmediatePermissionPolicy.Id
                }
            });

    private static AIFunction CreateFunction(string name, bool requiresPermission)
        => HPDAIFunctionFactory.Create(
            async (_, _, _) => "ok",
            new HPDAIFunctionFactoryOptions
            {
                Name = name,
                Description = $"{name} test function",
                FunctionPermission = requiresPermission
                    ? AIFunctionPermissionDeclaration.Required(name)
                    : null
            });

    private static BeforeFunctionContext CreateBeforeFunctionContext(
        AIFunction function,
        AgentRunConfig runConfig)
    {
        var agentContext = MiddlewareTestHelpers.CreateAgentContext();
        return agentContext.AsBeforeFunction(
            function,
            "call-1",
            new Dictionary<string, object?>(),
            runConfig);
    }

    private static AgentContext CreateAgentContext(
        EventCoordinator coordinator,
        IServiceProvider? services = null,
        ISessionStore? store = null,
        string sessionId = "test-session",
        string threadId = "test-thread")
    {
        var state = AgentLoopState.InitialSafe(
            new List<ChatMessage>(),
            "test-run",
            "test-conv",
            "TestAgent");

        var session = new HPD.Agent.Session(sessionId) { Store = store };
        return new AgentContext(
            "TestAgent",
            "test-conv",
            state,
            coordinator,
            session,
            new HPD.Agent.Thread(sessionId, "test-agent") { Id = threadId },
            CancellationToken.None,
            config: new AgentConfig { EventComposition = HPD.Agent.Tests.Infrastructure.TestEventComposition.Current },
            services: services);
    }
}

[JsonSerializable(typeof(PermissionMiddlewareRunConfigTests.ImmediatePermissionPresentation))]
internal partial class ImmediatePermissionJsonContext : JsonSerializerContext;
