namespace HPD.Base.Abstractions.Tests.Contracts;

public sealed class UndefinedSupportTypeGuardTests
{
    [Fact]
    public void PublicPropertiesDoNotExposeObjectDynamicOrTypeIdentity()
    {
        var forbidden = typeof(RecordId).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace is not "HPD.Base.Serialization")
            .SelectMany(type => type.GetProperties().Select(property => $"{type.FullName}.{property.Name}:{property.PropertyType.FullName}"))
            .Where(signature =>
                signature.EndsWith(":System.Object", StringComparison.Ordinal) ||
                signature.EndsWith(":System.Type", StringComparison.Ordinal) ||
                signature.Contains("System.Reflection.", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(forbidden);
    }
}
