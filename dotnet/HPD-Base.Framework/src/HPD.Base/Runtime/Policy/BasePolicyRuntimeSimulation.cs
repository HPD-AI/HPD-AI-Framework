using System.Text.Json;

namespace HPD.Base;

/// <summary>
/// Provides runtime-compatible helpers used when simulating policy-protected operations.
/// </summary>
public static class BasePolicyRuntimeSimulation
{
    /// <summary>
    /// Composes a user query with an effective policy record filter using the record runtime semantics.
    /// </summary>
    public static RecordQuery ComposePolicyFilter(RecordQuery query, FilterExpression? policyFilter)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (policyFilter is null || policyFilter.Kind == FilterNodeKind.True)
        {
            return query;
        }

        if (query.Filter is null)
        {
            return query with { Filter = policyFilter };
        }

        if (query.Filter.Kind == FilterNodeKind.True)
        {
            return query with { Filter = policyFilter };
        }

        if (policyFilter.Kind == FilterNodeKind.False || query.Filter.Kind == FilterNodeKind.False)
        {
            return query with { Filter = new FilterExpression { Kind = FilterNodeKind.False } };
        }

        return query with
        {
            Filter = new FilterExpression
            {
                Kind = FilterNodeKind.And,
                Children = [policyFilter, query.Filter]
            }
        };
    }

    /// <summary>
    /// Merges a top-level patch payload into an existing payload using the record runtime semantics.
    /// </summary>
    public static RecordPayload MergePatchPayload(RecordPayload existing, RecordPayload patch)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(patch);

        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        if (existing.Kind == RecordPayloadKind.FieldMap)
        {
            foreach (var field in existing.Fields ?? [])
            {
                fields[field.Key] = field.Value.Clone();
            }
        }
        else if (existing.Json.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in existing.Json.EnumerateObject())
            {
                fields[property.Name] = property.Value.Clone();
            }
        }

        if (patch.Kind == RecordPayloadKind.FieldMap)
        {
            foreach (var field in patch.Fields ?? [])
            {
                fields[field.Key] = field.Value.Clone();
            }
        }
        else if (patch.Json.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in patch.Json.EnumerateObject())
            {
                fields[property.Name] = property.Value.Clone();
            }
        }

        return new RecordPayload
        {
            Kind = RecordPayloadKind.FieldMap,
            Fields = fields
        };
    }

    /// <summary>
    /// Determines the descriptor or response view that the record runtime would use for a principal and operation.
    /// </summary>
    public static VisibilityLevel ViewFor(PrincipalContext principal, OperationContext operation)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(operation);

        if (operation.Mode is OperationMode.Admin or OperationMode.System
            || principal.AuthenticationState is PrincipalAuthenticationState.Admin or PrincipalAuthenticationState.System)
        {
            return VisibilityLevel.Admin;
        }

        return principal.AuthenticationState == PrincipalAuthenticationState.Anonymous
            ? VisibilityLevel.Public
            : VisibilityLevel.Authenticated;
    }

    /// <summary>
    /// Returns the fields written by a payload using the record runtime top-level field semantics.
    /// </summary>
    public static string[] PayloadFields(RecordPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.Kind == RecordPayloadKind.FieldMap)
        {
            return payload.Fields?.Keys.ToArray() ?? [];
        }

        if (payload.Json.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return payload.Json.EnumerateObject()
            .Select(static property => property.Name)
            .ToArray();
    }
}
