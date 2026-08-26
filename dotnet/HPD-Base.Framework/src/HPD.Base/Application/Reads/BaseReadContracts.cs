using System.Text.Json.Serialization.Metadata;
using System.ComponentModel;

namespace HPD.Base;

/// <summary>Identifies one typed generated read parameter.</summary>
public sealed class BaseReadParameter<TParameters, TValue>
{
    internal BaseReadParameter(string id) => Id = id;

    /// <summary>Gets the stable parameter identifier.</summary>
    public string Id { get; }
}

/// <summary>Identifies one typed generated read projection field.</summary>
public sealed class BaseReadField<TRow, TValue>
{
    internal BaseReadField(string id) => Id = id;

    /// <summary>Gets the stable projection-field identifier.</summary>
    public string Id { get; }
}

/// <summary>Describes one immutable generated read registration.</summary>
public sealed class BaseReadDefinition<TParameters, TRow> : IBaseReadRegistration
{
    internal BaseReadDefinition(
        BaseRelationalReadPlan plan,
        JsonTypeInfo<TParameters>? parameterJsonTypeInfo,
        JsonTypeInfo<TRow>? rowJsonTypeInfo,
        IBaseReadParameterCodec<TParameters> parameterCodec,
        IBaseReadRowCodec<TRow> rowCodec,
        BaseReadClientContract clientContract)
    {
        Plan = plan;
        _parameterJsonTypeInfo = parameterJsonTypeInfo;
        _rowJsonTypeInfo = rowJsonTypeInfo;
        ParameterCodec = parameterCodec;
        RowCodec = rowCodec;
        ClientContract = clientContract;
        Handle = new BaseReadHandle<TParameters, TRow>(this);
    }

    /// <summary>Gets the stable read-definition identifier.</summary>
    public string Id => Plan.Id;

    /// <summary>Gets the registered read's explicit HTTP exposure.</summary>
    public BaseReadExposure Exposure { get; internal init; }

    /// <summary>Gets the minimum principal authorization required to invoke the read.</summary>
    public BaseReadAuthorization Authorization { get; internal init; } = BaseReadAuthorization.Authenticated;

    /// <summary>Gets the result disclosure authority.</summary>
    public BaseRegisteredReadDisclosure Disclosure { get; internal init; }

    /// <summary>Gets the source authority.</summary>
    public BaseRegisteredReadSourceAuthority SourceAuthority { get; internal init; }

    /// <summary>Gets the sole endpoint audience.</summary>
    public HPDBaseEndpointAudience Audience { get; internal init; } = HPDBaseEndpointAudience.Application;

    /// <summary>Gets the exact invocation grant identifier.</summary>
    public string RequiredGrantId { get; internal init; } = string.Empty;

    /// <summary>Gets the declared Confidential result-field identifiers.</summary>
    public IReadOnlyList<string> ConfidentialOutputFieldIds { get; internal init; } = Array.Empty<string>();

    /// <summary>Gets the declared Secret result-field identifiers.</summary>
    public IReadOnlyList<string> SecretOutputFieldIds { get; internal init; } = Array.Empty<string>();

    /// <summary>Gets the exact declared system source collection identifiers.</summary>
    public IReadOnlyList<string> SystemSourceIds { get; internal init; } = Array.Empty<string>();

    internal BaseRelationalReadPlan Plan { get; private set; }

    /// <summary>Gets source-generated request metadata.</summary>
    private JsonTypeInfo<TParameters>? _parameterJsonTypeInfo;
    internal JsonTypeInfo<TParameters> ParameterJsonTypeInfo => _parameterJsonTypeInfo ?? throw new InvalidOperationException("base.schema.serializer.ownerRequired");

    /// <summary>Gets source-generated row metadata.</summary>
    private JsonTypeInfo<TRow>? _rowJsonTypeInfo;
    internal JsonTypeInfo<TRow> RowJsonTypeInfo => _rowJsonTypeInfo ?? throw new InvalidOperationException("base.schema.serializer.ownerRequired");

