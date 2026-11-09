# HPD-Agent.Evaluations.HpdBase

Durable HPD Base-backed score, run, dataset, history, and analytics storage for
HPD Agent evaluations.

This package targets `net10.0`, matching the current HPD Base target.

## Register

```csharp
using HPD.Agent.Evaluations.HpdBase;

services.AddHPDBase(hpd => hpd
    .AddHpdEvaluationStores()
    .UseStore(SqliteStore.Configure(options =>
        options.DataSource = "data/hpd-agent.db")));

services.AddHpdBaseEvaluationStore();
services.AddHpdBaseDatasetStore(options =>
    options.AddCodec(new DatasetCodec<MyInput>(
        MyJsonContext.Default.MyInput)));
```

The adapter stores the complete `ScoreRecord` and `EvaluationRunRecord` payload
plus indexed provenance used by direct queries. The initial analytics
implementation loads a bounded score snapshot and reuses the validated
in-memory analytics behavior; relational query optimization can be added
without changing the public storage contract.

Dataset versions require an input codec because the store must reconstruct the
generic `TInput` without reflection. Register the codec at service
configuration time, including after a process restart. If a dataset contains
evaluator or report-evaluator instances, the same codec must also supply their
serializer/parser pair; registration fails rather than silently dropping them.
Datasets without evaluator instances need only the source-generated input JSON
metadata.

Datasets preserve immutable version payloads, current-version metadata, case
validity windows, historical case revisions, active-case queries, and version
diffs. Initial history queries rebuild the validated in-memory dataset
behavior from bounded HPD Base pages; the public storage contract leaves room
for provider-side history optimization later.

## Content storage

This package does not advertise a generic HPD Base `IContentStore`. The HPD Base
release in this tree provides only an in-memory file provider, so a wrapper
would report restart durability without supplying it. Continue using
`LocalFileContentStore` when durable files are required, or add a durable HPD
Base `IFileStorageProvider` first.

Hosts remain responsible for HPD Base policy authority and schema planning.
