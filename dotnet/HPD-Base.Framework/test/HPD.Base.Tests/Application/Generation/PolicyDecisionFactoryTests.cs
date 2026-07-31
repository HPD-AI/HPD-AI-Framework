using FluentAssertions;
using HPD.Base;
using Xunit;

namespace HPD.Base.Tests.Application.Generation;

public sealed class PolicyDecisionFactoryTests
{
    [Fact]
    public void AllowFactoryComposesValidConstraints()
    {
        PolicyDecision decision = PolicyDecision.Allow()
            .WithRecordFilter(new FilterExpression
            {
                Kind = FilterNodeKind.Compare,
                Field = "tenantId",
                Operator = FilterOperator.Equal,
                Value = new QueryValue { Kind = QueryValueKind.String, String = "tenant-a" },
            })
            .WithReadMask(new FieldMask
            {
                Mode = FieldMaskMode.IncludeOnly,
                Include = ["name"],
            });

        decision.Effect.Should().Be(PolicyEffect.Allow);
        decision.Outcome.Should().Be(PolicyOutcome.AllowedWithConstraints);
        decision.Constraints!.RecordFilter.Should().NotBeNull();
        decision.Constraints.ReadMask.Should().NotBeNull();
    }

    [Fact]
    public void DenyFactoryRequiresSafeReasonAndRejectsConstraints()
    {
        PolicyDecision denied = PolicyDecision.Deny(
            "cloud.denied",
            "The operation is not allowed.");

        Action constrain = () => denied.WithWriteCheck(new FilterExpression
        {
            Kind = FilterNodeKind.Compare,
            Field = "owner",
            Operator = FilterOperator.Equal,
            Value = new QueryValue { Kind = QueryValueKind.Boolean, Boolean = true },
        });

        constrain.Should().Throw<InvalidOperationException>();
    }
}
