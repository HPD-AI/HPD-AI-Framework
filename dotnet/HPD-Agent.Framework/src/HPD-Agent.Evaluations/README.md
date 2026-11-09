# HPD-Agent.Evaluations

Evaluate HPD Agent turns and responses with deterministic assertions, datasets,
LLM judges, safety checks, red-team cases, human annotation, and saved score
history.

The package targets `net8.0`, `net9.0`, and `net10.0`.

## Install

```bash
dotnet add package HPD-Agent.Evaluations
```

## Evaluate One Turn

```csharp
using HPD.Agent.Evaluations;
using HPD.Agent.Evaluations.Integration;

var report = await agent.CheckAsync(
    "Return a JSON object with a title and summary.",
    Eval.Contains("\"title\""),
    Eval.Contains("\"summary\""));

report.Print();
report.ThrowIfPolicyViolations();
```

Deterministic evaluators default to `EvalPolicy.MustAlwaysPass`. Call
`ThrowIfPolicyViolations()` when the report is a CI or release gate.

## Run a Batch

```csharp
using HPD.Agent.Evaluations.Batch;

var report = await agent.EvaluateAsync(
    [
        ("Summarize the incident.", "Short incident summary"),
        ("List the mitigating controls.", "Controls list"),
    ],
    [new HPD.Agent.Evaluations.Evaluators.LlmJudge.AnswerSimilarityEvaluator()],
    experimentName: "nightly-support");

report.Print();
await report.WriteHtmlAsync("artifacts/evaluations.html");
```

Use `RunEvals.ExecuteAsync` when cases need their own metadata, evaluators,
ground truth, version, or validity window.

```csharp
var dataset = new Dataset<string>
{
    DatasetId = "support-bench",
    Version = "2026.11",
    Cases =
    [
        new EvalCase<string>
        {
            CaseId = "refund-policy",
            Input = "Can I refund an annual plan?",
            GroundTruth = "Follow the annual-plan refund policy.",
            Evaluators = [Eval.Contains("annual")],
        },
    ],
};

var report = await RunEvals.ExecuteAsync(
    agent,
    dataset,
    [new HPD.Agent.Evaluations.Evaluators.LlmJudge.AnswerSimilarityEvaluator()],
    new RunEvalsOptions<string>
    {
        Concurrency = 2,
        EvaluatorConcurrency = 2,
        Repeat = 1,
    },
    experimentName: "refund-regression");
```

## Structured Dataset Inputs

The agent receives one user message. A non-string dataset input therefore needs
an explicit formatter. HPD does not silently send `ToString()` output.

```csharp
var dataset = new Dataset<SearchRequest>
{
    Cases =
    [
        new EvalCase<SearchRequest>
        {
            Input = new SearchRequest("refund policy", 5),
        },
    ],
};

var report = await RunEvals.ExecuteAsync(
    agent,
    dataset,
    [Eval.Contains("refund")],
    new RunEvalsOptions<SearchRequest>
    {
        InputFormatter = input =>
            $"Search for '{input.Query}' and return {input.MaxResults} results.",
    });
```

When reading YAML or writing JSON/YAML in a Native AOT application, also supply
the parse/serialize delegates used by `Dataset<T>.FromFile` and `ToFile`.

## Live Evaluation

Live evaluation runs after a completed message turn without delaying the user
response. Set `samplingRate` to control judge cost.

```csharp
using HPD.Agent.Evaluations.Integration;
using HPD.Agent.Evaluations.Storage;

var scores = new InMemoryScoreStore();

await using var agent = await new AgentBuilder(config)
    .AddEvaluator(Eval.Contains("refund"))
    .AddEvaluator(
        new HPD.Agent.Evaluations.Evaluators.LlmJudge.TaskSuccessEvaluator(),
        samplingRate: 0.1,
        policy: EvalPolicy.TrackTrend)
    .UseEvaluationJudgeConfig(new EvaluationJudgeRunConfig
    {
        Timeout = TimeSpan.FromSeconds(45),
    })
    .UseScoreStore(scores)
    .BuildAsync();
```

`AddEvaluator` defaults deterministic evaluators to `MustAlwaysPass` and
model-judged or custom evaluators to `TrackTrend`. A per-evaluator judge config
takes precedence over `UseEvaluationJudgeConfig`.

