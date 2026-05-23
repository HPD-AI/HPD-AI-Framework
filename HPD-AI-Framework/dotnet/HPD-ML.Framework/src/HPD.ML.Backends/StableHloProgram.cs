using System.Globalization;
using System.Text;

namespace HPD.ML.Backends.Pjrt;

internal static class StableHloProgram
{
    public static string MatMul(int leftRows, int sharedDim, int rightCols, PjrtElementType elementType = PjrtElementType.Float32)
    {
        var leftType = Tensor2D(leftRows, sharedDim, elementType);
        var rightType = Tensor2D(sharedDim, rightCols, elementType);
        var resultType = Tensor2D(leftRows, rightCols, elementType);

        return $$"""
            module {
              func.func @main(%arg0: {{leftType}}, %arg1: {{rightType}}) -> {{resultType}} {
                %0 = stablehlo.dot %arg0, %arg1 : ({{leftType}}, {{rightType}}) -> {{resultType}}
                return %0 : {{resultType}}
              }
            }
            """;
    }

    public static string Unary(int rows, int cols, string stableHloOp, PjrtElementType elementType = PjrtElementType.Float32)
    {
        var type = Tensor2D(rows, cols, elementType);

        return $$"""
            module {
              func.func @main(%arg0: {{type}}) -> {{type}} {
                %0 = {{stableHloOp}} %arg0 : {{type}}
                return %0 : {{type}}
              }
            }
            """;
    }

    public static string Binary(int rows, int cols, string stableHloOp, PjrtElementType elementType = PjrtElementType.Float32)
    {
        var type = Tensor2D(rows, cols, elementType);

        return $$"""
            module {
              func.func @main(%arg0: {{type}}, %arg1: {{type}}) -> {{type}} {
                %0 = {{stableHloOp}} %arg0, %arg1 : {{type}}
                return %0 : {{type}}
              }
            }
            """;
    }

    public static string Sum(int rows, int cols, PjrtElementType elementType = PjrtElementType.Float32)
        => Reduction(rows, cols, divideByElementCount: false, elementType);

    public static string Mean(int rows, int cols, PjrtElementType elementType = PjrtElementType.Float32)
        => Reduction(rows, cols, divideByElementCount: true, elementType);

    public static string Norm(int rows, int cols, PjrtElementType elementType = PjrtElementType.Float32)
    {
        var inputType = Tensor2D(rows, cols, elementType);
        var scalarType = ScalarTensor(elementType);
        var outputType = Tensor2D(1, 1, elementType);

        return $$"""
            module {
              func.func @main(%arg0: {{inputType}}) -> {{outputType}} {
                %squares = stablehlo.multiply %arg0, %arg0 : {{inputType}}
                %zero = stablehlo.constant dense<0.000000e+00> : {{scalarType}}
                %sum = "stablehlo.reduce"(%squares, %zero) ({
                ^bb0(%lhs: {{scalarType}}, %rhs: {{scalarType}}):
                  %add = "stablehlo.add"(%lhs, %rhs) : ({{scalarType}}, {{scalarType}}) -> {{scalarType}}
                  "stablehlo.return"(%add) : ({{scalarType}}) -> ()
                }) {
                  dimensions = array<i64: 0, 1>
                } : ({{inputType}}, {{scalarType}}) -> {{scalarType}}
                %norm = "stablehlo.sqrt"(%sum) : ({{scalarType}}) -> {{scalarType}}
                %out = stablehlo.reshape %norm : ({{scalarType}}) -> {{outputType}}
                return %out : {{outputType}}
              }
            }
            """;
    }

    public static string Transpose(int rows, int cols, PjrtElementType elementType = PjrtElementType.Float32)
    {
        var inputType = Tensor2D(rows, cols, elementType);
        var resultType = Tensor2D(cols, rows, elementType);

        return $$"""
            module {
              func.func @main(%arg0: {{inputType}}) -> {{resultType}} {
                %0 = stablehlo.transpose %arg0, dims = [1, 0] : ({{inputType}}) -> {{resultType}}
                return %0 : {{resultType}}
              }
            }
            """;
    }

    public static string Reshape(int sourceRows, int sourceCols, int targetRows, int targetCols, PjrtElementType elementType = PjrtElementType.Float32)
    {
        var inputType = Tensor2D(sourceRows, sourceCols, elementType);
        var resultType = Tensor2D(targetRows, targetCols, elementType);

        return $$"""
            module {
              func.func @main(%arg0: {{inputType}}) -> {{resultType}} {
                %0 = stablehlo.reshape %arg0 : ({{inputType}}) -> {{resultType}}
                return %0 : {{resultType}}
              }
            }
            """;
    }

