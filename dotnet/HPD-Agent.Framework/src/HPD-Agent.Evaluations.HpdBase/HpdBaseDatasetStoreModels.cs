// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Agent.Evaluations.HpdBase;

[BaseCollection(
    "hpd.agent.evaluation-dataset-versions",
    typeof(HpdBaseEvaluationStoreJsonContext),
    Name = "HPD Agent immutable evaluation dataset versions")]
public sealed partial record HpdBaseDatasetVersion
{
    [BaseField(
        "dataset-version.dataset-id",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable,
        MinimumUtf8Bytes = 1,
        MaximumUtf8Bytes = 512)]
    public required string DatasetId { get; init; }

    [BaseField(
        "dataset-version.version",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable,
        MinimumUtf8Bytes = 1,
        MaximumUtf8Bytes = 512)]
    public required string Version { get; init; }

    [BaseField(
        "dataset-version.content-hash",
        Operators = BaseFieldOperator.Equal,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable,
        MinimumUtf8Bytes = 1,
        MaximumUtf8Bytes = 128)]
    public required string ContentHash { get; init; }

    [BaseField("dataset-version.input-type")]
    public required string InputType { get; init; }

    [BaseField(
        "dataset-version.case-count",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable)]
    public required int CaseCount { get; init; }

    [BaseField(
        "dataset-version.registered-at",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable)]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public required DateTimeOffset RegisteredAt { get; init; }

    [BaseField("dataset-version.description")]
    public required string Description { get; init; }

    [BaseField("dataset-version.metadata")]
    public required string MetadataPayload { get; init; }

    [BaseField("dataset-version.payload")]
    public required string Payload { get; init; }
}

[BaseCollection(
    "hpd.agent.evaluation-datasets",
    typeof(HpdBaseEvaluationStoreJsonContext),
    Name = "HPD Agent current evaluation datasets")]
public sealed partial record HpdBaseDatasetHead
{
    [BaseField(
        "dataset.dataset-id",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable,
        MinimumUtf8Bytes = 1,
        MaximumUtf8Bytes = 512)]
    public required string DatasetId { get; init; }

    [BaseField(
        "dataset.current-version",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable,
        MinimumUtf8Bytes = 1,
        MaximumUtf8Bytes = 512)]
    public required string CurrentVersion { get; init; }

    [BaseField(
        "dataset.content-hash",
        Operators = BaseFieldOperator.Equal,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable,
        MinimumUtf8Bytes = 1,
        MaximumUtf8Bytes = 128)]
    public required string ContentHash { get; init; }

    [BaseField(
        "dataset.registered-at",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable)]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public required DateTimeOffset RegisteredAt { get; init; }

    [BaseField("dataset.description")]
    public required string Description { get; init; }

    [BaseField("dataset.metadata")]
    public required string MetadataPayload { get; init; }
}
