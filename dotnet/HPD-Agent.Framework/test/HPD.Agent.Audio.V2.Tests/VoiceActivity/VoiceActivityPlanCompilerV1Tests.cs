using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.V2.Tests.VoiceActivity;

public sealed class VoiceActivityPlanCompilerV1Tests
{
    [Theory]
    [InlineData(VoiceActivityProfileV1.HpdManaged, ActivitySourceKindV1.LocalDetector, 1)]
    [InlineData(VoiceActivityProfileV1.ProviderManaged, ActivitySourceKindV1.ProviderNative, 1)]
    [InlineData(VoiceActivityProfileV1.Manual, ActivitySourceKindV1.Manual, 3)]
    public void Single_authority_profiles_compile_exactly_one_owner(
        VoiceActivityProfileV1 profile, ActivitySourceKindV1 kind, int mode)
    {
        var request = Request(profile, ActivityDegradationPolicyV1.Strict,
            new ActivitySourceRequestV1("primary", kind, ActivitySourceRoleV1.Authoritative, true));

        var plan = Assert.IsType<VoiceActivityPlanCompilationResultV1.Compiled>(
            VoiceActivityPlanCompilerV1.Compile(1, 1, request, [Candidate("primary", kind)])).Plan;

        Assert.Equal((VoiceActivityPromotionModeV1)mode, plan.PromotionAuthority.Mode);
        Assert.Equal(["primary"], plan.PromotionAuthority.SourceKeys);
        Assert.Equal(VoiceActivityHealthStateV1.Ready, plan.Health);
    }

    [Fact]
    public void Fused_plan_retains_one_promoter_and_ordered_authority_inputs()
    {
        var request = Request(VoiceActivityProfileV1.Fused, ActivityDegradationPolicyV1.Strict,
            new ActivitySourceRequestV1("local", ActivitySourceKindV1.LocalDetector, ActivitySourceRoleV1.Authoritative, true),
            new ActivitySourceRequestV1("provider", ActivitySourceKindV1.ProviderNative, ActivitySourceRoleV1.Corroborating, true));

        var plan = Assert.IsType<VoiceActivityPlanCompilationResultV1.Compiled>(
            VoiceActivityPlanCompilerV1.Compile(8, 9, request,
                [Candidate("local", ActivitySourceKindV1.LocalDetector, measurement: VoiceActivityMeasurementKindV1.CalibratedLikelihood),
                 Candidate("provider", ActivitySourceKindV1.ProviderNative, measurement: VoiceActivityMeasurementKindV1.CalibratedLikelihood)])).Plan;

        Assert.Equal(VoiceActivityPromotionModeV1.Fused, plan.PromotionAuthority.Mode);
        Assert.Equal(["local", "provider"], plan.PromotionAuthority.SourceKeys);
        Assert.Equal(8UL, plan.PlanGeneration);
        Assert.Equal(9UL, plan.ConfigRevision);
        var snapshot = plan.ToSnapshot();
        Assert.Equal(8UL, snapshot.PlanGeneration);
        Assert.Equal(["local", "provider"], snapshot.Sources.Select(static source => source.SourceKey).ToArray());
    }

    [Theory]
    [InlineData(VoiceActivityProfileV1.Automatic, ActivitySourceKindV1.LocalDetector)]
    [InlineData(VoiceActivityProfileV1.ProviderManaged, ActivitySourceKindV1.ProviderNative)]
    public void Source_less_profiles_select_only_from_declared_available_candidates(
        VoiceActivityProfileV1 profile, ActivitySourceKindV1 expectedKind)
    {
        var request = Request(profile, ActivityDegradationPolicyV1.AllowOptionalSources);
        var plan = Assert.IsType<VoiceActivityPlanCompilationResultV1.Compiled>(
            VoiceActivityPlanCompilerV1.Compile(1, 1, request,
            [
                Candidate("provider", ActivitySourceKindV1.ProviderNative),
                Candidate("local", ActivitySourceKindV1.LocalDetector),
            ])).Plan;

        Assert.Equal(expectedKind, Assert.Single(plan.Sources).Request.Kind);
        Assert.Equal($"source-auto-selected:{plan.Sources[0].Request.SourceKey}", Assert.Single(plan.Differences));
        Assert.Equal(VoiceActivityHealthStateV1.Ready, plan.Health);
    }

