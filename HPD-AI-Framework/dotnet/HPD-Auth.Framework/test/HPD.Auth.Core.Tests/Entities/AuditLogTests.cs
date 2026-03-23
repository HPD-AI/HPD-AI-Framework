using System.Reflection;
using FluentAssertions;
using HPD.Auth.Core.Entities;
using Xunit;

namespace HPD.Auth.Core.Tests.Entities;

[Trait("Category", "Entity")]
public class AuditLogTests
{
    [Fact]
    public void AuditLog_UsesInitProperties_NotSetters()
    {
        var properties = typeof(AuditLog).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var propertiesWithPublicSet = properties
            .Where(p => p.SetMethod is { IsPublic: true } && !p.SetMethod.ReturnParameter.GetRequiredCustomModifiers()
                .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit"))
            .ToList();

        // All public setters should be init-only (no writable setters)
        propertiesWithPublicSet.Should().BeEmpty(
            because: "AuditLog must be immutable — all writable properties must use 'init' not 'set'");
    }

    [Fact]
    public void AuditLog_InstanceId_DefaultsToGuidEmpty()
    {
        new AuditLog().InstanceId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void AuditLog_Timestamp_DefaultsToUtcNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-5);
        var log = new AuditLog();
        var after = DateTime.UtcNow.AddSeconds(5);

        log.Timestamp.Should().BeAfter(before).And.BeBefore(after);
    }

    [Fact]
    public void AuditLog_Success_DefaultsToTrue()
    {
        new AuditLog().Success.Should().BeTrue();
    }

    [Fact]
    public void AuditLog_Metadata_DefaultsToEmptyJsonObject()
    {
        new AuditLog().Metadata.Should().Be("{}");
    }

    [Fact]
    public void AuditLog_UserId_DefaultsToNull()
    {
        new AuditLog().UserId.Should().BeNull();
    }

    [Fact]
    public void AuditLog_CannotMutateAfterCreation_VerifiedViaReflection()
    {
        // Compile-time enforcement is verified by confirming no public set accessors exist.
        // If this test passes alongside AuditLog_UsesInitProperties_NotSetters, mutation is impossible.
        var actionProp = typeof(AuditLog).GetProperty(nameof(AuditLog.Action));
        actionProp.Should().NotBeNull();

        var setter = actionProp!.SetMethod;
        if (setter is null)
        {
            // No setter at all — property is init-only via field backing
            return;
        }

        // If a setter exists it must be init-only (has IsExternalInit modifier)
        var isInitOnly = setter.ReturnParameter
            .GetRequiredCustomModifiers()
            .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");

        isInitOnly.Should().BeTrue(because: "AuditLog.Action must be init-only, not a regular setter");
    }
}
