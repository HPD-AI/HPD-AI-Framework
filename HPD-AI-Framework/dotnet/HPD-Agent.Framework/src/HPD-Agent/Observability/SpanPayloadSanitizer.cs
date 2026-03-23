// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HPD.Agent;

/// <summary>
/// Sanitizes free-form string payloads before they are attached to OTel span tags or events.
///
/// Two concerns handled:
///
/// 1. Sensitive data redaction — JSON keys matching known-sensitive names (password, token,
///    api_key, secret, authorization, etc.) have their values replaced with "[REDACTED]"
///    before the payload leaves the process.
///
/// 2. Serialization budget — strings longer than <see cref="SpanSanitizerOptions.MaxStringLength"/>
///    are truncated with a "[truncated]" suffix so large tool results don't blow up
///    tracing backends.
///
/// Non-JSON strings (plain text) are only length-capped, not redacted.
/// </summary>
public sealed class SpanPayloadSanitizer
{
    private readonly SpanSanitizerOptions _options;

    // Normalized sensitive key names (lowercased, separators stripped).
    // Matches Mastra's SensitiveDataFilterProcessor field list.
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passwd", "pwd",
        "token", "accesstoken", "refreshtoken", "idtoken",
        "secret", "clientsecret",
        "apikey", "api_key", "apitoken",
        "auth", "authorization", "bearer", "bearertoken",
        "jwt",
        "credential", "credentials",
        "privatekey", "private_key",
        "ssn",
        "key",
    };

    public SpanPayloadSanitizer(SpanSanitizerOptions? options = null)
    {
        _options = options ?? new SpanSanitizerOptions();
    }

    /// <summary>
    /// Sanitize a payload string for use as a span tag or event attribute.
    /// Redacts sensitive JSON fields and enforces the length budget.
    /// </summary>
    public string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? string.Empty;

        // Attempt JSON redaction first (only if it looks like JSON).
        if (_options.EnableRedaction && LooksLikeJson(value))
        {
            value = RedactJson(value);
        }

        // Apply length cap.
        if (_options.MaxStringLength > 0 && value.Length > _options.MaxStringLength)
        {
            value = string.Concat(value.AsSpan(0, _options.MaxStringLength), " [truncated]");
        }

        return value;
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private static bool LooksLikeJson(string s)
    {
        var trimmed = s.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    private string RedactJson(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is null) return json;
            RedactNode(node);
            return node.ToJsonString();
        }
        catch (JsonException)
        {
            // Malformed JSON — return as-is, length cap will still apply.
            return json;
        }
    }

    private void RedactNode(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    if (IsSensitiveKey(key))
                    {
                        obj[key] = JsonValue.Create("[REDACTED]");
                    }
                    else if (obj[key] is JsonNode child)
                    {
                        RedactNode(child);
                    }
                }
                break;

            case JsonArray arr:
                foreach (var item in arr)
                {
                    if (item is not null)
                        RedactNode(item);
                }
                break;
        }
    }

    private static bool IsSensitiveKey(string key)
    {
        // Normalize: lowercase and strip common separators (_  -  .)
        var normalized = key
            .Replace("_", "")
            .Replace("-", "")
            .Replace(".", "");

        return SensitiveKeys.Contains(normalized);
    }
}

/// <summary>
/// Configuration for <see cref="SpanPayloadSanitizer"/>.
/// </summary>
public sealed class SpanSanitizerOptions
{
    /// <summary>
    /// Maximum length of a sanitized string before truncation.
    /// Strings longer than this are cut and suffixed with " [truncated]".
    /// Default: 4096 chars (~4KB) — enough for most tool results,
    /// prevents megabyte payloads reaching tracing backends.
    /// Set to 0 to disable truncation.
    /// </summary>
    public int MaxStringLength { get; init; } = 4096;

    /// <summary>
    /// Whether to redact sensitive JSON field values.
    /// Default: true.
    /// </summary>
    public bool EnableRedaction { get; init; } = true;
}
