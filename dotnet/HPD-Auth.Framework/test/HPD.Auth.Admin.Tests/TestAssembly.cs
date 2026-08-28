using Xunit;

// Each Admin test host owns an isolated, fully initialized SQLite authority.
// Running those authorities concurrently can exhaust host filesystem capacity
// before the behavioral assertions execute, so this integration assembly is
// intentionally serialized.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
