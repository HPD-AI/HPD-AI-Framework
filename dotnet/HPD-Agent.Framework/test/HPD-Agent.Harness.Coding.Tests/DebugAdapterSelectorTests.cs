using HPD.Agent.ToolHarness.Coding.Debugging;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugAdapterSelectorTests
{
    [Fact]
    public async Task Explicit_selection_returns_only_the_requested_available_adapter()
    {
        var python = new CountingFactory(DebugAdapterAvailabilityKind.Available);
        var other = new CountingFactory(DebugAdapterAvailabilityKind.Available);
        var selector = Selector((Entry("python", ["python"], [".py"]), python), (Entry("other", ["python"], [".py"]), other));

        var result = await selector.SelectAsync(Context(explicitId: "python", language: "unrelated"));

        result.Kind.Should().Be(DebugAdapterSelectionKind.Available);
        result.Entry!.Descriptor.Id.Should().Be("python");
        python.ProbeCount.Should().Be(1);
        other.ProbeCount.Should().Be(0);
    }

    [Fact]
    public async Task Automatic_selection_reports_available_unavailable_ambiguous_and_no_match()
    {
        var available = new CountingFactory(DebugAdapterAvailabilityKind.Available);
        var unavailable = new CountingFactory(DebugAdapterAvailabilityKind.Unavailable);
        var selected = await Selector((Entry("python", ["python"], [".py"], priority: 2), available))
            .SelectAsync(Context(language: "python"));
        var missing = await Selector((Entry("python", ["python"], [".py"]), unavailable))
            .SelectAsync(Context(language: "python"));
        var ambiguous = await Selector(
                (Entry("a", ["python"], [".py"]), new CountingFactory(DebugAdapterAvailabilityKind.Available)),
                (Entry("b", ["python"], [".py"]), new CountingFactory(DebugAdapterAvailabilityKind.Available)))
            .SelectAsync(Context(language: "python"));
        var noMatch = await Selector((Entry("rust", ["rust"], [".rs"]), available))
            .SelectAsync(Context(language: "python"));

        selected.Kind.Should().Be(DebugAdapterSelectionKind.Available);
        missing.Kind.Should().Be(DebugAdapterSelectionKind.Unavailable);
        ambiguous.Kind.Should().Be(DebugAdapterSelectionKind.Ambiguous);
        ambiguous.Candidates.Select(candidate => candidate.AdapterId).Should().Equal("a", "b");
        noMatch.Kind.Should().Be(DebugAdapterSelectionKind.NoMatch);
    }

    [Fact]
    public async Task Availability_cache_isolated_by_environment_policy_endpoint_workspace_and_markers()
    {
        var factory = new CountingFactory(DebugAdapterAvailabilityKind.Available);
        var selector = Selector((Entry("python", ["python"], [".py"]), factory));
        var baseline = Context(language: "python");

        await selector.SelectAsync(baseline);
        await selector.SelectAsync(baseline);
        await selector.SelectAsync(baseline with { Resolution = baseline.Resolution with { EnvironmentRevision = 2 } });
        await selector.SelectAsync(baseline with { Resolution = baseline.Resolution with { PolicyRevision = 2 } });
        await selector.SelectAsync(baseline with { Resolution = baseline.Resolution with { EndpointCatalogRevision = 2 } });
        await selector.SelectAsync(baseline with { Resolution = baseline.Resolution with { WorkspaceRoot = "/other" } });
        await selector.SelectAsync(baseline with { ProjectMarkerFingerprint = "other-markers" });

        factory.ProbeCount.Should().Be(6);
    }

    [Fact]
    public async Task Disabled_and_experimental_adapters_require_explicit_policy_or_id()
    {
        var factory = new CountingFactory(DebugAdapterAvailabilityKind.Available);
        var descriptor = Entry("experimental", ["test"], [".test"], enabled: false, experimental: true);
        var selector = Selector((descriptor, factory));

        var hidden = await selector.SelectAsync(Context(language: "test"));
        var enabled = await selector.SelectAsync(Context(language: "test") with
        {
            Policy = new DebugAdapterSelectionPolicy
            {
                EnabledAdapters = new HashSet<string>(["experimental"]),
                EnabledExperimentalAdapters = new HashSet<string>(["experimental"])
            }
        });

        hidden.Kind.Should().Be(DebugAdapterSelectionKind.NoMatch);
        enabled.Kind.Should().Be(DebugAdapterSelectionKind.Available);
    }

    [Fact]
    public async Task Host_trust_policy_overrides_caller_claims_before_any_probe()
    {
        var factory = new CountingFactory(DebugAdapterAvailabilityKind.Available);
        var selector = Selector(
            new FixedTrustPolicy(DebugAdapterTrustLevel.Denied, "trust-denied"),
            (Entry("python", ["python"], [".py"]), factory));

        var result = await selector.SelectAsync(Context(language: "python"));

        result.Kind.Should().Be(DebugAdapterSelectionKind.Unavailable);
        result.Candidates.Should().ContainSingle().Which.Availability.SafeReasonCode.Should().Be("DENIED_FOR_TEST");
        factory.ProbeCount.Should().Be(0);
    }

    [Fact]
    public async Task Trust_policy_revision_partitions_cached_availability()
    {
        var factory = new CountingFactory(DebugAdapterAvailabilityKind.Available);
        var policy = new MutableTrustPolicy("trust-1");
        var selector = Selector(policy, (Entry("python", ["python"], [".py"]), factory));
        var context = Context(language: "python");

        await selector.SelectAsync(context);
        await selector.SelectAsync(context);
        policy.Revision = "trust-2";
        await selector.SelectAsync(context);

        factory.ProbeCount.Should().Be(2);
    }

    [Fact]
    public async Task Equivalent_workspace_paths_share_one_canonical_cache_identity()
    {
        var factory = new CountingFactory(DebugAdapterAvailabilityKind.Available);
        var selector = Selector((Entry("python", ["python"], [".py"]), factory));
        var baseline = Context(language: "python");

        await selector.SelectAsync(baseline with
        {
            Resolution = baseline.Resolution with { WorkspaceRoot = "/workspace/./src/.." }
        });
        await selector.SelectAsync(baseline);

        factory.ProbeCount.Should().Be(1);
    }

    [Fact]
    public async Task Attach_selection_requires_process_or_registered_endpoint_capability()
    {
        var sourceFactory = new CountingFactory(DebugAdapterAvailabilityKind.Available);
        var processFactory = new CountingFactory(DebugAdapterAvailabilityKind.Available);
        var selector = Selector(
            (Entry("source", ["test"], [".test"]), sourceFactory),
            (Entry("process", ["test"], [".test"], targetKinds: DebugTargetKind.Process), processFactory));

        var result = await selector.SelectAsync(Context(language: "test") with
        {
            Operation = DebugAdapterSelectionOperation.Attach,
            TargetKind = DebugTargetKind.Process
        });

        result.Kind.Should().Be(DebugAdapterSelectionKind.Available);
        result.Entry!.Descriptor.Id.Should().Be("process");
        sourceFactory.ProbeCount.Should().Be(0);
        processFactory.ProbeCount.Should().Be(1);
    }

    [Fact]
    public async Task Attach_selection_uses_runtime_language_hint_to_avoid_wrong_process_adapter()
    {
        var pythonFactory = new CountingFactory(DebugAdapterAvailabilityKind.Available);
        var dotnetFactory = new CountingFactory(DebugAdapterAvailabilityKind.Available);
        var selector = Selector(
            (Entry("python", ["python"], [".py"], targetKinds: DebugTargetKind.Process), pythonFactory),
            (Entry("dotnet", ["csharp"], [".cs"], targetKinds: DebugTargetKind.Process), dotnetFactory));

        var result = await selector.SelectAsync(Context() with
        {
            Operation = DebugAdapterSelectionOperation.Attach,
            TargetKind = DebugTargetKind.Process,
            Language = null,
            FileExtension = null,
            RuntimeLanguageHint = "csharp"
        });

        result.Kind.Should().Be(DebugAdapterSelectionKind.Available);
        result.Entry!.Descriptor.Id.Should().Be("dotnet");
        pythonFactory.ProbeCount.Should().Be(0);
    }

    [Fact]
    public async Task Launch_selection_does_not_treat_attach_only_target_as_launchable()
    {
        var factory = new CountingFactory(DebugAdapterAvailabilityKind.Available);
        var selector = Selector((Entry("process", ["test"], [".test"], targetKinds: DebugTargetKind.Process), factory));

        var result = await selector.SelectAsync(Context(explicitId: "process", language: "test") with
        {
            Operation = DebugAdapterSelectionOperation.Launch,
            TargetKind = DebugTargetKind.Process
        });

        result.Kind.Should().Be(DebugAdapterSelectionKind.NoMatch);
        factory.ProbeCount.Should().Be(0);
    }

    private static DebugAdapterSelector Selector(params (DebugAdapterCatalogEntry Entry, CountingFactory Factory)[] values)
        => Selector(new FixedTrustPolicy(DebugAdapterTrustLevel.Trusted, "trust-1"), values);

    private static DebugAdapterSelector Selector(
        IDebugAdapterTrustPolicy trustPolicy,
        params (DebugAdapterCatalogEntry Entry, CountingFactory Factory)[] values)
    {
        var provider = new FixedProvider(values.Select(value => value.Entry with { FactoryResolver = _ => value.Factory }).ToArray());
        return new DebugAdapterSelector(
            new DebugAdapterCatalog([provider], new EmptyServices()),
            new DebugAdapterAvailabilityCache(),
            trustPolicy,
            new LexicalDebugWorkspaceCanonicalizer());
    }

    private static DebugAdapterCatalogEntry Entry(
        string id,
        IReadOnlyList<string> languages,
        IReadOnlyList<string> extensions,
        int priority = 0,
        bool enabled = true,
        bool experimental = false,
        DebugTargetKind targetKinds = DebugTargetKind.SourceFile) => new()
    {
        Descriptor = new()
        {
            Id = id,
            Languages = languages,
            FileExtensions = extensions,
            RootMarkers = ["project.marker"],
            TargetKinds = targetKinds,
            Priority = priority,
            EnabledByDefault = enabled,
            Experimental = experimental,
            Provenance = new() { PackageId = "tests", PackageVersion = "1", AssemblyName = "tests" }
        },
        FactoryResolver = static _ => throw new InvalidOperationException("Test resolver was not replaced.")
    };

    private static DebugAdapterSelectionContext Context(string? explicitId = null, string? language = null) => new()
    {
        ExplicitAdapterId = explicitId,
        Language = language,
        FileExtension = ".py",
        TargetKind = DebugTargetKind.SourceFile,
        MatchedRootMarkers = new HashSet<string>(["project.marker"]),
        ProjectMarkerFingerprint = "markers",
        Resolution = new DebugAdapterResolutionContext
        {
            WorkspaceRoot = "/workspace",
            EnvironmentId = "environment",
            EnvironmentRevision = 1,
            TargetPlatform = "linux-x64",
            PolicyRevision = 1,
            EndpointCatalogRevision = 1,
            TrustDecision = new DebugAdapterTrustDecision
            {
                TrustLevel = DebugAdapterTrustLevel.Trusted,
                PolicyRevision = "1",
                ReasonCode = "TEST"
            }
        }
    };

    private sealed class CountingFactory(DebugAdapterAvailabilityKind availability) : IDebugAdapterFactory
    {
        public int ProbeCount { get; private set; }
        public ValueTask<DebugAdapterAvailability> ProbeAsync(DebugAdapterDescriptor descriptor, DebugAdapterResolutionContext context, CancellationToken cancellationToken = default)
        {
            ProbeCount++;
            return ValueTask.FromResult(new DebugAdapterAvailability(availability));
        }
        public ValueTask<DebugAdapterLaunchPlan> CreateLaunchPlanAsync(DebugAdapterDescriptor descriptor, DebugLaunchContext context, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DebugAdapterLaunchPlan> CreateAttachPlanAsync(DebugAdapterDescriptor descriptor, DebugAttachContext context, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedProvider(params DebugAdapterCatalogEntry[] entries) : IDebugAdapterCatalogProvider
    {
        public IEnumerable<DebugAdapterCatalogEntry> GetEntries() => entries;
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class FixedTrustPolicy(DebugAdapterTrustLevel level, string revision) : IDebugAdapterTrustPolicy
    {
        public DebugAdapterTrustDecision Evaluate(DebugAdapterDescriptor descriptor) => new()
        {
            TrustLevel = level,
            PolicyRevision = revision,
            ReasonCode = level == DebugAdapterTrustLevel.Trusted ? "TRUSTED_FOR_TEST" : "DENIED_FOR_TEST"
        };
    }

    private sealed class MutableTrustPolicy(string revision) : IDebugAdapterTrustPolicy
    {
        public string Revision { get; set; } = revision;
        public DebugAdapterTrustDecision Evaluate(DebugAdapterDescriptor descriptor) => new()
        {
            TrustLevel = DebugAdapterTrustLevel.Trusted,
            PolicyRevision = Revision,
            ReasonCode = "TRUSTED_FOR_TEST"
        };
    }
}
