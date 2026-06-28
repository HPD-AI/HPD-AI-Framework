using FluentAssertions;
using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Views;
using HPD.TUI.Components;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Tests;

public sealed class AgentTuiContributionStoreTests
{
    [Fact]
    public void Builder_RecordsOwnersInRegistrySnapshot()
    {
        var store = new AgentTuiContributionStore();
        var owner = new HpdContributionOwner("hpd.test.package", "test", "1.2.3", "Test Package");
        var builder = new HpdAgentTuiBuilder(store, owner);

        builder
            .AddSlashCommand(new HpdAgentTuiCommandDescriptor("sample", _ => { }))
            .AddPage("sample.page", _ => new Text("page"))
            .AddStatusItem("sample.status", new TestStatusItem())
            .AddWidget(TuiSlot.AboveEditor, "sample.widget", new TestWidget());

        var registry = builder.Build();

        registry.CommandContributions.Should().ContainSingle(contribution =>
            contribution.Key == "sample" && contribution.Owner == owner);
        registry.PageContributions.Should().ContainSingle(contribution =>
            contribution.Key == "sample.page" && contribution.Owner == owner);
        registry.StatusItems.Should().ContainSingle(contribution =>
            contribution.Key == "sample.status" && contribution.Owner == owner);
        registry.AboveEditorWidgets.Should().ContainSingle(contribution =>
            contribution.Key == "sample.widget" && contribution.Owner == owner);
    }

    [Fact]
    public void RegistryProvider_RebuildsSnapshotWhenStoreChanges()
    {
        var store = new AgentTuiContributionStore();
        var owner = new HpdContributionOwner("hpd.test.package", "test");
        using var provider = new HpdAgentTuiRegistryProvider(store);
        var builder = new HpdAgentTuiBuilder(store, owner);
        HpdAgentTuiRegistryChangedEventArgs? changed = null;
        provider.Changed += (_, args) => changed = args;

        builder.AddSlashCommand(new HpdAgentTuiCommandDescriptor("sample", _ => { }));

        changed.Should().NotBeNull();
        changed!.Kind.Should().Be(AgentTuiContributionChangeKind.Command);
        changed.RequiresShellRebuild.Should().BeFalse();
        changed.Owners.Should().ContainSingle().Which.Should().Be(owner);
        provider.Current.Commands.Should().ContainSingle(command => command.SlashName == "sample");
    }

    [Fact]
    public void RegistryProvider_MarksStructuralChangesAsShellRebuilds()
    {
        var store = new AgentTuiContributionStore();
        var owner = new HpdContributionOwner("hpd.test.package", "test");
        using var provider = new HpdAgentTuiRegistryProvider(store);
        var builder = new HpdAgentTuiBuilder(store, owner);
        var changes = new List<HpdAgentTuiRegistryChangedEventArgs>();
        provider.Changed += (_, args) => changes.Add(args);

        builder.AddWidget(TuiSlot.BelowEditor, "sample.widget", new TestWidget());

        changes.Should().ContainSingle();
        changes[0].Kind.Should().Be(AgentTuiContributionChangeKind.Widget);
        changes[0].RequiresShellRebuild.Should().BeTrue();
        provider.Current.BelowEditorWidgets.Should().ContainSingle(contribution => contribution.Key == "sample.widget");
    }

    [Fact]
    public void RemoveWidget_UpdatesRegistryProvider()
    {
        var store = new AgentTuiContributionStore();
        var owner = new HpdContributionOwner("hpd.test.package", "test");
        using var provider = new HpdAgentTuiRegistryProvider(store);
        var builder = new HpdAgentTuiBuilder(store, owner);
        builder.AddWidget(TuiSlot.BelowEditor, "sample.widget", new TestWidget());
        var changes = new List<HpdAgentTuiRegistryChangedEventArgs>();
        provider.Changed += (_, args) => changes.Add(args);

        builder.RemoveWidget(TuiSlot.BelowEditor, "sample.widget");

        changes.Should().ContainSingle();
        changes[0].Kind.Should().Be(AgentTuiContributionChangeKind.Widget);
        changes[0].RequiresShellRebuild.Should().BeTrue();
        provider.Current.BelowEditorWidgets.Should().BeEmpty();
    }

