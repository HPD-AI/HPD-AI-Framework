using System.Text.Json;
using HPD.Agent.Audio.VoiceActivity;

namespace HPD.Agent.Audio.Tests.VoiceActivity;

public sealed class VoiceActivityPublicContractsTests
{
    [Fact]
    public void Request_DeeplyOwnsSourcesAndRejectsDuplicatesAndUnboundedProfiles()
    {
        var sources = new List<ActivitySourceRequest> { Source("local", ActivitySourceKind.LocalDetector) };
        var request = Request(sources);
        sources.Clear();
        Assert.Single(request.Sources);
        Assert.Throws<ArgumentException>(() => Request([Source("same", ActivitySourceKind.LocalDetector),
            Source("same", ActivitySourceKind.ProviderNative)]));
        Assert.Throws<ArgumentException>(() => new VoiceActivityRequest(VoiceActivityProfile.Fused,
            ActivityResponsiveness.Balanced, VoiceActivityNoiseEnvironment.Variable,
            VoiceActivitySpeechContinuity.Natural, null, [], ActivityDegradationPolicy.Strict, Limits()));
    }

    [Fact]
    public void OperationalLimits_AreFiniteAndBoundSourceCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoiceActivityOperationalLimits(0, 1, 0,
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoiceActivityOperationalLimits(1, 1, 2,
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10)));
        Assert.Throws<ArgumentException>(() => new VoiceActivityRequest(VoiceActivityProfile.HpdManaged,
            ActivityResponsiveness.Balanced, VoiceActivityNoiseEnvironment.Variable,
            VoiceActivitySpeechContinuity.Natural, null,
            [Source("one", ActivitySourceKind.LocalDetector), Source("two", ActivitySourceKind.LocalDetector)],
            ActivityDegradationPolicy.Strict,
            new VoiceActivityOperationalLimits(1, 16, 4, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1))));
    }

    [Fact]
    public void Snapshot_DeeplyOwnsEffectiveTruthAndDoesNotExposeRuntimeObjects()
    {
        var sources = new List<EffectiveActivitySource>
        {
            new("provider", ActivitySourceKind.ProviderNative, ActivitySourceRole.Advisory, false,
                ProviderActivityVisibility.NotObservable),
        };
        var differences = new List<string> { "provider-activity-not-observable" };
        var snapshot = new VoiceActivitySnapshot(1, 2, VoiceActivityProfile.ProviderManaged,
            sources, differences, VoiceActivityHealthState.Unobservable);
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
        Assert.Throws<ArgumentException>(() => new VoiceActivityUpdateResult(
            VoiceActivityUpdateDisposition.Applied, null, null));
        var unknown = new VoiceActivityUpdateResult(VoiceActivityUpdateDisposition.OutcomeUnknown,
            null, "update-outcome-unknown");
        Assert.Null(unknown.Snapshot);
        Assert.Equal("update-outcome-unknown", unknown.SafeCode);
    }

    [Fact]
    public void SourceGeneratedJson_RoundTripsRequestSnapshotAndUpdateWithoutReflectionOptions()
    {
        var request = Request([Source("local", ActivitySourceKind.LocalDetector)]);
        var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request, VoiceActivityJsonContext.Default.VoiceActivityRequest);
        var decodedRequest = JsonSerializer.Deserialize(requestBytes, VoiceActivityJsonContext.Default.VoiceActivityRequest);
        Assert.Equal(request.Profile, decodedRequest!.Profile);
        Assert.Equal("local", Assert.Single(decodedRequest.Sources).SourceKey);

        var snapshot = new VoiceActivitySnapshot(1, 1, request.Profile,
            [new("local", ActivitySourceKind.LocalDetector, ActivitySourceRole.Authoritative, true,
                ProviderActivityVisibility.ObservedConsistent)], [], VoiceActivityHealthState.Ready);
        var update = new VoiceActivityUpdateResult(VoiceActivityUpdateDisposition.Applied, snapshot, null);
        var updateBytes = JsonSerializer.SerializeToUtf8Bytes(update, VoiceActivityJsonContext.Default.VoiceActivityUpdateResult);
        var decodedUpdate = JsonSerializer.Deserialize(updateBytes, VoiceActivityJsonContext.Default.VoiceActivityUpdateResult);
        Assert.Equal(VoiceActivityUpdateDisposition.Applied, decodedUpdate!.Disposition);
        Assert.Equal(1UL, decodedUpdate.Snapshot!.PlanGeneration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Request_RejectsUnknownProfile(int raw)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoiceActivityRequest((VoiceActivityProfile)raw,
            ActivityResponsiveness.Balanced, VoiceActivityNoiseEnvironment.Variable,
            VoiceActivitySpeechContinuity.Natural, null, [], ActivityDegradationPolicy.Strict, null));
    }

    private static ActivitySourceRequest Source(string key, ActivitySourceKind kind) =>
        new(key, kind, ActivitySourceRole.Authoritative, true);

    private static VoiceActivityOperationalLimits Limits() =>
        new(4, 128, 16, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));

    private static VoiceActivityRequest Request(IReadOnlyList<ActivitySourceRequest> sources) =>
        new(VoiceActivityProfile.HpdManaged, ActivityResponsiveness.Balanced,
            VoiceActivityNoiseEnvironment.Variable, VoiceActivitySpeechContinuity.Natural,
            TimeSpan.FromMilliseconds(250), sources, ActivityDegradationPolicy.AllowOptionalSources, Limits());
}
