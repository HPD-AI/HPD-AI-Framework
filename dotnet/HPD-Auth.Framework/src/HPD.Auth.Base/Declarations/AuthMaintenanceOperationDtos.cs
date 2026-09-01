using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

internal sealed record AuthMaintenanceRunInitializeV1
{
    [BaseField("auth.operation.maintenance-run.initialize.activationId",
        MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)]
    public required string ActivationId { get; init; }

    [BaseField("auth.operation.maintenance-run.initialize.kind",
        AllowedEnumLiterals = ["deliveryExpiration", "refreshExpiration", "sessionExpiration"])]
    [JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthMaintenanceKindV1>))]
    public required AuthMaintenanceKindV1 Kind { get; init; }

    [BaseField("auth.operation.maintenance-run.initialize.cutoff")]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public required DateTimeOffset Cutoff { get; init; }
}

internal sealed record AuthMaintenanceRunResultV1
{
    [BaseField("auth.operation.maintenance-run.initialize.result.id", MinimumUtf8Bytes = 64,
        MaximumUtf8Bytes = 64)]
    public required string Id { get; init; }

    [BaseField("auth.operation.maintenance-run.initialize.result.revision")]
    public required RevisionToken Revision { get; init; }

    [BaseField("auth.operation.maintenance-run.initialize.result.kind",
        AllowedEnumLiterals = ["deliveryExpiration", "refreshExpiration", "sessionExpiration"])]
    [JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthMaintenanceKindV1>))]
    public required AuthMaintenanceKindV1 Kind { get; init; }

    [BaseField("auth.operation.maintenance-run.initialize.result.cutoff")]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public required DateTimeOffset Cutoff { get; init; }
}