    [Fact]
    public void Optional_loss_is_explicit_degradation_but_required_loss_rejects()
    {
        var optional = Request(VoiceActivityProfileV1.HpdManaged, ActivityDegradationPolicyV1.AllowOptionalSources,
            new ActivitySourceRequestV1("local", ActivitySourceKindV1.LocalDetector, ActivitySourceRoleV1.Authoritative, true),
            new ActivitySourceRequestV1("diagnostic", ActivitySourceKindV1.SttAdjacent, ActivitySourceRoleV1.Diagnostic, false));
        var degraded = Assert.IsType<VoiceActivityPlanCompilationResultV1.Compiled>(
            VoiceActivityPlanCompilerV1.Compile(1, 1, optional,
                [Candidate("local", ActivitySourceKindV1.LocalDetector)])).Plan;
        Assert.Equal(VoiceActivityHealthStateV1.Degraded, degraded.Health);
        Assert.Equal(["source-unavailable:diagnostic"], degraded.Differences);

        var strict = Request(VoiceActivityProfileV1.HpdManaged, ActivityDegradationPolicyV1.Strict,
            new ActivitySourceRequestV1("local", ActivitySourceKindV1.LocalDetector, ActivitySourceRoleV1.Authoritative, true),
            new ActivitySourceRequestV1("diagnostic", ActivitySourceKindV1.SttAdjacent, ActivitySourceRoleV1.Diagnostic, false));
        Assert.Equal("required-source-unavailable", Assert.IsType<VoiceActivityPlanCompilationResultV1.Rejected>(
            VoiceActivityPlanCompilerV1.Compile(1, 1, strict,
                [Candidate("local", ActivitySourceKindV1.LocalDetector)])).SafeCode);
    }

    [Theory]
    [InlineData(VoiceActivityProfileV1.ProviderManaged, ActivitySourceKindV1.LocalDetector, "profile-authority-mismatch")]
    [InlineData(VoiceActivityProfileV1.Manual, ActivitySourceKindV1.LocalDetector, "profile-authority-mismatch")]
    public void Profile_authority_contradictions_fail_closed(
        VoiceActivityProfileV1 profile, ActivitySourceKindV1 kind, string code)
    {
        var request = Request(profile, ActivityDegradationPolicyV1.Strict,
            new ActivitySourceRequestV1("wrong", kind, ActivitySourceRoleV1.Authoritative, true));
        Assert.Equal(code, Assert.IsType<VoiceActivityPlanCompilationResultV1.Rejected>(
            VoiceActivityPlanCompilerV1.Compile(1, 1, request, [Candidate("wrong", kind)])).SafeCode);
    }

    [Fact]
    public void Multiple_or_missing_authoritative_sources_never_compile()
    {
        var duplicate = Request(VoiceActivityProfileV1.Fused, ActivityDegradationPolicyV1.Strict,
            new ActivitySourceRequestV1("one", ActivitySourceKindV1.LocalDetector, ActivitySourceRoleV1.Authoritative, true),
            new ActivitySourceRequestV1("two", ActivitySourceKindV1.LocalDetector, ActivitySourceRoleV1.Authoritative, true));
        Assert.Equal("promotion-authority-count-invalid", Assert.IsType<VoiceActivityPlanCompilationResultV1.Rejected>(
            VoiceActivityPlanCompilerV1.Compile(1, 1, duplicate,
                [Candidate("one", ActivitySourceKindV1.LocalDetector), Candidate("two", ActivitySourceKindV1.LocalDetector)])).SafeCode);

        var insufficientFusion = Request(VoiceActivityProfileV1.Fused, ActivityDegradationPolicyV1.Strict,
            new ActivitySourceRequestV1("one", ActivitySourceKindV1.LocalDetector, ActivitySourceRoleV1.Authoritative, true),
            new ActivitySourceRequestV1("diagnostic", ActivitySourceKindV1.SttAdjacent, ActivitySourceRoleV1.Diagnostic, true));
        Assert.Equal("fusion-source-count-invalid", Assert.IsType<VoiceActivityPlanCompilationResultV1.Rejected>(
            VoiceActivityPlanCompilerV1.Compile(1, 1, insufficientFusion,
            [
                Candidate("one", ActivitySourceKindV1.LocalDetector),
                Candidate("diagnostic", ActivitySourceKindV1.SttAdjacent),
            ])).SafeCode);
    }

