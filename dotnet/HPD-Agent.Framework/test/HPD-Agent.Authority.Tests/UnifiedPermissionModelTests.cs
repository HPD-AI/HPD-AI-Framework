using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using HPD.Agent.Middleware;
using HPD.Agent.Permissions;
using HPD.Agent.Serialization;
using HPD.Events.Core;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests;

public sealed class UnifiedPermissionModelTests
{
    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
    private static PermissionDecisionKey Key(string agent = "coding", string policy = "write.v1") => new(agent, "files/write", null, policy);
    private static PermissionDecisionRecord Record(int index, JsonElement? rule = null, string? authority = null)
    {
        var key = Key(policy: $"write.v{index}") with { Authority = authority ?? "files/write" };
        return new()
        {
            RecordId = PermissionRecordIdentity.Create(key,
                rule is null ? PermissionPersistenceKind.Session : PermissionPersistenceKind.ValidatedRule,
                rule is null ? null : "rule.v1", rule),
            PermissionId = index.ToString(), Key = key,
            Persistence = rule is null ? PermissionPersistenceKind.Session : PermissionPersistenceKind.ValidatedRule,
            RuleTypeId = rule is null ? null : "rule.v1", CanonicalRule = rule, CreatedAt = DateTimeOffset.UtcNow
        };
    }

    [Fact]
    public void RuleIdentityIgnoresWhitespaceAndPropertyOrderButPreservesNumberSpelling()
    {
        string Id(string rule) => PermissionRecordIdentity.Create(Key(), PermissionPersistenceKind.ValidatedRule, "rule", Json(rule));
        Assert.Equal(Id("{\"b\":1.0,\"a\":1}"), Id("{ \"a\": 1, \"b\": 1.0 }"));
        Assert.NotEqual(Id("{\"a\":1}"), Id("{\"a\":1.0}"));
        Assert.Throws<InvalidOperationException>(() => Id("{\"a\":1,\"a\":2}"));
        Assert.NotEqual(PermissionRecordIdentity.Create(Key() with { Action = null }, PermissionPersistenceKind.Session),
            PermissionRecordIdentity.Create(Key() with { Action = "" }, PermissionPersistenceKind.Session));
    }

    [Fact]
    public void ApprovalIdentitySeparatesAgentsAndDescriptorVersions()
    {
        Assert.NotEqual(PermissionRecordIdentity.Create(Key("first"), PermissionPersistenceKind.Session),
            PermissionRecordIdentity.Create(Key("second"), PermissionPersistenceKind.Session));
        Assert.NotEqual(Record(1).RecordId, Record(2).RecordId);
    }

    [Fact]
    public void DistinctRecordsAccumulateWithoutFrameworkCapacity()
    {
        var state = new PermissionDecisionStateData
        {
            Decisions = Enumerable.Range(0, 300)
                .Select(index => Record(index, authority: $"files/write/{index}"))
                .ToImmutableArray()
        };
        var updated = PermissionDecisionIndex.Apply(
            state, Record(300, authority: "files/write/300"));
        Assert.Equal(301, updated.Decisions.Length);
    }

    [Fact]
    public void SessionDecisionForNewDescriptorSupersedesOldDescriptor()
    {
        var state = new PermissionDecisionStateData { Decisions = [Record(1)] };
        var replacement = PermissionDecisionIndex.Apply(state, Record(2));
        var decision = Assert.Single(replacement.Decisions);
        Assert.Equal("write.v2", decision.Key.PolicyDescriptorId);
    }

    [Fact]
    public void SimplePromptProjectsOnlyStableFieldsAndAnswersBuiltInChoices()
    {
        var request = new PermissionRequestEvent(
            "permission-1", "call-1", "PermissionMiddleware", PermissionDomains.ToolExecution,
            "acme.docs/write", "write", new PermissionResource("/tmp/report.md", "report.md"),
            "Write the report?", PermissionRisk.Medium,
            [
                new("allow_once", "Allow once", PermissionDecisionKind.Allow),
                new("allow_session", "Allow for session", PermissionDecisionKind.Allow),
                new("deny_once", "Deny", PermissionDecisionKind.Deny)
            ],
            FunctionName: "write_file",
            Arguments: Json("{\"path\":\"/tmp/report.md\"}"));

        var prompt = request.GetPrompt();
        Assert.Equal("permission-1", prompt.PermissionId);
        Assert.Equal("call-1", prompt.CallId);
        Assert.Equal("write_file", prompt.FunctionName);
        Assert.Equal("write", prompt.Action);
        Assert.Equal("/tmp/report.md", prompt.Arguments.GetProperty("path").GetString());
        Assert.Equal("Write the report?", prompt.Reason);

        var approved = request.Answer(new PermissionAnswer("permission-1", true));
        Assert.Equal("allow_once", approved.ChoiceId);
        var denied = request.Answer(false, "Do not write that file.");
        Assert.Equal("deny_once", denied.ChoiceId);
        Assert.Equal("Do not write that file.", denied.Feedback);
    }

