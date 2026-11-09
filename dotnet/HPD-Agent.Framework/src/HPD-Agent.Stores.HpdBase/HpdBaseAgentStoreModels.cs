// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Base;
using System.Text.Json.Serialization;

namespace HPD.Agent.Stores.HpdBase;

[BaseCollection(
    "hpd.agent.definitions",
    typeof(HpdAgentStoreJsonContext),
    Name = "HPD Agent definitions")]
public sealed partial record HpdBaseAgentDefinition
{
    [BaseField(
        "agent-id",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable,
        MinimumUtf8Bytes = 1,
        MaximumUtf8Bytes = 512)]
    public required string AgentId { get; init; }

    [BaseField(
        "name",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable,
        MaximumUtf8Bytes = 512)]
    public required string Name { get; init; }

    [BaseField("payload")]
    public required string Payload { get; init; }

    [BaseField("created-at")]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public required DateTimeOffset CreatedAt { get; init; }

    [BaseField("updated-at")]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public required DateTimeOffset UpdatedAt { get; init; }
}
