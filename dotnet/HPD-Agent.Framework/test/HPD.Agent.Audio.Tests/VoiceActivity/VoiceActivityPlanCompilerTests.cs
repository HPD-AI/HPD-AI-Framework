using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Tests.VoiceActivity;

public sealed class VoiceActivityPlanCompilerTests
{
    [Theory]
    [InlineData(VoiceActivityProfile.HpdManaged, ActivitySourceKind.LocalDetector, 1)]
    [InlineData(VoiceActivityProfile.ProviderManaged, ActivitySourceKind.ProviderNative, 1)]
    [InlineData(VoiceActivityProfile.Manual, ActivitySourceKind.Manual, 3)]
    public void Single_authority_profiles_compile_exactly_one_owner(
        VoiceActivityProfile profile, ActivitySourceKind kind, int mode)
    {
        var request = Request(profile, ActivityDegradationPolicy.Strict,
            new ActivitySourceRequest("primary", kind, ActivitySourceRole.Authoritative, true));

        var plan = Assert.IsType<VoiceActivityPlanCompilationResult.Compiled>(
            VoiceActivityPlanCompiler.Compile(1, 1, request, [Candidate("primary", kind)])).Plan;

        Assert.Equal((VoiceActivityPromotionMode)mode, plan.PromotionAuthority.Mode);
        Assert.Equal(["primary"], plan.PromotionAuthority.SourceKeys);
        Assert.Equal(VoiceActivityHealthState.Ready, plan.Health);
    }

    [Fact]
    public void Fused_plan_retains_one_promoter_and_ordered_authority_inputs()
    {
        var request = Request(VoiceActivityProfile.Fused, ActivityDegradationPolicy.Strict,
            new ActivitySourceRequest("local", ActivitySourceKind.LocalDetector, ActivitySourceRole.Authoritative, true),
            new ActivitySourceRequest("provider", ActivitySourceKind.ProviderNative, ActivitySourceRole.Corroborating, true));

        var plan = Assert.IsType<VoiceActivityPlanCompilationResult.Compiled>(
            VoiceActivityPlanCompiler.Compile(8, 9, request,
                [Candidate("local", ActivitySourceKind.LocalDetector, measurement: VoiceActivityMeasurementKind.CalibratedLikelihood),
                 Candidate("provider", ActivitySourceKind.ProviderNative, measurement: VoiceActivityMeasurementKind.CalibratedLikelihood)])).Plan;

        Assert.Equal(VoiceActivityPromotionMode.Fused, plan.PromotionAuthority.Mode);
        Assert.Equal(["local", "provider"], plan.PromotionAuthority.SourceKeys);
        Assert.Equal(8UL, plan.PlanGeneration);
        Assert.Equal(9UL, plan.ConfigRevision);
        var snapshot = plan.ToSnapshot();
        Assert.Equal(8UL, snapshot.PlanGeneration);
        Assert.Equal(["local", "provider"], snapshot.Sources.Select(static source => source.SourceKey).ToArray());
    }

    [Theory]
    [InlineData(VoiceActivityProfile.Automatic, ActivitySourceKind.LocalDetector)]
    [InlineData(VoiceActivityProfile.ProviderManaged, ActivitySourceKind.ProviderNative)]
    public void Source_less_profiles_select_only_from_declared_available_candidates(
        VoiceActivityProfile profile, ActivitySourceKind expectedKind)
    {
        var request = Request(profile, ActivityDegradationPolicy.AllowOptionalSources);
        var plan = Assert.IsType<VoiceActivityPlanCompilationResult.Compiled>(
            VoiceActivityPlanCompiler.Compile(1, 1, request,
            [
                Candidate("provider", ActivitySourceKind.ProviderNative),
                Candidate("local", ActivitySourceKind.LocalDetector),
            ])).Plan;

        Assert.Equal(expectedKind, Assert.Single(plan.Sources).Request.Kind);
        Assert.Equal($"source-auto-selected:{plan.Sources[0].Request.SourceKey}", Assert.Single(plan.Differences));
        Assert.Equal(VoiceActivityHealthState.Ready, plan.Health);
    }

    [Fact]
    public void Optional_loss_is_explicit_degradation_but_required_loss_rejects()
    {
        var optional = Request(VoiceActivityProfile.HpdManaged, ActivityDegradationPolicy.AllowOptionalSources,
            new ActivitySourceRequest("local", ActivitySourceKind.LocalDetector, ActivitySourceRole.Authoritative, true),
            new ActivitySourceRequest("diagnostic", ActivitySourceKind.SttAdjacent, ActivitySourceRole.Diagnostic, false));
        var degraded = Assert.IsType<VoiceActivityPlanCompilationResult.Compiled>(
            VoiceActivityPlanCompiler.Compile(1, 1, optional,
                [Candidate("local", ActivitySourceKind.LocalDetector)])).Plan;
        Assert.Equal(VoiceActivityHealthState.Degraded, degraded.Health);
        Assert.Equal(["source-unavailable:diagnostic"], degraded.Differences);

        var strict = Request(VoiceActivityProfile.HpdManaged, ActivityDegradationPolicy.Strict,
            new ActivitySourceRequest("local", ActivitySourceKind.LocalDetector, ActivitySourceRole.Authoritative, true),
            new ActivitySourceRequest("diagnostic", ActivitySourceKind.SttAdjacent, ActivitySourceRole.Diagnostic, false));
        Assert.Equal("required-source-unavailable", Assert.IsType<VoiceActivityPlanCompilationResult.Rejected>(
            VoiceActivityPlanCompiler.Compile(1, 1, strict,
                [Candidate("local", ActivitySourceKind.LocalDetector)])).SafeCode);
    }