    [Fact]
    public void RuleHeavyStateAccumulatesBeyondFormerGlobalByteBudget()
    {
        var state = new PermissionDecisionStateData();
        var rule = Json("\"" + new string('x', 16000) + "\"");
        for (var index = 0; index < 80; index++)
            state = PermissionDecisionIndex.Apply(state, Record(index, rule, authority: $"files/write/{index}"));
        Assert.Equal(80, state.Decisions.Length);
        Assert.True(Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(
            state, SessionJsonContext.Default.PermissionDecisionStateData)) > 1024 * 1024);
    }

    [Fact]
    public void DistinctRulesCoexistAndSameRuleReplacesOnlyItself()
    {
        var first = Record(1, Json("{\"command\":\"git status\"}"));
        var second = first with { CanonicalRule = Json("{\"command\":\"git diff\"}") };
        second = second with { RecordId = PermissionRecordIdentity.Create(second.Key, second.Persistence, second.RuleTypeId, second.CanonicalRule) };
        var state = PermissionDecisionIndex.Apply(PermissionDecisionIndex.Apply(null, first), second);
        state = PermissionDecisionIndex.Apply(state, first with { PermissionId = "new-user-decision" });
        Assert.Equal(2, state.Decisions.Length);
        Assert.Contains(state.Decisions, record => record.RecordId == first.RecordId && record.PermissionId == "new-user-decision");
        Assert.Contains(second, state.Decisions);
    }

    [Fact]
    public void CorruptRecordDoesNotHideValidSiblingOrAuthorize()
    {
        var valid = Record(1);
        var state = new PermissionDecisionStateData { Decisions = [valid, Record(2) with { RecordId = "corrupt" }] };
        Assert.Equal(valid, Assert.Single(PermissionDecisionIndex.Available(state).Decisions));
    }

    [Fact]
    public void RegisteredStateSerializerMatchesCapacityMeasurement()
    {
        var factory = AgentGeneratedRegistry.Snapshot().States.Single(state =>
            state.StateType == typeof(PermissionDecisionStateData));
        var state = new PermissionDecisionStateData { Decisions = [Record(1)] };
        Assert.Equal(JsonSerializer.Serialize(state, SessionJsonContext.Default.PermissionDecisionStateData), factory.Serialize(state));
        Assert.NotNull(factory.Deserialize(factory.Serialize(state)));
    }

    [Fact]
    public void InnerAndOuterSerializationPreserveRuleIdentity()
    {
        var record = Record(1, Json("{\"integral\":1,\"fractional\":1.0}"));
        var state = new PermissionDecisionStateData { Decisions = [record] };
        var session = new HPD.Agent.Session("roundtrip");
        new MiddlewareState().SetState(PermissionDecisionIndex.StateKey, state).SaveToSession(session, Factories());
        var serialized = JsonSerializer.Serialize(session, SessionJsonContext.Default.Session);
        var restored = JsonSerializer.Deserialize(serialized, SessionJsonContext.Default.Session)!;
        var loaded = MiddlewareState.LoadFromSession(restored, Factories()).GetState<PermissionDecisionStateData>(PermissionDecisionIndex.StateKey)!;
        Assert.Equal(record.RecordId, Assert.Single(loaded.Decisions).RecordId);
        Assert.True(PermissionDecisionIndex.IsValid(loaded.Decisions[0]));
        Assert.Contains("1.0", loaded.Decisions[0].CanonicalRule!.Value.GetRawText());
    }

    [Fact]
    public void AllowDeniedBehaviorAndPersistentDenialAreRejected()
    {
        PermissionPolicyResult Ask(PermissionChoice allow, PermissionChoice deny) => new()
        {
            Outcome = new PermissionAsk(null, null, null, [allow, deny]),
            PersistenceByChoice = new Dictionary<string, PermissionPersistenceProposal>
            { [deny.Id] = new() { Kind = PermissionPersistenceKind.Session } }
        };
        Assert.Throws<InvalidOperationException>(() => PermissionValidation.Validate(Ask(
            new("allow", "Allow", PermissionDecisionKind.Allow), new("deny", "Deny", PermissionDecisionKind.Deny)), null));
        Assert.Throws<InvalidOperationException>(() => PermissionValidation.Validate(new()
        { Outcome = new PermissionAsk(null, null, null, [new("allow", "Allow", PermissionDecisionKind.Allow, PermissionDeniedBehavior.ReturnToModel), new("deny", "Deny", PermissionDecisionKind.Deny)]) }, null));
    }

    [Fact]
    public void ImportRequiresExplicitStatusAndPreservesMetadata()
    {
        var original = AIFunctionFactory.Create(() => "result", new() { Name = "plain" });
        Assert.Throws<InvalidOperationException>(() => HPDAIFunctionFactory.Import(original));
        var imported = HPDAIFunctionFactory.Import(original, explicitlyUngoverned: true);
        Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(imported);
        Assert.Equal(original.Name, imported.Name);
        Assert.True(JsonElement.DeepEquals(original.JsonSchema, imported.JsonSchema));
        Assert.Equal(original.UnderlyingMethod, imported.UnderlyingMethod);
        Assert.Equal(original.ReturnJsonSchema?.GetRawText(), imported.ReturnJsonSchema?.GetRawText());
    }

    [Fact]
    public async Task GovernedRuntimeCallWithoutJournalFailsClosed()
    {
        var function = Function();
        var context = Context();
        var middleware = new PermissionMiddleware(new AgentConfig { AgentId = "coding" });
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await middleware.AuthorizeCallAsync(context.AsBeforeFunction(function, "call", new Dictionary<string, object?>(), new()),
                middleware.ResolveFunction(function, null, new()), "call", new Dictionary<string, object?>(), null, new(), CancellationToken.None));
    }

    internal static IReadOnlyDictionary<string, MiddlewareStateFactory> Factories() => new Dictionary<string, MiddlewareStateFactory>
    {
        [PermissionDecisionIndex.StateKey] = new(PermissionDecisionIndex.StateKey, typeof(PermissionDecisionStateData),
            "PermissionDecision", 1, true, StateScope.Session,
            json => JsonSerializer.Deserialize(json, SessionJsonContext.Default.PermissionDecisionStateData),
            value => JsonSerializer.Serialize((PermissionDecisionStateData)value, SessionJsonContext.Default.PermissionDecisionStateData))
    };
    [Fact]
    public void ClientOperationPermissionReplacesDefaultBeforeAdmissionAndGrantVerification()
    {
        var permission = AIFunctionPermissionDeclaration.Required("client.write");
        var tool = new HPD.Agent.ClientTools.ClientToolDefinition
        {
            Name = "client", Description = "Compound client tool", ParametersSchema = Json("{}"),
            OperationContract = new()
            {
                Discriminator = "action",
                Actions = new Dictionary<string, HPD.Agent.ClientTools.ClientToolPolicy>
                {
                    ["write"] = new() { Permission = permission },
                    ["read"] = new()
                }
            }
        };
        var function = HPDAIFunctionFactory.Create((_, _, _) => Task.FromResult<object?>("ok"), new()
        {
            Name = "client", AdditionalProperties = new Dictionary<string, object?> { ["ClientToolDefinition"] = tool }
        });
        Assert.Equal(permission, PermissionBindingResolver.ResolveDeclaration(function, "write"));
        Assert.Null(PermissionBindingResolver.ResolveDeclaration(function, "read"));
    }

    private static AIFunction Function() => HPDAIFunctionFactory.Create((_, _, _) => Task.FromResult<object?>("ok"), new()
    {
        Name = "write", SchemaProvider = () => Json("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"),
        FunctionPermission = AIFunctionPermissionDeclaration.Required("write")
    });
    private static AgentContext Context() => new("test", "test",
        AgentLoopState.Initial([], "run", "test", "test"), new EventCoordinator(), null, null, CancellationToken.None);
}
