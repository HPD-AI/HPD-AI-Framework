using System.Text.Json;
using HPD.Agent.Audio.VoiceActivity;

namespace HPD.Agent.Audio.V2.Tests.VoiceActivity;

public sealed class VoiceActivityPublicContractsV1Tests
{
    [Fact]
    public void Request_DeeplyOwnsSourcesAndRejectsDuplicatesAndUnboundedProfiles()
    {
        var sources = new List<ActivitySourceRequestV1> { Source("local", ActivitySourceKindV1.LocalDetector) };
        var request = Request(sources);
        sources.Clear();
        Assert.Single(request.Sources);
        Assert.Throws<ArgumentException>(() => Request([Source("same", ActivitySourceKindV1.LocalDetector),
            Source("same", ActivitySourceKindV1.ProviderNative)]));
        Assert.Throws<ArgumentException>(() => new VoiceActivityRequestV1(VoiceActivityProfileV1.Fused,
            ActivityResponsivenessV1.Balanced, VoiceActivityNoiseEnvironmentV1.Variable,
            VoiceActivitySpeechContinuityV1.Natural, null, [], ActivityDegradationPolicyV1.Strict, Limits()));
    }

    [Fact]
    public void OperationalLimits_AreFiniteAndBoundSourceCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoiceActivityOperationalLimitsV1(0, 1, 0,
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoiceActivityOperationalLimitsV1(1, 1, 2,
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10)));
        Assert.Throws<ArgumentException>(() => new VoiceActivityRequestV1(VoiceActivityProfileV1.HpdManaged,
            ActivityResponsivenessV1.Balanced, VoiceActivityNoiseEnvironmentV1.Variable,
            VoiceActivitySpeechContinuityV1.Natural, null,
            [Source("one", ActivitySourceKindV1.LocalDetector), Source("two", ActivitySourceKindV1.LocalDetector)],
            ActivityDegradationPolicyV1.Strict,
            new VoiceActivityOperationalLimitsV1(1, 16, 4, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1))));
    }

    [Fact]
    public void Snapshot_DeeplyOwnsEffectiveTruthAndDoesNotExposeRuntimeObjects()
    {
        var sources = new List<EffectiveActivitySourceV1>
        {
            new("provider", ActivitySourceKindV1.ProviderNative, ActivitySourceRoleV1.Advisory, false,
                ProviderActivityVisibilityV1.NotObservable),
        };
        var differences = new List<string> { "provider-activity-not-observable" };
        var snapshot = new VoiceActivitySnapshotV1(1, 2, VoiceActivityProfileV1.ProviderManaged,
            sources, differences, VoiceActivityHealthStateV1.Unobservable);
        sources.Clear(); differences.Clear();
        Assert.Single(snapshot.Sources);
        Assert.Single(snapshot.RequestedEffectiveDifferences);
        var forbidden = new[] { "Provider", "Stream", "Task", "Lease", "CancellationToken", "MemoryOwner" };
        Assert.DoesNotContain(snapshot.GetType().GetProperties(), property =>
            forbidden.Any(token => property.PropertyType.Name.Contains(token, StringComparison.Ordinal)));
    }

    [Fact]
    public void UpdateResult_RequiresSnapshotForProvenSuccessButNotForUnknown()
    {
        Assert.Throws<ArgumentException>(() => new VoiceActivityUpdateResultV1(
            VoiceActivityUpdateDispositionV1.Applied, null, null));
        var unknown = new VoiceActivityUpdateResultV1(VoiceActivityUpdateDispositionV1.OutcomeUnknown,
            null, "update-outcome-unknown");
        Assert.Null(unknown.Snapshot);
        Assert.Equal("update-outcome-unknown", unknown.SafeCode);
    }

    [Fact]
    public void SourceGeneratedJson_RoundTripsRequestSnapshotAndUpdateWithoutReflectionOptions()
    {
        var request = Request([Source("local", ActivitySourceKindV1.LocalDetector)]);
        var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request, VoiceActivityJsonContextV1.Default.VoiceActivityRequestV1);
        var decodedRequest = JsonSerializer.Deserialize(requestBytes, VoiceActivityJsonContextV1.Default.VoiceActivityRequestV1);
        Assert.Equal(request.Profile, decodedRequest!.Profile);
        Assert.Equal("local", Assert.Single(decodedRequest.Sources).SourceKey);

        var snapshot = new VoiceActivitySnapshotV1(1, 1, request.Profile,
            [new("local", ActivitySourceKindV1.LocalDetector, ActivitySourceRoleV1.Authoritative, true,
                ProviderActivityVisibilityV1.ObservedConsistent)], [], VoiceActivityHealthStateV1.Ready);
        var update = new VoiceActivityUpdateResultV1(VoiceActivityUpdateDispositionV1.Applied, snapshot, null);
        var updateBytes = JsonSerializer.SerializeToUtf8Bytes(update, VoiceActivityJsonContextV1.Default.VoiceActivityUpdateResultV1);
        var decodedUpdate = JsonSerializer.Deserialize(updateBytes, VoiceActivityJsonContextV1.Default.VoiceActivityUpdateResultV1);
        Assert.Equal(VoiceActivityUpdateDispositionV1.Applied, decodedUpdate!.Disposition);
        Assert.Equal(1UL, decodedUpdate.Snapshot!.PlanGeneration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Request_RejectsUnknownProfile(int raw)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoiceActivityRequestV1((VoiceActivityProfileV1)raw,
            ActivityResponsivenessV1.Balanced, VoiceActivityNoiseEnvironmentV1.Variable,
            VoiceActivitySpeechContinuityV1.Natural, null, [], ActivityDegradationPolicyV1.Strict, null));
    }

    private static ActivitySourceRequestV1 Source(string key, ActivitySourceKindV1 kind) =>
        new(key, kind, ActivitySourceRoleV1.Authoritative, true);

    private static VoiceActivityOperationalLimitsV1 Limits() =>
        new(4, 128, 16, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));

    private static VoiceActivityRequestV1 Request(IReadOnlyList<ActivitySourceRequestV1> sources) =>
        new(VoiceActivityProfileV1.HpdManaged, ActivityResponsivenessV1.Balanced,
            VoiceActivityNoiseEnvironmentV1.Variable, VoiceActivitySpeechContinuityV1.Natural,
            TimeSpan.FromMilliseconds(250), sources, ActivityDegradationPolicyV1.AllowOptionalSources, Limits());
}