    /// <summary>Gets the sole typed invocation handle.</summary>
    public BaseReadHandle<TParameters, TRow> Handle { get; }

    internal IBaseReadParameterCodec<TParameters> ParameterCodec { get; }
    internal IBaseReadRowCodec<TRow> RowCodec { get; }
    /// <summary>Gets the exact language-neutral client-generation contract.</summary>
    public BaseReadClientContract ClientContract { get; }
    internal BaseSerializerContextRegistration? SerializerRegistration { get; set; }
    /// <summary>Gets the serializer checksum for parameters.</summary>
    public string ParameterSerializerContractChecksum { get; internal set; } = string.Empty;
    /// <summary>Gets the serializer checksum for result rows.</summary>
    public string RowSerializerContractChecksum { get; internal set; } = string.Empty;

    JsonTypeInfo IBaseReadRegistration.ParameterJsonTypeInfo => ParameterJsonTypeInfo;
    JsonTypeInfo IBaseReadRegistration.RowJsonTypeInfo => RowJsonTypeInfo;
    BaseRelationalReadPlan IBaseReadRegistration.Plan => Plan;
    void IBaseReadRegistration.BindPlan(BaseRelationalReadPlan plan) => Plan = plan ?? throw new ArgumentNullException(nameof(plan));
    Type IBaseReadRegistration.ResponseType => typeof(BasePage<TRow>);
    BaseReadExposure IBaseReadRegistration.Exposure => Exposure;
    BaseReadAuthorization IBaseReadRegistration.Authorization => Authorization;
    BaseRegisteredReadDisclosure IBaseReadRegistration.Disclosure => Disclosure;
    BaseRegisteredReadSourceAuthority IBaseReadRegistration.SourceAuthority => SourceAuthority;
    HPDBaseEndpointAudience IBaseReadRegistration.Audience => Audience;
    string IBaseReadRegistration.RequiredGrantId => RequiredGrantId;
    IReadOnlyList<string> IBaseReadRegistration.ConfidentialOutputFieldIds => ConfidentialOutputFieldIds;
    IReadOnlyList<string> IBaseReadRegistration.SecretOutputFieldIds => SecretOutputFieldIds;
    IReadOnlyList<string> IBaseReadRegistration.SystemSourceIds => SystemSourceIds;
    BaseReadClientContract IBaseReadRegistration.ClientContract => ClientContract;
    string IBaseReadRegistration.ParameterSerializerContractChecksum => ParameterSerializerContractChecksum;
    string IBaseReadRegistration.RowSerializerContractChecksum => RowSerializerContractChecksum;
    BaseSerializerContextRegistration? IBaseSerializerMetadataSource.Registration => SerializerRegistration;
    IReadOnlyList<Type> IBaseSerializerMetadataSource.RootTypes => [typeof(TParameters), typeof(TRow)];
    IReadOnlyList<BaseSerializerPropertyDeclaration>? IBaseSerializerMetadataSource.SerializerDeclarations =>
        ParameterDeclarations is null || RowDeclarations is null ? null : [.. ParameterDeclarations, .. RowDeclarations];
    IReadOnlyList<JsonTypeInfo> IBaseSerializerMetadataSource.Roots => _parameterJsonTypeInfo is null || _rowJsonTypeInfo is null ? [] : [_parameterJsonTypeInfo, _rowJsonTypeInfo];
    CollectionDefinition? IBaseSerializerMetadataSource.CollectionDefinition => null;
    void IBaseSerializerMetadataSource.Bind(BaseSerializerMetadataOwner owner)
    {
        if (SerializerRegistration is null) return;
        _parameterJsonTypeInfo = (JsonTypeInfo<TParameters>)owner.Resolve(this, typeof(TParameters));
        _rowJsonTypeInfo = (JsonTypeInfo<TRow>)owner.Resolve(this, typeof(TRow));
        ParameterSerializerContractChecksum = BaseSerializerContract.Checksum(_parameterJsonTypeInfo,
            ClientContract.Parameters.Select(static value => (value.Id, value.GeneratedName, value.WireName)), ParameterDeclarations);
        RowSerializerContractChecksum = BaseSerializerContract.Checksum(_rowJsonTypeInfo,
            ClientContract.Row.Select(static value => (value.Id, value.GeneratedName, value.WireName)), RowDeclarations);
    }
    internal IReadOnlyList<BaseSerializerPropertyDeclaration>? ParameterDeclarations { get; set; }
    internal IReadOnlyList<BaseSerializerPropertyDeclaration>? RowDeclarations { get; set; }

