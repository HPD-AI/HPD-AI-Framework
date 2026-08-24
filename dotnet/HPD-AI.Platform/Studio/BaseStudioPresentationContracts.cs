using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace HPD.AI.Platform.Studio;

/// <summary>Identifies a task-oriented Studio area.</summary>
public enum BaseStudioArea : byte { Overview = 1, Data, Operations, Automations, Subjects, Search, Security, Infrastructure, Diagnostics }
/// <summary>Identifies how a page participates in authorized navigation.</summary>
public enum BaseStudioNavigationRole : byte { AreaLanding = 1, Contextual, HiddenResolver }
/// <summary>Identifies the page workspace layout.</summary>
public enum BaseStudioWorkspaceKind : byte { Landing = 1, ResourceMasterDetail, Detail, Timeline, QueryTool, Diagnostics }
/// <summary>Identifies resource-rail search behavior.</summary>
public enum BaseStudioRailSearchKind : byte { None = 1, CurrentFinitePage, RegisteredView }
/// <summary>Identifies persistable rail pin authority.</summary>
public enum BaseStudioPinClass : byte { None = 1, NonsecretIdentityAndSafeLabel }
/// <summary>Identifies permitted in-document draft retention.</summary>
public enum BaseStudioDraftRetentionClass : byte { None = 1, CurrentDocumentNavigation }
/// <summary>Identifies the semantic empty state for a view.</summary>
public enum BaseStudioEmptyStateKind : byte { NoItems = 1, NoMatches, NotConfigured, HistoricalUnavailable }
/// <summary>Identifies local row-selection behavior.</summary>
public enum BaseStudioSelectionMode : byte { None = 1, Single, MultipleLocal }
/// <summary>Identifies a disclosure-aware grid renderer.</summary>
public enum BaseStudioGridRendererKind : byte { Text = 1, Code, Boolean, Integer, Decimal, UtcDateTime, Status, IdentityLink, RelationExcerpt, DisclosureValue }
/// <summary>Identifies what a grid cell may disclose.</summary>
public enum BaseStudioGridDisclosureBehavior : byte { ProjectedValue = 1, SafeLabelOnly, DisclosureStateOnly }
/// <summary>Identifies contextual-detail close behavior.</summary>
public enum BaseStudioDetailCloseBehavior : byte { NavigateToParent = 1, RestoreReturnTarget }
/// <summary>Identifies dirty-state navigation behavior.</summary>
public enum BaseStudioDirtyStatePolicy : byte { None = 1, ConfirmDiscardOrStay }
/// <summary>Identifies a page section.</summary>
public enum BaseStudioSectionKind : byte { Summary = 1, Configuration, Evidence, History, Actions, CustomSemantic }
/// <summary>Identifies a safe aggregate chart.</summary>
public enum BaseStudioSafeChartKind : byte { TimeBuckets = 1, CategoryBuckets, StatusBuckets }
/// <summary>Identifies how a view responds to non-authoritative invalidation hints.</summary>
public enum BaseStudioActivityPolicyKind : byte { ExplicitRefreshOnly = 1, GovernedInvalidationRefresh }
/// <summary>Identifies one nonsecret display preference.</summary>
public enum BaseStudioPreferenceKind : byte { Theme = 1, Density, RailWidth, DetailWidth, VisibleColumns, ColumnOrder, ColumnWidths, NonsecretPins, PreferredTab }

/// <summary>Identifies one resource shape understood by the Studio graph.</summary>
public enum BaseStudioResourceKind : byte
{
    Application = 1, Module, Collection, Record, Relation, FileBucket, File, RegisteredRead,
    SelectionOperation, ModuleMutation, OperationExecution, Receipt, ActivationDefinition,
    Activation, Schedule, Occurrence, ActivationAttempt, Effect, Executor, SubjectContract,
    Subject, LifecycleConsumer, LifecycleCheckpoint, RetirementBarrier, TextIndex, VectorIndex,
    SearchRebuild, CertificationReceipt, Policy, Grant, Store, Provider, Schema, Migration,
    Backup, Restore, Maintenance, Health, Diagnostic, QuarantineItem, GraphDefinition,
    GraphExecution, GraphNode, GraphChannel, GraphCheckpoint,
}

