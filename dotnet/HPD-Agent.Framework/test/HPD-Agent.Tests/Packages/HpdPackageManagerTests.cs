using FluentAssertions;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Packages;
using HPD.Agent.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Tests.Packages;

public sealed class HpdPackageManagerTests
{
    [Fact]
    public void Enable_AppliesAgentAndProviderContributionsWithPackageOwner()
    {
        var agentContributors = new AgentBuilderContributorStore();
        var providerContributions = new ProviderContributionStore();
        var manager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(agentContributors, providerContributions));

        var loaded = manager.Enable(new TestPackage("hpd.test.package"), HpdPackageScopes.Workspace);

        loaded.Owner.Should().Be(new HpdContributionOwner(
            "hpd.test.package",
            HpdPackageScopes.Workspace,
            "1.2.3",
            "Test Package"));
        agentContributors.Contributions.Should().ContainSingle(contribution =>
            contribution.Key == "hpd.test.package.agent" &&
            contribution.Owner == loaded.Owner);
        providerContributions.ProviderFactories.Should().ContainSingle(contribution =>
            contribution.Key == "test-provider" &&
            contribution.Owner == loaded.Owner);
        loaded.Contributions.AgentContributors.Should().Equal("hpd.test.package.agent");
        loaded.Contributions.ProviderFactories.Should().Equal("test-provider");
        loaded.Contributions.HasAny.Should().BeTrue();
        loaded.Impacts.Should().Contain(HpdPackageChangeImpact.FutureAgentBuilds);
        loaded.Impacts.Should().Contain(HpdPackageChangeImpact.CachedAgentsStale);
    }

    [Fact]
    public void Disable_RemovesOwnedAgentAndProviderContributions()
    {
        var agentContributors = new AgentBuilderContributorStore();
        var providerContributions = new ProviderContributionStore();
        var manager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(agentContributors, providerContributions));
        manager.Enable(new TestPackage("hpd.test.package"), HpdPackageScopes.Workspace);

        manager.Disable("hpd.test.package").Should().BeTrue();

        agentContributors.Contributions.Should().BeEmpty();
        providerContributions.ProviderFactories.Should().BeEmpty();
        manager.Packages.Should().BeEmpty();
    }

    [Fact]
    public void Enable_ReplacesExistingPackageContributions()
    {
        var agentContributors = new AgentBuilderContributorStore();
        var providerContributions = new ProviderContributionStore();
        var manager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(agentContributors, providerContributions));

        var first = manager.Enable(new TestPackage("hpd.test.package", providerKey: "first-provider"));
        var second = manager.Enable(new TestPackage("hpd.test.package", providerKey: "second-provider"));

        second.Owner.Should().Be(first.Owner);
        providerContributions.ProviderFactories.Should().ContainSingle(contribution =>
            contribution.Key == "second-provider");
        providerContributions.ProviderFactories.Should().NotContain(contribution =>
            contribution.Key == "first-provider");
        agentContributors.Contributions.Should().ContainSingle(contribution =>
            contribution.Key == "hpd.test.package.agent");
    }

    [Fact]
    public void Enable_WhenPackageThrows_RollsBackContributionsAndRecordsFailure()
    {
        var agentContributors = new AgentBuilderContributorStore();
        var providerContributions = new ProviderContributionStore();
        var manager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(agentContributors, providerContributions));

        var loaded = manager.Enable(new ThrowingPackage("hpd.bad.package"));

        loaded.State.Should().Be(HpdPackageLoadState.Failed);
        loaded.Contributions.AgentContributors.Should().Equal("hpd.bad.package.agent");
        loaded.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Severity == HpdPackageDiagnosticSeverity.Error &&
            diagnostic.Message.Contains("Package activation failed", StringComparison.Ordinal));
        agentContributors.Contributions.Should().BeEmpty();
        providerContributions.ProviderFactories.Should().BeEmpty();
        manager.Packages.Should().ContainSingle(package =>
            package.Id == "hpd.bad.package" &&
            package.State == HpdPackageLoadState.Failed);
    }

    [Fact]
    public void Enable_WhenReloadCandidateThrows_KeepsPreviousPackageActive()
    {
        var agentContributors = new AgentBuilderContributorStore();
        var providerContributions = new ProviderContributionStore();
        var manager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(agentContributors, providerContributions));
        var previous = manager.Enable(new TestPackage("hpd.test.package", providerKey: "previous-provider"));

        var failed = manager.Enable(new ThrowingPackage("hpd.test.package"));

        failed.State.Should().Be(HpdPackageLoadState.Failed);
        manager.Packages.Should().ContainSingle(package =>
            package.Id == previous.Id &&
            package.State == HpdPackageLoadState.Enabled &&
            package.Owner == previous.Owner);
        agentContributors.Contributions.Should().ContainSingle(contribution =>
            contribution.Key == "hpd.test.package.agent" &&
            contribution.Owner == previous.Owner);
        providerContributions.ProviderFactories.Should().ContainSingle(contribution =>
            contribution.Key == "previous-provider" &&
            contribution.Owner == previous.Owner);
    }

    [Fact]
    public void Reload_ReplacesPreviousContributions()
    {
        var agentContributors = new AgentBuilderContributorStore();
        var providerContributions = new ProviderContributionStore();
        var manager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(agentContributors, providerContributions));
        manager.Enable(new TestPackage("hpd.test.package", providerKey: "first-provider"));

        var loaded = manager.Reload(new TestPackage("hpd.test.package", providerKey: "second-provider"));

        loaded.State.Should().Be(HpdPackageLoadState.Enabled);
        loaded.Impacts.Should().Contain(HpdPackageChangeImpact.CachedAgentsStale);
        providerContributions.ProviderFactories.Should().ContainSingle(contribution =>
            contribution.Key == "second-provider");
        providerContributions.ProviderFactories.Should().NotContain(contribution =>
            contribution.Key == "first-provider");
    }

    [Fact]
    public void PrepareAndCommit_AppliesCandidateOnlyAfterCommit()
    {
        var agentContributors = new AgentBuilderContributorStore();
        var providerContributions = new ProviderContributionStore();
        var manager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(agentContributors, providerContributions));

        var prepared = manager.Prepare(HpdPackageChangeRequest.Enable(new TestPackage("hpd.test.package")));

        prepared.IsValid.Should().BeTrue();
        agentContributors.Contributions.Should().BeEmpty();
        providerContributions.ProviderFactories.Should().BeEmpty();
        prepared.Contributions.AgentContributors.Should().Equal("hpd.test.package.agent");
        prepared.Contributions.ProviderFactories.Should().Equal("test-provider");

        var committed = manager.CommitPrepared(prepared);

        committed.Committed.Should().BeTrue();
        committed.Package.State.Should().Be(HpdPackageLoadState.Enabled);
        agentContributors.Contributions.Should().ContainSingle(contribution =>
            contribution.Key == "hpd.test.package.agent");
        providerContributions.ProviderFactories.Should().ContainSingle(contribution =>
            contribution.Key == "test-provider");
    }

    [Fact]
    public void Prepare_DetectsContributorConflictBeforeMutatingCurrentState()
    {
        var agentContributors = new AgentBuilderContributorStore();
        var providerContributions = new ProviderContributionStore();
        var manager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(agentContributors, providerContributions));
        manager.Enable(new TestPackage("hpd.test.package"));

        var prepared = manager.Prepare(HpdPackageChangeRequest.Enable(new ConflictingAgentPackage()));

        prepared.IsValid.Should().BeFalse();
        prepared.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == "HPD_PACKAGE_CONFLICT" &&
            diagnostic.Message.Contains("hpd.test.package.agent", StringComparison.Ordinal));
        agentContributors.Contributions.Should().ContainSingle(contribution =>
            contribution.Owner.Id == "hpd.test.package");
    }

    [Fact]
    public void Prepare_DetectsRuntimeContributionConflictBeforeMutatingCurrentState()
    {
        var runtimeContributions = new HpdPackageRuntimeContributionStore();
        var manager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(
                new AgentBuilderContributorStore(),
                new ProviderContributionStore(),
                runtimeContributions));
        manager.Enable(new RuntimeContributionPackage("hpd.first.runtime", "shared.runtime"));

        var prepared = manager.Prepare(
            HpdPackageChangeRequest.Enable(new RuntimeContributionPackage("hpd.second.runtime", "shared.runtime")));

        prepared.IsValid.Should().BeFalse();
        prepared.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == "HPD_PACKAGE_CONFLICT" &&
            diagnostic.Message.Contains("shared.runtime", StringComparison.Ordinal));
        runtimeContributions.Contributions.Should().ContainSingle(contribution =>
            contribution.Key == "shared.runtime" &&
            contribution.Owner.Id == "hpd.first.runtime");
        manager.Packages.Should().ContainSingle(package =>
            package.Id == "hpd.first.runtime" &&
            package.State == HpdPackageLoadState.Enabled);
    }

    [Fact]
    public void Enable_AppliesAndRemovesExternalProcessContributions()
    {
        var agentContributors = new AgentBuilderContributorStore();
        var providerContributions = new ProviderContributionStore();
        var externalProcesses = new HpdExternalPackageProcessRuntime();
        var manager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(
                agentContributors,
                providerContributions,
                new HpdPackageRuntimeContributionStore(),
                externalProcesses));

        var loaded = manager.Enable(new ExternalProcessPackage());

        loaded.Impacts.Should().Contain(HpdPackageChangeImpact.RequiresExternalProcess);
        loaded.Contributions.ExternalProcesses.Should().Equal("hpd.external.package.process");
        externalProcesses.Processes.Should().ContainSingle(process =>
            process.Key == "hpd.external.package.process" &&
            process.Spec.Command == "hpd-external" &&
            process.Spec.Protocol == "json-rpc");

        manager.Disable("hpd.external.package").Should().BeTrue();

        externalProcesses.Processes.Should().BeEmpty();
    }

    [Fact]
    public void Reload_ReplacesOwnedExternalProcessContribution()
    {
        var externalProcesses = new HpdExternalPackageProcessRuntime();
        var manager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(
                new AgentBuilderContributorStore(),
                new ProviderContributionStore(),
                new HpdPackageRuntimeContributionStore(),
                externalProcesses));
        manager.Enable(new ExternalProcessPackage(command: "hpd-external-v1"));

        var loaded = manager.Reload(new ExternalProcessPackage(command: "hpd-external-v2"));

        loaded.State.Should().Be(HpdPackageLoadState.Enabled);
        externalProcesses.Processes.Should().ContainSingle(process =>
            process.Key == "hpd.external.package.process" &&
            process.Spec.Command == "hpd-external-v2");
        externalProcesses.Processes.Should().NotContain(process =>
            process.Spec.Command == "hpd-external-v1");
    }

    [Fact]
    public void Prepare_AddsManifestMcpEntrypointAsExternalProcess()
    {
        var externalProcesses = new HpdExternalPackageProcessRuntime();
        var manager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(
                new AgentBuilderContributorStore(),
                new ProviderContributionStore(),
                new HpdPackageRuntimeContributionStore(),
                externalProcesses));

        var prepared = manager.Prepare(HpdPackageChangeRequest.Enable(new ManifestMcpPackage()));

        prepared.IsValid.Should().BeTrue();
        prepared.Contributions.ExternalProcesses.Should().Equal("hpd.mcp.package.mcp.test-mcp");
        prepared.CandidateStores.ExternalProcesses.Processes.Should().ContainSingle(process =>
            process.Spec.Protocol == "mcp" &&
            process.Spec.Command == "hpd-test-mcp");
        externalProcesses.Processes.Should().BeEmpty();
    }

    [Fact]
    public void Changed_ReportsEnableReloadDisableAndFailure()
    {
        var agentContributors = new AgentBuilderContributorStore();
        var providerContributions = new ProviderContributionStore();
        var manager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(agentContributors, providerContributions));
        var changes = new List<HpdPackageChangedEventArgs>();
        manager.Changed += (_, args) => changes.Add(args);

        manager.Enable(new TestPackage("hpd.test.package", providerKey: "first-provider"));
        manager.Reload(new TestPackage("hpd.test.package", providerKey: "second-provider"));
        manager.Disable("hpd.test.package");
        manager.Enable(new ThrowingPackage("hpd.bad.package"));

        changes.Select(change => change.Kind).Should().Equal(
            HpdPackageChangeKind.Enabled,
            HpdPackageChangeKind.Reloaded,
            HpdPackageChangeKind.Disabled,
            HpdPackageChangeKind.Failed);
        changes.Select(change => change.Package.Id).Should().Equal(
            "hpd.test.package",
            "hpd.test.package",
            "hpd.test.package",
            "hpd.bad.package");
    }

    private sealed class TestPackage : IHpdPackage
    {
        private readonly string _providerKey;

        public TestPackage(
            string id,
            string providerKey = "test-provider")
        {
            Id = id;
            _providerKey = providerKey;
        }

        public string Id { get; }

        public HpdPackageManifest Manifest => new(Id, DisplayName, Version)
        {
            Trust = HpdPackageTrust.Trusted,
            LoadMode = HpdPackageLoadMode.BuildTimeInProcess,
            Contributes = new HpdPackageContributes
            {
                Agent = true,
                Providers = true
            }
        };

        public string DisplayName => "Test Package";

        public Version Version { get; } = new(1, 2, 3);

        public void Configure(IHpdPackageBuilder builder)
        {
            builder.AddAgentContributor(
                $"{Id}.agent",
                new DelegateAgentBuilderContributor(agentBuilder =>
                    agentBuilder.WithProviderRegistry(new ProviderRegistry())));
            builder.AddProviderContributor(new TestProviderContributor(_providerKey));
        }
    }

    private sealed class TestProviderContributor : IProviderContributor
    {
        private readonly string _providerKey;

        public TestProviderContributor(string providerKey)
        {
            _providerKey = providerKey;
        }

        public void ConfigureProviders(
            IProviderContributionBuilder builder,
            HpdProviderContributionContext context)
            => builder.AddProviderFactory(_providerKey, _ => new TestProvider(_providerKey));
    }

    private sealed class ThrowingPackage : IHpdPackage
    {
        public ThrowingPackage(string id)
        {
            Id = id;
        }

        public string Id { get; }

        public HpdPackageManifest Manifest => new(Id, DisplayName, Version);

        public string DisplayName => "Bad Package";

        public Version Version { get; } = new(1, 0, 0);

        public void Configure(IHpdPackageBuilder builder)
        {
            builder.AddAgentContributor(
                $"{Id}.agent",
                new DelegateAgentBuilderContributor(_ => { }));
            throw new InvalidOperationException("nope");
        }
    }

    private sealed class ConflictingAgentPackage : IHpdPackage
    {
        public string Id => "hpd.conflicting.package";

        public HpdPackageManifest Manifest => new(Id, DisplayName, Version);

        public string DisplayName => "Conflicting Package";

        public Version Version { get; } = new(1, 0, 0);

        public void Configure(IHpdPackageBuilder builder)
        {
            builder.AddAgentContributor(
                "hpd.test.package.agent",
                new DelegateAgentBuilderContributor(_ => { }));
        }
    }

    private sealed class ExternalProcessPackage : IHpdPackage
    {
        private readonly string _command;

        public ExternalProcessPackage(string command = "hpd-external")
        {
            _command = command;
        }

        public string Id => "hpd.external.package";

        public HpdPackageManifest Manifest => new(Id, DisplayName, Version)
        {
            Trust = HpdPackageTrust.OutOfProcess,
            LoadMode = HpdPackageLoadMode.OutOfProcess,
            Entrypoints = new HpdPackageEntrypoints
            {
                Process = new HpdProcessPackageEntrypoint
                {
                    Command = _command,
                    Args = ["--stdio"],
                    Protocol = "json-rpc"
                }
            }
        };

        public string DisplayName => "External Package";

        public Version Version { get; } = new(1, 0, 0);

        public void Configure(IHpdPackageBuilder builder)
        {
        }
    }

    private sealed class RuntimeContributionPackage : IHpdPackage
    {
        private readonly string _key;

        public RuntimeContributionPackage(string id, string key)
        {
            Id = id;
            _key = key;
        }

        public string Id { get; }

        public HpdPackageManifest Manifest => new(Id, DisplayName, Version)
        {
            Trust = HpdPackageTrust.Trusted,
            LoadMode = HpdPackageLoadMode.BuildTimeInProcess
        };

        public string DisplayName => "Runtime Contribution Package";

        public Version Version { get; } = new(1, 0, 0);

        public void Configure(IHpdPackageBuilder builder)
            => builder.AddRuntimeContribution(_key, new object());
    }

    private sealed class ManifestMcpPackage : IHpdPackage
    {
        public string Id => "hpd.mcp.package";

        public HpdPackageManifest Manifest => new(Id, DisplayName, Version)
        {
            Trust = HpdPackageTrust.OutOfProcess,
            LoadMode = HpdPackageLoadMode.OutOfProcess,
            Entrypoints = new HpdPackageEntrypoints
            {
                Mcp =
                [
                    new HpdMcpPackageEntrypoint
                    {
                        Name = "test-mcp",
                        Command = "hpd-test-mcp",
                        Args = ["--stdio"]
                    }
                ]
            }
        };

        public string DisplayName => "MCP Package";

        public Version Version { get; } = new(1, 0, 0);

        public void Configure(IHpdPackageBuilder builder)
        {
        }
    }

    private sealed class TestProvider : IProvider
    {
        public TestProvider(string providerKey)
        {
            ProviderKey = providerKey;
        }

        public string ProviderKey { get; }

        public string DisplayName => ProviderKey;

        public IProviderErrorHandler CreateErrorHandler() => new TestProviderErrorHandler();

        public ProviderMetadata GetMetadata() => new()
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName
        };

        public ProviderValidationResult ValidateConfiguration(
            ClientProviderConfig config,
            ProviderClientFamily family)
            => ProviderValidationResult.Success();
    }

    private sealed class TestProviderErrorHandler : IProviderErrorHandler
    {
        public ProviderErrorDetails? ParseError(Exception exception) => new()
        {
            Message = exception.Message
        };

        public TimeSpan? GetRetryDelay(
            ProviderErrorDetails details,
            int attempt,
            TimeSpan initialDelay,
            double multiplier,
            TimeSpan maxDelay)
            => null;

        public bool RequiresSpecialHandling(ProviderErrorDetails details) => false;
    }
}