    async ValueTask<BaseUntypedRegisteredReadResult> IBaseReadRegistration.ExecuteAsync(
        IBaseRegisteredReadRuntime runtime,
        object parameters,
        BaseReadPageRequest page,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken)
    {
        if (parameters is not TParameters typed)
            return new BaseUntypedRegisteredReadResult { Status = OperationStatus.ValidationFailed, Error = new BaseError { Code = "base.relational.read.invalid", Message = "Registered read parameters are invalid.", Category = ErrorCategory.Validation } };
        OperationResult<BaseRegisteredReadEvaluation<TRow>> result = await runtime.ExecuteAsync(this, typed, new BaseRegisteredReadWindow
        {
            Kind = BaseRegisteredReadWindowKind.Page,
            Page = page.Page,
            PerPage = page.PerPage,
        }, principal, operation, cancellationToken).ConfigureAwait(false);
        return result.Value is null
            ? new BaseUntypedRegisteredReadResult { Status = result.Status, Error = result.Error }
            : new BaseUntypedRegisteredReadResult { Status = result.Status, Items = result.Value.Page.Items.Cast<object>().ToArray(), Page = result.Value.Page.Page, Count = result.Value.Page.Count, Dependencies = result.Value.Dependencies };
    }
}

/// <summary>Generated closed codec for registered-read request parameters.</summary>
public interface IBaseReadParameterCodec<in TParameters>
{
    /// <summary>Encodes every declared parameter without reflection.</summary>
    BaseRelationalParameterValue[] Encode(TParameters parameters);
}

/// <summary>Generated closed codec for one registered-read result row.</summary>
public interface IBaseReadRowCodec<out TRow>
{
    /// <summary>Decodes one validated canonical row without reflection.</summary>
    TRow Decode(BaseRelationalRow row);
}