    [Fact]
    public void Window_and_prefix_bounds_are_compiled_not_silently_ignored()
    {
        var limits = new VoiceActivityOperationalLimitsV1(2, 16, 2,
            TimeSpan.FromMilliseconds(15), TimeSpan.FromMilliseconds(100));
        var bounded = new VoiceActivityRequestV1(VoiceActivityProfileV1.HpdManaged,
            ActivityResponsivenessV1.Balanced, VoiceActivityNoiseEnvironmentV1.Variable,
            VoiceActivitySpeechContinuityV1.Natural, null,
            [new ActivitySourceRequestV1("local", ActivitySourceKindV1.LocalDetector, ActivitySourceRoleV1.Authoritative, true)],
            ActivityDegradationPolicyV1.Strict, limits);
        Assert.Equal("window-limit-unsupported", Assert.IsType<VoiceActivityPlanCompilationResultV1.Rejected>(
            VoiceActivityPlanCompilerV1.Compile(1, 1, bounded,
                [Candidate("local", ActivitySourceKindV1.LocalDetector, TimeSpan.FromMilliseconds(20))])).SafeCode);

        var prefix = new VoiceActivityRequestV1(VoiceActivityProfileV1.HpdManaged,
            ActivityResponsivenessV1.Balanced, VoiceActivityNoiseEnvironmentV1.Variable,
            VoiceActivitySpeechContinuityV1.Natural, TimeSpan.FromMilliseconds(20), bounded.Sources,
            ActivityDegradationPolicyV1.Strict, limits);
        Assert.Equal("prefix-context-exceeds-limit", Assert.IsType<VoiceActivityPlanCompilationResultV1.Rejected>(
            VoiceActivityPlanCompilerV1.Compile(1, 1, prefix,
                [Candidate("local", ActivitySourceKindV1.LocalDetector)])).SafeCode);
    }

    [Fact]
    public void Kind_and_measurement_semantics_cannot_be_relabelled()
    {
        var request = Request(VoiceActivityProfileV1.Manual, ActivityDegradationPolicyV1.Strict,
            new ActivitySourceRequestV1("manual", ActivitySourceKindV1.Manual, ActivitySourceRoleV1.Authoritative, true));
        var numeric = Candidate("manual", ActivitySourceKindV1.Manual,
            measurement: VoiceActivityMeasurementKindV1.EngineScore);
        Assert.Equal("source-capability-mismatch", Assert.IsType<VoiceActivityPlanCompilationResultV1.Rejected>(
            VoiceActivityPlanCompilerV1.Compile(1, 1, request, [numeric])).SafeCode);

        var relabelled = Candidate("manual", ActivitySourceKindV1.LocalDetector);
        Assert.Equal("source-kind-mismatch", Assert.IsType<VoiceActivityPlanCompilationResultV1.Rejected>(
            VoiceActivityPlanCompilerV1.Compile(1, 1, request, [relabelled])).SafeCode);
    }

