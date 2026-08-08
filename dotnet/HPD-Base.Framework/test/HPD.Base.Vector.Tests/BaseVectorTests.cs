using FluentAssertions;
using Xunit;

namespace HPD.Base.Vector.Tests;

public sealed class BaseVectorTests
{
    [Fact]
    public void Create_owns_input_and_output_storage()
    {
        float[] input = [1, 2, 3];
        BaseVector vector = BaseVector.Create(input);

        input[0] = 99;
        float[] output = vector.ToArray();
        output[1] = 99;

        vector[0].Should().Be(1);
        vector[1].Should().Be(2);
    }

    [Fact]
    public void TryCreate_rejects_empty_and_non_finite_values()
    {
        BaseVector.TryCreate([], out _).Should().BeFalse();
        BaseVector.TryCreate([float.NaN], out _).Should().BeFalse();
        BaseVector.TryCreate([float.PositiveInfinity], out _).Should().BeFalse();
    }

    [Fact]
    public void Default_is_not_readable()
    {
        BaseVector vector = default;

        vector.Dimensions.Should().Be(0);
        Action read = () => _ = vector[0];
        read.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Equality_uses_float32_bits()
    {
        BaseVector.Create([0f]).Should().NotBe(BaseVector.Create([-0f]));
        BaseVector.Create([1f, 2f]).Should().Be(BaseVector.Create([1f, 2f]));
    }

    [Fact]
    public void Diagnostic_text_never_contains_elements()
    {
        BaseVector.Create([12345.5f, -9876.25f]).ToString()
            .Should().Be("BaseVector(dimensions=2)");
    }
}
