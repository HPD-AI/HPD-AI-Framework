using HPD.AI.Platform.Studio;
using Xunit;

namespace HPD.AI.Platform.Tests;

public sealed class BaseStudioPresentationContractTests
{
    [Fact]
    public void Read_only_grid_and_preference_free_view_are_canonical()
    {
        BaseStudioGridDefinition first = CreateReadOnlyGrid();
        BaseStudioGridDefinition second = CreateReadOnlyGrid();
        BaseStudioPreferenceSchema preferences = BaseStudioPreferenceSchema.Create(
            "base.records.preferences", 1, [], 1, TimeSpan.FromDays(1));
        BaseStudioViewPresentationRegistration view = BaseStudioViewPresentationRegistration.Create(
            "base.records.list", first, null, BaseStudioEmptyStateKind.NoItems,
            BaseStudioActivityPolicy.Create(BaseStudioActivityPolicyKind.GovernedInvalidationRefresh, 10, 3, 32),
            preferences);

        Assert.Empty(first.RowCommandIds);
        Assert.Empty(preferences.Allowed);
        Assert.True(BaseStudioSha256.FixedTimeEquals(first.Checksum, second.Checksum));
        Assert.NotNull(view.Grid);
    }

    [Fact]
    public void Finite_observation_view_needs_no_grid_or_chart()
    {
        BaseStudioViewPresentationRegistration view = BaseStudioViewPresentationRegistration.Create(
            "base.health.summary", null, null, BaseStudioEmptyStateKind.NotConfigured,
            BaseStudioActivityPolicy.Create(BaseStudioActivityPolicyKind.ExplicitRefreshOnly, 1, 1, 1),
            BaseStudioPreferenceSchema.Create("base.health.preferences", 1, [], 1, TimeSpan.FromDays(1)));

        Assert.Null(view.Grid);
        Assert.Null(view.Chart);
    }

    [Fact]
    public void Caller_order_is_validated_and_never_silently_sorted()
    {
        BaseStudioGridColumnDefinition second = CreateColumn("second", 1);
        BaseStudioGridColumnDefinition first = CreateColumn("first", 0);

        Assert.Throws<ArgumentException>(() => BaseStudioGridDefinition.Create(
            "base.records.grid", 1, BaseStudioResourceKind.Record, "base.record.row", Digest(7),
            [second, first], BaseStudioSelectionMode.None, [], 100, 25, 1_000, 1_000_000));
        Assert.Throws<ArgumentException>(() => BaseStudioPreferenceSchema.Create(
            "base.preferences", 1, [BaseStudioPreferenceKind.Density, BaseStudioPreferenceKind.Theme],
            64_000, TimeSpan.FromDays(180)));
    }

    [Fact]
    public void Page_workspace_invariants_fail_closed()
    {
        BaseStudioSectionRegistration section = BaseStudioSectionRegistration.Create(
            "summary", "studio.section.summary", 0, BaseStudioSectionKind.Summary, [], []);

        Assert.Throws<ArgumentException>(() => BaseStudioPagePresentationRegistration.Create(
            "base.records", 1, BaseStudioNavigationRole.Contextual, BaseStudioWorkspaceKind.ResourceMasterDetail,
            [section], null, null, BaseStudioDraftRetentionClass.None));
        Assert.Throws<ArgumentException>(() => BaseStudioPagePresentationRegistration.Create(
            "base.records", 1, BaseStudioNavigationRole.Contextual, BaseStudioWorkspaceKind.Detail,
            [section], BaseStudioResourceRailRegistration.Create(
                "collections", "base.collections.list", BaseStudioResourceKind.Collection,
                BaseStudioRailSearchKind.RegisteredView, BaseStudioPinClass.NonsecretIdentityAndSafeLabel,
                280, 200, 600), null, BaseStudioDraftRetentionClass.None));
    }

    [Fact]
    public void Mutable_checksum_inputs_are_deeply_owned()
    {
        byte[] row = Enumerable.Repeat((byte)4, 32).ToArray();
        BaseStudioSha256 input = BaseStudioSha256.Compute(row);
        BaseStudioGridDefinition grid = BaseStudioGridDefinition.Create(
            "base.records.grid", 1, BaseStudioResourceKind.Record, "base.record.row", input,
            [CreateColumn("identity", 0)], BaseStudioSelectionMode.None, [], 100, 25, 1_000, 1_000_000);

        row[0] = 99;
        Assert.True(BaseStudioSha256.FixedTimeEquals(grid.RowNodeChecksum, Digest(4)));
    }

    private static BaseStudioGridDefinition CreateReadOnlyGrid() => BaseStudioGridDefinition.Create(
        "base.records.grid", 1, BaseStudioResourceKind.Record, "base.record.row", Digest(7),
        [CreateColumn("identity", 0)], BaseStudioSelectionMode.None, [], 100, 25, 1_000, 1_000_000);

    private static BaseStudioGridColumnDefinition CreateColumn(string id, int order) =>
        BaseStudioGridColumnDefinition.Create(id, $"base.record.{id}", BaseStudioGridRendererKind.IdentityLink,
            BaseStudioGridDisclosureBehavior.SafeLabelOnly, $"studio.column.{id}", true,
            order, 240, 160, 600);

    private static BaseStudioSha256 Digest(byte value) =>
        BaseStudioSha256.Compute(Enumerable.Repeat(value, 32).ToArray());
}
