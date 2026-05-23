using Helium.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Helium.Generators.Tests;

public class HeliumGeneratorTests
{
    [Fact]
    public void QuantizedType_GeneratesLimbFieldsAndAddition()
    {
        const string source = """
using Helium.Generators;

namespace Demo;

[QuantizedType(Bits = 128)]
public partial struct MyInt128 { }
""";

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(result.GeneratedTrees, tree => tree.FilePath.EndsWith("MyInt128.Quantized.g.cs", StringComparison.Ordinal));
        var text = generated.GetText().ToString();
        Assert.Contains("public ulong L0", text);
        Assert.Contains("public ulong L1", text);
        Assert.Contains("operator +", text);
        Assert.Contains("operator -", text);
        Assert.Contains("operator *", text);
    }

    [Fact]
    public void QuantizedType_512Bit_GeneratedArithmeticUsesAllLimbs()
    {
        const string source = """
using Helium.Generators;

namespace Demo;

[QuantizedType(Bits = 512)]
public partial struct MyInt512 { }
""";

        var result = RunGenerator(source);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var generated = Assert.Single(result.GeneratedTrees, tree => tree.FilePath.EndsWith("MyInt512.Quantized.g.cs", StringComparison.Ordinal));
        var text = generated.GetText().ToString();
        Assert.Contains("public ulong L7", text);

        var assembly = LoadAssembly(result.OutputCompilation);
        var type = assembly.GetType("Demo.MyInt512", throwOnError: true)!;
        var maxLow = NewGeneratedValue(type, ulong.MaxValue, 0, 0, 0, 0, 0, 0, 0);
        var one = NewGeneratedValue(type, 1, 0, 0, 0, 0, 0, 0, 0);
        var two = NewGeneratedValue(type, 2, 0, 0, 0, 0, 0, 0, 0);
        var limbOne = NewGeneratedValue(type, 0, 1, 0, 0, 0, 0, 0, 0);

        var sum = InvokeOperator(type, "op_Addition", maxLow, one);
        AssertLimbs(type, sum, 0, 1, 0, 0, 0, 0, 0, 0);

        var difference = InvokeOperator(type, "op_Subtraction", sum, one);
        AssertLimbs(type, difference, ulong.MaxValue, 0, 0, 0, 0, 0, 0, 0);

        var product = InvokeOperator(type, "op_Multiply", maxLow, two);
        AssertLimbs(type, product, ulong.MaxValue - 1, 1, 0, 0, 0, 0, 0, 0);

        var shiftedProduct = InvokeOperator(type, "op_Multiply", limbOne, limbOne);
        AssertLimbs(type, shiftedProduct, 0, 0, 1, 0, 0, 0, 0, 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(4097)]
    public void QuantizedType_InvalidBitWidth_ReportsDiagnostic(int bits)
    {
        var source = $$"""
using Helium.Generators;

namespace Demo;

[QuantizedType(Bits = {{bits}})]
public partial struct BadInt { }
""";

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "CS_HELIUM_004");
        Assert.DoesNotContain(result.GeneratedTrees, tree => tree.FilePath.EndsWith("BadInt.Quantized.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void RnsType_GeneratesResidueFieldsForCoprimeModuli()
    {
        const string source = """
using Helium.Generators;

namespace Demo;

[RnsType(3UL, 5UL, 17UL)]
public partial struct RnsValue { }
""";

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(result.GeneratedTrees, tree => tree.FilePath.EndsWith("RnsValue.Rns.g.cs", StringComparison.Ordinal));
        var text = generated.GetText().ToString();
        Assert.Contains("public const ulong Modulus0 = 3UL;", text);
        Assert.Contains("private const ulong Barrett0", text);
        Assert.Contains("public ulong R2", text);
        Assert.Contains("public static RnsValue Zero", text);
        Assert.Contains("public static RnsValue One", text);
        Assert.Contains("operator +", text);
        Assert.Contains("operator -", text);
        Assert.Contains("operator *", text);
        Assert.Contains("Reduce0", text);
    }

    [Fact]
    public void RnsType_GeneratedCode_DoesNotUseRuntimeDivisionOrModulo()
    {
        const string source = """
using Helium.Generators;

namespace Demo;

[RnsType(998244353UL, 18446744069414584321UL)]
public partial struct RnsValue { }
""";

        var result = RunGenerator(source);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var generated = Assert.Single(result.GeneratedTrees, tree => tree.FilePath.EndsWith("RnsValue.Rns.g.cs", StringComparison.Ordinal));
        var text = generated.GetText().ToString();
        Assert.Contains("private const ulong Barrett0", text);
        Assert.Contains("private const ulong Barrett1", text);
        Assert.Contains("private static ulong Reduce0(System.UInt128 value)", text);
        Assert.Contains("private static ulong Reduce1(System.UInt128 value)", text);
        Assert.DoesNotContain("%", text);
        Assert.DoesNotContain(" / ", text);
    }

    [Fact]
    public void RnsType_GeneratedArithmetic_ReducesEachResidue()
    {
        const string source = """
using Helium.Generators;

namespace Demo;

[RnsType(5UL, 7UL)]
public partial struct RnsValue { }
""";

        var result = RunGenerator(source);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var assembly = LoadAssembly(result.OutputCompilation);
        var type = assembly.GetType("Demo.RnsValue", throwOnError: true)!;
        var a = Activator.CreateInstance(type, 4UL)!;
        var b = Activator.CreateInstance(type, 3UL)!;

        var sum = InvokeOperator(type, "op_Addition", a, b);
        var difference = InvokeOperator(type, "op_Subtraction", a, b);
        var product = InvokeOperator(type, "op_Multiply", a, b);

        AssertResidues(type, sum, 2UL, 0UL);
        AssertResidues(type, difference, 1UL, 1UL);
        AssertResidues(type, product, 2UL, 5UL);

        var zero = type.GetProperty("Zero")!.GetValue(null)!;
        var one = type.GetProperty("One")!.GetValue(null)!;
        AssertResidues(type, zero, 0UL, 0UL);
        AssertResidues(type, one, 1UL, 1UL);
    }

    [Fact]
    public void RnsType_GeneratedArithmetic_HandlesConstructorReductionAndBorrow()
    {
        const string source = """
using Helium.Generators;

namespace Demo;

[RnsType(5UL, 7UL, 11UL)]
public partial struct RnsValue { }
""";

        var result = RunGenerator(source);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var assembly = LoadAssembly(result.OutputCompilation);
        var type = assembly.GetType("Demo.RnsValue", throwOnError: true)!;
        var a = Activator.CreateInstance(type, 26UL)!;
        var b = Activator.CreateInstance(type, 4UL)!;

        var difference = InvokeOperator(type, "op_Subtraction", a, b);
        var product = InvokeOperator(type, "op_Multiply", a, b);

        AssertResidues(type, a, 1UL, 5UL, 4UL);
        AssertResidues(type, difference, 2UL, 1UL, 0UL);
        AssertResidues(type, product, 4UL, 6UL, 5UL);
    }

    [Fact]
    public void RnsType_GeneratedArithmetic_ReducesGoldilocksResidueWithoutModulo()
    {
        const string source = """
using Helium.Generators;

namespace Demo;

[RnsType(18446744069414584321UL)]
public partial struct GoldilocksRns { }
""";

        var result = RunGenerator(source);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var assembly = LoadAssembly(result.OutputCompilation);
        var type = assembly.GetType("Demo.GoldilocksRns", throwOnError: true)!;
        var modulus = 18446744069414584321UL;
        var a = Activator.CreateInstance(type, modulus - 1)!;
        var b = Activator.CreateInstance(type, modulus - 2)!;

        var sum = InvokeOperator(type, "op_Addition", a, b);
        var difference = InvokeOperator(type, "op_Subtraction", a, b);
        var product = InvokeOperator(type, "op_Multiply", a, b);

        AssertResidues(type, sum, modulus - 3);
        AssertResidues(type, difference, 1UL);
        AssertResidues(type, product, 2UL);
    }

    [Fact]
    public void RnsType_NonCoprimeModuli_ReportsDiagnostic()
    {
        const string source = """
using Helium.Generators;

namespace Demo;

[RnsType(6UL, 9UL)]
public partial struct BadRns { }
""";

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "CS_HELIUM_003");
    }

    [Fact]
    public void CompileTimeDerivative_ExpressionBodiedMethod_GeneratesDerivativeCoefficients()
    {
        const string source = """
using Helium.Generators;

namespace Demo;

public static partial class Control
{
    [CompileTimeDerivative]
    public static int Law(int x) => 3 * x * x * x + 2 * x - 5;
}
""";

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(result.GeneratedTrees, tree => tree.FilePath.EndsWith("Control.Law.Derivative.g.cs", StringComparison.Ordinal));
        var text = generated.GetText().ToString();
        Assert.Contains("System.ReadOnlySpan<long>", text);
        Assert.Contains("=> [2L, 0L, 9L];", text);
    }

    [Fact]
    public void CompileTimeDerivative_UnsupportedExpression_ReportsDiagnostic()
    {
        const string source = """
using Helium.Generators;

namespace Demo;

public static partial class Control
{
    [CompileTimeDerivative]
    public static int Law(int x) => System.Math.Abs(x);
}
""";

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "CS_HELIUM_001");
    }

    [Fact]
    public void CompileTimeDerivative_BlockBody_ReportsDiagnostic()
    {
        const string source = """
using Helium.Generators;

namespace Demo;

public static partial class Control
{
    [CompileTimeDerivative]
    public static int Law(int x)
    {
        return x * x;
    }
}
""";

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "CS_HELIUM_001");
    }

