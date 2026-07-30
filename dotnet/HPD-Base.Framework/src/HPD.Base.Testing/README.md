# HPD.Base.Testing

`HPD.Base.Testing` provides deterministic in-process application tests for
the typed HPD.BASE API.

```csharp
await using BaseTestHost host = await BaseTestHost.CreateAsync(hpd =>
{
    hpd.UseInMemory();
    hpd.AddCollection(Project.Collection);
});

BaseSession session = host.Session(
    BaseTestPrincipal.System("project-tests"));
```

The host includes:

- a controllable `TimeProvider`;
- mutable allow/deny policy decisions that are evaluated per operation;
- one-shot atomic-commit and post-commit-observer failures;
- captured committed mutations and dependency invalidations;
- bounded SQLite durable-journal inspection;
- the normal typed files, realtime, and live-query session surfaces.

Use `host.Faults`, `host.Policy`, `host.Probe`, and `host.Time` to arrange and
assert behavior without constructing canonical runtime payloads or operation
contexts.