internal interface IBaseReadRegistration : IBaseSerializerMetadataSource
{
    /// <summary>Gets the ID.</summary>
    string Id { get; }
    /// <summary>Gets the plan.</summary>
    BaseRelationalReadPlan Plan { get; }
    void BindPlan(BaseRelationalReadPlan plan);
    /// <summary>Gets the parameter JSON type info.</summary>
    JsonTypeInfo ParameterJsonTypeInfo { get; }
    /// <summary>Gets the row JSON type info.</summary>
    JsonTypeInfo RowJsonTypeInfo { get; }
    IReadOnlyList<JsonTypeInfo> IBaseSerializerMetadataSource.Roots => [ParameterJsonTypeInfo, RowJsonTypeInfo];
    bool IBaseSerializerMetadataSource.Generated => true;
    BaseSerializerContextRegistration? IBaseSerializerMetadataSource.Registration => null;
    IReadOnlyList<Type> IBaseSerializerMetadataSource.RootTypes => [ParameterJsonTypeInfo.Type, RowJsonTypeInfo.Type];
    IReadOnlyList<BaseSerializerPropertyDeclaration>? IBaseSerializerMetadataSource.SerializerDeclarations => null;
    void IBaseSerializerMetadataSource.Bind(BaseSerializerMetadataOwner owner) { }
    CollectionDefinition? IBaseSerializerMetadataSource.CollectionDefinition => null;
    /// <summary>Gets the response type.</summary>
    Type ResponseType { get; }
    /// <summary>Gets the explicit HTTP exposure.</summary>
    BaseReadExposure Exposure { get; }
    /// <summary>Gets the minimum invocation authorization.</summary>
    BaseReadAuthorization Authorization { get; }
    /// <summary>Gets the disclosure authority.</summary>
    BaseRegisteredReadDisclosure Disclosure { get; }
    /// <summary>Gets the source authority.</summary>
    BaseRegisteredReadSourceAuthority SourceAuthority { get; }
    /// <summary>Gets the endpoint audience.</summary>
    HPDBaseEndpointAudience Audience { get; }
    /// <summary>Gets the required grant identifier.</summary>
    string RequiredGrantId { get; }
    /// <summary>Gets the confidential output IDs.</summary>
    IReadOnlyList<string> ConfidentialOutputFieldIds { get; }
    /// <summary>Gets the secret output IDs.</summary>
    IReadOnlyList<string> SecretOutputFieldIds { get; }
    /// <summary>Gets the declared system source IDs.</summary>
    IReadOnlyList<string> SystemSourceIds { get; }
    /// <summary>Gets the exact language-neutral client contract.</summary>
    BaseReadClientContract ClientContract { get; }
    string ParameterSerializerContractChecksum { get; }
    string RowSerializerContractChecksum { get; }
    /// <summary>Executes the execute async operation.</summary>
    ValueTask<BaseUntypedRegisteredReadResult> ExecuteAsync(IBaseRegisteredReadRuntime runtime, object parameters, BaseReadPageRequest page, PrincipalContext principal, OperationContext operation, CancellationToken cancellationToken);
}

/// <summary>Describes one source-generated registered-read client contract without reflection.</summary>
public sealed record BaseReadClientContract
{
    private BaseReadClientProperty[] _parameters = [];
    private BaseReadClientProperty[] _row = [];
    /// <summary>Gets the stable parameter DTO identifier.</summary>
    public required string ParameterTypeId { get; init; }
    /// <summary>Gets the stable result-row DTO identifier.</summary>
    public required string RowTypeId { get; init; }
    /// <summary>Gets the closed parameter properties.</summary>
    public required IReadOnlyList<BaseReadClientProperty> Parameters { get => Array.AsReadOnly(_parameters); init => _parameters = value?.ToArray() ?? throw new ArgumentNullException(nameof(value)); }
    /// <summary>Gets the closed result-row properties.</summary>
    public required IReadOnlyList<BaseReadClientProperty> Row { get => Array.AsReadOnly(_row); init => _row = value?.ToArray() ?? throw new ArgumentNullException(nameof(value)); }
}

/// <summary>Describes one bounded registered-read DTO property.</summary>
public sealed record BaseReadClientProperty
{
    /// <summary>Gets the stable wire identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the deterministic generated property name.</summary>
    public required string GeneratedName { get; init; }
    /// <summary>Gets the exact serializer-owned wire name.</summary>
    public required string WireName { get; init; }
    /// <summary>Gets the closed query-value kind.</summary>
    public required QueryValueKind Kind { get; init; }
    /// <summary>Gets whether the value is a bounded array.</summary>
    public required bool Array { get; init; }
    /// <summary>Gets whether the property may contain null.</summary>
    public required bool Nullable { get; init; }
}

internal sealed record BaseUntypedRegisteredReadResult
{
    internal OperationStatus Status { get; init; }
    internal BaseError? Error { get; init; }
    internal object[]? Items { get; init; }
    internal PageInfo? Page { get; init; }
    internal CountInfo? Count { get; init; }
    internal BaseDependencySet? Dependencies { get; init; }
}

internal sealed class BaseReadRegistry(IReadOnlyDictionary<string, IBaseReadRegistration> registrations)
{
    internal IReadOnlyDictionary<string, IBaseReadRegistration> Registrations { get; } = registrations;
}

/// <summary>Provides compile-time-safe invocation of one registered read.</summary>
public sealed class BaseReadHandle<TParameters, TRow>
{
    internal BaseReadHandle(BaseReadDefinition<TParameters, TRow> definition) =>
        Definition = definition;

