using System.Security.Cryptography;
using System.Text;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Data.Sqlite;

namespace HPD.Gateway.ControlPlane.Sqlite;

public sealed class GatewaySqliteAuthorityOptions
{
    private byte[]? _planProtectionKey;
    private byte[]? _tokenProtectionKey;
    private byte[]? _desiredStateTokenKey;
    private byte[]? _epochReservationKey;

    public string? ConnectionString { get; set; }
    public string? DataSource { get; set; }
    public bool EnableWal { get; set; } = true;
    public TimeSpan BusyTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public bool InitializeSQLitePCLRaw { get; set; } = true;
    public byte[]? PlanProtectionKey
    {
        get => Copy(_planProtectionKey);
        set => _planProtectionKey = Copy(value);
    }
    public byte[]? TokenProtectionKey
    {
        get => Copy(_tokenProtectionKey);
        set => _tokenProtectionKey = Copy(value);
    }
    public byte[]? DesiredStateTokenKey
    {
        get => Copy(_desiredStateTokenKey);
        set => _desiredStateTokenKey = Copy(value);
    }
    public byte[]? EpochReservationKey
    {
        get => Copy(_epochReservationKey);
        set => _epochReservationKey = Copy(value);
    }

    internal GatewaySqliteAuthoritySnapshot Snapshot()
    {
        Validate();
        return new GatewaySqliteAuthoritySnapshot(
            ConnectionString,
            DataSource,
            EnableWal,
            BusyTimeout,
            CommandTimeout,
            InitializeSQLitePCLRaw,
            _planProtectionKey!,
            _tokenProtectionKey!,
            _desiredStateTokenKey!,
            _epochReservationKey!);
    }

    private void Validate()
    {
        bool hasConnectionString = !string.IsNullOrWhiteSpace(ConnectionString);
        bool hasDataSource = !string.IsNullOrWhiteSpace(DataSource);
        if (hasConnectionString == hasDataSource)
            throw new ArgumentException("Configure exactly one SQLite ConnectionString or DataSource.");
        ValidateText(ConnectionString, 4_096, nameof(ConnectionString));
        ValidateText(DataSource, 1_024, nameof(DataSource));
        if (DataSource is not null && !IsFileBacked(DataSource))
            throw new ArgumentException("Restart-durable SQLite authority requires a file-backed data source.", nameof(DataSource));
        if (ConnectionString is not null)
        {
            try
            {
                var parsed = new SqliteConnectionStringBuilder(ConnectionString);
                if (string.IsNullOrWhiteSpace(parsed.DataSource))
                    throw new ArgumentException("The SQLite connection string must select a data source.", nameof(ConnectionString));
                if (parsed.Mode == SqliteOpenMode.Memory || !IsFileBacked(parsed.DataSource))
                    throw new ArgumentException("Restart-durable SQLite authority requires a file-backed connection string.", nameof(ConnectionString));
            }
            catch (ArgumentException exception) when (exception.ParamName != nameof(ConnectionString))
            {
                throw new ArgumentException("The SQLite connection string is invalid.", nameof(ConnectionString), exception);
            }
        }
        if (BusyTimeout < TimeSpan.FromMilliseconds(1) || BusyTimeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(BusyTimeout));
        if (CommandTimeout < TimeSpan.FromSeconds(1) || CommandTimeout > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(CommandTimeout));

        byte[][] keys =
        [
            RequireKey(_planProtectionKey, nameof(PlanProtectionKey)),
            RequireKey(_tokenProtectionKey, nameof(TokenProtectionKey)),
            RequireKey(_desiredStateTokenKey, nameof(DesiredStateTokenKey)),
            RequireKey(_epochReservationKey, nameof(EpochReservationKey)),
        ];
        for (int left = 0; left < keys.Length; left++)
            for (int right = left + 1; right < keys.Length; right++)
                if (CryptographicOperations.FixedTimeEquals(keys[left], keys[right]))
                    throw new ArgumentException("SQLite authority protection keys must be distinct.");
    }

    private static void ValidateText(string? value, int maximumUtf8Bytes, string name)
    {
        if (value is null)
            return;
        if (!value.IsNormalized(NormalizationForm.FormC) ||
            value.Any(char.IsControl) ||
            Encoding.UTF8.GetByteCount(value) > maximumUtf8Bytes)
            throw new ArgumentException($"{name} is invalid.", name);
    }

    private static byte[] RequireKey(byte[]? value, string name) =>
        value is { Length: 32 }
            ? value
            : throw new ArgumentException($"{name} must contain exactly 32 bytes.", name);

    private static byte[]? Copy(byte[]? value) => value is null ? null : [.. value];

    // URI filenames can select SQLite's memory database or a memory-backed VFS through
    // URI parameters. This restart-durable product accepts only conventional filesystem
    // paths; URI filename support requires a separately reviewed durable-path contract.
    private static bool IsFileBacked(string value) =>
        !StringComparer.OrdinalIgnoreCase.Equals(value, ":memory:") &&
        !value.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
}

internal sealed class GatewaySqliteAuthoritySnapshot
{
    private readonly byte[] _planProtectionKey;
    private readonly byte[] _tokenProtectionKey;
    private readonly byte[] _desiredStateTokenKey;
    private readonly byte[] _epochReservationKey;