Without `UseScoreStore`, scores are still published as `EvalScoreEvent`.
Subscribe to `EvalScoreEvent`, `EvalFailedEvent`, `EvalPersistenceFailedEvent`,
and `EvalPolicyViolationEvent` for live monitoring.

## Storage

`InMemoryScoreStore` and `InMemoryDatasetStore` are for development and tests.
They lose all data when the process exits.

For durable production storage, use `HPD-Agent.Evaluations.HpdBase` on
`net10.0`, or implement `IScoreStore`, `IDatasetStore`, or both. The analytics
endpoints use these interfaces and return `503` when the required store is not
registered.

## Evaluators

`Eval` exposes common deterministic assertions:

```csharp
Eval.Contains("text");
Eval.ContainsAny("one", "two");
Eval.ContainsAll("one", "two");
Eval.ContainsIgnoringCase("text");
Eval.OutputEquals("exact output");
Eval.MatchesRegex("^ok$");
Eval.StartsWith("prefix");
Eval.WordCount(min: 10, max: 200);
Eval.ToolCalled("search");
Eval.ToolCallCount("search", 1);
Eval.ToolArgumentMatches("search", "query", "refund");
Eval.ToolResultContains("search", "policy");
Eval.NoToolsCalled();
Eval.ToolCallOrder("authenticate", "search");
Eval.ToolCallF1("search", "summarize");
Eval.JsonValidity();
Eval.SchemaConformance("""{"type":"object"}""");
Eval.MaxDuration(30);
Eval.HasMatchingSpan(new SpanQuery { NameEquals = "search" });
Eval.Bleu("reference response");
```

Deterministic and NLP implementations are factory-backed and intentionally
internal. Model-judged, safety, and composite evaluators remain public under
`Evaluators.LlmJudge`, `Evaluators.Safety`, and `Evaluators.Composite` because
their constructors carry policy and rubric configuration. Implement
`IEvaluator` directly or derive from `HpdEvaluatorBase` for custom behavior.

`EvaluatorCatalog.All` provides stable IDs, categories, metric names, default
policy, judge requirements, and required evaluation contexts for every built-in
evaluator.

## Red Team

```csharp
using HPD.Agent.Evaluations.RedTeam;

var options = new RedTeamRunOptions
{
    Plugins =
    [
        RedTeamPlugins.Create("prompt-injection"),
        RedTeamPlugins.Create("secret-leak"),
    ],
    Strategies =
    [
        RedTeamStrategies.Create("basic"),
        RedTeamStrategies.BestOfN(variantCount: 4),
    ],
    GlobalEvaluators =
    [
        new HPD.Agent.Evaluations.Evaluators.Safety.PromptInjectionEvaluator(),
    ],
};

var report = await RedTeamRunner.ExecuteAsync(agent, options);
```

Built-in plugin and strategy implementations are internal. Use
`RedTeamPlugins.Catalog`, `RedTeamStrategies.Catalog`, the factories, JSON/YAML
configuration, or implement `IRedTeamPlugin` and `IRedTeamStrategy` directly.

## Datasets

Dataset files support:

```yaml
dataset_id: support-bench
version: "2026.11"
cases:
  - case_id: refund-policy
    name: annual refund
    input: Can I refund an annual plan?
    ground_truth: Follow the annual-plan refund policy.
    metadata:
      difficulty: easy
    evaluators:
      - OutputContains: annual
```

Evaluator definitions are intentionally limited to names that can be represented
without arbitrary reflection. Use code-based evaluators when a dataset needs a
custom evaluator.

## CI Suggestions

```csharp
var report = await RunEvals.ExecuteAsync(
    agent,
    dataset,
    evaluators,
    new RunEvalsOptions<string> { Concurrency = 4 },
    experimentName: "pull-request");

report.Print();
report.ThrowIfPolicyViolations();

if (report.Failures.Any(f => f.Kind == FailureKind.TaskFailure))
    throw new InvalidOperationException(
        $"Evaluation task failures: {report.Failures.Count}");
```

Inspect `InfrastructureErrorRate` before comparing scores. A high rate indicates
provider throttling or transient infrastructure noise rather than model quality.

## Related Package

Use `HPD-Agent.AspNetCore.Evaluations` to expose score, analytics, run, dataset,
and red-team data over HTTP.
