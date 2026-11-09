# HPD-Agent.AspNetCore.Evaluations

Optional ASP.NET Core endpoints for querying HPD evaluation scores, analytics,
runs, datasets, and red-team findings.

The package targets `net8.0`, `net9.0`, and `net10.0`.

## Install

```bash
dotnet add package HPD-Agent.AspNetCore.Evaluations
```

## Register

```csharp
using HPD.Agent.AspNetCore.Evaluations;
using HPD.Agent.Evaluations.Storage;

builder.Services.AddSingleton<IScoreStore, MyDurableScoreStore>();
builder.Services.AddSingleton<IDatasetStore, MyDurableDatasetStore>();
builder.Services.AddHPDAgentEvaluations();

var app = builder.Build();
app.MapHPDAgentEvaluationApi();
app.Run();
```

The default route prefix is `/evals`. Pass a different prefix when needed:

```csharp
app.MapHPDAgentEvaluationApi("/internal/evaluations");
```

The package does not define authentication or authorization. Apply the host
application's normal API policy to the evaluation route group.

## Store Requirements

The catalog and red-team metadata routes work without persistence:

- `GET /evals/evaluators/catalog`
- `GET /evals/red-team/plugins`
- `GET /evals/red-team/strategies`

Score, analytics, run, and red-team result routes return `503` when no
`IScoreStore` is registered. Dataset registry routes return `503` when no
`IDatasetStore` is registered.

`InMemoryScoreStore` and `InMemoryDatasetStore` are available for development,
but both lose data when the process exits.

## Route Groups

`MapHPDAgentEvaluationApi` registers these route families:

| Area | Routes |
| --- | --- |
| Scores | `/scores`, `/scores/by-thread`, `/scores/by-version` |
| Analytics | `/analytics/evaluators`, `/analytics/trend/{evaluatorName}`, `/analytics/pass-rate/{evaluatorName}`, `/analytics/failure-rate/{evaluatorName}` |
| Comparisons | `/analytics/agent-comparison/{evaluatorName}`, `/analytics/thread-comparison` |
| Runtime quality | `/analytics/tool-usage`, `/analytics/risk-autonomy`, `/analytics/cost`, `/analytics/safety` |
| Runs | `/runs`, `/runs/executions`, `/runs/scenarios`, `/runs/iterations` |
| Datasets | `/datasets`, `/datasets/{datasetId}/versions`, `/datasets/{datasetId}/active-cases`, `/datasets/{datasetId}/cases/{caseId}/history`, `/datasets/{datasetId}/diff` |
| Red team | `/red-team/plugins`, `/red-team/strategies`, `/analytics/red-team`, `/analytics/red-team/by-plugin`, `/analytics/red-team/by-strategy`, `/analytics/red-team/findings` |

`GET /evals/evaluators/catalog` is backed by the core evaluator registry and
includes stable IDs, default policy, judge requirements, metric names, and
required evaluation contexts.

Unexpected storage failures return `500` without exposing raw exception text.
Missing store registrations continue to return `503`. Cancellation is propagated
instead of being converted into a client error.

## HTTP Dataset Inputs

The HTTP registry intentionally accepts string-input datasets only. This keeps
the endpoint AOT-compatible and avoids reflection over arbitrary consumer input
types. Use the `HPD-Agent.Evaluations` APIs for typed, in-process datasets.

## Compatibility

Starting with `1.0.0`, documented endpoint paths, DTO names, and response
contracts follow semantic versioning. Breaking changes require a new major
version and a migration guide.
