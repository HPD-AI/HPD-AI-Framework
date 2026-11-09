# HPD-Agent.Stores.HpdBase

Durable HPD Base-backed implementations of HPD Agent storage contracts.

This package targets `net10.0`, matching the current HPD Base target.

## Install

```bash
dotnet add package HPD-Agent.Stores.HpdBase
dotnet add package HPD-Base.Sqlite
```

## Register stores

```csharp
using HPD.Agent.Serialization;
using HPD.Agent.Stores.HpdBase;

services.AddHPDBase(hpd => hpd
    .AddHpdAgentStores()
    .AddPolicyAuthority(...)
    .UseStore(SqliteStore.Configure(options =>
        options.DataSource = "data/hpd-agent.db")));

services.AddHpdBaseAgentStore();
services.AddHpdBaseSessionStore(eventCodec);
```
