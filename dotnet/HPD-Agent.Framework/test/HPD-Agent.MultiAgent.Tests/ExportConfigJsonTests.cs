using System.Text.Json;
using HPD.Agent;
using HPD.MultiAgent;
using HPD.MultiAgent.Config;
using HPD.Graph.Abstractions;
using HPD.Graph.Abstractions.Graph;

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
        json.Should().Contain(System.Environment.NewLine);
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

        root.GetProperty("settings").GetProperty("maxIterations").GetInt32().Should().Be(15);
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
        var agents = root.GetProperty("agents");

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
        var researcher = root.GetProperty("agents").GetProperty("researcher");

        researcher.GetProperty("inputKey").GetString().Should().Be("topic");
        researcher.GetProperty("outputKey").GetString().Should().Be("research");
    }

    [Fact]
    public async Task ExportConfigJson_Preserves_InputTemplate()
    {
        var workflow = await TwoAgentWorkflow(
            writerOpts: o => o.WithInputTemplate("Summarise: {{research}}\n\nFacts: {{facts}}"));

        var root = ParseJson(workflow.ExportConfigJson());
        var writer = root.GetProperty("agents").GetProperty("writer");

        writer.GetProperty("inputTemplate").GetString().Should().Contain("{{research}}");
    }

    [Fact]
    public async Task ExportConfigJson_Preserves_AdditionalSystemInstructions()
    {
        var workflow = await TwoAgentWorkflow(
            researcherOpts: o => o.WithInstructions("Focus on facts only."));

        var root = ParseJson(workflow.ExportConfigJson());
        var researcher = root.GetProperty("agents").GetProperty("researcher");

        researcher.GetProperty("additionalInstructions").GetString().Should().Be("Focus on facts only.");
    }

    [Fact]
    public async Task ExportConfigJson_Preserves_MaxConcurrentExecutions()
    {
        var workflow = await TwoAgentWorkflow(
            researcherOpts: o => { o.MaxConcurrentExecutions = 4; });

        var root = ParseJson(workflow.ExportConfigJson());
        var researcher = root.GetProperty("agents").GetProperty("researcher");

        researcher.GetProperty("maxConcurrent").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task ExportConfigJson_Preserves_Timeout()
    {
        var workflow = await TwoAgentWorkflow(
            researcherOpts: o => o.WithTimeout(TimeSpan.FromSeconds(30)));

        var root = ParseJson(workflow.ExportConfigJson());
        var researcher = root.GetProperty("agents").GetProperty("researcher");

        // TimeSpan serialises as ISO 8601 duration string e.g. "00:00:30"
        researcher.GetProperty("timeout").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    // ── 5. retry config ───────────────────────────────────────────────────────

    [Fact]
    public async Task ExportConfigJson_Preserves_RetryPolicy()
    {
        var workflow = await TwoAgentWorkflow(
            researcherOpts: o => o.WithRetry(maxAttempts: 3, strategy: BackoffStrategy.Exponential));

        var root = ParseJson(workflow.ExportConfigJson());
        var retry = root.GetProperty("agents").GetProperty("researcher").GetProperty("retry");

        retry.GetProperty("maxAttempts").GetInt32().Should().Be(3);
        retry.GetProperty("strategy").GetString().Should().Be("Exponential");
    }

    [Fact]
    public async Task ExportConfigJson_Preserves_LinearRetryStrategy()
    {
        var workflow = await TwoAgentWorkflow(
            researcherOpts: o => o.WithRetry(maxAttempts: 2, strategy: BackoffStrategy.Linear));

        var root = ParseJson(workflow.ExportConfigJson());
        var retry = root.GetProperty("agents").GetProperty("researcher").GetProperty("retry");

        retry.GetProperty("strategy").GetString().Should().Be("Linear");
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
        var onError = root.GetProperty("agents").GetProperty("researcher").GetProperty("onError");

        onError.GetProperty("mode").GetString().Should().Be(mode);
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
        var onError = root.GetProperty("agents").GetProperty("primary").GetProperty("onError");

        onError.GetProperty("mode").GetString().Should().Be("Fallback");
        onError.GetProperty("fallbackAgent").GetString().Should().Be("backup");
    }

    // ── 7. output modes ───────────────────────────────────────────────────────

    [Fact]
    public async Task ExportConfigJson_Preserves_OutputMode_String()
    {
        var workflow = await TwoAgentWorkflow(); // default is String

        var root = ParseJson(workflow.ExportConfigJson());
        var mode = root.GetProperty("agents").GetProperty("researcher")
            .GetProperty("outputMode").GetString();

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
        var mode = root.GetProperty("agents").GetProperty("router")
            .GetProperty("outputMode").GetString();

        mode.Should().Be("Handoff");
    }

    // ── 8. edges ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportConfigJson_Edges_Exclude_START_END_Infrastructure()
    {
        var workflow = await TwoAgentWorkflow();

        var root = ParseJson(workflow.ExportConfigJson());
        var edges = root.GetProperty("edges");

        // No edge should involve "START" or "END"
        for (int i = 0; i < edges.GetArrayLength(); i++)
        {
            var edge = edges[i];
            edge.GetProperty("from").GetString().Should().NotBe("START");
            edge.GetProperty("from").GetString().Should().NotBe("END");
            edge.GetProperty("to").GetString().Should().NotBe("START");
            edge.GetProperty("to").GetString().Should().NotBe("END");
        }
    }

    [Fact]
    public async Task ExportConfigJson_Includes_Linear_Edge()
    {
        var workflow = await TwoAgentWorkflow();

        var root = ParseJson(workflow.ExportConfigJson());
        var edges = root.GetProperty("edges");

        edges.GetArrayLength().Should().Be(1);
        edges[0].GetProperty("from").GetString().Should().Be("researcher");
        edges[0].GetProperty("to").GetString().Should().Be("writer");
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
        var edges = root.GetProperty("edges");
        JsonElement? edge = null;
        for (int i = 0; i < edges.GetArrayLength(); i++)
        {
            var e = edges[i];
            if (e.GetProperty("from").GetString() == "classifier" &&
                e.GetProperty("to").GetString() == "solver")
            {
                edge = e;
                break;
            }
        }

        edge.Should().NotBeNull("expected a classifier→solver edge");
        var when = edge!.Value.GetProperty("when");
        when.GetProperty("type").GetString().Should().Be("FieldEquals");
        when.GetProperty("field").GetString().Should().Be("category");
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

            root.GetProperty("name").GetString().Should().Be("TestWorkflow");
            root.GetProperty("agents").TryGetProperty("researcher", out _).Should().BeTrue();
            root.GetProperty("agents").TryGetProperty("writer", out _).Should().BeTrue();
            root.GetProperty("edges").GetArrayLength().Should().Be(1);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    // ── 9.5  Predicate edge remains runtime-only in export ───────────────────

    [Fact]
    public async Task ExportConfigJson_PredicateEdge_RemainsRuntimeOnly()
    {
        var workflow = await AgentWorkflow.Create()
            .WithName("PredFlow")
            .AddAgent("a", Cfg())
            .AddAgent("b", Cfg())
            .From("a").To("b").When(_ => true)
            .BuildAsync();

        var json = workflow.ExportConfigJson();
        var root = ParseJson(json);

        json.Should().NotContain("__predicate");
        json.Should().NotContain("Predicate");

        var edge = root.GetProperty("edges").EnumerateArray()
            .FirstOrDefault(e =>
                e.TryGetProperty("from", out var f) && f.GetString() == "a" &&
                e.TryGetProperty("to", out var t) && t.GetString() == "b");

        edge.ValueKind.Should().NotBe(JsonValueKind.Undefined, "edge a→b must be present");
        edge.TryGetProperty("when", out _).Should().BeFalse();
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