/// <summary>Defines the registered invalidation activity limits for one view.</summary>
public sealed class BaseStudioActivityPolicy
{
    private BaseStudioActivityPolicy(BaseStudioActivityPolicyKind kind, int hints, int superseded, int keys, BaseStudioSha256 checksum)
    { Kind = kind; MaximumHintsPerRollingSecond = hints; MaximumSupersededRefreshes = superseded; MaximumCoalescedKeys = keys; Checksum = checksum; }
    /// <summary>Gets the activity strategy.</summary>
    public BaseStudioActivityPolicyKind Kind { get; }
    /// <summary>Gets the maximum admitted hints in a rolling second.</summary>
    public int MaximumHintsPerRollingSecond { get; }
    /// <summary>Gets the maximum consecutively superseded refreshes.</summary>
    public int MaximumSupersededRefreshes { get; }
    /// <summary>Gets the maximum coalesced invalidation keys.</summary>
    public int MaximumCoalescedKeys { get; }
    /// <summary>Gets the canonical policy checksum.</summary>
    public BaseStudioSha256 Checksum { get; }

    /// <summary>Creates and checksums a registered activity policy.</summary>
    public static BaseStudioActivityPolicy Create(BaseStudioActivityPolicyKind kind, int hints, int superseded, int keys)
    {
        StudioContractValidation.Enum(kind);
        if (hints is < 1 or > 1_000 || superseded is < 1 or > 100 || keys is < 1 or > 2_048)
            throw new ArgumentOutOfRangeException(nameof(hints));
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.activity.v1", writer =>
        { writer.Enum(kind); writer.Int32(hints); writer.Int32(superseded); writer.Int32(keys); });
        return new(kind, hints, superseded, keys, checksum);
    }
}

/// <summary>Defines the registered nonsecret preference vocabulary for one view.</summary>
public sealed class BaseStudioPreferenceSchema
{
    private BaseStudioPreferenceSchema(string id, int version, ImmutableArray<BaseStudioPreferenceKind> allowed, long maximumBytes, TimeSpan lifetime, BaseStudioSha256 checksum)
    { SchemaId = id; Version = version; Allowed = allowed; MaximumBytes = maximumBytes; MaximumLifetime = lifetime; Checksum = checksum; }
    /// <summary>Gets the schema identity.</summary>
    public string SchemaId { get; }
    /// <summary>Gets the schema version.</summary>
    public int Version { get; }
    /// <summary>Gets the allowed preference kinds in discriminator order.</summary>
    public ImmutableArray<BaseStudioPreferenceKind> Allowed { get; }
    /// <summary>Gets the maximum canonical preference bytes.</summary>
    public long MaximumBytes { get; }
    /// <summary>Gets the maximum preference lifetime.</summary>
    public TimeSpan MaximumLifetime { get; }
    /// <summary>Gets the canonical schema checksum.</summary>
    public BaseStudioSha256 Checksum { get; }

    /// <summary>Creates and checksums a preference schema.</summary>
    public static BaseStudioPreferenceSchema Create(string id, int version, IEnumerable<BaseStudioPreferenceKind> allowed, long maximumBytes, TimeSpan lifetime)
    {
        StudioContractValidation.Id(id);
        ArgumentNullException.ThrowIfNull(allowed);
        if (version < 1 || maximumBytes is < 1 or > 64_000 || lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromDays(180))
            throw new ArgumentOutOfRangeException(nameof(version));
        ImmutableArray<BaseStudioPreferenceKind> values = allowed.ToImmutableArray();
        if (values.Length > 9 || values.Any(static value => !Enum.IsDefined(value)) ||
            values.Distinct().Count() != values.Length || !values.SequenceEqual(values.OrderBy(static value => (byte)value)))
            throw new ArgumentException("Studio preference kinds are not canonical.", nameof(allowed));
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.preference.v1", writer =>
        {
            writer.String(id); writer.Int32(version); writer.Count(values.Length);
            foreach (BaseStudioPreferenceKind value in values) writer.Enum(value);
            writer.Int64(maximumBytes); writer.Int64(checked((long)lifetime.TotalMilliseconds));
        });
        return new(id, version, values, maximumBytes, lifetime, checksum);
    }
}

