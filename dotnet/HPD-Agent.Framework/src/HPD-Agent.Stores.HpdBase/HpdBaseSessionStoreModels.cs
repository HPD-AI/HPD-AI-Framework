// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Agent.Stores.HpdBase;

[BaseCollection(
    "hpd.agent.sessions",
    typeof(HpdAgentStoreJsonContext),
    Name = "HPD Agent sessions")]
public sealed partial record HpdBaseSessionRecord
{
    [BaseField(
        "session.session-id",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable,
        MinimumUtf8Bytes = 1,
        MaximumUtf8Bytes = 512)]
    public required string SessionId { get; init; }

    [BaseField("session.payload")]
    public required string Payload { get; init; }

    [BaseField(
        "session.last-activity",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable)]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public required DateTimeOffset LastActivity { get; init; }
}

[BaseCollection(
    "hpd.agent.threads",
    typeof(HpdAgentStoreJsonContext),
    Name = "HPD Agent thread descriptors")]
public sealed partial record HpdBaseThreadRecord
{
    [BaseField(
        "thread.session-id",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable,
        MinimumUtf8Bytes = 1,
        MaximumUtf8Bytes = 512)]
    public required string SessionId { get; init; }

    [BaseField(
        "thread.thread-id",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable,
        MinimumUtf8Bytes = 1,
        MaximumUtf8Bytes = 1024)]
    public required string ThreadId { get; init; }

    [BaseField(
        "thread.generation",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable)]
    public required long Generation { get; init; }

    [BaseField(
        "thread.head",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable)]
    public required long Head { get; init; }

    [BaseField(
        "thread.updated-at",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable)]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public required DateTimeOffset UpdatedAt { get; init; }

    [BaseField("thread.descriptor")]
    public required string DescriptorPayload { get; init; }

    [BaseField("thread.message-ids")]
    public required string MessageIdsPayload { get; init; }
}

[BaseCollection(
    "hpd.agent.thread-event-blocks",
    typeof(HpdAgentStoreJsonContext),
    Name = "HPD Agent thread event blocks")]
public sealed partial record HpdBaseThreadEventBlock
{
    [BaseField(
        "event.session-id",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable,
        MinimumUtf8Bytes = 1,
        MaximumUtf8Bytes = 512)]
    public required string SessionId { get; init; }

    [BaseField(
        "event.thread-id",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable,
        MinimumUtf8Bytes = 1,
        MaximumUtf8Bytes = 1024)]
    public required string ThreadId { get; init; }

    [BaseField(
        "event.generation",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable)]
    public required long Generation { get; init; }

    [BaseField(
        "event.first-sequence",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable)]
    public required long FirstSequence { get; init; }

    [BaseField(
        "event.last-sequence",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable)]
    public required long LastSequence { get; init; }

    [BaseField(
        "event.created-at",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable)]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public required DateTimeOffset CreatedAt { get; init; }

    [BaseField("event.payload")]
    public required string Payload { get; init; }
}

[BaseCollection(
    "hpd.agent.thread-event-identities",
    typeof(HpdAgentStoreJsonContext),
    Name = "HPD Agent thread event identities")]
public sealed partial record HpdBaseThreadEventIdentity
{
    [BaseField(
        "event-identity.session-id",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable,
        MinimumUtf8Bytes = 1,
        MaximumUtf8Bytes = 512)]
    public required string SessionId { get; init; }

    [BaseField(
        "event-identity.thread-id",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable,
        MinimumUtf8Bytes = 1,
        MaximumUtf8Bytes = 1024)]
    public required string ThreadId { get; init; }

    [BaseField(
        "event-identity.generation",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable)]
    public required long Generation { get; init; }

    [BaseField(
        "event-identity.event-id",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable,
        MinimumUtf8Bytes = 1,
        MaximumUtf8Bytes = 1024)]
    public required string EventId { get; init; }

    [BaseField(
        "event-identity.sequence",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable)]
    public required long Sequence { get; init; }
}

[BaseCollection(
    "hpd.agent.thread-deltas",
    typeof(HpdAgentStoreJsonContext),
    Name = "HPD Agent staged thread deltas")]
public sealed partial record HpdBaseThreadDelta
{
    [BaseField(
        "delta.session-id",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable,
        MinimumUtf8Bytes = 1,
        MaximumUtf8Bytes = 512)]
    public required string SessionId { get; init; }

    [BaseField(
        "delta.thread-id",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable,
        MinimumUtf8Bytes = 1,
        MaximumUtf8Bytes = 1024)]
    public required string ThreadId { get; init; }

    [BaseField(
        "delta.message-id",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable,
        MinimumUtf8Bytes = 1,
        MaximumUtf8Bytes = 1024)]
    public required string MessageId { get; init; }

    [BaseField(
        "delta.kind",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable,
        MinimumUtf8Bytes = 1,
        MaximumUtf8Bytes = 32)]
    public required string Kind { get; init; }

    [BaseField(
        "delta.ordinal",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable)]
    public required long Ordinal { get; init; }

    [BaseField(
        "delta.created-at",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required,
        Nullability = BaseFieldNullability.NonNullable)]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public required DateTimeOffset CreatedAt { get; init; }

    [BaseField("delta.payload")]
    public required string Payload { get; init; }
}
