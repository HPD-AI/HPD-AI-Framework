namespace HPD.Payments.Tools.Conformance;

/// <summary>Evaluates one exact path/workload resource observation against an explicit budget.</summary>
internal static class ResourceClaimValidator
{
    internal static ResourceClaimResult Validate(ResourceClaimBudget budget, ResourceClaimObservation observation)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(observation);
        var errors = new List<string>();
        if (budget.Iterations < 1 || observation.Iterations != budget.Iterations || observation.StopwatchFrequency < 1)
            errors.Add("invalid-resource-iteration-envelope");
        if (observation.AllocatedBytes < 0 || observation.AllocationCount is null || observation.AllocationCount < 0 ||
            observation.LiveSetDeltaBytes is null || observation.LiveSetDeltaBytes < 0 || observation.ElapsedTicks < 0)
            errors.Add("incomplete-resource-observation");
        if (observation.QueueMaximum is null || observation.CacheMaximum is null || observation.PoolRents is null ||
            observation.PoolReturns is null || observation.PoolClears is null)
            errors.Add("incomplete-bounded-resource-observation");
        if (errors.Count == 0)
        {
            var allocatedPerOperation = (double)observation.AllocatedBytes / observation.Iterations;
            var allocationsPerOperation = (double)observation.AllocationCount!.Value / observation.Iterations;
            var nanosecondsPerOperation = observation.ElapsedTicks * 1_000_000_000d /
                observation.StopwatchFrequency / observation.Iterations;
            if (allocatedPerOperation > budget.MaximumAllocatedBytesPerOperation) errors.Add("allocated-byte-budget-missed");
            if (allocationsPerOperation > budget.MaximumAllocationCountPerOperation) errors.Add("allocation-count-budget-missed");
            if (nanosecondsPerOperation > budget.MaximumNanosecondsPerOperation) errors.Add("latency-budget-missed");
            if (observation.LiveSetDeltaBytes > budget.MaximumLiveSetDeltaBytes) errors.Add("live-set-budget-missed");
            if (observation.QueueMaximum > budget.MaximumQueueDepth) errors.Add("queue-budget-missed");
            if (observation.CacheMaximum > budget.MaximumCacheEntries) errors.Add("cache-budget-missed");
            if (observation.PoolRents != observation.PoolReturns || observation.PoolReturns != observation.PoolClears)
                errors.Add("pool-rent-return-clear-imbalance");
        }
        var distinct = errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new(distinct.Length == 0, distinct);
    }
}

internal sealed record ResourceClaimBudget(string Graph, string Rid, string Path, string Workload, int Iterations,
    double MaximumAllocatedBytesPerOperation, double MaximumAllocationCountPerOperation,
    double MaximumNanosecondsPerOperation, long MaximumLiveSetDeltaBytes, int MaximumQueueDepth, int MaximumCacheEntries);

internal sealed record ResourceClaimObservation(int Iterations, long AllocatedBytes, long? AllocationCount,
    long ElapsedTicks, long StopwatchFrequency, long? LiveSetDeltaBytes, int? QueueMaximum, int? CacheMaximum,
    long? PoolRents, long? PoolReturns, long? PoolClears);

internal sealed record ResourceClaimResult(bool IsWithinBudget, IReadOnlyList<string> Errors);