/// <summary>Defines one graph-owned disclosure-aware grid column.</summary>
public sealed class BaseStudioGridColumnDefinition
{
    private BaseStudioGridColumnDefinition(
        string columnId, string propertyId, BaseStudioGridRendererKind renderer,
        BaseStudioGridDisclosureBehavior disclosure, string labelMessageId, bool initiallyVisible,
        int order, int initialWidth, int minimumWidth, int maximumWidth, string? filterId,
        string? sortId, BaseStudioSha256 checksum)
    {
        ColumnId = columnId; StablePropertyOrEdgeId = propertyId; Renderer = renderer;
        Disclosure = disclosure; LabelMessageId = labelMessageId; InitiallyVisible = initiallyVisible;
        InitialOrder = order; InitialWidthCssPixels = initialWidth; MinimumWidthCssPixels = minimumWidth;
        MaximumWidthCssPixels = maximumWidth; FilterId = filterId; SortId = sortId; Checksum = checksum;
    }
    /// <summary>Gets the stable column identity.</summary>
    public string ColumnId { get; }
    /// <summary>Gets the stable L44 property or edge identity.</summary>
    public string StablePropertyOrEdgeId { get; }
    /// <summary>Gets the renderer.</summary>
    public BaseStudioGridRendererKind Renderer { get; }
    /// <summary>Gets the disclosure behavior.</summary>
    public BaseStudioGridDisclosureBehavior Disclosure { get; }
    /// <summary>Gets the localized label message identity.</summary>
    public string LabelMessageId { get; }
    /// <summary>Gets whether the column is initially visible.</summary>
    public bool InitiallyVisible { get; }
    /// <summary>Gets the initial contiguous order.</summary>
    public int InitialOrder { get; }
    /// <summary>Gets the initial width.</summary>
    public int InitialWidthCssPixels { get; }
    /// <summary>Gets the minimum width.</summary>
    public int MinimumWidthCssPixels { get; }
    /// <summary>Gets the maximum width.</summary>
    public int MaximumWidthCssPixels { get; }
    /// <summary>Gets the optional registered filter identity.</summary>
    public string? FilterId { get; }
    /// <summary>Gets the optional registered sort identity.</summary>
    public string? SortId { get; }
    /// <summary>Gets the canonical column checksum.</summary>
    public BaseStudioSha256 Checksum { get; }

    /// <summary>Creates and checksums one grid column.</summary>
    public static BaseStudioGridColumnDefinition Create(
        string columnId, string propertyId, BaseStudioGridRendererKind renderer,
        BaseStudioGridDisclosureBehavior disclosure, string labelMessageId, bool initiallyVisible,
        int order, int initialWidth, int minimumWidth, int maximumWidth, string? filterId = null, string? sortId = null)
    {
        StudioContractValidation.Id(columnId); StudioContractValidation.Id(propertyId); StudioContractValidation.Id(labelMessageId);
        StudioContractValidation.Enum(renderer); StudioContractValidation.Enum(disclosure);
        StudioContractValidation.OptionalId(filterId); StudioContractValidation.OptionalId(sortId);
        StudioContractValidation.Widths(minimumWidth, initialWidth, maximumWidth);
        if (order < 0) throw new ArgumentOutOfRangeException(nameof(order));
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.grid-column.v1", writer =>
        {
            writer.String(columnId); writer.String(propertyId); writer.Enum(renderer); writer.Enum(disclosure);
            writer.String(labelMessageId); writer.Boolean(initiallyVisible); writer.Int32(order);
            writer.Int32(initialWidth); writer.Int32(minimumWidth); writer.Int32(maximumWidth);
            writer.OptionalString(filterId); writer.OptionalString(sortId);
        });
        return new(columnId, propertyId, renderer, disclosure, labelMessageId, initiallyVisible,
            order, initialWidth, minimumWidth, maximumWidth, filterId, sortId, checksum);
    }
}

/// <summary>Defines one bounded, schema-aware Studio grid.</summary>
public sealed class BaseStudioGridDefinition
{
    private BaseStudioGridDefinition(string id, int version, BaseStudioResourceKind rowKind, string rowNodeId,
        BaseStudioSha256 rowNodeChecksum, ImmutableArray<BaseStudioGridColumnDefinition> columns,
        BaseStudioSelectionMode selection, ImmutableArray<string> commands, int virtualizationThreshold,
        int accessiblePageSize, int maximumRows, long maximumBytes, BaseStudioSha256 checksum)
    {
        GridId = id; Version = version; RowKind = rowKind; RowNodeId = rowNodeId; RowNodeChecksum = rowNodeChecksum;
        Columns = columns; Selection = selection; RowCommandIds = commands; VirtualizationThreshold = virtualizationThreshold;
        AccessiblePageSize = accessiblePageSize; MaximumRows = maximumRows; MaximumBytes = maximumBytes; Checksum = checksum;
    }
    /// <summary>Gets the grid identity.</summary>
    public string GridId { get; }
    /// <summary>Gets the grid version.</summary>
    public int Version { get; }
    /// <summary>Gets the row resource kind.</summary>
    public BaseStudioResourceKind RowKind { get; }
    /// <summary>Gets the L41 row-node identity.</summary>
    public string RowNodeId { get; }
    /// <summary>Gets the L41 row-node checksum.</summary>
    public BaseStudioSha256 RowNodeChecksum { get; }
    /// <summary>Gets columns in canonical display order.</summary>
    public ImmutableArray<BaseStudioGridColumnDefinition> Columns { get; }
    /// <summary>Gets local selection behavior.</summary>
    public BaseStudioSelectionMode Selection { get; }
    /// <summary>Gets registered row commands in ordinal identity order.</summary>
    public ImmutableArray<string> RowCommandIds { get; }
    /// <summary>Gets the virtualization threshold.</summary>
    public int VirtualizationThreshold { get; }
    /// <summary>Gets the accessible page size.</summary>
    public int AccessiblePageSize { get; }
    /// <summary>Gets the maximum retained rows.</summary>
    public int MaximumRows { get; }
    /// <summary>Gets the maximum retained result bytes.</summary>
    public long MaximumBytes { get; }
    /// <summary>Gets the canonical grid checksum.</summary>
    public BaseStudioSha256 Checksum { get; }

