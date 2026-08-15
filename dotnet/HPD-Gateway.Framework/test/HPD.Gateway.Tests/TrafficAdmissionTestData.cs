using HPD.Gateway;

namespace HPD.Gateway.Tests;

internal static class TrafficAdmissionTestData
{
    internal static TrafficAdmissionCapability Capability(string name) => new(
        name, 1, TrafficAdmissionScope.ProcessLocal, TrafficAdmissionKind.RequestRate,
        TrafficAdmissionRateAlgorithm.FixedWindow, TrafficAdmissionPartitionKind.Global,
        TrafficAdmissionFailureDisposition.Reject,
        new TrafficAdmissionLimits(1, 100_000_000, TimeSpan.FromSeconds(1), TimeSpan.FromDays(1), 0, 0, 0, 0),
        "hpd.gateway/process-local", new ContentHash("sha-256", new string('a', 64)), null);
}