    internal BaseReadDefinition<TParameters, TRow> Definition { get; }

    /// <summary>Gets the stable read-definition identifier.</summary>
    public string Id => Definition.Id;
}

/// <summary>Defines a bounded page request for a registered read.</summary>
/// <param name="Page">The one-based page number.</param>
/// <param name="PerPage">The positive page size.</param>
public readonly record struct BaseReadPageRequest(int Page, int PerPage)
{
    /// <summary>Creates and validates a page request.</summary>
    public static BaseReadPageRequest Create(int page, int perPage)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(perPage, 1);
        return new BaseReadPageRequest(page, perPage);
    }
}

/// <summary>Defines a bounded arbitrary-offset request for an offset-enabled registered read.</summary>
/// <param name="Offset">The zero-based row offset.</param>
/// <param name="Limit">The positive result limit.</param>
public readonly record struct BaseReadOffsetRequest(int Offset, int Limit)
{
    /// <summary>Creates and validates an arbitrary-offset request.</summary>
    public static BaseReadOffsetRequest Create(int offset, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        return new BaseReadOffsetRequest(offset, limit);
    }
}

/// <summary>Infrastructure hooks used only by generated registered-read code.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class BaseReadGeneratedContract
{
    /// <summary>Creates one generated parameter handle.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static BaseReadParameter<TParameters, TValue> Parameter<TParameters, TValue>(string id) => new(id);

    /// <summary>Creates one generated projection-field handle.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static BaseReadField<TRow, TValue> Field<TRow, TValue>(string id) => new(id);

    /// <summary>Runs generated definition configuration once and freezes it.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static BaseReadDefinition<TParameters, TRow> Create<TParameters, TRow>(
        string id,
        JsonTypeInfo<TParameters> parameterJson,
        JsonTypeInfo<TRow> rowJson,
        BaseRelationalReadParameter[] parameters,
        IBaseReadParameterCodec<TParameters> parameterCodec,
        IBaseReadRowCodec<TRow> rowCodec,
        BaseReadExposure exposure,
        BaseReadAuthorization authorization,
        BaseRegisteredReadDisclosure disclosure,
        BaseRegisteredReadSourceAuthority sourceAuthority,
        HPDBaseEndpointAudience audience,
        string requiredGrantId,
        string[] confidentialOutputFieldIds,
        string[] secretOutputFieldIds,
        string[] systemSourceIds,
        BaseReadClientContract clientContract,
        Action<BaseReadDefinitionBuilder<TParameters, TRow>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(clientContract);
        var builder = new BaseReadDefinitionBuilder<TParameters, TRow>(
            id, parameters);
        configure(builder);
        if (!Enum.IsDefined(exposure) || !Enum.IsDefined(authorization) || !Enum.IsDefined(disclosure) ||
            !Enum.IsDefined(sourceAuthority) || !Enum.IsDefined(audience))
            throw new ArgumentOutOfRangeException(nameof(exposure));
        if (exposure == BaseReadExposure.Admin && authorization == BaseReadAuthorization.Authenticated)
            throw new InvalidOperationException("An admin-exposed registered read must require admin or system authorization.");
        string[] confidential = NormalizeIds(confidentialOutputFieldIds, nameof(confidentialOutputFieldIds));
        string[] secret = NormalizeIds(secretOutputFieldIds, nameof(secretOutputFieldIds));
        string[] systemSources = NormalizeIds(systemSourceIds, nameof(systemSourceIds));
        if (string.IsNullOrWhiteSpace(requiredGrantId))
            throw new InvalidOperationException("A registered read must declare one exact operation grant.");
        BaseApplicationId.Validate(requiredGrantId, nameof(requiredGrantId));
        if (disclosure == BaseRegisteredReadDisclosure.Ordinary && (confidential.Length != 0 || secret.Length != 0) ||
            disclosure == BaseRegisteredReadDisclosure.ConfidentialProjection && secret.Length != 0 ||
            sourceAuthority == BaseRegisteredReadSourceAuthority.Ordinary && systemSources.Length != 0 ||
            sourceAuthority == BaseRegisteredReadSourceAuthority.System && systemSources.Length == 0)
            throw new InvalidOperationException("The registered read confidentiality declaration is inconsistent.");
        parameterJson.Options.MakeReadOnly(); parameterJson.MakeReadOnly();
        rowJson.Options.MakeReadOnly(); rowJson.MakeReadOnly();
        var definition = new BaseReadDefinition<TParameters, TRow>(
            builder.Build(),
            parameterJson,
            rowJson,
            parameterCodec,
            rowCodec,
            clientContract)
        {
            Exposure = exposure,
            Authorization = authorization,
            Disclosure = disclosure,
            SourceAuthority = sourceAuthority,
            Audience = audience,
            RequiredGrantId = new string(requiredGrantId.AsSpan()),
            ConfidentialOutputFieldIds = Array.AsReadOnly(confidential),
            SecretOutputFieldIds = Array.AsReadOnly(secret),
            SystemSourceIds = Array.AsReadOnly(systemSources),
        };
        definition.ParameterSerializerContractChecksum = BaseSerializerContract.Checksum(parameterJson,
            clientContract.Parameters.Select(static value => (value.Id, value.GeneratedName, value.WireName)));
        definition.RowSerializerContractChecksum = BaseSerializerContract.Checksum(rowJson,
            clientContract.Row.Select(static value => (value.Id, value.GeneratedName, value.WireName)));
        return definition;
    }

    /// <summary>Creates a generated read from an opaque serializer registration and closed declarations.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static BaseReadDefinition<TParameters, TRow> CreateGenerated<TParameters, TRow>(
        string id,
        BaseSerializerContextRegistration registration,
        IReadOnlyList<BaseSerializerPropertyDeclaration> parameterDeclarations,
        IReadOnlyList<BaseSerializerPropertyDeclaration> rowDeclarations,
        BaseRelationalReadParameter[] parameters,
        IBaseReadParameterCodec<TParameters> parameterCodec,
        IBaseReadRowCodec<TRow> rowCodec,
        BaseReadExposure exposure,
        BaseReadAuthorization authorization,
        BaseRegisteredReadDisclosure disclosure,
        BaseRegisteredReadSourceAuthority sourceAuthority,
        HPDBaseEndpointAudience audience,
        string requiredGrantId,
        string[] confidentialOutputFieldIds,
        string[] secretOutputFieldIds,
        string[] systemSourceIds,
        BaseReadClientContract clientContract,
        Action<BaseReadDefinitionBuilder<TParameters, TRow>> configure)
    {
        ArgumentNullException.ThrowIfNull(registration);
        registration.AssertOwner(typeof(TParameters));
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(clientContract);
        var builder = new BaseReadDefinitionBuilder<TParameters, TRow>(id, parameters);
        configure(builder);
        if (!Enum.IsDefined(exposure) || !Enum.IsDefined(authorization) || !Enum.IsDefined(disclosure) ||
            !Enum.IsDefined(sourceAuthority) || !Enum.IsDefined(audience)) throw new ArgumentOutOfRangeException(nameof(exposure));
        if (exposure == BaseReadExposure.Admin && authorization == BaseReadAuthorization.Authenticated)
            throw new InvalidOperationException("An admin-exposed registered read must require admin or system authorization.");
        string[] confidential = NormalizeIds(confidentialOutputFieldIds, nameof(confidentialOutputFieldIds));
        string[] secret = NormalizeIds(secretOutputFieldIds, nameof(secretOutputFieldIds));
        string[] systemSources = NormalizeIds(systemSourceIds, nameof(systemSourceIds));
        if (string.IsNullOrWhiteSpace(requiredGrantId)) throw new InvalidOperationException("A registered read must declare one exact operation grant.");
        BaseApplicationId.Validate(requiredGrantId, nameof(requiredGrantId));
        if (disclosure == BaseRegisteredReadDisclosure.Ordinary && (confidential.Length != 0 || secret.Length != 0) ||
            disclosure == BaseRegisteredReadDisclosure.ConfidentialProjection && secret.Length != 0 ||
            sourceAuthority == BaseRegisteredReadSourceAuthority.Ordinary && systemSources.Length != 0 ||
            sourceAuthority == BaseRegisteredReadSourceAuthority.System && systemSources.Length == 0)
            throw new InvalidOperationException("The registered read confidentiality declaration is inconsistent.");
        var definition = new BaseReadDefinition<TParameters, TRow>(builder.Build(), null, null, parameterCodec, rowCodec, clientContract)
        {
            Exposure = exposure, Authorization = authorization, Disclosure = disclosure, SourceAuthority = sourceAuthority,
            Audience = audience, RequiredGrantId = new string(requiredGrantId.AsSpan()),
            ConfidentialOutputFieldIds = Array.AsReadOnly(confidential), SecretOutputFieldIds = Array.AsReadOnly(secret),
            SystemSourceIds = Array.AsReadOnly(systemSources),
        };
        definition.SerializerRegistration = registration;
        definition.ParameterDeclarations = parameterDeclarations.ToArray();
        definition.RowDeclarations = rowDeclarations.ToArray();
        return definition;
    }

    private static string[] NormalizeIds(string[]? ids, string parameter)
    {
        ArgumentNullException.ThrowIfNull(ids, parameter);
        string[] result = ids.Select(static id => new string(id.AsSpan())).OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        foreach (string id in result) BaseApplicationId.Validate(id, parameter);
        if (result.Distinct(StringComparer.Ordinal).Count() != result.Length)
            throw new InvalidOperationException("Registered read identifiers must be unique.");
        return result;
    }

    /// <summary>Encodes one generated supported scalar parameter.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static QueryValue Value<TValue>(TValue value) => BaseQueryValue.From(value);

    /// <summary>Decodes one generated supported scalar projection value.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TValue Read<TValue>(BaseRelationalRow row, string fieldId)
    {
        QueryValue value = row.Fields.SingleOrDefault(field => string.Equals(field.FieldId, fieldId, StringComparison.Ordinal))?.Value
            ?? throw new InvalidOperationException("The provider omitted a required registered-read projection field.");
        QueryValueKind expected = typeof(TValue) == typeof(string) ? QueryValueKind.String
            : typeof(TValue) == typeof(bool) ? QueryValueKind.Boolean
            : typeof(TValue) == typeof(int) || typeof(TValue) == typeof(long) ? QueryValueKind.Integer
            : typeof(TValue) == typeof(double) ? QueryValueKind.Number
            : typeof(TValue) == typeof(decimal) ? QueryValueKind.Decimal
            : typeof(TValue) == typeof(DateTimeOffset) || typeof(TValue) == typeof(DateTime) ? QueryValueKind.DateTime
            : typeof(TValue) == typeof(Guid) || typeof(TValue) == typeof(RecordId) ? QueryValueKind.Id
            : typeof(TValue) == typeof(RevisionToken) ? QueryValueKind.String
            : throw new InvalidOperationException("The registered-read projection type is unsupported.");
        if (value.Kind != expected)
            throw new InvalidOperationException("The provider returned a registered-read projection value with the wrong type.");
        object decoded = typeof(TValue) == typeof(string) ? value.String!
            : typeof(TValue) == typeof(bool) ? value.Boolean!.Value
            : typeof(TValue) == typeof(int) ? checked((int)value.Integer!.Value)
            : typeof(TValue) == typeof(long) ? value.Integer!.Value
            : typeof(TValue) == typeof(double) ? value.Number!.Value
            : typeof(TValue) == typeof(decimal) ? decimal.Parse(value.Decimal!, System.Globalization.CultureInfo.InvariantCulture)
            : typeof(TValue) == typeof(DateTimeOffset) ? value.DateTime!.Value
            : typeof(TValue) == typeof(DateTime) ? value.DateTime!.Value.UtcDateTime
            : typeof(TValue) == typeof(Guid) ? Guid.Parse(value.Id!)
            : typeof(TValue) == typeof(RevisionToken) ? new RevisionToken(value.String!)
            : RecordId.Create(value.Id!);
        return (TValue)decoded;
    }

    /// <summary>Decodes one nullable generated scalar projection value.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TValue? ReadNullable<TValue>(BaseRelationalRow row, string fieldId)
        where TValue : struct => IsNull(row, fieldId) ? null : Read<TValue>(row, fieldId);

    /// <summary>Decodes one opaque module generation projected by a certified relational provider.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static BaseModuleGeneration ReadModuleGeneration(BaseRelationalRow row, string fieldId) =>
        BaseModuleGeneration.ParseCanonical(Read<string>(row, fieldId));

    /// <summary>Materializes canonical JSON after Runtime has validated its installed source-field authority.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static BaseCanonicalJson ReadCanonicalJson(BaseRelationalRow row, string fieldId)
    {
        QueryValue value = row.Fields.SingleOrDefault(field => string.Equals(field.FieldId, fieldId, StringComparison.Ordinal))?.Value
            ?? throw new InvalidOperationException("The provider omitted a required registered-read projection field.");
        if (value.Kind != QueryValueKind.CanonicalJson || value.CanonicalJsonUtf8.IsDefaultOrEmpty)
            throw new InvalidOperationException("The provider returned a registered-read projection value with the wrong type.");
        return BaseCanonicalJson.ParseAndValidate(value.CanonicalJsonUtf8.AsSpan(), new BaseCanonicalJsonLimits
        {
            MaximumCanonicalBytes = 1_048_576,
            MaximumDepth = 64,
            MaximumArrayItemsPerContainer = 16_384,
            MaximumObjectPropertiesPerContainer = 16_384,
            MaximumTotalNodes = 65_536,
            MaximumTotalStringUtf8Bytes = 1_048_576,
            MaximumTotalNameUtf8Bytes = 1_048_576,
        });
    }

    /// <summary>Returns whether one exact generated projection value is canonical null.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool IsNull(BaseRelationalRow row, string fieldId)
    {
        ArgumentNullException.ThrowIfNull(row);
        QueryValue value = row.Fields.SingleOrDefault(field => string.Equals(field.FieldId, fieldId, StringComparison.Ordinal))?.Value
            ?? throw new InvalidOperationException("The provider omitted a required registered-read projection field.");
        return value.Kind == QueryValueKind.Null;
    }

    /// <summary>Decodes one validated output-only exported-subject reference projection.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static BaseSubjectReference<TSubject> ReadSubjectReference<TSubject>(BaseRelationalRow row, string fieldId)
    {
        ArgumentNullException.ThrowIfNull(row);
        QueryValue value = row.Fields.SingleOrDefault(field => string.Equals(field.FieldId, fieldId, StringComparison.Ordinal))?.Value
            ?? throw new InvalidOperationException("The provider omitted a required registered-read projection field.");
        if (value.Kind != QueryValueKind.SubjectReference || value.SubjectId is null || value.SubjectAuthorityEpoch is null || value.SubjectIncarnation is null)
            throw new InvalidOperationException("The provider returned an invalid exported-subject reference projection.");
        return new BaseSubjectReference<TSubject>(
            BaseSubjectId.Create(
                value.SubjectId,
                value.SubjectIdKind ?? throw new InvalidOperationException("The provider omitted the subject-ID grammar."),
                value.SubjectIdMaximumUtf8Bytes ?? throw new InvalidOperationException("The provider omitted the subject-ID bound.")),
            BaseSubjectAuthorityEpoch.Parse(value.SubjectAuthorityEpoch),
            BaseSubjectIncarnation.Parse(value.SubjectIncarnation));
    }
}
