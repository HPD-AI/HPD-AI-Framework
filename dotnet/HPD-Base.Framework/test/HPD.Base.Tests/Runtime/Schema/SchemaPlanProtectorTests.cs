using FluentAssertions;
using Microsoft.Extensions.Options;

namespace HPD.Base.Tests.Runtime.Schema;

public sealed class SchemaPlanProtectorTests
{
    [Fact]
    public void RoundTripAuthenticatesBindingsAndHidesProviderArtifact()
    {
        var protector = Protector(0x41);
        BaseSchemaPlan plan = Plan();
        byte[] providerArtifact = "private-ddl-material"u8.ToArray();
        plan = plan with { ProviderApplyArtifactDigest = DefaultBaseSchemaPlanProtector.Digest(providerArtifact) };

        byte[] wire = protector.Protect(plan, providerArtifact);
        string printable = Convert.ToBase64String(wire);
        printable.Should().NotContain(Convert.ToBase64String(providerArtifact));

        OperationResult<BaseSchemaVerifiedPlan> result = protector.Unprotect(wire);
        result.Status.Should().Be(OperationStatus.Ok);
        result.Value!.Plan.PlanId.Should().Be(plan.PlanId);
        result.Value.ProviderApplyArtifact.Should().Equal(providerArtifact);
        result.Value.Plan.ProtectedArtifact.Should().Equal(wire);
    }

    [Fact]
    public void TamperAndWrongKeyFailWithOneSafeError()
    {
        byte[] artifact = "provider-plan"u8.ToArray();
        BaseSchemaPlan plan = Plan() with { ProviderApplyArtifactDigest = DefaultBaseSchemaPlanProtector.Digest(artifact) };
        byte[] wire = Protector(0x41).Protect(plan, artifact);
        wire[^1] ^= 0x01;

        Protector(0x41).Unprotect(wire).Error!.Code.Should().Be(BaseSchemaErrorCodes.PlanInvalid);
        byte[] intact = Protector(0x41).Protect(plan, artifact);
        Protector(0x42).Unprotect(intact).Error!.Code.Should().Be(BaseSchemaErrorCodes.PlanInvalid);
    }

    private static DefaultBaseSchemaPlanProtector Protector(byte value) => new(
        Options.Create(new HPDBaseSchemaOptions { PlanProtectionKey = Enumerable.Repeat(value, 32).ToArray() }));

    private static BaseSchemaPlan Plan() => new()
    {
        PlanId = "plan", ApplicationId = "app", StoreId = "store", PersistedStoreInstanceId = "instance",
        ProviderId = "sqlite", ProviderVersion = "1", PlannerVersion = "1", ExpectedGeneration = 2,
        BaselineId = "baseline", BaselineChecksum = "before", TargetBaselineId = "target", TargetChecksum = "after",
        Classification = BaseSchemaPlanClassification.SafeStructural,
        Operations = [new BaseSchemaLogicalOperation { Kind = BaseSchemaOperationKind.AddField, LogicalId = "f:items:title" }],
        CreatedAt = DateTimeOffset.UnixEpoch, ExpiresAt = DateTimeOffset.UnixEpoch.AddMinutes(15),
        LogicalPlanDigest = "logical", ProviderApplyArtifactDigest = "placeholder", ProtectedArtifact = []
    };
}
