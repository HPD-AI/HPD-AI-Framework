using FluentAssertions;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Packages;
using HPD.Agent.Providers;
using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Components;
using HPD.TUI.Core;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.TUI.Tests;

public sealed class AgentTuiPackageManagerTests
{
    [Fact]
    public void Enable_AppliesCoreAndTuiPackageContributions()
    {
        var agentContributors = new AgentBuilderContributorStore();
        var providerContributions = new ProviderContributionStore();
        var packageManager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(agentContributors, providerContributions));
        var tuiStore = new AgentTuiContributionStore();
        using var services = new ServiceCollection().BuildServiceProvider();
        var tuiPackages = new AgentTuiPackageManager(packageManager, tuiStore, services);

        var loaded = tuiPackages.Enable(new TestPackage(), HpdPackageScopes.Workspace);

        tuiPackages.Packages.Should().ContainSingle(package =>
            package.Id == loaded.Id &&
            package.Owner == loaded.Owner);
        agentContributors.Contributions.Should().ContainSingle(contribution =>
            contribution.Owner == loaded.Owner);
        providerContributions.ProviderFactories.Should().ContainSingle(contribution =>
            contribution.Owner == loaded.Owner);
        var registry = new HpdAgentTuiRegistry(tuiStore);
        registry.CommandContributions.Should().ContainSingle(contribution =>
            contribution.Key == "test" &&
            contribution.Owner == loaded.Owner);
        loaded.Impacts.Should().Contain(HpdPackageChangeImpact.LiveNow);
        loaded.Impacts.Should().Contain(HpdPackageChangeImpact.CachedAgentsStale);
    }

    [Fact]
    public void Disable_RemovesCoreAndTuiPackageContributions()
    {
        var agentContributors = new AgentBuilderContributorStore();
        var providerContributions = new ProviderContributionStore();
        var packageManager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(agentContributors, providerContributions));
        var tuiStore = new AgentTuiContributionStore();
        using var services = new ServiceCollection().BuildServiceProvider();
        var tuiPackages = new AgentTuiPackageManager(packageManager, tuiStore, services);
        tuiPackages.Enable(new TestPackage(), HpdPackageScopes.Workspace);

        tuiPackages.Disable("hpd.test.package").Should().BeTrue();

        agentContributors.Contributions.Should().BeEmpty();
        providerContributions.ProviderFactories.Should().BeEmpty();
        new HpdAgentTuiRegistry(tuiStore).Commands.Should().BeEmpty();
    }

    [Fact]
    public void Enable_WhenTuiContributorThrows_RollsBackCoreAndTuiContributions()
    {
        var agentContributors = new AgentBuilderContributorStore();
        var providerContributions = new ProviderContributionStore();
        var packageManager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(agentContributors, providerContributions));
        var tuiStore = new AgentTuiContributionStore();
        using var services = new ServiceCollection().BuildServiceProvider();
        var tuiPackages = new AgentTuiPackageManager(packageManager, tuiStore, services);

        var loaded = tuiPackages.Enable(new ThrowingTuiPackage(), HpdPackageScopes.Workspace);

        loaded.State.Should().Be(HpdPackageLoadState.Failed);
        loaded.Impacts.Should().Contain(HpdPackageChangeImpact.LiveNow);
        loaded.Impacts.Should().NotContain(HpdPackageChangeImpact.CachedAgentsStale);
        loaded.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Message.Contains("TUI package activation failed", StringComparison.Ordinal));
        agentContributors.Contributions.Should().BeEmpty();
        providerContributions.ProviderFactories.Should().BeEmpty();
        new HpdAgentTuiRegistry(tuiStore).Commands.Should().BeEmpty();
        packageManager.Packages.Should().BeEmpty();
    }

    [Fact]
    public void Enable_WhenTuiReloadCandidateThrows_KeepsPreviousPackageActive()
    {
        var agentContributors = new AgentBuilderContributorStore();
        var providerContributions = new ProviderContributionStore();
        var packageManager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(agentContributors, providerContributions));
        var tuiStore = new AgentTuiContributionStore();
        using var services = new ServiceCollection().BuildServiceProvider();
        var tuiPackages = new AgentTuiPackageManager(packageManager, tuiStore, services);
        var previous = tuiPackages.Enable(new TestPackage(), HpdPackageScopes.Workspace);

        var failed = tuiPackages.Enable(new ThrowingReplacementTuiPackage(), HpdPackageScopes.Workspace);

        failed.State.Should().Be(HpdPackageLoadState.Failed);
        packageManager.Packages.Should().ContainSingle(package =>
            package.Id == previous.Id &&
            package.State == HpdPackageLoadState.Enabled &&
            package.Owner == previous.Owner);
        agentContributors.Contributions.Should().ContainSingle(contribution =>
            contribution.Owner == previous.Owner);
        providerContributions.ProviderFactories.Should().ContainSingle(contribution =>
            contribution.Owner == previous.Owner);
        new HpdAgentTuiRegistry(tuiStore).Commands.Should().ContainSingle(command =>
            command.SlashName == "test");
    }

    [Fact]
    public void EnableRegisteredPackages_AppliesRegisteredCoreAndTuiContributions()
    {
        var agentContributors = new AgentBuilderContributorStore();
        var providerContributions = new ProviderContributionStore();
        var packageManager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(agentContributors, providerContributions));
        var tuiStore = new AgentTuiContributionStore();
        using var services = new ServiceCollection().BuildServiceProvider();
        var tuiPackages = new AgentTuiPackageManager(packageManager, tuiStore, services);
        HpdPackageRegistry.Register(new RegisteredTestPackage());

        var loaded = tuiPackages.EnableRegisteredPackages(HpdPackageScopes.App);

        loaded.Should().ContainSingle(package =>
            package.Id == "hpd.test.registered-package" &&
            package.State == HpdPackageLoadState.Enabled);
        providerContributions.ProviderFactories.Should().ContainSingle();
        new HpdAgentTuiRegistry(tuiStore).Commands.Should().ContainSingle(command =>
            command.SlashName == "registered-test");
    }

    [Fact]
    public void Enable_AppliesTuiContributorRegisteredThroughPackageBuilder()
    {
        var agentContributors = new AgentBuilderContributorStore();
        var providerContributions = new ProviderContributionStore();
        var packageManager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(agentContributors, providerContributions));
        var tuiStore = new AgentTuiContributionStore();
        using var services = new ServiceCollection().BuildServiceProvider();
        var tuiPackages = new AgentTuiPackageManager(packageManager, tuiStore, services);

        var loaded = tuiPackages.Enable(new BuilderTuiPackage(), HpdPackageScopes.Workspace);

        loaded.State.Should().Be(HpdPackageLoadState.Enabled);
        loaded.Contributions.RuntimeContributions.Should().Contain("hpd.builder-tui.package.tui");
        new HpdAgentTuiRegistry(tuiStore).CommandContributions.Should().ContainSingle(contribution =>
            contribution.Key == "builder-tui" &&
            contribution.Owner == loaded.Owner);
    }

    [Fact]
    public void Changed_ReportsTuiEnableReloadDisableAndFailure()
    {
        var agentContributors = new AgentBuilderContributorStore();
        var providerContributions = new ProviderContributionStore();
        var packageManager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(agentContributors, providerContributions));
        var tuiStore = new AgentTuiContributionStore();
        using var services = new ServiceCollection().BuildServiceProvider();
        var tuiPackages = new AgentTuiPackageManager(packageManager, tuiStore, services);
        var changes = new List<HpdPackageChangedEventArgs>();
        tuiPackages.Changed += (_, args) => changes.Add(args);

        tuiPackages.Enable(new TestPackage(), HpdPackageScopes.Workspace);
        tuiPackages.Enable(new TestPackage(), HpdPackageScopes.Workspace);
        tuiPackages.Disable("hpd.test.package");
        tuiPackages.Enable(new ThrowingTuiPackage(), HpdPackageScopes.Workspace);

        changes.Select(change => change.Kind).Should().Equal(
            HpdPackageChangeKind.Enabled,
            HpdPackageChangeKind.Reloaded,
            HpdPackageChangeKind.Disabled,
            HpdPackageChangeKind.Failed);
        changes.Last().Package.State.Should().Be(HpdPackageLoadState.Failed);
        changes.Last().Package.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Message.Contains("TUI package activation failed", StringComparison.Ordinal));
    }

    [Fact]
    public void AddPackageManagement_AddsPackagesPageAndCommands()
    {
        var agentContributors = new AgentBuilderContributorStore();
        var providerContributions = new ProviderContributionStore();
        var packageManager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(agentContributors, providerContributions));
        var tuiStore = new AgentTuiContributionStore();
        using var services = new ServiceCollection().BuildServiceProvider();
        var tuiPackages = new AgentTuiPackageManager(packageManager, tuiStore, services);
        tuiPackages.Enable(new TestPackage(), HpdPackageScopes.Workspace);

        var registry = TuiTestBuilder.CreateRegistry(builder =>
            builder.AddPackageManagement(tuiPackages));

        registry.TryFindSlashCommand("/packages", out _, out _).Should().BeTrue();
        registry.TryFindSlashCommand("/package info hpd.test.package", out _, out _).Should().BeTrue();
        registry.TryFindPage(AgentTuiPackageManagement.PackagesPageId, out var page).Should().BeTrue();
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        page.Render(new AgentTuiPageContext(
                scope,
                new ChatShellModel(scope),
                new AgentTuiNavigationModel(),
                registry,
                page,
                height: 10))
            .Should()
            .BeOfType<Markdown>()
            .Subject
            .Source
            .Should()
            .Contain("hpd.test.package")
            .And.Contain(HpdPackageScopes.Workspace)
            .And.Contain("agent: `hpd.test.package.agent`")
            .And.Contain("providers: `test-provider`");
    }

    [Fact]
    public async Task PackageCommand_EnablesAndDisablesRegisteredPackage()
    {
        var agentContributors = new AgentBuilderContributorStore();
        var providerContributions = new ProviderContributionStore();
        var packageManager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(agentContributors, providerContributions));
        var tuiStore = new AgentTuiContributionStore();
        using var services = new ServiceCollection().BuildServiceProvider();
        var tuiPackages = new AgentTuiPackageManager(packageManager, tuiStore, services);
        HpdPackageRegistry.Register(new RegisteredTestPackage());
        var registry = TuiTestBuilder.CreateRegistry(builder =>
            builder.AddPackageManagement(tuiPackages));
        var shell = new ChatShellModel(new AgentTuiRuntimeScope("agent", "session", "main"));

        registry.TryFindSlashCommand("/package enable hpd.test.registered-package", out var command, out var arguments)
            .Should()
            .BeTrue();
        await command.ExecuteAsync(CreateCommandContext(shell, command, arguments));

        tuiPackages.Packages.Should().ContainSingle(package =>
            package.Id == "hpd.test.registered-package" &&
            package.State == HpdPackageLoadState.Enabled);
        shell.Navigation.ActivePageId.Should().Be(AgentTuiPackageManagement.PackagesPageId);

        registry.TryFindSlashCommand("/package disable hpd.test.registered-package", out command, out arguments)
            .Should()
            .BeTrue();
        await command.ExecuteAsync(CreateCommandContext(shell, command, arguments));

        tuiPackages.Packages.Should().BeEmpty();
        shell.Transcript.Snapshot().Entries
            .Any(entry =>
                entry.Cell is NoticeCell notice &&
                notice.Severity == TranscriptSeverity.Success &&
                notice.Title.Contains("Disabled package", StringComparison.Ordinal))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task PackagesCommand_RefreshesRuntimeBeforeNavigating()
    {
        var packages = new RecordingPackageRuntime();
        var registry = TuiTestBuilder.CreateRegistry(builder =>
            builder.AddPackageManagement(packages));
        var shell = new ChatShellModel(new AgentTuiRuntimeScope("agent", "session", "main"));

        registry.TryFindSlashCommand("/packages", out var command, out var arguments)
            .Should()
            .BeTrue();
        await command.ExecuteAsync(CreateCommandContext(shell, command, arguments));

        packages.ListCallCount.Should().Be(1);
        shell.Navigation.ActivePageId.Should().Be(AgentTuiPackageManagement.PackagesPageId);
    }

    private sealed class TestPackage : IHpdPackage, IAgentTuiContributor
    {
        public string Id => "hpd.test.package";

        public HpdPackageManifest Manifest => new(Id, DisplayName, Version)
        {
            Trust = HpdPackageTrust.Trusted,
            LoadMode = HpdPackageLoadMode.BuildTimeInProcess,
            Contributes = new HpdPackageContributes
            {
                Agent = true,
                Tui = true,
                Providers = true
            }
        };

        public string DisplayName => "Test Package";

        public Version Version { get; } = new(1, 2, 3);

        public void Configure(IHpdPackageBuilder builder)
        {
            builder.AddAgentContributor(
                "hpd.test.package.agent",
                new DelegateAgentBuilderContributor(agentBuilder =>
                    agentBuilder.WithProviderRegistry(new ProviderRegistry())));
            builder.AddProviderContributor(new TestProviderContributor());
        }

        public void ConfigureTui(
            HpdAgentTuiBuilder builder,
            HpdPackageContributionContext context)
        {
            builder.AddSlashCommand(new HpdAgentTuiCommandDescriptor("test", _ => { }));
        }
    }

    private sealed class RegisteredTestPackage : IHpdPackage, IAgentTuiContributor
    {
        public string Id => "hpd.test.registered-package";

        public HpdPackageManifest Manifest => new(Id, DisplayName, Version)
        {
            Trust = HpdPackageTrust.Trusted,
            LoadMode = HpdPackageLoadMode.BuildTimeInProcess,
            Contributes = new HpdPackageContributes
            {
                Agent = true,
                Tui = true,
                Providers = true
            }
        };

        public string DisplayName => "Registered Test Package";

        public Version Version { get; } = new(1, 2, 3);

        public void Configure(IHpdPackageBuilder builder)
        {
            builder.AddAgentContributor(
                "hpd.test.registered-package.agent",
                new DelegateAgentBuilderContributor(agentBuilder =>
                    agentBuilder.WithProviderRegistry(new ProviderRegistry())));
            builder.AddProviderContributor(new TestProviderContributor());
        }

        public void ConfigureTui(
            HpdAgentTuiBuilder builder,
            HpdPackageContributionContext context)
        {
            builder.AddSlashCommand(new HpdAgentTuiCommandDescriptor("registered-test", _ => { }));
        }
    }

    private sealed class BuilderTuiPackage : IHpdPackage
    {
        public string Id => "hpd.builder-tui.package";

        public HpdPackageManifest Manifest => new(Id, DisplayName, Version)
        {
            Trust = HpdPackageTrust.Trusted,
            LoadMode = HpdPackageLoadMode.BuildTimeInProcess,
            Contributes = new HpdPackageContributes
            {
                Tui = true
            }
        };

        public string DisplayName => "Builder TUI Package";

        public Version Version { get; } = new(1, 0, 0);

        public void Configure(IHpdPackageBuilder builder)
        {
            builder.AddTuiContributor(
                $"{Id}.tui",
                new BuilderTuiContributor());
        }
    }

    private sealed class BuilderTuiContributor : IAgentTuiContributor
    {
        public void ConfigureTui(
            HpdAgentTuiBuilder builder,
            HpdPackageContributionContext context)
        {
            builder.AddSlashCommand(new HpdAgentTuiCommandDescriptor("builder-tui", _ => { }));
        }
    }

    private sealed class TestProviderContributor : IProviderContributor
    {
        public void ConfigureProviders(
            IProviderContributionBuilder builder,
            HpdProviderContributionContext context)
            => builder.AddProviderFactory("test-provider", _ => new TestProvider());
    }

    private sealed class ThrowingTuiPackage : IHpdPackage, IAgentTuiContributor
    {
        public string Id => "hpd.bad-tui.package";

        public HpdPackageManifest Manifest => new(Id, DisplayName, Version)
        {
            Trust = HpdPackageTrust.Trusted,
            LoadMode = HpdPackageLoadMode.BuildTimeInProcess,
            Contributes = new HpdPackageContributes
            {
                Agent = true,
                Tui = true,
                Providers = true
            }
        };

        public string DisplayName => "Bad TUI Package";

        public Version Version { get; } = new(1, 0, 0);

        public void Configure(IHpdPackageBuilder builder)
        {
            builder.AddAgentContributor(
                "hpd.bad-tui.package.agent",
                new DelegateAgentBuilderContributor(_ => { }));
            builder.AddProviderContributor(new TestProviderContributor());
        }

        public void ConfigureTui(
            HpdAgentTuiBuilder builder,
            HpdPackageContributionContext context)
        {
            builder.AddSlashCommand(new HpdAgentTuiCommandDescriptor("bad", _ => { }));
            throw new InvalidOperationException("nope");
        }
    }

    private sealed class ThrowingReplacementTuiPackage : IHpdPackage, IAgentTuiContributor
    {
        public string Id => "hpd.test.package";

        public HpdPackageManifest Manifest => new(Id, DisplayName, Version)
        {
            Trust = HpdPackageTrust.Trusted,
            LoadMode = HpdPackageLoadMode.BuildTimeInProcess,
            Contributes = new HpdPackageContributes
            {
                Agent = true,
                Tui = true,
                Providers = true
            }
        };

        public string DisplayName => "Bad Replacement TUI Package";

        public Version Version { get; } = new(9, 0, 0);

        public void Configure(IHpdPackageBuilder builder)
        {
            builder.AddAgentContributor(
                "hpd.test.package.replacement-agent",
                new DelegateAgentBuilderContributor(_ => { }));
            builder.AddProviderContributor(new TestProviderContributor());
        }

        public void ConfigureTui(
            HpdAgentTuiBuilder builder,
            HpdPackageContributionContext context)
        {
            builder.AddSlashCommand(new HpdAgentTuiCommandDescriptor("replacement", _ => { }));
            throw new InvalidOperationException("nope");
        }
    }

    private sealed class TestProvider : IProvider
    {
        public string ProviderKey => "test-provider";

        public string DisplayName => "Test Provider";

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

    private static AgentTuiCommandContext CreateCommandContext(
        ChatShellModel shell,
        HpdAgentTuiCommandDescriptor command,
        string arguments)
        => new(
            shell.Scope,
            shell,
            shell.Navigation,
            new NoopRuntime(),
            NoopDialogs.Instance,
            TuiTestBuilder.NoopSessionUi,
            static (_, _) => ValueTask.CompletedTask,
            command,
            arguments);

    private sealed class RecordingPackageRuntime : IHpdPackageRuntime
    {
        public event EventHandler<HpdPackageChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public int ListCallCount { get; private set; }

        public IReadOnlyList<HpdLoadedPackage> Packages { get; private set; } = [];

        public ValueTask<IReadOnlyList<HpdLoadedPackage>> ListAsync(
            CancellationToken cancellationToken = default)
        {
            ListCallCount++;
            return ValueTask.FromResult(Packages);
        }

        public ValueTask<HpdLoadedPackage> EnableRegisteredAsync(
            string packageId,
            string scope = HpdPackageScopes.App,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<HpdLoadedPackage> ReloadRegisteredAsync(
            string packageId,
            string scope = HpdPackageScopes.App,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<bool> DisableAsync(
            string packageId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NoopRuntime : IHpdAgentTuiRuntime
    {
        public Task<AgentTuiScopeResolution> ResolveInitialScopeAsync(
            AgentTuiRuntimeScope? requested,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiScopeResolution(
                requested ?? new AgentTuiRuntimeScope("agent", "session", "main"),
                IsDurable: true));

        public Task<AgentTuiRuntimeScope> EnsureDurableScopeAsync(
            AgentTuiRuntimeScope scope,
            CancellationToken cancellationToken = default)
            => Task.FromResult(scope);

        public async IAsyncEnumerable<AgentEvent> ObserveAsync(
            AgentTuiRuntimeScope scope,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task SubmitInputAsync(
            AgentTuiRuntimeScope scope,
            AgentInputEvent input,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task InterruptAsync(
            AgentTuiRuntimeScope scope,
            string reason,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RespondAsync(
            AgentTuiRuntimeScope scope,
            AgentEvent response,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<AgentEvent>> GetThreadEventsAsync(
            AgentTuiRuntimeScope scope,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentEvent>>([]);

        public Task<AgentTuiThreadRun?> GetActiveRunAsync(
            AgentTuiRuntimeScope scope,
            CancellationToken cancellationToken = default)
            => Task.FromResult<AgentTuiThreadRun?>(null);
    }

    private sealed class NoopDialogs : IAgentTuiDialogService
    {
        public static NoopDialogs Instance { get; } = new();

        public bool HasOpenDialog => false;

        public Task<TResult?> ShowAsync<TResult>(
            string key,
            Func<AgentTuiDialogContext<TResult>, IComponent> componentFactory,
            CancellationToken cancellationToken = default)
            => Task.FromResult<TResult?>(default);

        public bool Close(string key) => false;

        public bool CloseTop() => false;

        public Task<bool?> ConfirmAsync(
            string title,
            bool? defaultValue = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<bool?>(defaultValue);

        public Task<T?> SelectAsync<T>(
            string title,
            IReadOnlyList<T> options,
            Func<T, string> titleSelector,
            CancellationToken cancellationToken = default)
            => Task.FromResult(options.Count > 0 ? options[0] : default);

        public Task<string?> InputAsync(
            string title,
            string? defaultValue = null,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(defaultValue);

        public Task<string?> SecretInputAsync(
            string title,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }
}