    public static string BroadcastScalar(int targetRows, int targetCols, PjrtElementType elementType = PjrtElementType.Float32)
    {
        var scalarType = Tensor2D(1, 1, elementType);
        var resultType = Tensor2D(targetRows, targetCols, elementType);

        return $$"""
            module {
              func.func @main(%arg0: {{scalarType}}) -> {{resultType}} {
                %0 = stablehlo.broadcast_in_dim %arg0, dims = [0, 1] : ({{scalarType}}) -> {{resultType}}
                return %0 : {{resultType}}
              }
            }
            """;
    }

    public static string Slice(
        int sourceRows,
        int sourceCols,
        int startRow,
        int startCol,
        int rowCount,
        int colCount,
        PjrtElementType elementType = PjrtElementType.Float32)
    {
        var inputType = Tensor2D(sourceRows, sourceCols, elementType);
        var resultType = Tensor2D(rowCount, colCount, elementType);

        return $$"""
            module {
              func.func @main(%arg0: {{inputType}}) -> {{resultType}} {
                %0 = stablehlo.slice %arg0 [{{startRow}}:{{startRow + rowCount}}, {{startCol}}:{{startCol + colCount}}] : ({{inputType}}) -> {{resultType}}
                return %0 : {{resultType}}
              }
            }
            """;
    }

    public static string Concatenate(int rows, int cols, int axis, PjrtElementType elementType = PjrtElementType.Float32)
    {
        var inputType = Tensor2D(rows, cols, elementType);
        var resultType = axis switch
        {
            0 => Tensor2D(rows * 2, cols, elementType),
            1 => Tensor2D(rows, cols * 2, elementType),
            _ => throw new ArgumentOutOfRangeException(nameof(axis), "Axis must be 0 or 1.")
        };

        return $$"""
            module {
              func.func @main(%arg0: {{inputType}}, %arg1: {{inputType}}) -> {{resultType}} {
                %0 = stablehlo.concatenate %arg0, %arg1, dim = {{axis}} : ({{inputType}}, {{inputType}}) -> {{resultType}}
                return %0 : {{resultType}}
              }
            }
            """;
    }

    public static string Scale(int rows, int cols, float scalar, PjrtElementType elementType = PjrtElementType.Float32)
    {
        var type = Tensor2D(rows, cols, elementType);
        var scalarLiteral = ScalarLiteral(scalar);

        return $$"""
            module {
              func.func @main(%arg0: {{type}}) -> {{type}} {
                %scalar = stablehlo.constant dense<{{scalarLiteral}}> : {{type}}
                %0 = stablehlo.multiply %arg0, %scalar : {{type}}
                return %0 : {{type}}
              }
            }
            """;
    }

    public static string ReLU(int rows, int cols, PjrtElementType elementType = PjrtElementType.Float32)
    {
        var type = Tensor2D(rows, cols, elementType);
        var zero = ZeroLiteral(elementType);

        return $$"""
            module {
              func.func @main(%arg0: {{type}}) -> {{type}} {
                %zero = stablehlo.constant dense<{{zero}}> : {{type}}
                %0 = stablehlo.maximum %arg0, %zero : {{type}}
                return %0 : {{type}}
              }
            }
            """;
    }

    public static string Scale(int rows, int cols, double scalar, PjrtElementType elementType)
    {
        var type = Tensor2D(rows, cols, elementType);
        var scalarLiteral = ScalarLiteral(scalar);

        return $$"""
            module {
              func.func @main(%arg0: {{type}}) -> {{type}} {
                %scalar = stablehlo.constant dense<{{scalarLiteral}}> : {{type}}
                %0 = stablehlo.multiply %arg0, %scalar : {{type}}
                return %0 : {{type}}
              }
            }
            """;
    }

    public static string Scale(int rows, int cols, int scalar, PjrtElementType elementType)
        => ScaleInteger(rows, cols, scalar.ToString(CultureInfo.InvariantCulture), elementType);

    public static string Scale(int rows, int cols, long scalar, PjrtElementType elementType)
        => ScaleInteger(rows, cols, scalar.ToString(CultureInfo.InvariantCulture), elementType);

