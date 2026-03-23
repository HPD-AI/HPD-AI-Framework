namespace HPD.RAG.Evaluation;

/// <summary>
/// A single test-suite entry that identifies a (scenario, iteration) pair.
/// </summary>
public sealed record TestCase
{
    /// <summary>Name of the evaluation scenario (e.g. "QA-Finance-v1").</summary>
    public required string ScenarioName { get; init; }

    /// <summary>Name of the iteration within the scenario (e.g. "iter-001").</summary>
    public required string IterationName { get; init; }
}

/// <summary>
/// Converts a test-suite enumeration into the <see cref="PartitionKey"/> sequence
/// required by the Microsoft.Extensions.AI.Evaluation reporting infrastructure.
/// </summary>
public static class MragEvalPartitionDefinition
{
    /// <summary>
    /// Projects each <see cref="TestCase"/> in <paramref name="testSuite"/> into a
    /// two-segment <see cref="PartitionKey"/> of <c>[ScenarioName, IterationName]</c>.
    /// </summary>
    /// <remarks>
    /// Duplicates are preserved in the output — deduplication is the caller's
    /// responsibility.  The resulting sequence is lazily evaluated.
    /// </remarks>
    /// <param name="testSuite">The test-suite to convert. Must not be <see langword="null"/>.</param>
    /// <returns>
    /// An enumerable of <see cref="PartitionKey"/> values, one per test case.
    /// Returns an empty enumerable when <paramref name="testSuite"/> is empty.
    /// </returns>
    public static IEnumerable<PartitionKey> FromTestSuite(IEnumerable<TestCase> testSuite)
    {
        ArgumentNullException.ThrowIfNull(testSuite);
        return testSuite.Select(tc => new PartitionKey([tc.ScenarioName, tc.IterationName]));
    }
}
