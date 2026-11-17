using HPD.AI.Platform.Studio;
using Xunit;

namespace HPD.AI.Platform.Tests;

public sealed class StudioPresentationContractTests
{
    [Fact]
    public void Read_only_grid_and_preference_free_view_are_canonical()
    {
        StudioGridDefinition first = CreateReadOnlyGrid();
        StudioGridDefinition second = CreateReadOnlyGrid();
        StudioPreferenceSchema preferences = StudioPreferenceSchema.Create(
            "base.records.preferences", 1, [], 1, TimeSpan.FromDays(1));
        StudioViewPresentationRegistration view = StudioViewPresentationRegistration.Create(
            "base.records.list", first, null, StudioEmptyStateKind.NoItems,
            StudioActivityPolicy.Create(StudioActivityPolicyKind.GovernedInvalidationRefresh, 10, 3, 32),
            preferences);

        Assert.Empty(first.RowCommandIds);
        Assert.Empty(preferences.Allowed);
        Assert.True(StudioSha256.FixedTimeEquals(first.Checksum, second.Checksum));
        Assert.NotNull(view.Grid);
    }

    [Fact]
    public void Finite_observation_view_needs_no_grid_or_chart()
    {
        StudioViewPresentationRegistration view = StudioViewPresentationRegistration.Create(
            "base.health.summary", null, null, StudioEmptyStateKind.NotConfigured,
            StudioActivityPolicy.Create(StudioActivityPolicyKind.ExplicitRefreshOnly, 1, 1, 1),
            StudioPreferenceSchema.Create("base.health.preferences", 1, [], 1, TimeSpan.FromDays(1)));

        Assert.Null(view.Grid);
        Assert.Null(view.Chart);
    }

    [Fact]
    public void Caller_order_is_validated_and_never_silently_sorted()
    {
        StudioGridColumnDefinition second = CreateColumn("second", 1);
        StudioGridColumnDefinition first = CreateColumn("first", 0);

        Assert.Throws<ArgumentException>(() => StudioGridDefinition.Create(
            "base.records.grid", 1, StudioResourceKind.Record, "base.record.row", Digest(7),
            [second, first], StudioSelectionMode.None, [], 100, 25, 1_000, 1_000_000));
        Assert.Throws<ArgumentException>(() => StudioPreferenceSchema.Create(
            "base.preferences", 1, [StudioPreferenceKind.Density, StudioPreferenceKind.Theme],
            64_000, TimeSpan.FromDays(180)));
    }

    [Fact]
    public void Page_workspace_invariants_fail_closed()
    {
        StudioSectionRegistration section = StudioSectionRegistration.Create(
            "summary", "studio.section.summary", 0, StudioSectionKind.Summary, [], []);

        Assert.Throws<ArgumentException>(() => StudioPagePresentationRegistration.Create(
            "base.records", 1, StudioNavigationRole.Contextual, StudioWorkspaceKind.ResourceMasterDetail,
            [section], null, null, StudioDraftRetentionClass.None));
        Assert.Throws<ArgumentException>(() => StudioPagePresentationRegistration.Create(
            "base.records", 1, StudioNavigationRole.Contextual, StudioWorkspaceKind.Detail,
            [section], StudioResourceRailRegistration.Create(
                "collections", "base.collections.list", StudioResourceKind.Collection,
                StudioRailSearchKind.RegisteredView, StudioPinClass.NonsecretIdentityAndSafeLabel,
                280, 200, 600), null, StudioDraftRetentionClass.None));
    }

    [Fact]
    public void Mutable_checksum_inputs_are_deeply_owned()
    {
        byte[] row = Enumerable.Repeat((byte)4, 32).ToArray();
        StudioSha256 input = StudioSha256.Compute(row);
        StudioGridDefinition grid = StudioGridDefinition.Create(
            "base.records.grid", 1, StudioResourceKind.Record, "base.record.row", input,
            [CreateColumn("identity", 0)], StudioSelectionMode.None, [], 100, 25, 1_000, 1_000_000);

        row[0] = 99;
        Assert.True(StudioSha256.FixedTimeEquals(grid.RowNodeChecksum, Digest(4)));
    }

    private static StudioGridDefinition CreateReadOnlyGrid() => StudioGridDefinition.Create(
        "base.records.grid", 1, StudioResourceKind.Record, "base.record.row", Digest(7),
        [CreateColumn("identity", 0)], StudioSelectionMode.None, [], 100, 25, 1_000, 1_000_000);

    private static StudioGridColumnDefinition CreateColumn(string id, int order) =>
        StudioGridColumnDefinition.Create(id, $"base.record.{id}", StudioGridRendererKind.IdentityLink,
            StudioGridDisclosureBehavior.SafeLabelOnly, $"studio.column.{id}", true,
            order, 240, 160, 600);

    private static StudioSha256 Digest(byte value) =>
        StudioSha256.Compute(Enumerable.Repeat(value, 32).ToArray());
}
