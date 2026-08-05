namespace HPD.Base;
/// <summary>Configures bounded relational and include execution.</summary>
public sealed class HPDBaseRelationalOptions
{
    /// <summary>Gets or sets max Sources.</summary>
    public int MaxSources { get; set; } = 8;
    /// <summary>Gets or sets max Joins.</summary>
    public int MaxJoins { get; set; } = 8;
    /// <summary>Gets or sets max Predicate Nodes.</summary>
    public int MaxPredicateNodes { get; set; } = 256;
    /// <summary>Gets or sets max Predicate Depth.</summary>
    public int MaxPredicateDepth { get; set; } = 12;
    /// <summary>Gets or sets max Parameters.</summary>
    public int MaxParameters { get; set; } = 64;
    /// <summary>Gets or sets max Parameter String Length.</summary>
    public int MaxParameterStringLength { get; set; } = 4_096;
    /// <summary>Gets or sets max Parameter Array Items.</summary>
    public int MaxParameterArrayItems { get; set; } = 256;
    /// <summary>Gets or sets max Group Keys.</summary>
    public int MaxGroupKeys { get; set; } = 8;
    /// <summary>Gets or sets max Aggregates.</summary>
    public int MaxAggregates { get; set; } = 16;
    /// <summary>Gets or sets max Projection Fields.</summary>
    public int MaxProjectionFields { get; set; } = 64;
    /// <summary>Gets or sets max Sort Fields.</summary>
    public int MaxSortFields { get; set; } = 8;
    /// <summary>Gets or sets max Page Size.</summary>
    public int MaxPageSize { get; set; } = 500;
    /// <summary>Gets or sets max Result Rows.</summary>
    public int MaxResultRows { get; set; } = 1_000;
    /// <summary>Gets or sets max Groups.</summary>
    public int MaxGroups { get; set; } = 1_000;
    /// <summary>Gets or sets max Result Bytes.</summary>
    public int MaxResultBytes { get; set; } = 1_048_576;
    /// <summary>Gets or sets max Include Depth.</summary>
    public int MaxIncludeDepth { get; set; } = 3;
    /// <summary>Gets or sets max Includes.</summary>
    public int MaxIncludes { get; set; } = 8;
    /// <summary>Gets or sets max Included Records.</summary>
    public int MaxIncludedRecords { get; set; } = 1_000;
    /// <summary>Gets or sets max Included Records Per Parent.</summary>
    public int MaxIncludedRecordsPerParent { get; set; } = 100;
    /// <summary>Gets or sets max Execution Duration.</summary>
    public TimeSpan MaxExecutionDuration { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets snapshot Acquisition Timeout.</summary>
    public TimeSpan SnapshotAcquisitionTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Performs validate.</summary>
    internal void Validate()
    {
        Bounded(MaxSources, 8, nameof(MaxSources));
        Bounded(MaxJoins, 8, nameof(MaxJoins));
        Bounded(MaxPredicateNodes, 256, nameof(MaxPredicateNodes));
        Bounded(MaxPredicateDepth, 12, nameof(MaxPredicateDepth));
        Bounded(MaxParameters, 64, nameof(MaxParameters));
        Bounded(MaxParameterStringLength, 4_096, nameof(MaxParameterStringLength));
        Bounded(MaxParameterArrayItems, 256, nameof(MaxParameterArrayItems));
        Bounded(MaxGroupKeys, 8, nameof(MaxGroupKeys));
        Bounded(MaxAggregates, 16, nameof(MaxAggregates));
        Bounded(MaxProjectionFields, 64, nameof(MaxProjectionFields));
        Bounded(MaxSortFields, 8, nameof(MaxSortFields));
        Bounded(MaxPageSize, 500, nameof(MaxPageSize));
        Bounded(MaxResultRows, 1_000, nameof(MaxResultRows));
        Bounded(MaxGroups, 1_000, nameof(MaxGroups));
        Bytes(MaxResultBytes, nameof(MaxResultBytes));
        Bounded(MaxIncludeDepth, 3, nameof(MaxIncludeDepth));
        Bounded(MaxIncludes, 8, nameof(MaxIncludes));
        Bounded(MaxIncludedRecords, 1_000, nameof(MaxIncludedRecords));
        Bounded(MaxIncludedRecordsPerParent, 100, nameof(MaxIncludedRecordsPerParent));
        Duration(MaxExecutionDuration, TimeSpan.FromMilliseconds(10), TimeSpan.FromMinutes(2), nameof(MaxExecutionDuration));
        Duration(SnapshotAcquisitionTimeout, TimeSpan.FromMilliseconds(10), TimeSpan.FromMinutes(1), nameof(SnapshotAcquisitionTimeout));
    }

    /// <summary>Performs bounded.</summary>
    private static void Bounded(int value, int defaultValue, string name)
    {
        if (value < 1 || value > defaultValue * 10)
            throw new ArgumentOutOfRangeException(name);
    }

    /// <summary>Performs bytes.</summary>
    private static void Bytes(int value, string name)
    {
        if (value < 1_024 || value > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(name);
    }

    /// <summary>Performs duration.</summary>
    private static void Duration(TimeSpan value, TimeSpan minimum, TimeSpan maximum, string name)
    {
        if (value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(name);
    }
}

/// <summary>Configures bounded schema planning and application.</summary>
public sealed class HPDBaseSchemaOptions
{
    /// <summary>Gets or sets the required 32-byte host-owned schema-plan encryption key.</summary>
    public byte[] PlanProtectionKey { get; set; } = [];
    /// <summary>Gets or sets the optional 32-byte key used to authenticate external-migration attestations.</summary>
    public byte[] ExternalMigrationAttestationKey { get; set; } = [];
    /// <summary>Gets or sets the stable application identity bound into schema state and protected plans.</summary>
    public string ApplicationId { get; set; } = "hpd.base.application";
    /// <summary>Gets or sets the application-owned logical contract version.</summary>
    public string ContractVersion { get; set; } = "1";
    /// <summary>Gets or sets max Collections.</summary>
    public int MaxCollections { get; set; } = 512;
    /// <summary>Gets or sets max Fields Per Collection.</summary>
    public int MaxFieldsPerCollection { get; set; } = 512;
    /// <summary>Gets or sets max Relations.</summary>
    public int MaxRelations { get; set; } = 1_024;
    /// <summary>Gets or sets max Indexes.</summary>
    public int MaxIndexes { get; set; } = 1_024;
    /// <summary>Gets or sets max Read Definitions.</summary>
    public int MaxReadDefinitions { get; set; } = 512;
    /// <summary>Gets or sets max Plan Operations.</summary>
    public int MaxPlanOperations { get; set; } = 2_048;
    /// <summary>Gets or sets max Plan Artifact Bytes.</summary>
    public int MaxPlanArtifactBytes { get; set; } = 4_194_304;
    /// <summary>Gets or sets plan Lifetime.</summary>
    public TimeSpan PlanLifetime { get; set; } = TimeSpan.FromMinutes(15);
    /// <summary>Gets or sets migration Lease Timeout.</summary>
    public TimeSpan MigrationLeaseTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets max Apply Duration.</summary>
    public TimeSpan MaxApplyDuration { get; set; } = TimeSpan.FromMinutes(5);
    /// <summary>Gets or sets commit Completion Timeout.</summary>
    public TimeSpan CommitCompletionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets history Page Size.</summary>
    public int HistoryPageSize { get; set; } = 100;

    /// <summary>Performs validate.</summary>
    internal void Validate()
    {
        BaseApplicationId.Validate(ApplicationId, nameof(ApplicationId));
        BaseApplicationId.Validate(ContractVersion, nameof(ContractVersion));
        Bounded(MaxCollections, 5_120, nameof(MaxCollections));
        Bounded(MaxFieldsPerCollection, 10_000, nameof(MaxFieldsPerCollection));
        Bounded(MaxRelations, 10_240, nameof(MaxRelations));
        Bounded(MaxIndexes, 10_240, nameof(MaxIndexes));
        Bounded(MaxReadDefinitions, 5_120, nameof(MaxReadDefinitions));
        Bounded(MaxPlanOperations, 10_000, nameof(MaxPlanOperations));
        if (MaxPlanArtifactBytes < 1_024 || MaxPlanArtifactBytes > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(MaxPlanArtifactBytes));
        Duration(PlanLifetime, TimeSpan.FromMinutes(1), TimeSpan.FromHours(24), nameof(PlanLifetime));
        Duration(MigrationLeaseTimeout, TimeSpan.FromMilliseconds(10), TimeSpan.FromMinutes(1), nameof(MigrationLeaseTimeout));
        Duration(MaxApplyDuration, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(30), nameof(MaxApplyDuration));
        Duration(CommitCompletionTimeout, TimeSpan.FromMilliseconds(10), TimeSpan.FromMinutes(1), nameof(CommitCompletionTimeout));
        Bounded(HistoryPageSize, 1_000, nameof(HistoryPageSize));
        if (PlanProtectionKey.Length is> 0 and not 32)
            throw new ArgumentOutOfRangeException(nameof(PlanProtectionKey), "Schema plan protection key must contain exactly 32 bytes.");
        if (ExternalMigrationAttestationKey.Length is> 0 and not 32)
            throw new ArgumentOutOfRangeException(nameof(ExternalMigrationAttestationKey), "External migration attestation key must contain exactly 32 bytes.");
    }

    /// <summary>Performs bounded.</summary>
    private static void Bounded(int value, int maximum, string name)
    {
        if (value < 1 || value > maximum)
            throw new ArgumentOutOfRangeException(name);
    }

    /// <summary>Performs duration.</summary>
    private static void Duration(TimeSpan value, TimeSpan minimum, TimeSpan maximum, string name)
    {
        if (value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(name);
    }
}