    [Fact]
    public void CompileTimeDerivative_TwoParameters_ReportsDiagnostic()
    {
        const string source = """
using Helium.Generators;

namespace Demo;

public static partial class Control
{
    [CompileTimeDerivative]
    public static int Law(int x, int y) => x * y;
}
""";

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "CS_HELIUM_001");
    }

    [Fact]
    public void CompileTimeDerivative_EmitsReadOnlySpanLiteralNotArrayAllocation()
    {
        const string source = """
using Helium.Generators;

namespace Demo;

public static partial class Control
{
    [CompileTimeDerivative]
    public static int Law(int x) => x * x * x + x;
}
""";

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(result.GeneratedTrees, tree => tree.FilePath.EndsWith("Control.Law.Derivative.g.cs", StringComparison.Ordinal));
        var text = generated.GetText().ToString();
        Assert.Contains("System.ReadOnlySpan<long>", text);
        Assert.Contains("=> [1L, 0L, 3L];", text);
        Assert.DoesNotContain("new long[]", text);
        Assert.DoesNotContain("new[]", text);
    }

    private static GeneratorTestResult RunGenerator(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [syntaxTree],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create([new HeliumGenerator().AsSourceGenerator()], parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        var runResult = driver.GetRunResult();
        return new GeneratorTestResult(
            outputCompilation.SyntaxTrees.Where(tree => tree != syntaxTree).ToArray(),
            runResult.Diagnostics.AddRange(diagnostics).AddRange(outputCompilation.GetDiagnostics()),
            outputCompilation);
    }

    private static IEnumerable<MetadataReference> References() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location));

    private static System.Reflection.Assembly LoadAssembly(Compilation compilation)
    {
        using var stream = new System.IO.MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        stream.Position = 0;
        return System.Reflection.Assembly.Load(stream.ToArray());
    }

    private static object InvokeOperator(Type type, string name, object left, object right) =>
        type.GetMethod(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [left, right])!;

    private static void AssertResidues(Type type, object value, ulong r0, ulong r1)
    {
        Assert.Equal(r0, (ulong)type.GetProperty("R0")!.GetValue(value)!);
        Assert.Equal(r1, (ulong)type.GetProperty("R1")!.GetValue(value)!);
    }

    private static void AssertResidues(Type type, object value, ulong r0, ulong r1, ulong r2)
    {
        Assert.Equal(r0, (ulong)type.GetProperty("R0")!.GetValue(value)!);
        Assert.Equal(r1, (ulong)type.GetProperty("R1")!.GetValue(value)!);
        Assert.Equal(r2, (ulong)type.GetProperty("R2")!.GetValue(value)!);
    }

    private static void AssertResidues(Type type, object value, ulong r0)
    {
        Assert.Equal(r0, (ulong)type.GetProperty("R0")!.GetValue(value)!);
    }

    private static object NewGeneratedValue(Type type, params ulong[] limbs)
    {
        var args = limbs.Cast<object>().ToArray();
        return Activator.CreateInstance(type, args)!;
    }

    private static void AssertLimbs(Type type, object value, params ulong[] expected)
    {
        for (var i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], (ulong)type.GetProperty($"L{i}")!.GetValue(value)!);
    }

    private sealed class GeneratorTestResult
    {
        public GeneratorTestResult(
            IReadOnlyList<SyntaxTree> generatedTrees,
            IReadOnlyList<Diagnostic> diagnostics,
            Compilation outputCompilation)
        {
            GeneratedTrees = generatedTrees;
            Diagnostics = diagnostics;
            OutputCompilation = outputCompilation;
        }

        public IReadOnlyList<SyntaxTree> GeneratedTrees { get; }
        public IReadOnlyList<Diagnostic> Diagnostics { get; }
        public Compilation OutputCompilation { get; }
    }
}