    /// <summary>Creates and checksums one grid definition.</summary>
    public static BaseStudioGridDefinition Create(
        string id, int version, BaseStudioResourceKind rowKind, string rowNodeId, BaseStudioSha256 rowNodeChecksum,
        IEnumerable<BaseStudioGridColumnDefinition> columns, BaseStudioSelectionMode selection,
        IEnumerable<string> rowCommandIds, int virtualizationThreshold, int accessiblePageSize,
        int maximumRows, long maximumBytes)
    {
        StudioContractValidation.Id(id); StudioContractValidation.Id(rowNodeId); StudioContractValidation.Enum(rowKind);
        StudioContractValidation.Enum(selection); ArgumentNullException.ThrowIfNull(rowNodeChecksum);
        if (version < 1 || virtualizationThreshold < 1 || accessiblePageSize < 1 || maximumRows < accessiblePageSize || maximumBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(version));
        ImmutableArray<BaseStudioGridColumnDefinition> ownedColumns = StudioContractValidation.Materialize(columns, 128, false, nameof(columns));
        if (ownedColumns.Select(static value => value.ColumnId).Distinct(StringComparer.Ordinal).Count() != ownedColumns.Length ||
            !ownedColumns.Select(static value => value.InitialOrder).SequenceEqual(Enumerable.Range(0, ownedColumns.Length)))
            throw new ArgumentException("Studio grid columns are not canonical.", nameof(columns));
        ImmutableArray<string> commands = StudioContractValidation.Ids(rowCommandIds, 128, true, nameof(rowCommandIds));
        BaseStudioSha256 rowChecksum = BaseStudioSha256.FromBytes(rowNodeChecksum.ToArray());
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.grid.v1", writer =>
        {
            writer.String(id); writer.Int32(version); writer.Enum(rowKind); writer.String(rowNodeId); writer.Checksum(rowChecksum);
            writer.Count(ownedColumns.Length); foreach (BaseStudioGridColumnDefinition column in ownedColumns) writer.Checksum(column.Checksum);
            writer.Enum(selection); writer.Count(commands.Length); foreach (string command in commands) writer.String(command);
            writer.Int32(virtualizationThreshold); writer.Int32(accessiblePageSize); writer.Int32(maximumRows); writer.Int64(maximumBytes);
        });
        return new(id, version, rowKind, rowNodeId, rowChecksum, ownedColumns, selection, commands,
            virtualizationThreshold, accessiblePageSize, maximumRows, maximumBytes, checksum);
    }
}

/// <summary>Defines one page section and its registered view and command ownership.</summary>
public sealed class BaseStudioSectionRegistration
{
    private BaseStudioSectionRegistration(string id, string label, int order, BaseStudioSectionKind kind,
        ImmutableArray<string> views, ImmutableArray<string> commands, BaseStudioSha256 checksum)
    { SectionId = id; LabelMessageId = label; Order = order; Kind = kind; ViewIds = views; CommandIds = commands; Checksum = checksum; }
    /// <summary>Gets the section identity.</summary>
    public string SectionId { get; }
    /// <summary>Gets the localized label message identity.</summary>
    public string LabelMessageId { get; }
    /// <summary>Gets the contiguous section order.</summary>
    public int Order { get; }
    /// <summary>Gets the semantic section kind.</summary>
    public BaseStudioSectionKind Kind { get; }
    /// <summary>Gets registered view identities.</summary>
    public ImmutableArray<string> ViewIds { get; }
    /// <summary>Gets registered command identities.</summary>
    public ImmutableArray<string> CommandIds { get; }
    /// <summary>Gets the canonical section checksum.</summary>
    public BaseStudioSha256 Checksum { get; }

