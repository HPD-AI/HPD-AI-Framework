using System.Text.Json;
using HPD.Agent;
using HPD.MultiAgent;
using HPD.MultiAgent.Config;
using HPDAgent.Graph.Abstractions;
using HPDAgent.Graph.Abstractions.Graph;

namespace HPD.MultiAgent.Tests;

/// <summary>
/// Tests for AgentWorkflowInstance.ExportConfigJson().
/// All tests use AgentConfig-based agents so the config is recoverable.
/// </summary>
public class ExportConfigJsonTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static AgentConfig Cfg(string name = "Agent", string instructions = "Do work.")
        => new() { Name = name, SystemInstructions = instructions };

    private static async Task<AgentWorkflowInstance> TwoAgentWorkflow(
        Action<AgentNodeOptions>? researcherOpts = null,
        Action<AgentNodeOptions>? writerOpts = null,
        string workflowName = "TestWorkflow")
    {
        return await AgentWorkflow.Create()
            .WithName(workflowName)
            .AddAgent("researcher", Cfg("Researcher", "Research thoroughly."), researcherOpts)
            .AddAgent("writer", Cfg("Writer", "Write clearly."), writerOpts)
            .From("researcher").To("writer")
            .BuildAsync();
    }

    private static JsonElement ParseJson(string json) =>
        JsonDocument.Parse(json).RootElement;

    // ── 1. basic validity ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExportConfigJson_Returns_Valid_Json()
    {
        var workflow = await TwoAgentWorkflow();

        var json = workflow.ExportConfigJson();

        json.Should().NotBeNullOrWhiteSpace();
        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task ExportConfigJson_Output_Is_Indented()
    {
        var workflow = await TwoAgentWorkflow();

        var json = workflow.ExportConfigJson();

        // Indented JSON always contains newlines
        json.Should().Contain(Environment.NewLine);
    }

    // ── 2. workflow-level fields ───────────────────────────────────────────────

    [Fact]
    public async Task ExportConfigJson_Includes_WorkflowName()
    {
        var workflow = await AgentWorkflow.Create()
            .WithName("MyPipeline")
            .AddAgent("only", Cfg())
            .BuildAsync();

        var json = workflow.ExportConfigJson();

        json.Should().Contain("MyPipeline");
    }

    [Fact]
    public async Task ExportConfigJson_Preserves_MaxIterations_From_Graph()
    {
        var workflow = await AgentWorkflow.Create()
            .WithName("CyclicWorkflow")
            .WithMaxIterations(15)
            .AddAgent("a", Cfg())
            .BuildAsync();

        var root = ParseJson(workflow.ExportConfigJson());

        root.GetProperty("Settings").GetProperty("MaxIterations").GetInt32().Should().Be(15);
    }

    // ── 3. agent config round-trip ────────────────────────────────────────────

    [Fact]
    public async Task ExportConfigJson_Includes_All_AgentIds()
    {
        var workflow = await AgentWorkflow.Create()
            .WithName("Three")
            .AddAgent("a", Cfg())
            .AddAgent("b", Cfg())
            .AddAgent("c", Cfg())
            .From("a").To("b")
            .From("b").To("c")
            .BuildAsync();

        var root = ParseJson(workflow.ExportConfigJson());
        var agents = root.GetProperty("Agents");

        agents.TryGetProperty("a", out _).Should().BeTrue();
        agents.TryGetProperty("b", out _).Should().BeTrue();
        agents.TryGetProperty("c", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ExportConfigJson_Preserves_SystemInstructions()
    {
        var workflow = await AgentWorkflow.Create()
            .WithName("W")
            .AddAgent("agent", Cfg("A", "Research peer-reviewed sources only."))
            .BuildAsync();

        var json = workflow.ExportConfigJson();

        json.Should().Contain("Research peer-reviewed sources only.");
    }

    // ── 4. node options ───────────────────────────────────────────────────────

    [Fact]
    public async Task ExportConfigJson_Preserves_InputOutputKeys()
    {
        var workflow = await TwoAgentWorkflow(
            researcherOpts: o => o.WithInputKey("topic").WithOutputKey("research"));

        var root = ParseJson(workflow.ExportConfigJson());
        var researcher = root.GetProperty("Agents").GetProperty("researcher");

        researcher.GetProperty("InputKey").GetString().Should().Be("topic");
        researcher.GetProperty("OutputKey").GetString().Should().Be("research");
    }

    [Fact]
    public async Task ExportConfigJson_Preserves_InputTemplate()
    {
        var workflow = await TwoAgentWorkflow(
            writerOpts: o => o.WithInputTemplate("Summarise: {{research}}\n\nFacts: {{facts}}"));

        var root = ParseJson(workflow.ExportConfigJson());
        var writer = root.GetProperty("Agents").GetProperty("writer");

        writer.GetProperty("InputTemplate").GetString().Should().Contain("{{research}}");
    }

    [Fact]
    public async Task ExportConfigJson_Preserves_AdditionalSystemInstructions()
    {
        var workflow = await TwoAgentWorkflow(
            researcherOpts: o => o.WithInstructions("Focus on facts only."));

        var root = ParseJson(workflow.ExportConfigJson());
        var researcher = root.GetProperty("Agents").GetProperty("researcher");

        researcher.GetProperty("AdditionalInstructions").GetString().Should().Be("Focus on facts only.");
    }

    [Fact]
    public async Task ExportConfigJson_Preserves_MaxConcurrentExecutions()
    {
        var workflow = await TwoAgentWorkflow(
            researcherOpts: o => { o.MaxConcurrentExecutions = 4; });

        var root = ParseJson(workflow.ExportConfigJson());
        var researcher = root.GetProperty("Agents").GetProperty("researcher");

        researcher.GetProperty("MaxConcurrent").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task ExportConfigJson_Preserves_Timeout()
    {
        var workflow = await TwoAgentWorkflow(
            researcherOpts: o => o.WithTimeout(TimeSpan.FromSeconds(30)));

        var root = ParseJson(workflow.ExportConfigJson());
        var researcher = root.GetProperty("Agents").GetProperty("researcher");

        // TimeSpan serialises as ISO 8601 duration string e.g. "00:00:30"
        researcher.GetProperty("Timeout").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    // ── 5. retry config ───────────────────────────────────────────────────────

    [Fact]
    public async Task ExportConfigJson_Preserves_RetryPolicy()
    {
        var workflow = await TwoAgentWorkflow(
            researcherOpts: o => o.WithRetry(maxAttempts: 3, strategy: BackoffStrategy.Exponential));

        var root = ParseJson(workflow.ExportConfigJson());
        var retry = root.GetProperty("Agents").GetProperty("researcher").GetProperty("Retry");

        retry.GetProperty("MaxAttempts").GetInt32().Should().Be(3);
        retry.GetProperty("Strategy").GetString().Should().Be("Exponential");
    }

    [Fact]
    public async Task ExportConfigJson_Preserves_LinearRetryStrategy()
    {
        var workflow = await TwoAgentWorkflow(
            researcherOpts: o => o.WithRetry(maxAttempts: 2, strategy: BackoffStrategy.Linear));

        var root = ParseJson(workflow.ExportConfigJson());
        var retry = root.GetProperty("Agents").GetProperty("researcher").GetProperty("Retry");

        retry.GetProperty("Strategy").GetString().Should().Be("Linear");
    }

    // ── 6. error mode ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Skip")]
    [InlineData("Isolate")]
    public async Task ExportConfigJson_Preserves_ErrorMode(string mode)
    {
        Action<AgentNodeOptions> configure = mode switch
        {
            "Skip" => o => o.OnErrorSkip(),
            "Isolate" => o => o.OnErrorIsolate(),
            _ => throw new InvalidOperationException()
        };

        var workflow = await TwoAgentWorkflow(researcherOpts: configure);

        var root = ParseJson(workflow.ExportConfigJson());
        var onError = root.GetProperty("Agents").GetProperty("researcher").GetProperty("OnError");

        onError.GetProperty("Mode").GetString().Should().Be(mode);
    }

    [Fact]
    public async Task ExportConfigJson_Preserves_ErrorMode_Fallback_With_Agent()
    {
        var workflow = await AgentWorkflow.Create()
            .WithName("W")
            .AddAgent("primary", Cfg(), o => o.OnErrorFallback("backup"))
            .AddAgent("backup", Cfg())
            .BuildAsync();

        var root = ParseJson(workflow.ExportConfigJson());
        var onError = root.GetProperty("Agents").GetProperty("primary").GetProperty("OnError");

        onError.GetProperty("Mode").GetString().Should().Be("Fallback");
        onError.GetProperty("FallbackAgent").GetString().Should().Be("backup");
    }

    // ── 7. output modes ───────────────────────────────────────────────────────

    [Fact]
    public async Task ExportConfigJson_Preserves_OutputMode_String()
    {
        var workflow = await TwoAgentWorkflow(); // default is String

        var root = ParseJson(workflow.ExportConfigJson());
        var mode = root.GetProperty("Agents").GetProperty("researcher")
            .GetProperty("OutputMode").GetString();

        mode.Should().Be("String");
    }

    [Fact]
    public async Task ExportConfigJson_Preserves_OutputMode_Handoff()
    {
        var workflow = await AgentWorkflow.Create()
            .WithName("W")
            .AddAgent("router", Cfg(), o => o
                .WithHandoff("a", "Route to A")
                .WithHandoff("b", "Route to B"))
            .AddAgent("a", Cfg())
            .AddAgent("b", Cfg())
            .BuildAsync();

        var root = ParseJson(workflow.ExportConfigJson());
        var mode = root.GetProperty("Agents").GetProperty("router")
            .GetProperty("OutputMode").GetString();

        mode.Should().Be("Handoff");
    }

    // ── 8. edges ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportConfigJson_Edges_Exclude_START_END_Infrastructure()
    {
        var workflow = await TwoAgentWorkflow();

        var root = ParseJson(workflow.ExportConfigJson());
        var edges = root.GetProperty("Edges");

        // No edge should involve "START" or "END"
        for (int i = 0; i < edges.GetArrayLength(); i++)
        {
            var edge = edges[i];
            edge.GetProperty("From").GetString().Should().NotBe("START");
            edge.GetProperty("From").GetString().Should().NotBe("END");
            edge.GetProperty("To").GetString().Should().NotBe("START");
            edge.GetProperty("To").GetString().Should().NotBe("END");
        }
    }

    [Fact]
    public async Task ExportConfigJson_Includes_Linear_Edge()
    {
        var workflow = await TwoAgentWorkflow();

        var root = ParseJson(workflow.ExportConfigJson());
        var edges = root.GetProperty("Edges");

        edges.GetArrayLength().Should().Be(1);
        edges[0].GetProperty("From").GetString().Should().Be("researcher");
        edges[0].GetProperty("To").GetString().Should().Be("writer");
    }

    [Fact]
    public async Task ExportConfigJson_Preserves_Conditional_Edge_FieldEquals()
    {
        var workflow = await AgentWorkflow.Create()
            .WithName("W")
            .AddAgent("classifier", Cfg())
            .AddAgent("solver", Cfg())
            .From("classifier").To("solver").WhenEquals("category", "math")
            .BuildAsync();

        var root = ParseJson(workflow.ExportConfigJson());

        // Find the classifier→solver edge
        var edges = root.GetProperty("Edges");
        JsonElement? edge = null;
        for (int i = 0; i < edges.GetArrayLength(); i++)
        {
            var e = edges[i];
            if (e.GetProperty("From").GetString() == "classifier" &&
                e.GetProperty("To").GetString() == "solver")
            {
                edge = e;
                break;
            }
        }

        edge.Should().NotBeNull("expected a classifier→solver edge");
        var when = edge!.Value.GetProperty("When");
        when.GetProperty("Type").GetString().Should().Be("FieldEquals");
        when.GetProperty("Field").GetString().Should().Be("category");
    }

    // ── 9. null-value omission ────────────────────────────────────────────────

    [Fact]
    public async Task ExportConfigJson_NullValues_Omitted()
    {
        // Minimal node — no retry, no error override, no inputKey, no outputKey
        var workflow = await AgentWorkflow.Create()
            .WithName("W")
            .AddAgent("only", Cfg())
            .BuildAsync();

        var json = workflow.ExportConfigJson();

        // Null-value fields should not appear in output at all
        json.Should().NotContain("\"InputKey\": null");
        json.Should().NotContain("\"OutputKey\": null");
        json.Should().NotContain("\"Retry\": null");
    }

    // ── 10. round-trip ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportConfigJson_Roundtrip_Produces_Valid_Json_File()
    {
        // Verifies that ExportConfigJson produces a file that is valid JSON and
        // contains the expected workflow structure. A full FromJson() round-trip
        // requires AgentConfig's enum fields to share the same JsonSerializerOptions
        // (JsonStringEnumConverter) — that alignment is a separate concern tracked
        // in the AgentConfig serialisation layer.
        var workflow = await TwoAgentWorkflow();
        var exportedJson = workflow.ExportConfigJson();

        var tmp = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
        try
        {
            await File.WriteAllTextAsync(tmp, exportedJson);

            File.Exists(tmp).Should().BeTrue();
            var reRead = await File.ReadAllTextAsync(tmp);
            var root = ParseJson(reRead);

            root.GetProperty("Name").GetString().Should().Be("TestWorkflow");
            root.GetProperty("Agents").TryGetProperty("researcher", out _).Should().BeTrue();
            root.GetProperty("Agents").TryGetProperty("writer", out _).Should().BeTrue();
            root.GetProperty("Edges").GetArrayLength().Should().Be(1);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    // ── 9.5  Predicate edge serialises as FieldEquals on synthetic key ────────

    [Fact]
    public async Task ExportConfigJson_PredicateEdge_SerializesAsSyntheticFieldEquals()
    {
        var workflow = await AgentWorkflow.Create()
            .WithName("PredFlow")
            .AddAgent("a", Cfg())
            .AddAgent("b", Cfg())
            .From("a").To("b").When(_ => true)
            .BuildAsync();

        var json = workflow.ExportConfigJson();
        var root = ParseJson(json);

        // The edges array must contain the synthetic FieldEquals condition
        json.Should().Contain("__predicate_a_b",
            "predicate edge must be persisted as FieldEquals on the synthetic key");

        var edge = root.GetProperty("Edges").EnumerateArray()
            .FirstOrDefault(e =>
                e.TryGetProperty("From", out var f) && f.GetString() == "a" &&
                e.TryGetProperty("To", out var t) && t.GetString() == "b");

        edge.ValueKind.Should().NotBe(JsonValueKind.Undefined, "edge a→b must be present");
        edge.TryGetProperty("When", out var when).Should().BeTrue();
        when.GetProperty("Field").GetString().Should().Be("__predicate_a_b");
    }

    // ── 9.6  Default settings → no checkpoint store required ─────────────────

    [Fact]
    public async Task AgentWorkflowInstance_DefaultSettings_DoesNotRequireCheckpointStore()
    {
        // Build and call ExportConfigJson — neither must throw about missing DI store
        var workflow = await AgentWorkflow.Create()
            .WithName("DefaultW")
            .AddAgent("a", Cfg())
            .BuildAsync();

        var act = () => workflow.ExportConfigJson();
        act.Should().NotThrow("default EnableCheckpointing=false must never resolve IGraphCheckpointStore");
    }

    // ── Phase 4 — New condition type round-trip tests ─────────────────────────

    [Fact]
    public async Task ExportConfigJson_Preserves_AndCondition_RoundTrip()
    {
        var workflow = await AgentWorkflow.Create()
            .WithName("AndFlow")
            .AddAgent("triage", Cfg())
            .AddAgent("vipbilling", Cfg())
            .From("triage").To("vipbilling")
                .When(HPD.MultiAgent.Routing.Condition.And(
                    HPD.MultiAgent.Routing.Condition.Equals("intent", "billing"),
                    HPD.MultiAgent.Routing.Condition.Equals("tier", "VIP")
                ))
            .BuildAsync();

        var json = workflow.ExportConfigJson();

        var options = new JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            PropertyNameCaseInsensitive = true
        };
        var config = JsonSerializer.Deserialize<MultiAgentWorkflowConfig>(json, options);

        config.Should().NotBeNull();
        var edge = config!.Edges.FirstOrDefault(e => e.From == "triage" && e.To == "vipbilling");
        edge.Should().NotBeNull();
        edge!.When!.Type.Should().Be(ConditionType.And);
        edge.When.Conditions.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExportConfigJson_Preserves_RegexOptions_RoundTrip()
    {
        var workflow = await AgentWorkflow.Create()
            .WithName("RegexFlow")
            .AddAgent("classifier", Cfg())
            .AddAgent("affirm", Cfg())
            .From("classifier").To("affirm")
                .WhenMatchesRegex("response", @"^yes$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .BuildAsync();

        var json = workflow.ExportConfigJson();

        var options = new JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            PropertyNameCaseInsensitive = true
        };
        var config = JsonSerializer.Deserialize<MultiAgentWorkflowConfig>(json, options);

        var edge = config!.Edges.FirstOrDefault(e => e.From == "classifier" && e.To == "affirm");
        edge!.When!.Type.Should().Be(ConditionType.FieldMatchesRegex);
        edge.When.RegexOptions.Should().Be("IgnoreCase");
    }

    [Fact]
    public async Task ExportConfigJson_Preserves_ContainsAny_ArrayValue_RoundTrip()
    {
        var workflow = await AgentWorkflow.Create()
            .WithName("ContainsAnyFlow")
            .AddAgent("classifier", Cfg())
            .AddAgent("escalate", Cfg())
            .From("classifier").To("escalate")
                .WhenContainsAny("tags", "urgent", "escalate")
            .BuildAsync();

        var json = workflow.ExportConfigJson();
        json.Should().Contain("FieldContainsAny");

        var options = new JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            PropertyNameCaseInsensitive = true
        };
        var config = JsonSerializer.Deserialize<MultiAgentWorkflowConfig>(json, options);

        var edge = config!.Edges.FirstOrDefault(e => e.From == "classifier" && e.To == "escalate");
        edge!.When!.Type.Should().Be(ConditionType.FieldContainsAny);
        edge.When.Field.Should().Be("tags");
    }
}