    public static string Scale(int rows, int cols, BFloat16 scalar, PjrtElementType elementType)
    {
        var type = Tensor2D(rows, cols, elementType);
        var scalarLiteral = ScalarLiteral(scalar.ToSingle());

        return $$"""
            module {
              func.func @main(%arg0: {{type}}) -> {{type}} {
                %scalar = stablehlo.constant dense<{{scalarLiteral}}> : {{type}}
                %0 = stablehlo.multiply %arg0, %scalar : {{type}}
                return %0 : {{type}}
              }
            }
            """;
    }

    private static string ScaleInteger(int rows, int cols, string scalarLiteral, PjrtElementType elementType)
    {
        var type = Tensor2D(rows, cols, elementType);

        return $$"""
            module {
              func.func @main(%arg0: {{type}}) -> {{type}} {
                %scalar = stablehlo.constant dense<{{scalarLiteral}}> : {{type}}
                %0 = stablehlo.multiply %arg0, %scalar : {{type}}
                return %0 : {{type}}
              }
            }
            """;
    }

    public static string MatrixInverse(int n, PjrtElementType elementType = PjrtElementType.Float32)
    {
        ValidateSmallSolveSize(n);
        return GaussJordan(n, n, inverse: true, elementType);
    }

    public static string LinearSolve(int n, int rhsCols, PjrtElementType elementType = PjrtElementType.Float32)
    {
        ValidateSmallSolveSize(n);
        if (rhsCols <= 0)
            throw new ArgumentOutOfRangeException(nameof(rhsCols), "Right-hand side columns must be positive.");

        return GaussJordan(n, rhsCols, inverse: false, elementType);
    }

    public static string TriangularSolve(
        int n,
        int rhsCols,
        bool lower,
        bool unitDiagonal,
        PjrtElementType elementType = PjrtElementType.Float32,
        bool transposeA = false)
    {
        if (n <= 0)
            throw new ArgumentOutOfRangeException(nameof(n), "Matrix dimension must be positive.");
        if (rhsCols <= 0)
            throw new ArgumentOutOfRangeException(nameof(rhsCols), "Right-hand side columns must be positive.");

        var matrixType = Tensor2D(n, n, elementType);
        var rhsType = Tensor2D(n, rhsCols, elementType);
        var lowerLiteral = lower ? "true" : "false";
        var unitLiteral = unitDiagonal ? "true" : "false";
        var transpose = transposeA ? "TRANSPOSE" : "NO_TRANSPOSE";

        return $$"""
            module {
              func.func @main(%arg0: {{matrixType}}, %arg1: {{rhsType}}) -> {{rhsType}} {
                %0 = "stablehlo.triangular_solve"(%arg0, %arg1) <{left_side = true, lower = {{lowerLiteral}}, transpose_a = #stablehlo<transpose {{transpose}}>, unit_diagonal = {{unitLiteral}}}> : ({{matrixType}}, {{rhsType}}) -> {{rhsType}}
                return %0 : {{rhsType}}
              }
            }
            """;
    }

    public static string CholeskySolve(int n, int rhsCols, PjrtElementType elementType = PjrtElementType.Float32)
    {
        if (n <= 0)
            throw new ArgumentOutOfRangeException(nameof(n), "Matrix dimension must be positive.");
        if (rhsCols <= 0)
            throw new ArgumentOutOfRangeException(nameof(rhsCols), "Right-hand side columns must be positive.");

        var matrixType = Tensor2D(n, n, elementType);
        var rhsType = Tensor2D(n, rhsCols, elementType);

        return $$"""
            module {
              func.func @main(%arg0: {{matrixType}}, %arg1: {{rhsType}}) -> {{rhsType}} {
                %l = stablehlo.cholesky %arg0 {lower = true} : ({{matrixType}}) -> {{matrixType}}
                %y = "stablehlo.triangular_solve"(%l, %arg1) <{left_side = true, lower = true, transpose_a = #stablehlo<transpose NO_TRANSPOSE>, unit_diagonal = false}> : ({{matrixType}}, {{rhsType}}) -> {{rhsType}}
                %x = "stablehlo.triangular_solve"(%l, %y) <{left_side = true, lower = true, transpose_a = #stablehlo<transpose TRANSPOSE>, unit_diagonal = false}> : ({{matrixType}}, {{rhsType}}) -> {{rhsType}}
                return %x : {{rhsType}}
              }
            }
            """;
    }

