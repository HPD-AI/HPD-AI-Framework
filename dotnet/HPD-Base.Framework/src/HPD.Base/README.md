# HPD.Base

HPD.Base is a typed, policy-aware application data runtime for .NET 10 and
later. It combines generated collection contracts, one canonical mutation
engine, relational reads and includes, schema lifecycle management, durable
realtime delivery, dependency invalidation, and server-side live queries.

```csharp
services.AddHPDBase(hpd => hpd
    .AddCollection(Projects.Collection));

BaseSession session = sessions.For(principal);
BaseResult<BaseRecord<Project>> project = await session
    .Collection(Projects.Collection)
    .GetAsync(projectId);
```

The volatile provider is installed by default for local and in-process use.
Install `HPD.Base.Sqlite` when the application requires persistent storage,
transactional journaling, durable replay, or physical schema execution.

HPD.Base is Native AOT-oriented. Collection contracts, registered reads, JSON
metadata, and typed handles are generated at build time without reflection
fallback.