    [Theory]
    [InlineData(VoiceActivityProfile.ProviderManaged, ActivitySourceKind.LocalDetector, "profile-authority-mismatch")]
    [InlineData(VoiceActivityProfile.Manual, ActivitySourceKind.LocalDetector, "profile-authority-mismatch")]
    public void Profile_authority_contradictions_fail_closed(
        VoiceActivityProfile profile, ActivitySourceKind kind, string code)
    {
        var request = Request(profile, ActivityDegradationPolicy.Strict,
            new ActivitySourceRequest("wrong", kind, ActivitySourceRole.Authoritative, true));
        Assert.Equal(code, Assert.IsType<VoiceActivityPlanCompilationResult.Rejected>(
            VoiceActivityPlanCompiler.Compile(1, 1, request, [Candidate("wrong", kind)])).SafeCode);
    }

    [Fact]
    public void Multiple_or_missing_authoritative_sources_never_compile()
    {
        var duplicate = Request(VoiceActivityProfile.Fused, ActivityDegradationPolicy.Strict,
            new ActivitySourceRequest("one", ActivitySourceKind.LocalDetector, ActivitySourceRole.Authoritative, true),
            new ActivitySourceRequest("two", ActivitySourceKind.LocalDetector, ActivitySourceRole.Authoritative, true));
        Assert.Equal("promotion-authority-count-invalid", Assert.IsType<VoiceActivityPlanCompilationResult.Rejected>(
            VoiceActivityPlanCompiler.Compile(1, 1, duplicate,
                [Candidate("one", ActivitySourceKind.LocalDetector), Candidate("two", ActivitySourceKind.LocalDetector)])).SafeCode);

        var insufficientFusion = Request(VoiceActivityProfile.Fused, ActivityDegradationPolicy.Strict,
            new ActivitySourceRequest("one", ActivitySourceKind.LocalDetector, ActivitySourceRole.Authoritative, true),
            new ActivitySourceRequest("diagnostic", ActivitySourceKind.SttAdjacent, ActivitySourceRole.Diagnostic, true));
        Assert.Equal("fusion-source-count-invalid", Assert.IsType<VoiceActivityPlanCompilationResult.Rejected>(
            VoiceActivityPlanCompiler.Compile(1, 1, insufficientFusion,
            [
                Candidate("one", ActivitySourceKind.LocalDetector),
                Candidate("diagnostic", ActivitySourceKind.SttAdjacent),
            ])).SafeCode);
    }

    [Fact]
    public void Window_and_prefix_bounds_are_compiled_not_silently_ignored()
    {
        var limits = new VoiceActivityOperationalLimits(2, 16, 2,
            TimeSpan.FromMilliseconds(15), TimeSpan.FromMilliseconds(100));
        var bounded = new VoiceActivityRequest(VoiceActivityProfile.HpdManaged,
            ActivityResponsiveness.Balanced, VoiceActivityNoiseEnvironment.Variable,
            VoiceActivitySpeechContinuity.Natural, null,
            [new ActivitySourceRequest("local", ActivitySourceKind.LocalDetector, ActivitySourceRole.Authoritative, true)],
            ActivityDegradationPolicy.Strict, limits);
        Assert.Equal("window-limit-unsupported", Assert.IsType<VoiceActivityPlanCompilationResult.Rejected>(
            VoiceActivityPlanCompiler.Compile(1, 1, bounded,
                [Candidate("local", ActivitySourceKind.LocalDetector, TimeSpan.FromMilliseconds(20))])).SafeCode);

        var prefix = new VoiceActivityRequest(VoiceActivityProfile.HpdManaged,
            ActivityResponsiveness.Balanced, VoiceActivityNoiseEnvironment.Variable,
            VoiceActivitySpeechContinuity.Natural, TimeSpan.FromMilliseconds(20), bounded.Sources,
            ActivityDegradationPolicy.Strict, limits);
        Assert.Equal("prefix-context-exceeds-limit", Assert.IsType<VoiceActivityPlanCompilationResult.Rejected>(
            VoiceActivityPlanCompiler.Compile(1, 1, prefix,
                [Candidate("local", ActivitySourceKind.LocalDetector)])).SafeCode);
    }