    private static string GaussJordan(int n, int rhsCols, bool inverse, PjrtElementType elementType)
    {
        var matrixType = Tensor2D(n, n, elementType);
        var rhsType = Tensor2D(n, rhsCols, elementType);
        var resultType = Tensor2D(n, rhsCols, elementType);
        var scalarType = Tensor2D(1, 1, elementType);
        var signature = inverse
            ? $"func.func @main(%arg0: {matrixType}) -> {resultType}"
            : $"func.func @main(%arg0: {matrixType}, %arg1: {rhsType}) -> {resultType}";

        var builder = new StringBuilder();
        var counter = 0;
        var a = new string[n, n];
        var x = new string[n, rhsCols];

        string Next(string prefix) => $"%{prefix}{counter++}";
        void Line(string text) => builder.Append("                ").AppendLine(text);

        builder.AppendLine("            module {");
        builder.Append("              ").Append(signature).AppendLine(" {");
        Line($"%zero = stablehlo.constant dense<{ZeroLiteral(elementType)}> : {scalarType}");
        Line($"%one = stablehlo.constant dense<{OneLiteral(elementType)}> : {scalarType}");

        for (var row = 0; row < n; row++)
            for (var col = 0; col < n; col++)
            {
                var name = Next("a");
                Line($"{name} = stablehlo.slice %arg0 [{row}:{row + 1}, {col}:{col + 1}] : ({matrixType}) -> {scalarType}");
                a[row, col] = name;
            }

        for (var row = 0; row < n; row++)
            for (var col = 0; col < rhsCols; col++)
            {
                if (inverse)
                {
                    x[row, col] = row == col ? "%one" : "%zero";
                }
                else
                {
                    var name = Next("b");
                    Line($"{name} = stablehlo.slice %arg1 [{row}:{row + 1}, {col}:{col + 1}] : ({rhsType}) -> {scalarType}");
                    x[row, col] = name;
                }
            }

        for (var pivotIndex = 0; pivotIndex < n; pivotIndex++)
        {
            var pivot = a[pivotIndex, pivotIndex];
            for (var col = pivotIndex; col < n; col++)
                a[pivotIndex, col] = Divide(builder, Next, a[pivotIndex, col], pivot, scalarType);

            for (var col = 0; col < rhsCols; col++)
                x[pivotIndex, col] = Divide(builder, Next, x[pivotIndex, col], pivot, scalarType);

            for (var row = 0; row < n; row++)
            {
                if (row == pivotIndex)
                    continue;

                var factor = a[row, pivotIndex];
                for (var col = pivotIndex; col < n; col++)
                    a[row, col] = Subtract(builder, Next, a[row, col], Multiply(builder, Next, factor, a[pivotIndex, col], scalarType), scalarType);

                for (var col = 0; col < rhsCols; col++)
                    x[row, col] = Subtract(builder, Next, x[row, col], Multiply(builder, Next, factor, x[pivotIndex, col], scalarType), scalarType);
            }
        }

        var rows = new string[n];
        for (var row = 0; row < n; row++)
            rows[row] = ConcatenateRow(builder, Next, x, row, rhsCols, elementType);

        var output = ConcatenateRows(builder, Next, rows, n, rhsCols, elementType);
        Line($"return {output} : {resultType}");
        builder.AppendLine("              }");
        builder.AppendLine("            }");
        return builder.ToString();
    }

    private static string Divide(StringBuilder builder, Func<string, string> next, string left, string right, string scalarType)
    {
        var name = next("d");
        builder.Append("                ").AppendLine($"{name} = \"stablehlo.divide\"({left}, {right}) : ({scalarType}, {scalarType}) -> {scalarType}");
        return name;
    }

    private static string Multiply(StringBuilder builder, Func<string, string> next, string left, string right, string scalarType)
    {
        var name = next("m");
        builder.Append("                ").AppendLine($"{name} = stablehlo.multiply {left}, {right} : {scalarType}");
        return name;
    }

    private static string Subtract(StringBuilder builder, Func<string, string> next, string left, string right, string scalarType)
    {
        var name = next("s");
        builder.Append("                ").AppendLine($"{name} = stablehlo.subtract {left}, {right} : {scalarType}");
        return name;
    }

    private static string ConcatenateRow(StringBuilder builder, Func<string, string> next, string[,] values, int row, int cols, PjrtElementType elementType)
    {
        if (cols == 1)
            return values[row, 0];

        var operands = Enumerable.Range(0, cols).Select(col => values[row, col]).ToArray();
        var operandTypes = string.Join(", ", Enumerable.Repeat(Tensor2D(1, 1, elementType), cols));
        var name = next("row");
        builder.Append("                ").AppendLine($"{name} = stablehlo.concatenate {string.Join(", ", operands)}, dim = 1 : ({operandTypes}) -> {Tensor2D(1, cols, elementType)}");
        return name;
    }