    /// <summary>Creates and checksums one page section.</summary>
    public static BaseStudioSectionRegistration Create(string id, string label, int order, BaseStudioSectionKind kind,
        IEnumerable<string> viewIds, IEnumerable<string> commandIds)
    {
        StudioContractValidation.Id(id); StudioContractValidation.Id(label); StudioContractValidation.Enum(kind);
        if (order < 0) throw new ArgumentOutOfRangeException(nameof(order));
        ImmutableArray<string> views = StudioContractValidation.Ids(viewIds, 64, true, nameof(viewIds));
        ImmutableArray<string> commands = StudioContractValidation.Ids(commandIds, 64, true, nameof(commandIds));
        bool finiteWithoutMembers = kind is BaseStudioSectionKind.Summary or BaseStudioSectionKind.Configuration or
            BaseStudioSectionKind.Evidence or BaseStudioSectionKind.History;
        if (views.Length == 0 && commands.Length == 0 && !finiteWithoutMembers)
            throw new ArgumentException("This Studio section requires a registered view or command.");
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.section.v1", writer =>
        {
            writer.String(id); writer.String(label); writer.Int32(order); writer.Enum(kind);
            writer.Count(views.Length); foreach (string view in views) writer.String(view);
            writer.Count(commands.Length); foreach (string command in commands) writer.String(command);
        });
        return new(id, label, order, kind, views, commands, checksum);
    }
}

/// <summary>Defines a bounded resource rail owned by one page.</summary>
public sealed class BaseStudioResourceRailRegistration
{
    private BaseStudioResourceRailRegistration(string railId, string viewId, BaseStudioResourceKind itemKind,
        BaseStudioRailSearchKind search, BaseStudioPinClass pinning, int initial, int minimum, int maximum,
        BaseStudioSha256 checksum)
    { RailId = railId; ViewId = viewId; ItemKind = itemKind; Search = search; Pinning = pinning;
      InitialWidthCssPixels = initial; MinimumWidthCssPixels = minimum; MaximumWidthCssPixels = maximum; Checksum = checksum; }
    /// <summary>Gets the rail identity.</summary>
    public string RailId { get; }
    /// <summary>Gets the registered rail view identity.</summary>
    public string ViewId { get; }
    /// <summary>Gets the rail item kind.</summary>
    public BaseStudioResourceKind ItemKind { get; }
    /// <summary>Gets rail search behavior.</summary>
    public BaseStudioRailSearchKind Search { get; }
    /// <summary>Gets rail pinning authority.</summary>
    public BaseStudioPinClass Pinning { get; }
    /// <summary>Gets the initial width.</summary>
    public int InitialWidthCssPixels { get; }
    /// <summary>Gets the minimum width.</summary>
    public int MinimumWidthCssPixels { get; }
    /// <summary>Gets the maximum width.</summary>
    public int MaximumWidthCssPixels { get; }
    /// <summary>Gets the canonical rail checksum.</summary>
    public BaseStudioSha256 Checksum { get; }

    /// <summary>Creates and checksums a resource rail.</summary>
    public static BaseStudioResourceRailRegistration Create(string railId, string viewId, BaseStudioResourceKind itemKind,
        BaseStudioRailSearchKind search, BaseStudioPinClass pinning, int initial, int minimum, int maximum)
    {
        StudioContractValidation.Id(railId); StudioContractValidation.Id(viewId); StudioContractValidation.Enum(itemKind);
        StudioContractValidation.Enum(search); StudioContractValidation.Enum(pinning); StudioContractValidation.Widths(minimum, initial, maximum);
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.rail.v1", writer =>
        { writer.String(railId); writer.String(viewId); writer.Enum(itemKind); writer.Enum(search); writer.Enum(pinning); writer.Int32(initial); writer.Int32(minimum); writer.Int32(maximum); });
        return new(railId, viewId, itemKind, search, pinning, initial, minimum, maximum, checksum);
    }
}

/// <summary>Defines contextual detail behavior for a resource workspace.</summary>
public sealed class BaseStudioContextualDetailRegistration
{
    private BaseStudioContextualDetailRegistration(ImmutableArray<BaseStudioResourceKind> kinds, ImmutableArray<string> pages,
        int threshold, BaseStudioDetailCloseBehavior close, BaseStudioDirtyStatePolicy dirty, BaseStudioSha256 checksum)
    { AcceptedKinds = kinds; DetailPageIds = pages; FullScreenBelowCssPixels = threshold; CloseBehavior = close; DirtyState = dirty; Checksum = checksum; }
    /// <summary>Gets accepted resource kinds in discriminator order.</summary>
    public ImmutableArray<BaseStudioResourceKind> AcceptedKinds { get; }
    /// <summary>Gets detail page identities in ordinal order.</summary>
    public ImmutableArray<string> DetailPageIds { get; }
    /// <summary>Gets the responsive page-promotion threshold.</summary>
    public int FullScreenBelowCssPixels { get; }
    /// <summary>Gets close navigation behavior.</summary>
    public BaseStudioDetailCloseBehavior CloseBehavior { get; }
    /// <summary>Gets dirty-state behavior.</summary>
    public BaseStudioDirtyStatePolicy DirtyState { get; }
    /// <summary>Gets the canonical detail checksum.</summary>
    public BaseStudioSha256 Checksum { get; }