    internal GatewaySqliteAuthoritySnapshot(
        string? connectionString,
        string? dataSource,
        bool enableWal,
        TimeSpan busyTimeout,
        TimeSpan commandTimeout,
        bool initializeSQLitePCLRaw,
        byte[] planProtectionKey,
        byte[] tokenProtectionKey,
        byte[] desiredStateTokenKey,
        byte[] epochReservationKey)
    {
        ConnectionString = connectionString;
        DataSource = dataSource;
        EnableWal = enableWal;
        BusyTimeout = busyTimeout;
        CommandTimeout = commandTimeout;
        InitializeSQLitePCLRaw = initializeSQLitePCLRaw;
        _planProtectionKey = [.. planProtectionKey];
        _tokenProtectionKey = [.. tokenProtectionKey];
        _desiredStateTokenKey = [.. desiredStateTokenKey];
        _epochReservationKey = [.. epochReservationKey];
    }

    internal string? ConnectionString { get; }
    internal string? DataSource { get; }
    internal bool EnableWal { get; }
    internal TimeSpan BusyTimeout { get; }
    internal TimeSpan CommandTimeout { get; }
    internal bool InitializeSQLitePCLRaw { get; }
    internal byte[] PlanProtectionKey => [.. _planProtectionKey];
    internal byte[] TokenProtectionKey => [.. _tokenProtectionKey];
    internal byte[] DesiredStateTokenKey => [.. _desiredStateTokenKey];
    internal byte[] EpochReservationKey => [.. _epochReservationKey];
}

public static class GatewaySqliteControlPlaneExtensions
{
    public static GatewayControlPlaneBuilder UseSqlite(
        this GatewayControlPlaneBuilder builder,
        Action<GatewaySqliteAuthorityOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new GatewaySqliteAuthorityOptions();
        configure(options);
        GatewaySqliteAuthoritySnapshot snapshot = options.Snapshot();

        builder.ConfigureAuthority(management =>
        {
            management.RequiredDurability = GatewayAuthorityDurability.RestartDurable;
            management.DesiredStateTokenKey = snapshot.DesiredStateTokenKey;
            management.EpochReservationKey = snapshot.EpochReservationKey;
        }, hpdBase =>
        {
            hpdBase.ConfigureSchema(schema => schema.PlanProtectionKey = snapshot.PlanProtectionKey);
            hpdBase.ConfigureTokenProtection(tokens => tokens.ActiveKey = new BaseOpaqueTokenKey
            {
                Id = 1,
                Key = snapshot.TokenProtectionKey,
                IssueNotBefore = DateTimeOffset.UnixEpoch,
            });
            hpdBase.UseStore(SqliteStore.Configure(sqlite => Project(snapshot, sqlite)));
        }, "gateway-management");
        return builder;
    }

    private static void Project(GatewaySqliteAuthoritySnapshot source, HPDBaseSqliteOptions target)
    {
        target.StoreId = "gateway-management";
        target.ModuleId = "hpd.gateway.control-plane.sqlite";
        target.ModuleName = "HPD Gateway Control Plane SQLite";
        target.StoreVersion = "1";
        target.ConnectionString = source.ConnectionString;
        target.DataSource = source.DataSource;
        target.SchemaPrefix = "hpd_gateway";
        target.EnableWal = source.EnableWal;
        target.BusyTimeout = source.BusyTimeout;
        target.CommandTimeout = source.CommandTimeout;
        target.DefaultPageSize = 64;
        target.MaxPageSize = 256;
        target.MaxFilterDepth = 8;
        target.MaxFilterNodes = 128;
        target.MaxInValues = 256;
        target.MaxSortFields = 8;
        target.MaxSelectFields = 64;
        target.AllowClientRequestedIds = true;
        target.ContributeModuleDescriptor = true;
        target.ContributeCapabilities = true;
        target.ContributeHealth = true;
        target.ContributeDiagnostics = true;
        target.ContributeRelationalDescriptors = true;
        target.InitializeSQLitePCLRaw = source.InitializeSQLitePCLRaw;
        target.MutationJournalRetention = TimeSpan.FromDays(7);
        target.MutationJournalMaxEntries = 100_000;
        target.MutationJournalMaxReadSize = 1_000;
        target.MaxTrackedMutationExecutions = 8;
        target.QuarantinedMutationDrainTimeout = TimeSpan.FromSeconds(5);
        target.AdministrationEnabled = true;
        target.MaxBackupArtifactBytes = 4L * 1024 * 1024 * 1024;
        target.AdministrationAcquisitionTimeout = TimeSpan.FromSeconds(30);
        target.NativeBackupCompletionWait = TimeSpan.FromMinutes(5);
        target.RestoreStagingTimeout = TimeSpan.FromMinutes(10);
        target.IntegrityCheckTimeout = TimeSpan.FromMinutes(5);
        target.MaxQuarantinedAdministrationExecutions = 1;
        target.HealthRefId = "gateway-management-sqlite-health";
        target.DiagnosticRefId = "gateway-management-sqlite-diagnostics";
    }
}
