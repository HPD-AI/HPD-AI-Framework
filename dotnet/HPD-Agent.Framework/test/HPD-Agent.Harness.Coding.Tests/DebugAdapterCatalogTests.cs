using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Agent.ToolHarness.Coding.Debugging.Attributes;
using HPD.Agent.ToolHarness.Coding.Debugging.Adapters;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.ToolHarness.Coding.Tests;

[HpdDebugAdapter("constructor-injected-test")]
[DebugAdapterLanguages("test")]
[DebugAdapterFileExtensions(".test")]
[DebugAdapterRootMarkers("test.project")]
[DebugAdapterTargetKinds(DebugTargetKind.SourceFile)]
[DebugAdapterFactory(typeof(ConstructorInjectedTestFactory))]
public sealed class ConstructorInjectedTestAdapterDeclaration;

internal sealed class ConstructorInjectedTestFactory(string dependency) : IDebugAdapterFactory
{
    public string Dependency { get; } = dependency;

    public ValueTask<DebugAdapterAvailability> ProbeAsync(DebugAdapterDescriptor descriptor, DebugAdapterResolutionContext context, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new DebugAdapterAvailability(DebugAdapterAvailabilityKind.Available));

    public ValueTask<DebugAdapterLaunchPlan> CreateLaunchPlanAsync(DebugAdapterDescriptor descriptor, DebugLaunchContext context, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public ValueTask<DebugAdapterLaunchPlan> CreateAttachPlanAsync(DebugAdapterDescriptor descriptor, DebugAttachContext context, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

public sealed class DebugAdapterCatalogTests
{
    [Fact]
    public void Built_in_catalog_contains_expected_stable_metadata()
    {
        BuiltInDebugAdapterCatalog.Entries.Should().HaveCount(8);
        var netCore = BuiltInDebugAdapterCatalog.Entries.Single(entry => entry.Descriptor.Id == "netcoredbg");

        netCore.Descriptor.Languages.Should().Contain(["csharp", "fsharp"]);
        netCore.Descriptor.TargetKinds.Should().HaveFlag(DebugTargetKind.Process);
        netCore.Descriptor.CommandHints.Should().Equal("netcoredbg");
        netCore.Descriptor.ArgumentHints.Should().Equal("--interpreter=vscode");
        netCore.Descriptor.Provenance.AssemblyName.Should().Be("HPD-Agent.Harness.Coding");
    }

    [Fact]
    public void Built_in_catalog_preserves_reviewed_real_adapter_defaults()
    {
        var entries = BuiltInDebugAdapterCatalog.Entries.ToDictionary(entry => entry.Descriptor.Id);

        entries["gdb"].Descriptor.ArgumentHints.Should().Equal("-i", "dap");
        entries["lldb-dap"].Descriptor.Languages.Should().Contain("zig");
        entries["lldb-dap"].Descriptor.RootMarkers.Should().Contain("build.zig");
        entries["codelldb"].Descriptor.ArgumentHints.Should().Equal("--port", "0");
        entries["delve"].Descriptor.RootMarkers.Should().Contain(["go.mod", "go.sum", "go.work"]);
        entries["javascript"].Descriptor.RootMarkers.Should().Contain(["package.json", "tsconfig.json", "jsconfig.json"]);
        entries["debugpy"].Descriptor.ArgumentHints.Should().Equal("-m", "debugpy.adapter");
        entries["rdbg"].Descriptor.RootMarkers.Should().Contain("Rakefile");
    }

    [Fact]
    public void Behavioral_factory_resolver_uses_the_DI_owned_constructor_injected_instance()
    {
        var expected = new ConstructorInjectedTestFactory("injected");
        var provider = new HPD.Agent.ToolHarness.Coding.Debugging.Generated.GeneratedDebugAdapterCatalogProvider_HPD_Agent_Harness_Coding_Tests();
        var entry = provider.GetEntries().Single();

        var resolved = entry.FactoryResolver(new SingleServiceProvider(expected));

        resolved.Should().BeSameAs(expected);
        ((ConstructorInjectedTestFactory)resolved).Dependency.Should().Be("injected");
        entry.Descriptor.CommandHints.Should().BeEmpty();
    }

    [Fact]
    public void Catalog_rejects_duplicate_ids_with_package_provenance()
    {
        var descriptor = new DebugAdapterDescriptor
        {
            Id = "duplicate",
            Languages = ["test"],
            FileExtensions = [".test"],
            RootMarkers = [],
            TargetKinds = DebugTargetKind.SourceFile,
            Provenance = new DebugAdapterProvenance { PackageId = "package-a", PackageVersion = "1", AssemblyName = "a" }
        };
        var first = Entry(descriptor);
        var second = Entry(descriptor with { Provenance = descriptor.Provenance with { PackageId = "package-b" } });

        var action = () => new DebugAdapterCatalog(
            [new FixedProvider(first), new FixedProvider(second)],
            new SingleServiceProvider(new StandardDebugAdapterFactory(new UnavailableToolResolver())));

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*duplicate*package-a*package-b*");
    }

    [Fact]
    public void Catalog_materialization_validates_and_retains_DI_factories()
    {
        var expected = new StandardDebugAdapterFactory(new UnavailableToolResolver());
        var services = new ServiceCollection()
            .AddSingleton(expected)
            .AddSingleton<DebugPyAdapterFactory>()
            .AddSingleton<CodeLldbAdapterFactory>()
            .AddSingleton<DelveAdapterFactory>()
            .AddSingleton<JavaScriptDebugAdapterFactory>()
            .BuildServiceProvider();
        var catalog = new DebugAdapterCatalog(
            [BuiltInDebugAdapterCatalog.CreateProvider()],
            services);

        catalog.Entries.Should().HaveCount(8);
        catalog.GetFactory("gdb").Should().BeSameAs(expected);
        catalog.GetFactory("debugpy").Should().BeOfType<DebugPyAdapterFactory>();
    }

    [Fact]
    public void Catalog_materialization_snapshots_mutable_provider_metadata()
    {
        var languages = new[] { "before" };
        var descriptor = new DebugAdapterDescriptor
        {
            Id = "snapshot",
            Languages = languages,
            FileExtensions = [".before"],
            RootMarkers = [],
            TargetKinds = DebugTargetKind.SourceFile,
            Provenance = new() { PackageId = "snapshot", PackageVersion = "1", AssemblyName = "snapshot" }
        };
        var catalog = new DebugAdapterCatalog(
            [new FixedProvider(Entry(descriptor))],
            new SingleServiceProvider(new StandardDebugAdapterFactory(new UnavailableToolResolver())));

        languages[0] = "after";

        catalog.Entries.Single().Descriptor.Languages.Should().Equal("before");
        catalog.Entries.Single().Descriptor.Languages.Should().NotBeOfType<string[]>();
    }

    [Fact]
    public void External_factory_failure_is_fatal_by_default()
    {
        var entry = Entry(ExternalDescriptor("broken")) with
        {
            FactoryResolver = static _ => throw new InvalidOperationException("broken resolver")
        };

        var action = () => new DebugAdapterCatalog(
            [new FixedProvider(entry)],
            new SingleServiceProvider(new object()));

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*broken*external.package*");
    }

    [Fact]
    public void Host_policy_can_disable_broken_external_entry_with_bounded_diagnostic()
    {
        var entry = Entry(ExternalDescriptor("optional")) with
        {
            FactoryResolver = static _ => throw new InvalidOperationException("broken resolver")
        };

        var catalog = new DebugAdapterCatalog(
            [new FixedProvider(entry)],
            new SingleServiceProvider(new object()),
            new DisableExternalFailuresPolicy());

        catalog.Entries.Should().BeEmpty();
        catalog.Diagnostics.Should().ContainSingle().Which.Should().Be(
            new DebugAdapterCatalogDiagnostic("optional", "external.package", "EXTERNAL_FACTORY_RESOLUTION_FAILED"));
    }

    private static DebugAdapterDescriptor ExternalDescriptor(string id) => new()
    {
        Id = id,
        Languages = ["test"],
        FileExtensions = [".test"],
        RootMarkers = [],
        TargetKinds = DebugTargetKind.SourceFile,
        Provenance = new() { PackageId = "external.package", PackageVersion = "1", AssemblyName = "External.Debug.Package" }
    };

    private static DebugAdapterCatalogEntry Entry(DebugAdapterDescriptor descriptor) => new()
    {
        Descriptor = descriptor,
        FactoryResolver = static _ => new StandardDebugAdapterFactory(new UnavailableToolResolver())
    };

    private sealed class FixedProvider(params DebugAdapterCatalogEntry[] entries) : IDebugAdapterCatalogProvider
    {
        public IEnumerable<DebugAdapterCatalogEntry> GetEntries() => entries;
    }

    private sealed class SingleServiceProvider(object service) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType.IsInstanceOfType(service) ? service : null;
    }

    private sealed class UnavailableToolResolver : IDebugAdapterToolResolver
    {
        public ValueTask<DebugAdapterToolResolution> ResolveAsync(DebugAdapterDescriptor descriptor, DebugAdapterResolutionContext context, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new DebugAdapterToolResolution(false));
    }

    private sealed class DisableExternalFailuresPolicy : IDebugAdapterCatalogFailurePolicy
    {
        public DebugAdapterCatalogFailureAction OnFactoryResolutionFailure(DebugAdapterDescriptor descriptor, Exception exception)
            => DebugAdapterCatalogFailureAction.DisableExternalEntry;
    }
}