    /// <summary>Creates and checksums contextual-detail behavior.</summary>
    public static BaseStudioContextualDetailRegistration Create(IEnumerable<BaseStudioResourceKind> acceptedKinds,
        IEnumerable<string> detailPageIds, int threshold, BaseStudioDetailCloseBehavior close, BaseStudioDirtyStatePolicy dirty)
    {
        ImmutableArray<BaseStudioResourceKind> kinds = StudioContractValidation.Materialize(acceptedKinds, 64, false, nameof(acceptedKinds));
        if (kinds.Any(static value => !Enum.IsDefined(value)) || kinds.Distinct().Count() != kinds.Length ||
            !kinds.SequenceEqual(kinds.OrderBy(static value => (byte)value))) throw new ArgumentException("Studio detail kinds are not canonical.", nameof(acceptedKinds));
        ImmutableArray<string> pages = StudioContractValidation.Ids(detailPageIds, 64, false, nameof(detailPageIds));
        StudioContractValidation.Enum(close); StudioContractValidation.Enum(dirty);
        if (threshold is < 320 or > 1_280) throw new ArgumentOutOfRangeException(nameof(threshold));
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.contextual-detail.v1", writer =>
        {
            writer.Count(kinds.Length); foreach (BaseStudioResourceKind kind in kinds) writer.Enum(kind);
            writer.Count(pages.Length); foreach (string page in pages) writer.String(page);
            writer.Int32(threshold); writer.Enum(close); writer.Enum(dirty);
        });
        return new(kinds, pages, threshold, close, dirty, checksum);
    }
}

/// <summary>Defines one disclosure-safe aggregate chart and accessible table equivalent.</summary>
public sealed class BaseStudioSafeChartDefinition
{
    private BaseStudioSafeChartDefinition(string id, BaseStudioSafeChartKind kind, string bucketViewId,
        string tableViewId, int buckets, BaseStudioSha256 disclosureChecksum, BaseStudioSha256 checksum)
    { ChartId = id; Kind = kind; BucketViewId = bucketViewId; EquivalentTableViewId = tableViewId;
      MaximumBuckets = buckets; DisclosureChannelChecksum = disclosureChecksum; Checksum = checksum; }
    /// <summary>Gets the chart identity.</summary>
    public string ChartId { get; }
    /// <summary>Gets the chart kind.</summary>
    public BaseStudioSafeChartKind Kind { get; }
    /// <summary>Gets the registered bucket view.</summary>
    public string BucketViewId { get; }
    /// <summary>Gets the equivalent accessible table view.</summary>
    public string EquivalentTableViewId { get; }
    /// <summary>Gets the maximum bucket count.</summary>
    public int MaximumBuckets { get; }
    /// <summary>Gets the shared disclosure-channel checksum.</summary>
    public BaseStudioSha256 DisclosureChannelChecksum { get; }
    /// <summary>Gets the canonical chart checksum.</summary>
    public BaseStudioSha256 Checksum { get; }

    /// <summary>Creates and checksums a safe chart.</summary>
    public static BaseStudioSafeChartDefinition Create(string id, BaseStudioSafeChartKind kind, string bucketViewId,
        string tableViewId, int maximumBuckets, BaseStudioSha256 disclosureChecksum)
    {
        StudioContractValidation.Id(id); StudioContractValidation.Id(bucketViewId); StudioContractValidation.Id(tableViewId);
        StudioContractValidation.Enum(kind); ArgumentNullException.ThrowIfNull(disclosureChecksum);
        if (maximumBuckets is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(maximumBuckets));
        BaseStudioSha256 disclosure = BaseStudioSha256.FromBytes(disclosureChecksum.ToArray());
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.chart.v1", writer =>
        { writer.String(id); writer.Enum(kind); writer.String(bucketViewId); writer.String(tableViewId); writer.Int32(maximumBuckets); writer.Checksum(disclosure); });
        return new(id, kind, bucketViewId, tableViewId, maximumBuckets, disclosure, checksum);
    }
}

