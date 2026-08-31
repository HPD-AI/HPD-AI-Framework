using System.Text.Json;
using HPD.Agent.Permissions;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Permissions;

public sealed class GeneratedPermissionArchitectureTests
{
    [Fact]
    public void Composition_fingerprint_changes_with_permission_authority()
    {
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{},"additionalProperties":false}""");
        var first = CreateFunction("scope/one");
        var second = CreateFunction("scope/two");

        Assert.NotEqual(first.ContractDescriptor!.CanonicalSchemaFingerprint,
            second.ContractDescriptor!.CanonicalSchemaFingerprint);

        HPDAIFunctionFactory.HPDAIFunction CreateFunction(string scope) =>
            (HPDAIFunctionFactory.HPDAIFunction)HPDAIFunctionFactory.Create(
                static (_, _, _) => Task.FromResult<object?>(null),
                new HPDAIFunctionFactoryOptions
                {
                    Name = "secured",
                    SchemaProvider = () => schema.RootElement,
                    FunctionPermission = new AIFunctionPermissionDeclaration
                    {
                        RequiresPermission = true,
                        Scope = scope,
                        Source = PermissionDeclarationSource.FunctionAttribute
                    }
                });
    }

    [Fact]
    public void Function_snapshots_permission_options_at_creation()
    {
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{},"additionalProperties":false}""");
        var options = new HPDAIFunctionFactoryOptions
        {
            Name = "secured",
            SchemaProvider = () => schema.RootElement,
            FunctionPermission = new AIFunctionPermissionDeclaration
            {
                RequiresPermission = true,
                Scope = "scope/original",
                Source = PermissionDeclarationSource.FunctionAttribute
            }
        };
        var function = (HPDAIFunctionFactory.HPDAIFunction)HPDAIFunctionFactory.Create(
            static (_, _, _) => Task.FromResult<object?>(null), options);

        options.FunctionPermission = options.FunctionPermission with { Scope = "scope/mutated" };

        Assert.Equal("scope/original", function.HPDOptions.FunctionPermission!.Scope);
    }

    [Fact]
    public void Verified_action_composition_defers_author_clr_binding()
    {
        var binderCalls = 0;
        using var schemaDocument = JsonDocument.Parse("""
            {
              "type":"object",
              "properties":{
                "request":{
                  "oneOf":[
                    {
                      "type":"object",
                      "properties":{"action":{"type":"string","const":"read"}},
                      "required":["action"],
                      "additionalProperties":false
                    }
                  ]
                }
              },
              "required":["request"],
              "additionalProperties":false
            }
            """);
        var composition = new VerifiedAIFunctionActionComposition(
            schemaDocument.RootElement,
            new AIFunctionOperationContract
            {
                ActionArgumentName = "request",
                Discriminator = "action",
                Actions = new Dictionary<string, AIFunctionActionPolicy>(StringComparer.Ordinal)
                {
                    ["read"] = new()
                    {
                        InvocationModePolicy = AgentInvocationModePolicy.SynchronousOnly,
                        InvocationModeHandling = AgentInvocationModeHandling.Runtime,
                        Permission = new AIFunctionPermissionDeclaration
                        {
                            RequiresPermission = true,
                            Scope = "function/Test/action/read",
                            Source = PermissionDeclarationSource.ActionOverride
                        }
                    }
                }
            },
            json =>
            {
                binderCalls++;
                return AIFunctionBindingResult.Success(new object(), json);
            });

        using var arguments = JsonDocument.Parse("""{"request":{"action":"read"}}""");
        var validation = composition.InputContract.Bind(arguments.RootElement);

        Assert.Empty(validation.Errors);
        Assert.Equal(0, binderCalls);
        Assert.NotNull(composition.FinalArgumentBinder);
        composition.FinalArgumentBinder!(validation.EffectiveJson);
        Assert.Equal(1, binderCalls);
    }

    [Fact]
    public void Permission_override_registry_prefers_exact_typed_selector()
    {
        var registry = new PermissionOverrideRegistry();
        registry.Set(new PermissionOverrideSelector("Portfolio"), false);
        registry.Set(
            new PermissionOverrideSelector(
                "Portfolio",
                "submit",
                "function/Portfolio/action/submit"),
            true);

        Assert.True(registry.Resolve(new PermissionOverrideSelector(
            "Portfolio", "submit", "function/Portfolio/action/submit")));
        Assert.False(registry.Resolve(new PermissionOverrideSelector(
            "Portfolio", "snapshot", "function/Portfolio/action/snapshot")));
    }

    [Fact]
    public void Validated_permission_input_reads_canonical_values_without_author_dto()
    {
        using var arguments = JsonDocument.Parse("""{"path":"/tmp/demo","recursive":true}""");
        var input = new ValidatedPermissionInput(
            arguments.RootElement,
            new ResolvedFunctionInvocation
            {
                Mode = AgentInvocationMode.Synchronous,
                Policy = AgentInvocationModePolicy.SynchronousOnly,
                Handling = AgentInvocationModeHandling.Runtime
            });

        Assert.Equal("/tmp/demo", input.GetRequiredString("path"));
        Assert.True(input.GetBoolean("recursive"));
        Assert.Throws<InvalidOperationException>(() => input.RequireAction());
    }

    [Fact]
    public async Task Preference_commit_is_versioned_idempotent_and_carries_exact_committed_event()
    {
        var store = new InMemorySessionStore(TestEventApplication.Codec);
        var key = new PermissionKey("Documents", "delete", "function/Documents/action/delete", "policy", "1");
        var record = new PermissionPreferenceRecord
        {
            PreferenceId = "preference-1",
            Key = key,
            Decision = PermissionDecisionKind.Allow,
            Kind = PermissionPersistenceKind.SessionKey,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var commit = new PermissionPreferenceCommit
        {
            SessionId = "session-1",
            AuditThread = new ThreadKey("session-1", "main"),
            ExpectedVersion = 0,
            Replacement = new PermissionPreferenceSnapshot(1, [record]),
            Event = new PermissionPreferenceChangedEvent(
                record.PreferenceId, key, record.Decision, record.Kind),
            IdempotencyKey = "permission-1:always_allow:preference-1",
            PublisherClaimantId = "publisher-1"
        };

        var first = await store.CommitAsync(commit, CancellationToken.None);
        var replay = await store.CommitAsync(commit, CancellationToken.None);

        Assert.Equal(PermissionPreferenceCommitStatus.Committed, first.Status);
        Assert.Equal(PermissionPreferenceCommitStatus.AlreadyCommitted, replay.Status);
        Assert.Equal(first.Outbox!.SettlementId, replay.Outbox!.SettlementId);
        Assert.Equal(1, first.Outbox.CommittedEvent.ThreadSequenceNumber);
        Assert.Equal("publisher-1", first.Outbox.ClaimantId);
        Assert.Equal(1, (await store.ReadAsync("session-1", CancellationToken.None)).Version);
    }
}
