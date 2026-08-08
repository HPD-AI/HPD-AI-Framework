using FluentAssertions;
using Xunit;

namespace HPD.Base.Vector.Tests;

public sealed class BaseVectorContractTests
{
    [Fact]
    public void Constraint_normalization_is_commutative_and_deterministic()
    {
        var field = new BaseVectorFilterField("document.tenant", BaseVectorFilterValueKind.String);
        var first = new BaseVectorCandidateConstraint.Equal(field, BaseVectorFilterValue.FromString("a"));
        var second = new BaseVectorCandidateConstraint.Equal(field, BaseVectorFilterValue.FromString("b"));

        var left = BaseVectorConstraintNormalizer.Normalize(new BaseVectorCandidateConstraint.Or([first, second]));
        var right = BaseVectorConstraintNormalizer.Normalize(new BaseVectorCandidateConstraint.Or([second, first]));

        left.Digest.Should().Be(right.Digest);
        ((BaseVectorCandidateConstraint.Or)left.Constraint).Children
            .Select(static child => ((BaseVectorCandidateConstraint.Equal)child).Value.Text)
            .Should().Equal("a", "b");
    }

    [Fact]
    public void Constraint_values_own_caller_strings()
    {
        string source = new(['s', 'e', 'c', 'u', 'r', 'e']);
        BaseVectorFilterValue value = BaseVectorFilterValue.FromString(source);

        value.Text.Should().Be("secure");
        value.Should().Be(BaseVectorFilterValue.FromString("secure"));
    }

    [Fact]
    public void Constraint_complexity_is_fixed_and_fail_closed()
    {
        BaseVectorCandidateConstraint node = new BaseVectorCandidateConstraint.True();
        for (int index = 0; index < 9; index++) node = new BaseVectorCandidateConstraint.And([node]);

        Action normalize = () => BaseVectorConstraintNormalizer.Normalize(node);

        normalize.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Consistency_token_wire_shape_is_bounded_and_redacted()
    {
        BaseVectorConsistencyToken.TryParse("abc-123", out BaseVectorConsistencyToken token).Should().BeTrue();
        token.Encode().Should().Be("abc-123");
        token.ToString().Should().Be("BaseVectorConsistencyToken[redacted]");
        BaseVectorConsistencyToken.TryParse(new string('a', 2049), out _).Should().BeFalse();
    }

    [Fact]
    public void Options_reject_values_outside_locked_ranges()
    {
        var options = new HPDBaseVectorOptions { MaxTopK = 1_001 };
        Action validate = options.Validate;
        validate.Should().Throw<ArgumentOutOfRangeException>();
    }
}