    private static string ConcatenateRows(StringBuilder builder, Func<string, string> next, string[] rows, int rowCount, int cols, PjrtElementType elementType)
    {
        if (rowCount == 1)
            return rows[0];

        var operandTypes = string.Join(", ", Enumerable.Repeat(Tensor2D(1, cols, elementType), rowCount));
        var name = next("out");
        builder.Append("                ").AppendLine($"{name} = stablehlo.concatenate {string.Join(", ", rows)}, dim = 0 : ({operandTypes}) -> {Tensor2D(rowCount, cols, elementType)}");
        return name;
    }

    private static void ValidateSmallSolveSize(int n)
    {
        if (n is < 1 or > 8)
            throw new NotSupportedException("XLA generated linear solve currently supports matrix sizes 1 through 8.");
    }

    private static string Reduction(int rows, int cols, bool divideByElementCount, PjrtElementType elementType)
    {
        var inputType = Tensor2D(rows, cols, elementType);
        var outputType = Tensor2D(1, 1, elementType);
        var scalarType = ScalarTensor(elementType);
        var denominator = ScalarLiteral(checked(rows * cols));
        var postReduction = divideByElementCount
            ? $$"""
                  %count = stablehlo.constant dense<{{denominator}}> : {{scalarType}}
                  %mean = "stablehlo.divide"(%sum, %count) : ({{scalarType}}, {{scalarType}}) -> {{scalarType}}
                  %out = stablehlo.reshape %mean : ({{scalarType}}) -> {{outputType}}
              """
            : $$"""
                  %out = stablehlo.reshape %sum : ({{scalarType}}) -> {{outputType}}
              """;

        return $$"""
            module {
              func.func @main(%arg0: {{inputType}}) -> {{outputType}} {
                %zero = stablehlo.constant dense<{{ZeroLiteral(elementType)}}> : {{scalarType}}
                %sum = "stablehlo.reduce"(%arg0, %zero) ({
                ^bb0(%lhs: {{scalarType}}, %rhs: {{scalarType}}):
                  %add = "stablehlo.add"(%lhs, %rhs) : ({{scalarType}}, {{scalarType}}) -> {{scalarType}}
                  "stablehlo.return"(%add) : ({{scalarType}}) -> ()
                }) {
                  dimensions = array<i64: 0, 1>
                } : ({{inputType}}, {{scalarType}}) -> {{scalarType}}
            {{postReduction}}
                return %out : {{outputType}}
              }
            }
            """;
    }

    private static string Tensor2D(int rows, int cols, PjrtElementType elementType = PjrtElementType.Float32)
        => $"tensor<{rows}x{cols}x{ElementName(elementType)}>";

    private static string ScalarTensor(PjrtElementType elementType)
        => $"tensor<{ElementName(elementType)}>";

    private static string ElementName(PjrtElementType elementType)
        => elementType switch
        {
            PjrtElementType.Float32 => "f32",
            PjrtElementType.Float64 => "f64",
            PjrtElementType.Int32 => "i32",
            PjrtElementType.Int64 => "i64",
            PjrtElementType.BFloat16 => "bf16",
            _ => throw new NotSupportedException($"Unsupported XLA element type: {elementType}")
        };

    private static string ZeroLiteral(PjrtElementType elementType)
        => elementType switch
        {
            PjrtElementType.Float32 or PjrtElementType.Float64 or PjrtElementType.BFloat16 => "0.000000e+00",
            PjrtElementType.Int32 or PjrtElementType.Int64 => "0",
            _ => throw new NotSupportedException($"Unsupported XLA element type: {elementType}")
        };

    private static string OneLiteral(PjrtElementType elementType)
        => elementType switch
        {
            PjrtElementType.Float32 or PjrtElementType.Float64 or PjrtElementType.BFloat16 => "1.000000e+00",
            PjrtElementType.Int32 or PjrtElementType.Int64 => "1",
            _ => throw new NotSupportedException($"Unsupported XLA element type: {elementType}")
        };

    private static string ScalarLiteral(int value)
        => value.ToString("0.000000", CultureInfo.InvariantCulture) + "e+00";

    private static string ScalarLiteral(float value)
        => value.ToString("0.000000", CultureInfo.InvariantCulture) + "e+00";

    private static string ScalarLiteral(double value)
        => value.ToString("0.000000", CultureInfo.InvariantCulture) + "e+00";
}
