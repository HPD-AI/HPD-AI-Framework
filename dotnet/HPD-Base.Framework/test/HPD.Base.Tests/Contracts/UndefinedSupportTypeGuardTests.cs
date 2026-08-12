namespace HPD.Base.Tests.Abstractions.Contracts;

public sealed class UndefinedSupportTypeGuardTests
{
    [Fact]
    public void PublicPropertiesDoNotExposeObjectDynamicOrTypeIdentity()
    {
        var forbidden = typeof(RecordId).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace?.EndsWith(".Serialization", StringComparison.Ordinal) is not true)
            .SelectMany(type => type.GetProperties()
                .Where(property => property.DeclaringType == type)
                .Select(property => $"{type.FullName}.{property.Name}:{property.PropertyType.FullName}"))
            .Where(signature =>
                signature.EndsWith(":System.Object", StringComparison.Ordinal) ||
                signature.EndsWith(":System.Type", StringComparison.Ordinal) &&
                    signature != "HPD.Base.BaseCollectionAttribute.JsonContextType:System.Type" &&
                    signature != "HPD.Base.BaseCollectionStorageProtectionAttribute.DeclaringType:System.Type" &&
                    signature != "HPD.Base.BaseRelationAttribute.TargetRecordType:System.Type" &&
                    signature != "HPD.Base.BaseReadAttribute.JsonContextType:System.Type" ||
                signature.Contains("System.Reflection.", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(forbidden);
    }
}
