// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;

namespace HPD.Agent.FFI;

/// <summary>
/// FFI-friendly structured result event (non-generic, value is JsonElement).
/// Used for cross-language serialization where generic types aren't supported.
/// </summary>
/// <remarks>
/// <para>
/// This DTO mirrors <see cref="StructuredResultEvent{T}"/> but replaces the generic
/// <c>T Value</c> with <see cref="JsonElement"/> for FFI compatibility.
/// </para>
/// <para>
/// FFI consumers (Python, Rust, etc.) can deserialize the JsonElement value
/// into their native types using their language's JSON libraries.
/// </para>
/// </remarks>
/// <param name="Value">The parsed value as a JsonElement (for FFI compatibility)</param>
/// <param name="IsPartial">True if this is an intermediate result, false if final</param>
/// <param name="RawJson">The raw JSON string that was parsed</param>
/// <param name="TypeName">The expected type name (for observability and debugging)</param>
public sealed record StructuredResultEventDto(
    JsonElement Value,
    bool IsPartial,
    string RawJson,
    string TypeName
) : AgentEvent;