    [Fact]
    public void Kind_and_measurement_semantics_cannot_be_relabelled()
    {
        var request = Request(VoiceActivityProfile.Manual, ActivityDegradationPolicy.Strict,
            new ActivitySourceRequest("manual", ActivitySourceKind.Manual, ActivitySourceRole.Authoritative, true));
        var numeric = Candidate("manual", ActivitySourceKind.Manual,
            measurement: VoiceActivityMeasurementKind.EngineScore);
        Assert.Equal("source-capability-mismatch", Assert.IsType<VoiceActivityPlanCompilationResult.Rejected>(
            VoiceActivityPlanCompiler.Compile(1, 1, request, [numeric])).SafeCode);

        var relabelled = Candidate("manual", ActivitySourceKind.LocalDetector);
        Assert.Equal("source-kind-mismatch", Assert.IsType<VoiceActivityPlanCompilationResult.Rejected>(
            VoiceActivityPlanCompiler.Compile(1, 1, request, [relabelled])).SafeCode);
    }

    [Fact]
    public void Candidate_identity_is_unique_and_closed_before_selection()
    {
        var request = Request(VoiceActivityProfile.Automatic, ActivityDegradationPolicy.Strict);
        var candidate = Candidate("same", ActivitySourceKind.LocalDetector);

        Assert.Equal("candidate-set-invalid", Assert.IsType<VoiceActivityPlanCompilationResult.Rejected>(
            VoiceActivityPlanCompiler.Compile(1, 1, request, [candidate, candidate])).SafeCode);
    }

    [Fact]
    public void Provider_visibility_unknown_is_not_promoted_as_observed_truth()
    {
        var request = Request(VoiceActivityProfile.ProviderManaged, ActivityDegradationPolicy.Strict,
            new ActivitySourceRequest("provider", ActivitySourceKind.ProviderNative,
                ActivitySourceRole.Authoritative, true));
        var candidate = Candidate("provider", ActivitySourceKind.ProviderNative,
            visibility: ProviderActivityVisibility.Unknown);

        Assert.Equal("provider-visibility-insufficient",
            Assert.IsType<VoiceActivityPlanCompilationResult.Rejected>(
                VoiceActivityPlanCompiler.Compile(1, 1, request, [candidate])).SafeCode);
    }

    private static VoiceActivityRequest Request(VoiceActivityProfile profile,
        ActivityDegradationPolicy degradation, params ActivitySourceRequest[] sources) => new(
        profile, ActivityResponsiveness.Balanced, VoiceActivityNoiseEnvironment.Variable,
        VoiceActivitySpeechContinuity.Natural, null, sources, degradation,
        new VoiceActivityOperationalLimits(8, 64, 8, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));

    private static VoiceActivitySourceCandidate Candidate(string key, ActivitySourceKind kind,
        TimeSpan? minimumWindow = null, VoiceActivityMeasurementKind? measurement = null,
        ProviderActivityVisibility? visibility = null)
    {
        var opaque = kind == ActivitySourceKind.ProviderNative;
        var measurementKind = measurement ?? kind switch
        {
            ActivitySourceKind.Manual => VoiceActivityMeasurementKind.PostProcessedState,
            ActivitySourceKind.SttAdjacent => VoiceActivityMeasurementKind.ProviderOpaqueCategory,
            _ => VoiceActivityMeasurementKind.EngineScore,
        };
        return new VoiceActivitySourceCandidate(key, kind, new VoiceActivitySourceCapabilities(
            opaque ? VoiceActivityInputOwnership.ProviderOpaque : VoiceActivityInputOwnership.BorrowedSynchronous,
            [opaque
                ? new VoiceActivityInputFormat(VoiceActivitySampleEncoding.ProviderOpaque, 0, 0)
                : new VoiceActivityInputFormat(VoiceActivitySampleEncoding.SignedPcm16, 16_000, 1)],
            new VoiceActivityWindowCapability(minimumWindow ?? TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(10), 1),
            new VoiceActivityMeasurementDescriptor(measurementKind, new BoundedAscii("measurement"), -1, 1,
                measurementKind == VoiceActivityMeasurementKind.CalibratedLikelihood
                    ? Hash256.Compute(System.Text.Encoding.ASCII.GetBytes("measurement"))
                    : null),
            opaque ? VoiceActivitySourceStateModel.ProviderOpaque : VoiceActivitySourceStateModel.Stateless,
            opaque ? VoiceActivitySourceConcurrency.ProviderManaged : VoiceActivitySourceConcurrency.Serial,
            VoiceActivitySourceControl.Unsupported, VoiceActivitySourceControl.Unsupported,
            opaque ? VoiceActivitySourceControl.Sequenced : VoiceActivitySourceControl.Unsupported,
            VoiceActivitySourceControl.ReplacementRequired, true, false, opaque ? 4 : 1),
            true, visibility ?? (opaque ? ProviderActivityVisibility.Acknowledged : ProviderActivityVisibility.AcceptedLocally));
    }
}