/// <summary>Defines the immutable presentation mechanics for one registered view.</summary>
public sealed class BaseStudioViewPresentationRegistration
{
    private BaseStudioViewPresentationRegistration(string id, BaseStudioGridDefinition? grid, BaseStudioSafeChartDefinition? chart,
        BaseStudioEmptyStateKind empty, BaseStudioActivityPolicy activity, BaseStudioPreferenceSchema preferences, BaseStudioSha256 checksum)
    { ViewId = id; Grid = grid; Chart = chart; EmptyState = empty; Activity = activity; Preferences = preferences; Checksum = checksum; }
    /// <summary>Gets the registered view identity.</summary>
    public string ViewId { get; }
    /// <summary>Gets the optional grid.</summary>
    public BaseStudioGridDefinition? Grid { get; }
    /// <summary>Gets the optional chart.</summary>
    public BaseStudioSafeChartDefinition? Chart { get; }
    /// <summary>Gets the semantic empty state.</summary>
    public BaseStudioEmptyStateKind EmptyState { get; }
    /// <summary>Gets the activity policy.</summary>
    public BaseStudioActivityPolicy Activity { get; }
    /// <summary>Gets the preference schema.</summary>
    public BaseStudioPreferenceSchema Preferences { get; }
    /// <summary>Gets the canonical view-presentation checksum.</summary>
    public BaseStudioSha256 Checksum { get; }

    /// <summary>Creates and checksums one view presentation.</summary>
    public static BaseStudioViewPresentationRegistration Create(string id, BaseStudioGridDefinition? grid,
        BaseStudioSafeChartDefinition? chart, BaseStudioEmptyStateKind empty, BaseStudioActivityPolicy activity,
        BaseStudioPreferenceSchema preferences)
    {
        StudioContractValidation.Id(id); StudioContractValidation.Enum(empty);
        ArgumentNullException.ThrowIfNull(activity); ArgumentNullException.ThrowIfNull(preferences);
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.view-presentation.v1", writer =>
        {
            writer.String(id); writer.OptionalChecksum(grid?.Checksum); writer.OptionalChecksum(chart?.Checksum);
            writer.Enum(empty); writer.Checksum(activity.Checksum); writer.Checksum(preferences.Checksum);
        });
        return new(id, grid, chart, empty, activity, preferences, checksum);
    }
}

/// <summary>Defines the complete immutable presentation graph for one page.</summary>
public sealed class BaseStudioPagePresentationRegistration
{
    private BaseStudioPagePresentationRegistration(string pageId, int pageVersion, BaseStudioNavigationRole role, BaseStudioWorkspaceKind workspace,
        ImmutableArray<BaseStudioSectionRegistration> sections, BaseStudioResourceRailRegistration? rail,
        BaseStudioContextualDetailRegistration? detail, BaseStudioDraftRetentionClass draft, BaseStudioSha256 checksum)
    { PageId = pageId; PageVersion = pageVersion; NavigationRole = role; Workspace = workspace; Sections = sections; ResourceRail = rail; ContextualDetail = detail;
      DraftRetention = draft; Checksum = checksum; }
    /// <summary>Gets the owning page identity.</summary>
    public string PageId { get; }
    /// <summary>Gets the owning page version.</summary>
    public int PageVersion { get; }
    /// <summary>Gets the navigation role.</summary>
    public BaseStudioNavigationRole NavigationRole { get; }
    /// <summary>Gets the workspace kind.</summary>
    public BaseStudioWorkspaceKind Workspace { get; }
    /// <summary>Gets page sections in contiguous order.</summary>
    public ImmutableArray<BaseStudioSectionRegistration> Sections { get; }
    /// <summary>Gets the optional resource rail.</summary>
    public BaseStudioResourceRailRegistration? ResourceRail { get; }
    /// <summary>Gets optional contextual-detail behavior.</summary>
    public BaseStudioContextualDetailRegistration? ContextualDetail { get; }
    /// <summary>Gets draft-retention authority.</summary>
    public BaseStudioDraftRetentionClass DraftRetention { get; }
    /// <summary>Gets the canonical page-presentation checksum.</summary>
    public BaseStudioSha256 Checksum { get; }

