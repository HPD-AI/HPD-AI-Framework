using Xunit;

// Each integration test host owns an isolated, fully initialized SQLite authority.
// Serial execution keeps bounded provider deadlines from measuring unrelated test-host
// filesystem contention instead of the behavior under test.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
