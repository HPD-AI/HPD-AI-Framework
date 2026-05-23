using HPD.ML.Backends.Pjrt;

namespace HPD.ML.Backends.Tests;

public sealed class StableHloProgramTests
{
    [Fact]
    public void MatMul_UsesExpectedShapesAndDot()
    {
        var mlir = StableHloProgram.MatMul(2, 3, 4);

        Assert.Contains("tensor<2x3xf32>", mlir);
        Assert.Contains("tensor<3x4xf32>", mlir);
        Assert.Contains("tensor<2x4xf32>", mlir);
        Assert.Contains("stablehlo.dot", mlir);
    }

    [Fact]
    public void Reduction_UsesReduceAndOneByOneResult()
    {
        var mlir = StableHloProgram.Sum(2, 3);

        Assert.Contains("\"stablehlo.reduce\"", mlir);
        Assert.Contains("dimensions = array<i64: 0, 1>", mlir);
        Assert.Contains("tensor<1x1xf32>", mlir);
    }

    [Fact]
    public void Mean_UsesScalarDivide()
    {
        var mlir = StableHloProgram.Mean(2, 3);

        Assert.Contains("\"stablehlo.divide\"", mlir);
        Assert.Contains("dense<6.000000e+00>", mlir);
        Assert.Contains("tensor<1x1xf32>", mlir);
    }

    [Fact]
    public void Norm_UsesSquaresReduceAndSqrt()
    {
        var mlir = StableHloProgram.Norm(2, 3);

        Assert.Contains("stablehlo.multiply %arg0, %arg0", mlir);
        Assert.Contains("\"stablehlo.reduce\"", mlir);
        Assert.Contains("\"stablehlo.sqrt\"", mlir);
        Assert.Contains("tensor<1x1xf32>", mlir);
    }

    [Fact]
    public void Transpose_UsesExpectedPermutationAndShape()
    {
        var mlir = StableHloProgram.Transpose(2, 3);

        Assert.Contains("tensor<2x3xf32>", mlir);
        Assert.Contains("tensor<3x2xf32>", mlir);
        Assert.Contains("stablehlo.transpose", mlir);
        Assert.Contains("dims = [1, 0]", mlir);
    }

    [Fact]
    public void Scale_UsesSplatConstant()
    {
        var mlir = StableHloProgram.Scale(2, 3, -2.5f);

        Assert.Contains("dense<-2.500000e+00>", mlir);
        Assert.Contains("stablehlo.multiply", mlir);
        Assert.Contains("tensor<2x3xf32>", mlir);
    }

    [Fact]
    public void MatrixInverse_2x2_UsesClosedFormSlicesAndConcatenate()
    {
        var mlir = StableHloProgram.MatrixInverse(2);

        Assert.Contains("stablehlo.slice %arg0 [0:1, 0:1]", mlir);
        Assert.Contains("\"stablehlo.divide\"", mlir);
        Assert.Contains("stablehlo.concatenate", mlir);
        Assert.Contains("tensor<2x2xf32>", mlir);
    }

    [Fact]
    public void LinearSolve_2x2_UsesClosedFormWithMultipleRightHandSides()
    {
        var mlir = StableHloProgram.LinearSolve(2, 3);

        Assert.Contains("tensor<2x3xf32>", mlir);
        Assert.Contains("stablehlo.slice %arg1 [0:1, 0:1]", mlir);
        Assert.Contains("stablehlo.slice %arg1 [1:2, 2:3]", mlir);
        Assert.Contains("stablehlo.concatenate", mlir);
    }

    [Fact]
    public void LinearSolve_8x8_GeneratesUnrolledProgram()
    {
        var mlir = StableHloProgram.LinearSolve(8, 2);

        Assert.Contains("tensor<8x8xf32>", mlir);
        Assert.Contains("tensor<8x2xf32>", mlir);
        Assert.Contains("\"stablehlo.divide\"", mlir);
        Assert.Contains("stablehlo.concatenate", mlir);
    }

    [Fact]
    public void MatMul_Float64_UsesF64TensorTypes()
    {
        var mlir = StableHloProgram.MatMul(2, 3, 4, PjrtElementType.Float64);

        Assert.Contains("tensor<2x3xf64>", mlir);
        Assert.Contains("tensor<3x4xf64>", mlir);
        Assert.Contains("tensor<2x4xf64>", mlir);
        Assert.Contains("stablehlo.dot", mlir);
    }

    [Fact]
    public void LinearSolve_Float64_UsesF64TensorTypes()
    {
        var mlir = StableHloProgram.LinearSolve(3, 2, PjrtElementType.Float64);

        Assert.Contains("tensor<3x3xf64>", mlir);
        Assert.Contains("tensor<3x2xf64>", mlir);
        Assert.Contains("tensor<1x1xf64>", mlir);
        Assert.Contains("\"stablehlo.divide\"", mlir);
    }

    [Fact]
    public void TriangularSolve_EmitsStableHloPrimitive()
    {
        var mlir = StableHloProgram.TriangularSolve(4, 2, lower: true, unitDiagonal: false);

        Assert.Contains("stablehlo.triangular_solve", mlir);
        Assert.Contains("lower = true", mlir);
        Assert.Contains("unit_diagonal = false", mlir);
        Assert.Contains("tensor<4x4xf32>", mlir);
        Assert.Contains("tensor<4x2xf32>", mlir);
    }

    [Fact]
    public void CholeskySolve_EmitsCholeskyAndTriangularSolves()
    {
        var mlir = StableHloProgram.CholeskySolve(4, 2);

        Assert.Contains("stablehlo.cholesky", mlir);
        Assert.Contains("stablehlo.triangular_solve", mlir);
        Assert.Contains("#stablehlo<transpose TRANSPOSE>", mlir);
        Assert.Contains("tensor<4x4xf32>", mlir);
        Assert.Contains("tensor<4x2xf32>", mlir);
    }

    [Fact]
    public void ShapeOps_GenerateExpectedStableHlo()
    {
        Assert.Contains("stablehlo.reshape", StableHloProgram.Reshape(2, 3, 3, 2));
        Assert.Contains("stablehlo.broadcast_in_dim", StableHloProgram.BroadcastScalar(2, 3));
        Assert.Contains("stablehlo.slice %arg0 [1:3, 0:2]", StableHloProgram.Slice(4, 4, 1, 0, 2, 2));
        Assert.Contains("stablehlo.concatenate", StableHloProgram.Concatenate(2, 3, axis: 1));
    }
}
