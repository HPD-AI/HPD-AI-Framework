using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class FieldTypeTests
{
    [Fact]
    public void Scalar_SetsClrType_NotVector()
    {
        var ft = FieldType.Scalar<float>();

        Assert.Equal(typeof(float), ft.ClrType);
        Assert.False(ft.IsVector);
        Assert.Null(ft.Dimensions);
    }

    [Fact]
    public void Vector_SetsClrType_IsVector_WithDimensions()
    {
        var ft = FieldType.Vector<float>(128);

        Assert.Equal(typeof(float), ft.ClrType);
        Assert.True(ft.IsVector);
        Assert.Equal([128], ft.Dimensions);
    }

    [Fact]
    public void Vector_MultiDimensional()
    {
        var ft = FieldType.Vector<float>(3, 4);

        Assert.Equal([3, 4], ft.Dimensions);
    }

    [Fact]
    public void RecordEquality_SameScalar_AreEqual()
    {
        var a = FieldType.Scalar<int>();
        var b = FieldType.Scalar<int>();

        Assert.Equal(a, b);
    }
}