    /// <summary>Creates and cross-validates a page presentation graph.</summary>
    public static BaseStudioPagePresentationRegistration Create(string pageId, int pageVersion,
        BaseStudioNavigationRole role, BaseStudioWorkspaceKind workspace,
        IEnumerable<BaseStudioSectionRegistration> sections, BaseStudioResourceRailRegistration? rail,
        BaseStudioContextualDetailRegistration? detail, BaseStudioDraftRetentionClass draft)
    {
        StudioContractValidation.Id(pageId); StudioContractValidation.Enum(role); StudioContractValidation.Enum(workspace); StudioContractValidation.Enum(draft);
        if (pageVersion < 1) throw new ArgumentOutOfRangeException(nameof(pageVersion));
        ImmutableArray<BaseStudioSectionRegistration> ownedSections = StudioContractValidation.Materialize(sections, 64, false, nameof(sections));
        if (!ownedSections.Select(static value => value.Order).SequenceEqual(Enumerable.Range(0, ownedSections.Length)) ||
            ownedSections.Select(static value => value.SectionId).Distinct(StringComparer.Ordinal).Count() != ownedSections.Length)
            throw new ArgumentException("Studio page sections are not canonical.", nameof(sections));
        if (workspace == BaseStudioWorkspaceKind.ResourceMasterDetail && (rail is null || detail is null))
            throw new ArgumentException("A resource master-detail workspace requires both rail and detail definitions.");
        if (workspace != BaseStudioWorkspaceKind.ResourceMasterDetail && rail is not null)
            throw new ArgumentException("Only a resource master-detail workspace may own a resource rail.");
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.presentation.v1", writer =>
        {
            writer.String(pageId); writer.Int32(pageVersion); writer.Enum(role); writer.Enum(workspace);
            writer.Count(ownedSections.Length); foreach (BaseStudioSectionRegistration section in ownedSections) writer.Checksum(section.Checksum);
            writer.OptionalChecksum(rail?.Checksum); writer.OptionalChecksum(detail?.Checksum); writer.Enum(draft);
        });
        return new(pageId, pageVersion, role, workspace, ownedSections, rail, detail, draft, checksum);
    }
}

internal static class StudioContractValidation
{
    internal static void Id(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!value.IsNormalized(NormalizationForm.FormC) || Encoding.UTF8.GetByteCount(value) is < 1 or > 128 || value.Any(char.IsControl))
            throw new ArgumentException("A Studio identity is invalid.", nameof(value));
    }
    internal static void OptionalId(string? value) { if (value is not null) Id(value); }
    internal static void Enum<T>(T value) where T : struct, Enum { if (!System.Enum.IsDefined(value) || Convert.ToByte(value) == 0) throw new ArgumentOutOfRangeException(nameof(value)); }
    internal static void Widths(int minimum, int initial, int maximum)
    { if (minimum < 160 || maximum > 1_600 || minimum > initial || initial > maximum) throw new ArgumentOutOfRangeException(nameof(initial)); }
    internal static ImmutableArray<T> Materialize<T>(IEnumerable<T> values, int maximum, bool allowEmpty, string parameter)
    {
        ArgumentNullException.ThrowIfNull(values);
        T[] items = values.Take(maximum + 1).ToArray();
        if ((!allowEmpty && items.Length == 0) || items.Length > maximum || items.Any(static value => value is null))
            throw new ArgumentException("A Studio array is invalid.", parameter);
        return items.ToImmutableArray();
    }
    internal static ImmutableArray<string> Ids(IEnumerable<string> values, int maximum, bool allowEmpty, string parameter)
    {
        ImmutableArray<string> items = Materialize(values, maximum, allowEmpty, parameter);
        foreach (string item in items) Id(item);
        if (items.Distinct(StringComparer.Ordinal).Count() != items.Length ||
            !items.SequenceEqual(items.Order(StringComparer.Ordinal)))
            throw new ArgumentException("Studio identities are not in canonical ordinal order.", parameter);
        return items;
    }
}

internal static class StudioCanonicalEncoding
{
    internal static BaseStudioSha256 Hash(string purpose, Action<Writer> encode)
    {
        using var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes(purpose)); stream.WriteByte(0); stream.WriteByte(1);
        var writer = new Writer(stream); encode(writer);
        return BaseStudioSha256.Compute(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
    }

    internal sealed class Writer(Stream stream)
    {
        internal void Boolean(bool value) => stream.WriteByte(value ? (byte)1 : (byte)0);
        internal void Enum<T>(T value) where T : struct, Enum => stream.WriteByte(Convert.ToByte(value));
        internal void Count(int value) => UInt32(checked((uint)value));
        internal void Int32(int value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(bytes, value); stream.Write(bytes); }
        internal void Int64(long value) { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, value); stream.Write(bytes); }
        internal void String(string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); UInt32(checked((uint)bytes.Length)); stream.Write(bytes); }
        internal void OptionalString(string? value) { stream.WriteByte(value is null ? (byte)0 : (byte)1); if (value is not null) String(value); }
        internal void OptionalChecksum(BaseStudioSha256? value) { stream.WriteByte(value is null ? (byte)0 : (byte)1); if (value is not null) Checksum(value); }
        internal void Checksum(BaseStudioSha256 value) => stream.Write(value.ToArray());
        internal void Bytes(ReadOnlySpan<byte> value) { UInt32(checked((uint)value.Length)); stream.Write(value); }
        private void UInt32(uint value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, value); stream.Write(bytes); }
    }
}
