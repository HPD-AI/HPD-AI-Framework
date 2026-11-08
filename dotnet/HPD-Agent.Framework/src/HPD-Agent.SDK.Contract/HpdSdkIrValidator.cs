namespace HPD.Agent.SDK.Contract;

/// <summary>Validates a generated HPD Agent SDK IR graph before emission.</summary>
public static class HpdSdkIrValidator
{
    /// <summary>Throws when the IR version or graph is internally inconsistent.</summary>
    public static void Validate(HpdSdkIr ir)
    {
        ArgumentNullException.ThrowIfNull(ir);
        if (!string.Equals(ir.IrVersion, HpdSdkIrVersions.V1, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported HPD Agent SDK IR version '{ir.IrVersion}'.");
        }
        var errors = ir.Diagnostics
            .Where(static diagnostic =>
                string.Equals(diagnostic.Severity, "error", StringComparison.Ordinal))
            .ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidDataException(
                "HPD Agent SDK IR contains errors: " +
                string.Join(" | ", errors.Select(static diagnostic =>
                    $"{diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}")));
        }

        var componentIds = new HashSet<string>(StringComparer.Ordinal);
        var schemaReferences = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in ir.Types)
        {
            if (!componentIds.Add(type.ComponentId))
            {
                throw new InvalidDataException(
                    $"Duplicate HPD Agent SDK IR component '{type.ComponentId}'.");
            }
            CollectReferences(type.Schema, schemaReferences);
        }

        var operationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in ir.Operations)
        {
            if (!operationIds.Add(operation.OperationId))
            {
                throw new InvalidDataException(
                    $"Duplicate HPD Agent SDK IR operation '{operation.OperationId}'.");
            }
            foreach (var parameter in operation.Parameters)
                CollectReferences(parameter.Type, schemaReferences);
            if (operation.RequestBody is not null)
                CollectReferences(operation.RequestBody.Type, schemaReferences);
            if (operation.SuccessType is not null)
                CollectReferences(operation.SuccessType, schemaReferences);
        }

        var unionNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var union in ir.Unions)
        {
            if (!unionNames.Add(union.Name))
            {
                throw new InvalidDataException(
                    $"Duplicate HPD Agent SDK IR union '{union.Name}'.");
            }
            var discriminators = new HashSet<string>(StringComparer.Ordinal);
            foreach (var variant in union.Variants)
            {
                if (!discriminators.Add(variant.Discriminator))
                {
                    throw new InvalidDataException(
                        $"Duplicate discriminator '{variant.Discriminator}' in IR union '{union.Name}'.");
                }
                CollectReferences(variant.Type, schemaReferences);
            }
        }

        foreach (var reference in schemaReferences)
        {
            var componentId = DecodeReference(reference);
            if (!componentIds.Contains(componentId))
            {
                throw new InvalidDataException(
                    $"HPD Agent SDK IR reference '{reference}' does not resolve.");
            }
        }

        foreach (var capability in ir.Capabilities)
        {
            foreach (var operationId in capability.Operations)
            {
                if (!operationIds.Contains(operationId))
                {
                    throw new InvalidDataException(
                        $"Capability '{capability.Name}' references unknown operation '{operationId}'.");
                }
            }
        }
    }

    private static void CollectReferences(HpdTypeIr type, HashSet<string> references)
    {
        if (type.Reference is { Length: > 0 } reference)
            references.Add(reference);
        if (type.ItemType is not null)
            CollectReferences(type.ItemType, references);
        if (type.ValueType is not null)
            CollectReferences(type.ValueType, references);
        if (type.Properties is not null)
        {
            foreach (var property in type.Properties)
                CollectReferences(property.Type, references);
        }
        if (type.Branches is not null)
        {
            foreach (var branch in type.Branches)
                CollectReferences(branch, references);
        }
    }

    private static string DecodeReference(string reference)
    {
        const string prefix = "#/components/schemas/";
        if (!reference.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported external HPD Agent SDK IR reference '{reference}'.");
        }
        return reference[prefix.Length..]
            .Replace("~1", "/", StringComparison.Ordinal)
            .Replace("~0", "~", StringComparison.Ordinal);
    }
}
