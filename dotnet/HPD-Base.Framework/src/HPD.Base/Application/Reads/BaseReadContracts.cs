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
        JsonTypeInfo<TParameters> parameterJsonTypeInfo,
        JsonTypeInfo<TRow> rowJsonTypeInfo,
        IBaseReadParameterCodec<TParameters> parameterCodec,
        IBaseReadRowCodec<TRow> rowCodec)
    {
        Plan = plan;
        ParameterJsonTypeInfo = parameterJsonTypeInfo;
        RowJsonTypeInfo = rowJsonTypeInfo;
        ParameterCodec = parameterCodec;
        RowCodec = rowCodec;
        Handle = new BaseReadHandle<TParameters, TRow>(this);
    }

    /// <summary>Gets the stable read-definition identifier.</summary>
    public string Id => Plan.Id;

    /// <summary>Gets the registered read's explicit HTTP exposure.</summary>
    public BaseReadExposure Exposure { get; internal init; }

    /// <summary>Gets the minimum principal authorization required to invoke the read.</summary>
    public BaseReadAuthorization Authorization { get; internal init; } = BaseReadAuthorization.Authenticated;

    internal BaseRelationalReadPlan Plan { get; }

    /// <summary>Gets source-generated request metadata.</summary>
    public JsonTypeInfo<TParameters> ParameterJsonTypeInfo { get; }

    /// <summary>Gets source-generated row metadata.</summary>
    public JsonTypeInfo<TRow> RowJsonTypeInfo { get; }

    /// <summary>Gets the sole typed invocation handle.</summary>
    public BaseReadHandle<TParameters, TRow> Handle { get; }

    internal IBaseReadParameterCodec<TParameters> ParameterCodec { get; }
    internal IBaseReadRowCodec<TRow> RowCodec { get; }

    JsonTypeInfo IBaseReadRegistration.ParameterJsonTypeInfo => ParameterJsonTypeInfo;
    JsonTypeInfo IBaseReadRegistration.RowJsonTypeInfo => RowJsonTypeInfo;
    BaseRelationalReadPlan IBaseReadRegistration.Plan => Plan;
    Type IBaseReadRegistration.ResponseType => typeof(BasePage<TRow>);
    BaseReadExposure IBaseReadRegistration.Exposure => Exposure;
    BaseReadAuthorization IBaseReadRegistration.Authorization => Authorization;

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
        OperationResult<BaseRegisteredReadEvaluation<TRow>> result = await runtime.ExecuteAsync(this, typed, page, principal, operation, cancellationToken).ConfigureAwait(false);
        return result.Value is null
            ? new BaseUntypedRegisteredReadResult { Status = result.Status, Error = result.Error }
            : new BaseUntypedRegisteredReadResult { Status = result.Status, Items = result.Value.Page.Items.Cast<object>().ToArray(), Page = result.Value.Page.Page, Count = result.Value.Page.Count };
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

internal interface IBaseReadRegistration
{
    /// <summary>Gets the ID.</summary>
    string Id { get; }
    /// <summary>Gets the plan.</summary>
    BaseRelationalReadPlan Plan { get; }
    /// <summary>Gets the parameter JSON type info.</summary>
    JsonTypeInfo ParameterJsonTypeInfo { get; }
    /// <summary>Gets the row JSON type info.</summary>
    JsonTypeInfo RowJsonTypeInfo { get; }
    /// <summary>Gets the response type.</summary>
    Type ResponseType { get; }
    /// <summary>Gets the explicit HTTP exposure.</summary>
    BaseReadExposure Exposure { get; }
    /// <summary>Gets the minimum invocation authorization.</summary>
    BaseReadAuthorization Authorization { get; }
    /// <summary>Executes the execute async operation.</summary>
    ValueTask<BaseUntypedRegisteredReadResult> ExecuteAsync(IBaseRegisteredReadRuntime runtime, object parameters, BaseReadPageRequest page, PrincipalContext principal, OperationContext operation, CancellationToken cancellationToken);
}

internal sealed record BaseUntypedRegisteredReadResult
{
    internal OperationStatus Status { get; init; }
    internal BaseError? Error { get; init; }
    internal object[]? Items { get; init; }
    internal PageInfo? Page { get; init; }
    internal CountInfo? Count { get; init; }
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
        Action<BaseReadDefinitionBuilder<TParameters, TRow>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new BaseReadDefinitionBuilder<TParameters, TRow>(
            id, parameters);
        configure(builder);
        if (!Enum.IsDefined(exposure) || !Enum.IsDefined(authorization))
            throw new ArgumentOutOfRangeException(nameof(exposure));
        if (exposure == BaseReadExposure.Admin && authorization == BaseReadAuthorization.Authenticated)
            throw new InvalidOperationException("An admin-exposed registered read must require admin or system authorization.");
        return new BaseReadDefinition<TParameters, TRow>(
            builder.Build(),
            parameterJson,
            rowJson,
            parameterCodec,
            rowCodec)
        {
            Exposure = exposure,
            Authorization = authorization,
        };
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
            : RecordId.Create(value.Id!);
        return (TValue)decoded;
    }

    /// <summary>Decodes one nullable generated scalar projection value.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TValue? ReadNullable<TValue>(BaseRelationalRow row, string fieldId)
        where TValue : struct => IsNull(row, fieldId) ? null : Read<TValue>(row, fieldId);

    /// <summary>Returns whether one exact generated projection value is canonical null.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool IsNull(BaseRelationalRow row, string fieldId)
    {
        ArgumentNullException.ThrowIfNull(row);
        QueryValue value = row.Fields.SingleOrDefault(field => string.Equals(field.FieldId, fieldId, StringComparison.Ordinal))?.Value
            ?? throw new InvalidOperationException("The provider omitted a required registered-read projection field.");
        return value.Kind == QueryValueKind.Null;
    }
}
