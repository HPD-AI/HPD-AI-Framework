using FluentAssertions;
using HPD.Environment.Contracts;
using HPD.Environment.HostProcess;
using HPD.Environment.Runtime;
using Xunit;

namespace HPD.Environment.HostProcess.Tests.Projections;

public sealed class HostProcessContentProjectionProviderTests
{
    [Fact]
    public async Task Authorized_host_path_projects_at_its_effective_source_path()
    {
        string source = CreateTemporaryDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(source, "file.txt"),
            "content");
        try
        {
            await using ProjectionFixture fixture =
                await ProjectionFixture.CreateAsync();
            ContentProjectionStatus status =
                await fixture.Provider.ProjectAsync(
                    Metadata(),
                    Spec(source),
                    fixture.Host,
                    fixture.Unit);

            status.ProjectionPhase.Should().Be(
                ContentProjectionPhase.Projected);
            status.ProviderHandle.Should().NotBeNull();
            RealizedProjectionView view =
                status.Views.Should().ContainSingle().Subject;
            view.GuestPath.Should().Be(new GuestPath(source));
            view.EffectiveWriteEffect.Should().Be(
                ProjectionWriteEffect.NoWrites);
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public async Task Destination_remapping_fails_closed()
    {
        string source = CreateTemporaryDirectory();
        try
        {
            await using ProjectionFixture fixture =
                await ProjectionFixture.CreateAsync();
            ContentProjectionSpec spec = Spec(source) with
            {
                View = new ProjectionView
                {
                    Kind = ProjectionViewKind.FilesystemTree,
                    GuestPath = new GuestPath("/different/location"),
                },
            };

            ContentProjectionStatus status =
                await fixture.Provider.ProjectAsync(
                    Metadata(),
                    spec,
                    fixture.Host,
                    fixture.Unit);

            status.ProjectionPhase.Should().Be(
                ContentProjectionPhase.Failed);
            status.Diagnostics.Should().ContainSingle(
                diagnostic => diagnostic.Message.Contains(
                    "cannot remap",
                    StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public async Task Required_projection_level_read_only_enforcement_fails_closed()
    {
        string source = CreateTemporaryDirectory();
        try
        {
            await using ProjectionFixture fixture =
                await ProjectionFixture.CreateAsync();
            ContentProjectionSpec spec = Spec(source) with
            {
                SecurityPolicy = new ContentProjectionSecurityPolicy
                {
                    AllowHostPathSource = true,
                    ReadOnlyEnforcement =
                        ReadOnlyEnforcementPolicy.Required,
                },
            };

            ContentProjectionStatus status =
                await fixture.Provider.ProjectAsync(
                    Metadata(),
                    spec,
                    fixture.Host,
                    fixture.Unit);

            status.ProjectionPhase.Should().Be(
                ContentProjectionPhase.Failed);
            status.Diagnostics.Should().ContainSingle(
                diagnostic => diagnostic.Message.Contains(
                    "cannot prove",
                    StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public async Task Direct_source_mutation_requires_explicit_authorization()
    {
        string source = CreateTemporaryDirectory();
        try
        {
            await using ProjectionFixture fixture =
                await ProjectionFixture.CreateAsync();
            ContentProjectionSpec spec = Spec(source) with
            {
                AccessMode = AccessMode.ReadWrite,
            };

            ContentProjectionStatus status =
                await fixture.Provider.ProjectAsync(
                    Metadata(),
                    spec,
                    fixture.Host,
                    fixture.Unit);

            status.ProjectionPhase.Should().Be(
                ContentProjectionPhase.Failed);
            status.Diagnostics.Should().ContainSingle(
                diagnostic => diagnostic.Message.Contains(
                    "direct-source mutation",
                    StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    private static ContentProjectionSpec Spec(string source) =>
        new()
        {
            Source = new ContentSelector
            {
                Kind = ContentSelectorKind.HostPath,
                HostPath = new HostPathSelection(
                    new HostPath(source),
                    HostPathKind.Directory),
            },
            Target = new ContentProjectionTarget(),
            View = new ProjectionView
            {
                Kind = ProjectionViewKind.FilesystemTree,
                GuestPath = new GuestPath(source),
            },
            Role = ContentProjectionRole.Workspace,
            SecurityPolicy = new ContentProjectionSecurityPolicy
            {
                AllowHostPathSource = true,
                ReadOnlyEnforcement =
                    ReadOnlyEnforcementPolicy.BestEffort,
            },
        };

    private static ResourceMetadata<ContentProjection> Metadata() =>
        new()
        {
            Id = new ResourceId<ContentProjection>(
                "projection-" + Guid.NewGuid().ToString("N")),
            Kind = new ResourceKind("ContentProjection"),
            Scope = new ResourceScope("host-process-tests"),
            Generation = new ResourceGeneration(1),
            SchemaVersion = new SchemaVersion("1"),
        };

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "hpd-host-process-projection-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class ProjectionFixture : IAsyncDisposable
    {
        private readonly HostProcessProviderModule _module;
        private readonly InMemoryEnvironmentRuntime _runtime;
        private readonly ResourceRef<ExecutionUnit> _unitRef;

        private ProjectionFixture(
            HostProcessProviderModule module,
            InMemoryEnvironmentRuntime runtime,
            IContentProjectionProvider provider,
            TargetHandle<RuntimeHost> host,
            TargetHandle<ExecutionUnit> unit,
            ResourceRef<ExecutionUnit> unitRef)
        {
            _module = module;
            _runtime = runtime;
            Provider = provider;
            Host = host;
            Unit = unit;
            _unitRef = unitRef;
        }

        public IContentProjectionProvider Provider { get; }

        public TargetHandle<RuntimeHost> Host { get; }

        public TargetHandle<ExecutionUnit> Unit { get; }

        public static async Task<ProjectionFixture> CreateAsync()
        {
            var module = new HostProcessProviderModule();
            var registry = new EnvironmentProviderRegistry();
            registry.RegisterModule(module);
            var runtime = new InMemoryEnvironmentRuntime(registry);
            ResourceSnapshot<
                RuntimeHost,
                RuntimeHostSpec,
                RuntimeHostStatus> host =
                await runtime.EnsureHostAsync(new RuntimeHostSpec
                {
                    PreferredProvider =
                        HostProcessProviderDescriptor.ProviderId,
                    Platform =
                        HostProcessProviderDescriptor.CurrentPlatform(),
                });
            ResourceSnapshot<
                ExecutionUnit,
                ExecutionUnitSpec,
                ExecutionUnitStatus> unit =
                await runtime.EnsureExecutionUnitAsync(
                    new ExecutionUnitSpec
                    {
                        PreferredHost = new ResourceRef<RuntimeHost>(
                            host.Metadata.Id,
                            host.Metadata.Scope,
                            host.Metadata.Generation),
                    });
            return new ProjectionFixture(
                module,
                runtime,
                registry.ContentProjectionProviders.Single(),
                host.Status.Handle!.Value,
                unit.Status.Handle!.Value,
                new ResourceRef<ExecutionUnit>(
                    unit.Metadata.Id,
                    unit.Metadata.Scope,
                    unit.Metadata.Generation));
        }

        public async ValueTask DisposeAsync()
        {
            await _runtime.DeleteExecutionUnitAsync(_unitRef);
            await _runtime.DeleteHostAsync();
            await _module.DisposeAsync();
        }
    }
}