    [Fact]
    public void Candidate_identity_is_unique_and_closed_before_selection()
    {
        var request = Request(VoiceActivityProfileV1.Automatic, ActivityDegradationPolicyV1.Strict);
        var candidate = Candidate("same", ActivitySourceKindV1.LocalDetector);

        Assert.Equal("candidate-set-invalid", Assert.IsType<VoiceActivityPlanCompilationResultV1.Rejected>(
            VoiceActivityPlanCompilerV1.Compile(1, 1, request, [candidate, candidate])).SafeCode);
    }

    [Fact]
    public void Provider_visibility_unknown_is_not_promoted_as_observed_truth()
    {
        var request = Request(VoiceActivityProfileV1.ProviderManaged, ActivityDegradationPolicyV1.Strict,
            new ActivitySourceRequestV1("provider", ActivitySourceKindV1.ProviderNative,
                ActivitySourceRoleV1.Authoritative, true));
        var candidate = Candidate("provider", ActivitySourceKindV1.ProviderNative,
            visibility: ProviderActivityVisibilityV1.Unknown);

        Assert.Equal("provider-visibility-insufficient",
            Assert.IsType<VoiceActivityPlanCompilationResultV1.Rejected>(
                VoiceActivityPlanCompilerV1.Compile(1, 1, request, [candidate])).SafeCode);
    }

    private static VoiceActivityRequestV1 Request(VoiceActivityProfileV1 profile,
        ActivityDegradationPolicyV1 degradation, params ActivitySourceRequestV1[] sources) => new(
        profile, ActivityResponsivenessV1.Balanced, VoiceActivityNoiseEnvironmentV1.Variable,
        VoiceActivitySpeechContinuityV1.Natural, null, sources, degradation,
        new VoiceActivityOperationalLimitsV1(8, 64, 8, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));

    private static VoiceActivitySourceCandidateV1 Candidate(string key, ActivitySourceKindV1 kind,
        TimeSpan? minimumWindow = null, VoiceActivityMeasurementKindV1? measurement = null,
        ProviderActivityVisibilityV1? visibility = null)
    {
        var opaque = kind == ActivitySourceKindV1.ProviderNative;
        var measurementKind = measurement ?? kind switch
        {
            ActivitySourceKindV1.Manual => VoiceActivityMeasurementKindV1.PostProcessedState,
            ActivitySourceKindV1.SttAdjacent => VoiceActivityMeasurementKindV1.ProviderOpaqueCategory,
            _ => VoiceActivityMeasurementKindV1.EngineScore,
        };
        return new VoiceActivitySourceCandidateV1(key, kind, new VoiceActivitySourceCapabilitiesV1(
            opaque ? VoiceActivityInputOwnershipV1.ProviderOpaque : VoiceActivityInputOwnershipV1.BorrowedSynchronous,
            [opaque
                ? new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.ProviderOpaque, 0, 0)
                : new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.SignedPcm16, 16_000, 1)],
            new VoiceActivityWindowCapabilityV1(minimumWindow ?? TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(10), 1),
            new VoiceActivityMeasurementDescriptorV1(measurementKind, new BoundedAscii("measurement"), -1, 1,
                measurementKind == VoiceActivityMeasurementKindV1.CalibratedLikelihood
                    ? Hash256.Compute(System.Text.Encoding.ASCII.GetBytes("measurement"))
                    : null),
            opaque ? VoiceActivitySourceStateModelV1.ProviderOpaque : VoiceActivitySourceStateModelV1.Stateless,
            opaque ? VoiceActivitySourceConcurrencyV1.ProviderManaged : VoiceActivitySourceConcurrencyV1.Serial,
            VoiceActivitySourceControlV1.Unsupported, VoiceActivitySourceControlV1.Unsupported,
            opaque ? VoiceActivitySourceControlV1.Sequenced : VoiceActivitySourceControlV1.Unsupported,
            VoiceActivitySourceControlV1.ReplacementRequired, true, false, opaque ? 4 : 1),
            true, visibility ?? (opaque ? ProviderActivityVisibilityV1.Acknowledged : ProviderActivityVisibilityV1.AcceptedLocally));
    }
}