    [Fact]
    public void RunConfigContributor_UpdatesRegistryProviderWithoutShellRebuild()
    {
        var store = new AgentTuiContributionStore();
        var owner = new HpdContributionOwner("hpd.test.package", "test");
        using var provider = new HpdAgentTuiRegistryProvider(store);
        var builder = new HpdAgentTuiBuilder(store, owner);
        HpdAgentTuiRegistryChangedEventArgs? changed = null;
        provider.Changed += (_, args) => changed = args;

        builder.AddRunConfigContributor("sample.run-config", (_, runConfig) =>
            runConfig.SetProviderModel("openrouter", "model-a"));

        changed.Should().NotBeNull();
        changed!.Kind.Should().Be(AgentTuiContributionChangeKind.RunConfigContributor);
        changed.RequiresShellRebuild.Should().BeFalse();
        provider.Current.RunConfigContributors.Should().ContainSingle(contribution =>
            contribution.Key == "sample.run-config" && contribution.Owner == owner);
    }

    [Fact]
    public void RemoveOwner_RemovesOwnedContributionsAndFreesShortcutGestures()
    {
        var store = new AgentTuiContributionStore();
        var owner = new HpdContributionOwner("hpd.test.package", "test");
        var otherOwner = new HpdContributionOwner("hpd.other.package", "test");
        var gesture = new KeyGesture(KeyCode.Enter, KeyModifiers.Ctrl);
        var builder = new HpdAgentTuiBuilder(store, owner);
        var otherBuilder = new HpdAgentTuiBuilder(store, otherOwner);

        builder
            .AddSlashCommand(new HpdAgentTuiCommandDescriptor("sample", _ => { }))
            .AddWidget(TuiSlot.BelowEditor, "sample.widget", new TestWidget())
            .AddShortcut(new HpdAgentTuiShortcutDescriptor("sample.shortcut", gesture, _ => { }))
            .AddRunConfigContributor("sample.run-config", (_, runConfig) =>
                runConfig.SetProviderModel("openrouter", "model-a"))
            .AddHeader(_ => new Text("owned header"));
        otherBuilder.AddSlashCommand(new HpdAgentTuiCommandDescriptor("other", _ => { }));

        store.RemoveOwner(owner).Should().BeTrue();

        var registry = new HpdAgentTuiRegistry(store);
        registry.Commands.Should().ContainSingle(command => command.SlashName == "other");
        registry.BelowEditorWidgets.Should().BeEmpty();
        registry.Shortcuts.Should().BeEmpty();
        registry.RunConfigContributors.Should().BeEmpty();
        registry.Header.Should().BeNull();
        store.Owners.Should().ContainSingle().Which.Should().Be(otherOwner);

        new HpdAgentTuiBuilder(store, otherOwner)
            .AddShortcut(new HpdAgentTuiShortcutDescriptor("other.shortcut", gesture, _ => { }));
        new HpdAgentTuiRegistry(store).Shortcuts.Should().ContainSingle(shortcut => shortcut.Key == "other.shortcut");
    }

    [Fact]
    public void RemoveOwner_NotifiesRegistryProviderWithStructuralChange()
    {
        var store = new AgentTuiContributionStore();
        var owner = new HpdContributionOwner("hpd.test.package", "test");
        using var provider = new HpdAgentTuiRegistryProvider(store);
        var builder = new HpdAgentTuiBuilder(store, owner);
        builder.AddSlashCommand(new HpdAgentTuiCommandDescriptor("sample", _ => { }));
        HpdAgentTuiRegistryChangedEventArgs? changed = null;
        provider.Changed += (_, args) => changed = args;

        store.RemoveOwner(owner).Should().BeTrue();

        changed.Should().NotBeNull();
        changed!.Kind.Should().Be(AgentTuiContributionChangeKind.OwnerRemoved);
        changed.RequiresShellRebuild.Should().BeTrue();
        changed.Owners.Should().ContainSingle().Which.Should().Be(owner);
        provider.Current.Commands.Should().BeEmpty();
    }

    private sealed class TestStatusItem : IAgentTuiStatusItem
    {
        public IComponent Create(AgentTuiStatusContext context) => new Text("status");
    }

    private sealed class TestWidget : IAgentTuiWidget
    {
        public IComponent Create(AgentTuiWidgetContext context) => new Text("widget");
    }
}
