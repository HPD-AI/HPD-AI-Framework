using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace HPD.Math.Generators;

[Generator]
public sealed class MathGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor TypeMustBePartial = new(
        "HPDMATH001",
        "Generated math type must be partial",
        "Type '{0}' must be declared partial for HPD-Math generation",
        "HPD.Math.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor TypeMustBeStruct = new(
        "HPDMATH002",
        "Generated math type must be a struct",
        "Type '{0}' must be a struct for HPD-Math generation",
        "HPD.Math.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor WitnessValueMustBePositive = new(
        "HPDMATH003",
        "Static witness value must be positive",
        "Witness '{0}' value must be positive",
        "HPD.Math.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ScopeCapacityMustBePositive = new(
        "HPDMATH005",
        "Scope capacity must be positive",
        "Scope '{0}' requires positive Terms, Workspace, and Handles capacities",
        "HPD.Math.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is TypeDeclarationSyntax { AttributeLists.Count: > 0 },
                static (ctx, _) => GetModel(ctx))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!);

        context.RegisterSourceOutput(candidates.Collect(), static (ctx, models) =>
        {
            foreach (var model in models)
            {
                foreach (var diagnostic in model.Diagnostics)
                    ctx.ReportDiagnostic(diagnostic);

                if (!model.CanGenerate)
                    continue;

                ctx.AddSource(
                    model.HintName,
                    SourceText.From(Generate(model), Encoding.UTF8));
            }
        });
    }

    private static GeneratedType? GetModel(GeneratorSyntaxContext context)
    {
        if (context.Node is not TypeDeclarationSyntax syntax)
            return null;

        if (context.SemanticModel.GetDeclaredSymbol(syntax) is not INamedTypeSymbol symbol)
            return null;

        var attributes = symbol.GetAttributes();
        var dimensions = GetIntAttribute(attributes, "HPD.Math.Core.DimensionAttribute", "DimensionAttribute");
        var precision = GetIntAttribute(attributes, "HPD.Math.Core.PrecisionAttribute", "PrecisionAttribute");
        var prime = GetIntAttribute(attributes, "HPD.Math.Core.PrimeModulusAttribute", "PrimeModulusAttribute");
        var polynomialContext = GetPolynomialContextAttribute(attributes);
        var sparsePolynomialContext = GetSparsePolynomialContextAttribute(attributes);
        var polynomialScope = GetPolynomialScopeAttribute(attributes);
        var matrixContext = GetMatrixContextAttribute(attributes);
        var matrixScope = GetMatrixScopeAttribute(attributes);
        var reverseDiffContext = GetReverseDiffContextAttribute(attributes);
        var reverseDiffScope = GetReverseDiffScopeAttribute(attributes);
        var polynomialQuotientContext = GetPolynomialQuotientContextAttribute(attributes);
        var polynomialQuotientScope = GetPolynomialQuotientScopeAttribute(attributes);
        var rationalFunctionContext = GetRationalFunctionContextAttribute(attributes);
        var rationalFunctionScope = GetRationalFunctionScopeAttribute(attributes);
        var fieldExtensionContext = GetFieldExtensionContextAttribute(attributes);
        var fieldExtensionScope = GetFieldExtensionScopeAttribute(attributes);
        var padicContext = GetPadicContextAttribute(attributes);
        var padicScope = GetPadicScopeAttribute(attributes);
        var wittVectorContext = GetWittVectorContextAttribute(attributes);
        var wittVectorScope = GetWittVectorScopeAttribute(attributes);
        var finitePowerSetContext = GetFinitePowerSetContextAttribute(attributes);

        if (dimensions is null &&
            precision is null &&
            prime is null &&
            polynomialContext is null &&
            sparsePolynomialContext is null &&
            polynomialScope is null &&
            matrixContext is null &&
            matrixScope is null &&
            reverseDiffContext is null &&
            reverseDiffScope is null &&
            polynomialQuotientContext is null &&
            polynomialQuotientScope is null &&
            rationalFunctionContext is null &&
            rationalFunctionScope is null &&
            fieldExtensionContext is null &&
            fieldExtensionScope is null &&
            padicContext is null &&
            padicScope is null &&
            wittVectorContext is null &&
            wittVectorScope is null &&
            finitePowerSetContext is null)
            return null;

        var diagnostics = new List<Diagnostic>();
        var canGenerate = true;

        if (syntax is not StructDeclarationSyntax)
        {
            diagnostics.Add(Diagnostic.Create(TypeMustBeStruct, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        if (!syntax.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            diagnostics.Add(Diagnostic.Create(TypeMustBePartial, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        if (dimensions is <= 0)
        {
            diagnostics.Add(Diagnostic.Create(WitnessValueMustBePositive, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        if (precision is <= 0)
        {
            diagnostics.Add(Diagnostic.Create(WitnessValueMustBePositive, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        if (prime is <= 1)
        {
            diagnostics.Add(Diagnostic.Create(WitnessValueMustBePositive, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        if (polynomialContext is not null &&
            (polynomialContext.Terms <= 0 ||
             polynomialContext.Workspace <= 0 ||
             polynomialContext.Handles <= 0))
        {
            diagnostics.Add(Diagnostic.Create(ScopeCapacityMustBePositive, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        if (sparsePolynomialContext is not null && sparsePolynomialContext.Terms <= 0)
        {
            diagnostics.Add(Diagnostic.Create(ScopeCapacityMustBePositive, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        if (polynomialScope is not null &&
            (polynomialScope.Terms <= 0 ||
             polynomialScope.Workspace <= 0 ||
             polynomialScope.Handles <= 0))
        {
            diagnostics.Add(Diagnostic.Create(ScopeCapacityMustBePositive, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        if (matrixContext is not null &&
            (matrixContext.Rows <= 0 ||
             matrixContext.Columns <= 0 ||
             matrixContext.Handles <= 0))
        {
            diagnostics.Add(Diagnostic.Create(ScopeCapacityMustBePositive, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        if (matrixScope is not null &&
            (matrixScope.Rows <= 0 ||
             matrixScope.Columns <= 0 ||
             matrixScope.Handles <= 0))
        {
            diagnostics.Add(Diagnostic.Create(ScopeCapacityMustBePositive, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        if (reverseDiffContext is not null && reverseDiffContext.Nodes <= 0)
        {
            diagnostics.Add(Diagnostic.Create(ScopeCapacityMustBePositive, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        if (reverseDiffScope is not null && reverseDiffScope.Nodes <= 0)
        {
            diagnostics.Add(Diagnostic.Create(ScopeCapacityMustBePositive, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        if (polynomialQuotientContext is not null &&
            (polynomialQuotientContext.Terms <= 0 ||
             polynomialQuotientContext.Handles <= 0 ||
             polynomialQuotientContext.Workspace <= 0))
        {
            diagnostics.Add(Diagnostic.Create(ScopeCapacityMustBePositive, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        if (polynomialQuotientScope is not null &&
            (polynomialQuotientScope.Terms <= 0 ||
             polynomialQuotientScope.Handles <= 0 ||
             polynomialQuotientScope.Workspace <= 0))
        {
            diagnostics.Add(Diagnostic.Create(ScopeCapacityMustBePositive, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        if (rationalFunctionContext is not null &&
            (rationalFunctionContext.Terms <= 0 ||
             rationalFunctionContext.Handles <= 0 ||
             rationalFunctionContext.Workspace <= 0))
        {
            diagnostics.Add(Diagnostic.Create(ScopeCapacityMustBePositive, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        if (rationalFunctionScope is not null &&
            (rationalFunctionScope.Terms <= 0 ||
             rationalFunctionScope.Handles <= 0 ||
             rationalFunctionScope.Workspace <= 0))
        {
            diagnostics.Add(Diagnostic.Create(ScopeCapacityMustBePositive, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        if (fieldExtensionContext is not null &&
            (fieldExtensionContext.Terms <= 0 ||
             fieldExtensionContext.Handles <= 0 ||
             fieldExtensionContext.Workspace <= 0))
        {
            diagnostics.Add(Diagnostic.Create(ScopeCapacityMustBePositive, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        if (fieldExtensionScope is not null &&
            (fieldExtensionScope.Terms <= 0 ||
             fieldExtensionScope.Handles <= 0 ||
             fieldExtensionScope.Workspace <= 0))
        {
            diagnostics.Add(Diagnostic.Create(ScopeCapacityMustBePositive, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        if (padicContext is not null && padicContext.Handles <= 0)
        {
            diagnostics.Add(Diagnostic.Create(ScopeCapacityMustBePositive, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        if (padicScope is not null && padicScope.Handles <= 0)
        {
            diagnostics.Add(Diagnostic.Create(ScopeCapacityMustBePositive, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        if (wittVectorContext is not null && wittVectorContext.Length <= 0)
        {
            diagnostics.Add(Diagnostic.Create(ScopeCapacityMustBePositive, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        if (wittVectorScope is not null && wittVectorScope.Handles <= 0)
        {
            diagnostics.Add(Diagnostic.Create(ScopeCapacityMustBePositive, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        if (finitePowerSetContext is not null && finitePowerSetContext.Cardinality <= 0)
        {
            diagnostics.Add(Diagnostic.Create(WitnessValueMustBePositive, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        return new GeneratedType(
            symbol,
            GetNamespace(symbol),
            GetContainingTypes(symbol),
            GetDeclarationModifiers(syntax),
            dimensions,
            precision,
            prime,
            polynomialContext,
            sparsePolynomialContext,
            polynomialScope,
            matrixContext,
            matrixScope,
            reverseDiffContext,
            reverseDiffScope,
            polynomialQuotientContext,
            polynomialQuotientScope,
            rationalFunctionContext,
            rationalFunctionScope,
            fieldExtensionContext,
            fieldExtensionScope,
            padicContext,
            padicScope,
            wittVectorContext,
            wittVectorScope,
            finitePowerSetContext,
            diagnostics.ToImmutableArray(),
            canGenerate);
    }

    private static string Generate(GeneratedType model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        if (model.Namespace.Length > 0)
        {
            sb.Append("namespace ").Append(model.Namespace).AppendLine(";");
            sb.AppendLine();
        }

        foreach (var containingType in model.ContainingTypes)
        {
            sb.Append(containingType.Modifiers).Append(' ')
                .Append(containingType.Kind).Append(' ')
                .Append(containingType.DeclarationName)
                .AppendLine();
            sb.AppendLine("{");
        }

        sb.Append(model.Modifiers).Append(" partial struct ").Append(model.DeclarationName);

        var interfaces = new List<string>();
        if (model.Dimension is not null)
            interfaces.Add("global::HPD.Math.Core.IStaticDimension");
        if (model.Precision is not null)
            interfaces.Add("global::HPD.Math.Core.IStaticPrecision");
        if (model.PrimeModulus is not null)
            interfaces.Add("global::HPD.Math.Core.IPrimeModulus");

        if (interfaces.Count > 0)
            sb.Append(" : ").Append(string.Join(", ", interfaces));

        sb.AppendLine();
        sb.AppendLine("{");

        if (model.Dimension is not null)
            sb.Append("    public static int Value => ").Append(model.Dimension.Value).AppendLine(";");
        if (model.Precision is not null)
            sb.Append("    public static int Value => ").Append(model.Precision.Value).AppendLine(";");
        if (model.PrimeModulus is not null)
            sb.Append("    public static int Value => ").Append(model.PrimeModulus.Value).AppendLine(";");

        if (model.PolynomialContext is not null)
            GeneratePolynomialContext(sb, model.Symbol.Name, model.PolynomialContext);
        if (model.SparsePolynomialContext is not null)
            GenerateSparsePolynomialContext(sb, model.Symbol.Name, model.SparsePolynomialContext);
        if (model.PolynomialScope is not null)
            GeneratePolynomialScope(sb, model.PolynomialScope);
        if (model.MatrixContext is not null)
            GenerateMatrixContext(sb, model.Symbol.Name, model.MatrixContext);
        if (model.MatrixScope is not null)
            GenerateMatrixScope(sb, model.MatrixScope);
        if (model.ReverseDiffContext is not null)
            GenerateReverseDiffContext(sb, model.ReverseDiffContext);
        if (model.ReverseDiffScope is not null)
            GenerateReverseDiffScope(sb, model.ReverseDiffScope);
        if (model.PolynomialQuotientContext is not null)
            GeneratePolynomialQuotientContext(sb, model.PolynomialQuotientContext);
        if (model.PolynomialQuotientScope is not null)
            GeneratePolynomialQuotientScope(sb, model.PolynomialQuotientScope);
        if (model.RationalFunctionContext is not null)
            GenerateRationalFunctionContext(sb, model.RationalFunctionContext);
        if (model.RationalFunctionScope is not null)
            GenerateRationalFunctionScope(sb, model.RationalFunctionScope);
        if (model.FieldExtensionContext is not null)
            GeneratePolynomialQuotientContext(sb, model.FieldExtensionContext);
        if (model.FieldExtensionScope is not null)
            GeneratePolynomialQuotientScope(sb, model.FieldExtensionScope);
        if (model.PadicContext is not null)
            GeneratePadicContext(sb, model.PadicContext);
        if (model.PadicScope is not null)
            GeneratePadicScope(sb, model.PadicScope);
        if (model.WittVectorContext is not null)
            GenerateWittVectorContext(sb, model.Symbol.Name, model.WittVectorContext);
        if (model.WittVectorScope is not null)
            GenerateWittVectorScope(sb, model.WittVectorScope);
        if (model.FinitePowerSetContext is not null)
            GenerateFinitePowerSetContext(sb, model.FinitePowerSetContext);

        sb.AppendLine("}");

        for (var i = 0; i < model.ContainingTypes.Length; i++)
            sb.AppendLine("}");

        if (HasGeneratedExtensionMembers(model))
            GenerateExtensionMembers(sb, model);

        return sb.ToString();
    }

    private static void GeneratePolynomialScope(StringBuilder sb, PolynomialScopeModel scope)
    {
        GeneratePolynomialAuthoringSurface(sb, scope, emitRunner: true);
    }

    private static void GeneratePolynomialContext(StringBuilder sb, string contextName, PolynomialScopeModel context)
    {
        GeneratePolynomialValueContext(sb, contextName, context);
    }

    private static void GeneratePolynomialValueContext(StringBuilder sb, string contextName, PolynomialScopeModel context)
    {
        var coefficientType = context.CoefficientType;
        var coefficientOpsType = context.CoefficientOpsType;

        sb.AppendLine();
        sb.Append("    public const int CoefficientCapacity = ").Append(context.Terms).AppendLine(";");
        sb.Append("    public const int TermCapacity = ").Append(context.Terms).AppendLine(";");
        sb.AppendLine();
        sb.AppendLine("    public static Poly Zero => default;");
        sb.AppendLine();
        sb.AppendLine("    public static global::HPD.Math.Core.AlgebraStatus TryConst(int value, out Poly result)");
        sb.AppendLine("    {");
        sb.AppendLine("        result = default;");
        sb.AppendLine("        var ops = new " + coefficientOpsType + "();");
        sb.AppendLine("        var status = ops.TryFromInt(value, out var coefficient);");
        sb.AppendLine("        return status == global::HPD.Math.Core.AlgebraStatus.Ok");
        sb.AppendLine("            ? TryConst(coefficient, out result)");
        sb.AppendLine("            : status;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public static global::HPD.Math.Core.AlgebraStatus TryConst(" + coefficientType + " coefficient, out Poly result) =>");
        sb.AppendLine("        TryMonomial(0, coefficient, out result);");
        sb.AppendLine();
        sb.AppendLine("    public static global::HPD.Math.Core.AlgebraStatus TryVariable(out Poly result)");
        sb.AppendLine("    {");
        sb.AppendLine("        result = default;");
        sb.AppendLine("        return TryMonomial(1, new " + coefficientOpsType + "().One, out result);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public static global::HPD.Math.Core.AlgebraStatus TryMonomial(int degree, " + coefficientType + " coefficient, out Poly result)");
        sb.AppendLine("    {");
        sb.AppendLine("        result = default;");
        sb.AppendLine("        if (degree < 0)");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine("        if (degree >= CoefficientCapacity)");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.InsufficientDestination;");
        sb.AppendLine("        var ops = new " + coefficientOpsType + "();");
        sb.AppendLine("        if (ops.Eq(coefficient, ops.Zero))");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        result.SetCount(degree + 1);");
        sb.AppendLine("        for (var i = 0; i < degree; i++)");
        sb.AppendLine("            result.SetCoefficient(i, ops.Zero);");
        sb.AppendLine("        result.SetCoefficient(degree, coefficient);");
        sb.AppendLine("        return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public static global::HPD.Math.Core.AlgebraStatus TryFromCoefficients(global::System.ReadOnlySpan<" + coefficientType + "> coefficients, out Poly result)");
        sb.AppendLine("    {");
        sb.AppendLine("        result = default;");
        sb.AppendLine("        var ops = new " + coefficientOpsType + "();");
        sb.AppendLine("        var count = coefficients.Length;");
        sb.AppendLine("        while (count > 0 && ops.Eq(coefficients[count - 1], ops.Zero))");
        sb.AppendLine("            count--;");
        sb.AppendLine("        if (count > CoefficientCapacity)");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.InsufficientDestination;");
        sb.AppendLine("        for (var i = 0; i < count; i++)");
        sb.AppendLine("            result.SetCoefficient(i, coefficients[i]);");
        sb.AppendLine("        result.SetCount(count);");
        sb.AppendLine("        return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public struct Poly : global::System.IEquatable<Poly>");
        sb.AppendLine("    {");
        sb.AppendLine("        private CoefficientBuffer _coefficients;");
        sb.AppendLine("        private int _count;");
        sb.AppendLine();
        sb.AppendLine("        public int CoefficientCount => _count;");
        sb.AppendLine("        public int TermCount => _count;");
        sb.AppendLine("        public bool IsZero => _count == 0;");
        sb.AppendLine("        public int Degree => _count == 0 ? -1 : _count - 1;");
        sb.Append("        public ").Append(coefficientType).AppendLine(" CoefficientAt(int degree) => degree < 0 || degree >= _count ? default! : GetCoefficient(degree);");
        sb.AppendLine();
        sb.AppendLine("        internal global::System.ReadOnlySpan<" + coefficientType + "> ReadOnlyCoefficients => _coefficients.AsSpan();");
        sb.AppendLine("        internal " + coefficientType + " GetCoefficient(int degree) => ReadOnlyCoefficients[degree];");
        sb.AppendLine("        internal void SetCoefficient(int degree, " + coefficientType + " value) => _coefficients.AsSpan()[degree] = value;");
        sb.AppendLine("        internal void SetCount(int count) => _count = count;");
        sb.AppendLine("        internal void Normalize(" + coefficientOpsType + " ops)");
        sb.AppendLine("        {");
        sb.AppendLine("            while (_count > 0 && ops.Eq(GetCoefficient(_count - 1), ops.Zero))");
        sb.AppendLine("                _count--;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public bool Equals(Poly other)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (_count != other._count)");
        sb.AppendLine("                return false;");
        sb.AppendLine("            var ops = new " + coefficientOpsType + "();");
        sb.AppendLine("            for (var i = 0; i < _count; i++)");
        sb.AppendLine("                if (!ops.Eq(GetCoefficient(i), other.GetCoefficient(i)))");
        sb.AppendLine("                    return false;");
        sb.AppendLine("            return true;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public override bool Equals(object? obj) => obj is Poly other && Equals(other);");
        sb.AppendLine();
        sb.AppendLine("        public override int GetHashCode()");
        sb.AppendLine("        {");
        sb.AppendLine("            var hash = new global::System.HashCode();");
        sb.AppendLine("            hash.Add(_count);");
        sb.AppendLine("            for (var i = 0; i < _count; i++)");
        sb.AppendLine("                hash.Add(GetCoefficient(i));");
        sb.AppendLine("            return hash.ToHashCode();");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public static bool operator ==(Poly left, Poly right) => left.Equals(right);");
        sb.AppendLine("        public static bool operator !=(Poly left, Poly right) => !left.Equals(right);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public readonly struct Ops");
        sb.AppendLine("    {");
        sb.Append("        public Poly Zero => ").Append(contextName).AppendLine(".Zero;");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryAdd(in Poly left, in Poly right, out Poly result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            var ops = new " + coefficientOpsType + "();");
        sb.AppendLine("            var count = left.CoefficientCount > right.CoefficientCount ? left.CoefficientCount : right.CoefficientCount;");
        sb.AppendLine("            if (count > CoefficientCapacity)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InsufficientDestination;");
        sb.AppendLine("            for (var i = 0; i < count; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                var coefficient = ops.Zero;");
        sb.AppendLine("                var leftCoefficient = i < left.CoefficientCount ? left.GetCoefficient(i) : ops.Zero;");
        sb.AppendLine("                var rightCoefficient = i < right.CoefficientCount ? right.GetCoefficient(i) : ops.Zero;");
        sb.AppendLine("                var status = ops.TryAdd(ref coefficient, leftCoefficient, rightCoefficient);");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                    return status;");
        sb.AppendLine("                result.SetCoefficient(i, coefficient);");
        sb.AppendLine("            }");
        sb.AppendLine("            result.SetCount(count);");
        sb.AppendLine("            result.Normalize(ops);");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryNeg(in Poly value, out Poly result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            var ops = new " + coefficientOpsType + "();");
        sb.AppendLine("            for (var i = 0; i < value.CoefficientCount; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                var coefficient = ops.Zero;");
        sb.AppendLine("                var status = ops.TryNeg(ref coefficient, value.GetCoefficient(i));");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                    return status;");
        sb.AppendLine("                result.SetCoefficient(i, coefficient);");
        sb.AppendLine("            }");
        sb.AppendLine("            result.SetCount(value.CoefficientCount);");
        sb.AppendLine("            result.Normalize(ops);");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TrySub(in Poly left, in Poly right, out Poly result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            var ops = new " + coefficientOpsType + "();");
        sb.AppendLine("            var count = left.CoefficientCount > right.CoefficientCount ? left.CoefficientCount : right.CoefficientCount;");
        sb.AppendLine("            if (count > CoefficientCapacity)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InsufficientDestination;");
        sb.AppendLine("            for (var i = 0; i < count; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                var coefficient = ops.Zero;");
        sb.AppendLine("                var leftCoefficient = i < left.CoefficientCount ? left.GetCoefficient(i) : ops.Zero;");
        sb.AppendLine("                var rightCoefficient = i < right.CoefficientCount ? right.GetCoefficient(i) : ops.Zero;");
        sb.AppendLine("                var status = ops.TrySub(ref coefficient, leftCoefficient, rightCoefficient);");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                    return status;");
        sb.AppendLine("                result.SetCoefficient(i, coefficient);");
        sb.AppendLine("            }");
        sb.AppendLine("            result.SetCount(count);");
        sb.AppendLine("            result.Normalize(ops);");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryMul(in Poly left, in Poly right, out Poly result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            if (left.IsZero || right.IsZero)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("            var count = left.Degree + right.Degree + 1;");
        sb.AppendLine("            if (count > CoefficientCapacity)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InsufficientDestination;");
        sb.AppendLine("            var ops = new " + coefficientOpsType + "();");
        sb.AppendLine("            for (var i = 0; i < count; i++)");
        sb.AppendLine("                result.SetCoefficient(i, ops.Zero);");
        sb.AppendLine("            for (var i = 0; i < left.CoefficientCount; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                for (var j = 0; j < right.CoefficientCount; j++)");
        sb.AppendLine("                {");
        sb.AppendLine("                    var product = ops.Zero;");
        sb.AppendLine("                    var status = ops.TryMul(ref product, left.GetCoefficient(i), right.GetCoefficient(j));");
        sb.AppendLine("                    if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                        return status;");
        sb.AppendLine("                    var sum = result.GetCoefficient(i + j);");
        sb.AppendLine("                    status = ops.TryAdd(ref sum, sum, product);");
        sb.AppendLine("                    if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                        return status;");
        sb.AppendLine("                    result.SetCoefficient(i + j, sum);");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine("            result.SetCount(count);");
        sb.AppendLine("            result.Normalize(ops);");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryDerivative(in Poly value, out Poly result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            if (value.CoefficientCount <= 1)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("            var ops = new " + coefficientOpsType + "();");
        sb.AppendLine("            for (var degree = 1; degree < value.CoefficientCount; degree++)");
        sb.AppendLine("            {");
        sb.AppendLine("                var status = ops.TryFromInt(degree, out var scalar);");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                    return status;");
        sb.AppendLine("                var coefficient = ops.Zero;");
        sb.AppendLine("                status = ops.TryMul(ref coefficient, value.GetCoefficient(degree), scalar);");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                    return status;");
        sb.AppendLine("                result.SetCoefficient(degree - 1, coefficient);");
        sb.AppendLine("            }");
        sb.AppendLine("            result.SetCount(value.CoefficientCount - 1);");
        sb.AppendLine("            result.Normalize(ops);");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    [global::System.Runtime.CompilerServices.InlineArray(CoefficientCapacity)]");
        sb.AppendLine("    private struct CoefficientBuffer");
        sb.AppendLine("    {");
        sb.Append("        private ").Append(coefficientType).AppendLine(" _element0;");
        sb.Append("        public global::System.Span<").Append(coefficientType).AppendLine("> AsSpan() => global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref _element0, CoefficientCapacity);");
        sb.AppendLine("    }");
    }

    private static void GenerateSparsePolynomialContext(StringBuilder sb, string contextName, PolynomialScopeModel context)
    {
        var coefficientType = context.CoefficientType;
        var coefficientOpsType = context.CoefficientOpsType;

        sb.AppendLine();
        sb.Append("    public const int TermCapacity = ").Append(context.Terms).AppendLine(";");
        sb.AppendLine();
        sb.AppendLine("    public static Poly Zero => default;");
        sb.AppendLine();
        sb.AppendLine("    public static global::HPD.Math.Core.AlgebraStatus TryConst(int value, out Poly result)");
        sb.AppendLine("    {");
        sb.AppendLine("        result = default;");
        sb.AppendLine("        var ops = new " + coefficientOpsType + "();");
        sb.AppendLine("        var status = ops.TryFromInt(value, out var coefficient);");
        sb.AppendLine("        return status == global::HPD.Math.Core.AlgebraStatus.Ok ? TryMonomial(0, coefficient, out result) : status;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public static global::HPD.Math.Core.AlgebraStatus TryVariable(out Poly result) =>");
        sb.AppendLine("        TryMonomial(1, new " + coefficientOpsType + "().One, out result);");
        sb.AppendLine();
        sb.AppendLine("    public static global::HPD.Math.Core.AlgebraStatus TryMonomial(int degree, " + coefficientType + " coefficient, out Poly result)");
        sb.AppendLine("    {");
        sb.AppendLine("        result = default;");
        sb.AppendLine("        if (degree < 0)");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine("        var ops = new " + coefficientOpsType + "();");
        sb.AppendLine("        if (ops.Eq(coefficient, ops.Zero))");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        result.SetTerm(0, degree, coefficient);");
        sb.AppendLine("        result.SetCount(1);");
        sb.AppendLine("        return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public static global::HPD.Math.Core.AlgebraStatus TryFromTerms(global::System.ReadOnlySpan<int> degrees, global::System.ReadOnlySpan<" + coefficientType + "> coefficients, out Poly result)");
        sb.AppendLine("    {");
        sb.AppendLine("        result = default;");
        sb.AppendLine("        if (degrees.Length != coefficients.Length)");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.DimensionMismatch;");
        sb.AppendLine("        var ops = new " + coefficientOpsType + "();");
        sb.AppendLine("        for (var i = 0; i < degrees.Length; i++)");
        sb.AppendLine("        {");
        sb.AppendLine("            var status = result.TryAppendTerm(degrees[i], coefficients[i], ops);");
        sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("            {");
        sb.AppendLine("                result = default;");
        sb.AppendLine("                return status;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("        return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public struct Poly : global::System.IEquatable<Poly>");
        sb.AppendLine("    {");
        sb.AppendLine("        private DegreeBuffer _degrees;");
        sb.AppendLine("        private CoefficientBuffer _coefficients;");
        sb.AppendLine("        private int _count;");
        sb.AppendLine();
        sb.AppendLine("        public int TermCount => _count;");
        sb.AppendLine("        public bool IsZero => _count == 0;");
        sb.AppendLine("        public int Degree => _count == 0 ? -1 : DegreeAt(_count - 1);");
        sb.AppendLine("        public int DegreeAt(int index) => _degrees.AsSpan()[index];");
        sb.Append("        public ").Append(coefficientType).AppendLine(" CoefficientAt(int index) => _coefficients.AsSpan()[index];");
        sb.AppendLine("        internal void SetCount(int count) => _count = count;");
        sb.AppendLine("        internal void SetTerm(int index, int degree, " + coefficientType + " coefficient) { _degrees.AsSpan()[index] = degree; _coefficients.AsSpan()[index] = coefficient; }");
        sb.AppendLine("        internal global::HPD.Math.Core.AlgebraStatus TryAppendTerm(int degree, " + coefficientType + " coefficient, " + coefficientOpsType + " ops)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (degree < 0)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine("            if (ops.Eq(coefficient, ops.Zero))");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("            if (_count > 0 && degree <= DegreeAt(_count - 1))");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine("            if (_count >= TermCapacity)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InsufficientDestination;");
        sb.AppendLine("            SetTerm(_count, degree, coefficient);");
        sb.AppendLine("            _count++;");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine("        internal global::HPD.Math.Core.AlgebraStatus TryAccumulateTerm(int degree, " + coefficientType + " coefficient, " + coefficientOpsType + " ops)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (degree < 0)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine("            if (ops.Eq(coefficient, ops.Zero))");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("            var index = 0;");
        sb.AppendLine("            while (index < _count && DegreeAt(index) < degree)");
        sb.AppendLine("                index++;");
        sb.AppendLine("            if (index < _count && DegreeAt(index) == degree)");
        sb.AppendLine("            {");
        sb.AppendLine("                var sum = CoefficientAt(index);");
        sb.AppendLine("                var status = ops.TryAdd(ref sum, sum, coefficient);");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                    return status;");
        sb.AppendLine("                if (ops.Eq(sum, ops.Zero))");
        sb.AppendLine("                {");
        sb.AppendLine("                    for (var move = index + 1; move < _count; move++)");
        sb.AppendLine("                        SetTerm(move - 1, DegreeAt(move), CoefficientAt(move));");
        sb.AppendLine("                    _count--;");
        sb.AppendLine("                }");
        sb.AppendLine("                else");
        sb.AppendLine("                {");
        sb.AppendLine("                    SetTerm(index, degree, sum);");
        sb.AppendLine("                }");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("            }");
        sb.AppendLine("            if (_count >= TermCapacity)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InsufficientDestination;");
        sb.AppendLine("            for (var move = _count; move > index; move--)");
        sb.AppendLine("                SetTerm(move, DegreeAt(move - 1), CoefficientAt(move - 1));");
        sb.AppendLine("            SetTerm(index, degree, coefficient);");
        sb.AppendLine("            _count++;");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine("        public bool Equals(Poly other)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (_count != other._count) return false;");
        sb.AppendLine("            var ops = new " + coefficientOpsType + "();");
        sb.AppendLine("            for (var i = 0; i < _count; i++)");
        sb.AppendLine("                if (DegreeAt(i) != other.DegreeAt(i) || !ops.Eq(CoefficientAt(i), other.CoefficientAt(i))) return false;");
        sb.AppendLine("            return true;");
        sb.AppendLine("        }");
        sb.AppendLine("        public override bool Equals(object? obj) => obj is Poly other && Equals(other);");
        sb.AppendLine("        public override int GetHashCode()");
        sb.AppendLine("        {");
        sb.AppendLine("            var hash = new global::System.HashCode();");
        sb.AppendLine("            hash.Add(_count);");
        sb.AppendLine("            for (var i = 0; i < _count; i++) { hash.Add(DegreeAt(i)); hash.Add(CoefficientAt(i)); }");
        sb.AppendLine("            return hash.ToHashCode();");
        sb.AppendLine("        }");
        sb.AppendLine("        public static bool operator ==(Poly left, Poly right) => left.Equals(right);");
        sb.AppendLine("        public static bool operator !=(Poly left, Poly right) => !left.Equals(right);");
        sb.AppendLine("    }");
        sb.AppendLine("    public readonly struct Ops");
        sb.AppendLine("    {");
        sb.Append("        public Poly Zero => ").Append(contextName).AppendLine(".Zero;");
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryAdd(in Poly left, in Poly right, out Poly result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            var ops = new " + coefficientOpsType + "();");
        sb.AppendLine("            var i = 0; var j = 0;");
        sb.AppendLine("            while (i < left.TermCount || j < right.TermCount)");
        sb.AppendLine("            {");
        sb.AppendLine("                global::HPD.Math.Core.AlgebraStatus status;");
        sb.AppendLine("                if (j >= right.TermCount || (i < left.TermCount && left.DegreeAt(i) < right.DegreeAt(j)))");
        sb.AppendLine("                    status = result.TryAppendTerm(left.DegreeAt(i), left.CoefficientAt(i++), ops);");
        sb.AppendLine("                else if (i >= left.TermCount || right.DegreeAt(j) < left.DegreeAt(i))");
        sb.AppendLine("                    status = result.TryAppendTerm(right.DegreeAt(j), right.CoefficientAt(j++), ops);");
        sb.AppendLine("                else");
        sb.AppendLine("                {");
        sb.AppendLine("                    var sum = ops.Zero;");
        sb.AppendLine("                    status = ops.TryAdd(ref sum, left.CoefficientAt(i), right.CoefficientAt(j));");
        sb.AppendLine("                    if (status == global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                        status = result.TryAppendTerm(left.DegreeAt(i), sum, ops);");
        sb.AppendLine("                    i++; j++;");
        sb.AppendLine("                }");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok) return status;");
        sb.AppendLine("            }");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryMul(in Poly left, in Poly right, out Poly result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            var ops = new " + coefficientOpsType + "();");
        sb.AppendLine("            for (var i = 0; i < left.TermCount; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                for (var j = 0; j < right.TermCount; j++)");
        sb.AppendLine("                {");
        sb.AppendLine("                    var product = ops.Zero;");
        sb.AppendLine("                    var status = ops.TryMul(ref product, left.CoefficientAt(i), right.CoefficientAt(j));");
        sb.AppendLine("                    if (status != global::HPD.Math.Core.AlgebraStatus.Ok) return status;");
        sb.AppendLine("                    status = result.TryAccumulateTerm(checked(left.DegreeAt(i) + right.DegreeAt(j)), product, ops);");
        sb.AppendLine("                    if (status != global::HPD.Math.Core.AlgebraStatus.Ok) return status;");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryDerivative(in Poly value, out Poly result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            var ops = new " + coefficientOpsType + "();");
        sb.AppendLine("            for (var i = 0; i < value.TermCount; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                var degree = value.DegreeAt(i);");
        sb.AppendLine("                if (degree == 0) continue;");
        sb.AppendLine("                var status = ops.TryFromInt(degree, out var scalar);");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok) return status;");
        sb.AppendLine("                var coefficient = ops.Zero;");
        sb.AppendLine("                status = ops.TryMul(ref coefficient, value.CoefficientAt(i), scalar);");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok) return status;");
        sb.AppendLine("                status = result.TryAppendTerm(degree - 1, coefficient, ops);");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok) return status;");
        sb.AppendLine("            }");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("    [global::System.Runtime.CompilerServices.InlineArray(TermCapacity)]");
        sb.AppendLine("    private struct DegreeBuffer { private int _element0; public global::System.Span<int> AsSpan() => global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref _element0, TermCapacity); }");
        sb.AppendLine("    [global::System.Runtime.CompilerServices.InlineArray(TermCapacity)]");
        sb.AppendLine("    private struct CoefficientBuffer { private " + coefficientType + " _element0; public global::System.Span<" + coefficientType + "> AsSpan() => global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref _element0, TermCapacity); }");
    }

    private static void GeneratePolynomialAuthoringSurface(
        StringBuilder sb,
        PolynomialScopeModel scope,
        bool emitRunner)
    {
        var coefficientType = scope.CoefficientType;
        var coefficientOpsType = scope.CoefficientOpsType;
        var builderType = "global::HPD.Math.Algebra.SparsePolynomialBuilder<" + coefficientType + ">";
        var viewType = "global::HPD.Math.Algebra.SparsePolynomialView<" + coefficientType + ">";
        var finsuppViewType = "global::HPD.Math.Finite.FinsuppView<int, " + coefficientType + ">";

        sb.AppendLine();
        sb.Append("    public const int TermCapacity = ").Append(scope.Terms).AppendLine(";");
        sb.Append("    public const int WorkspaceCapacity = ").Append(scope.Workspace).AppendLine(";");
        sb.Append("    public const int HandleCapacity = ").Append(scope.Handles).AppendLine(";");
        sb.AppendLine();
        if (emitRunner)
        {
            sb.AppendLine("    public global::HPD.Math.Core.AlgebraStatus Run(ref Result result)");
            sb.AppendLine("    {");
            sb.AppendLine("        result.Clear();");
            sb.AppendLine("        global::System.Span<int> degrees = stackalloc int[TermCapacity * HandleCapacity];");
            sb.Append("        global::System.Span<").Append(coefficientType).AppendLine("> coefficients = stackalloc " + coefficientType + "[TermCapacity * HandleCapacity];");
            sb.AppendLine("        global::System.Span<int> counts = stackalloc int[HandleCapacity];");
            sb.AppendLine("        global::System.Span<int> state = stackalloc int[2];");
            sb.AppendLine("        global::System.Span<int> workspaceDegrees = stackalloc int[WorkspaceCapacity];");
            sb.Append("        global::System.Span<").Append(coefficientType).AppendLine("> workspaceCoefficients = stackalloc " + coefficientType + "[WorkspaceCapacity];");
            sb.AppendLine();
            sb.AppendLine("        var scope = new Scope(degrees, coefficients, counts, state, workspaceDegrees, workspaceCoefficients);");
            sb.AppendLine("        Build(ref scope);");
            sb.AppendLine("        var status = scope.CopyReturned(result.DegreeStorage, result.CoefficientStorage, out var termCount);");
            sb.AppendLine("        if (scope.Status != global::HPD.Math.Core.AlgebraStatus.Ok)");
            sb.AppendLine("            return scope.Status;");
            sb.AppendLine("        if (status == global::HPD.Math.Core.AlgebraStatus.Ok)");
            sb.AppendLine("            result.SetTermCount(termCount);");
            sb.AppendLine("        return status;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    public global::HPD.Math.Core.AlgebraStatus Run()");
            sb.AppendLine("    {");
            sb.AppendLine("        global::System.Span<int> degrees = stackalloc int[TermCapacity * HandleCapacity];");
            sb.Append("        global::System.Span<").Append(coefficientType).AppendLine("> coefficients = stackalloc " + coefficientType + "[TermCapacity * HandleCapacity];");
            sb.AppendLine("        global::System.Span<int> counts = stackalloc int[HandleCapacity];");
            sb.AppendLine("        global::System.Span<int> state = stackalloc int[2];");
            sb.AppendLine("        global::System.Span<int> workspaceDegrees = stackalloc int[WorkspaceCapacity];");
            sb.Append("        global::System.Span<").Append(coefficientType).AppendLine("> workspaceCoefficients = stackalloc " + coefficientType + "[WorkspaceCapacity];");
            sb.AppendLine();
            sb.AppendLine("        var scope = new Scope(degrees, coefficients, counts, state, workspaceDegrees, workspaceCoefficients);");
            sb.AppendLine("        Build(ref scope);");
            sb.AppendLine("        return scope.Status;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    partial void Build(ref Scope q);");
            sb.AppendLine();
            sb.AppendLine("    public struct Result");
            sb.AppendLine("    {");
            sb.AppendLine("        private DegreeBuffer _degrees;");
            sb.Append("        private CoefficientBuffer _coefficients;");
            sb.AppendLine();
            sb.AppendLine("        public int TermCount { get; private set; }");
            sb.AppendLine("        public int DegreeAt(int index) => _degrees[index];");
            sb.Append("        public ").Append(coefficientType).AppendLine(" CoefficientAt(int index) => _coefficients[index];");
            sb.AppendLine("        internal global::System.Span<int> DegreeStorage => _degrees.AsSpan();");
            sb.Append("        internal global::System.Span<").Append(coefficientType).AppendLine("> CoefficientStorage => _coefficients.AsSpan();");
            sb.AppendLine("        internal void SetTermCount(int count) => TermCount = count;");
            sb.AppendLine("        internal void Clear() => TermCount = 0;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    [global::System.Runtime.CompilerServices.InlineArray(TermCapacity)]");
            sb.AppendLine("    private struct DegreeBuffer");
            sb.AppendLine("    {");
            sb.AppendLine("        private int _element0;");
            sb.AppendLine("        public global::System.Span<int> AsSpan() => global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref _element0, TermCapacity);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    [global::System.Runtime.CompilerServices.InlineArray(TermCapacity)]");
            sb.AppendLine("    private struct CoefficientBuffer");
            sb.AppendLine("    {");
            sb.Append("        private ").Append(coefficientType).AppendLine(" _element0;");
            sb.Append("        public global::System.Span<").Append(coefficientType).AppendLine("> AsSpan() => global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref _element0, TermCapacity);");
            sb.AppendLine("    }");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("    public static " + coefficientOpsType + " CreateOps() => new();");
            sb.AppendLine();
            sb.AppendLine("    public static Scope CreateScope(");
            sb.AppendLine("        global::System.Span<int> degrees,");
            sb.Append("        global::System.Span<").Append(coefficientType).AppendLine("> coefficients,");
            sb.AppendLine("        global::System.Span<int> counts,");
            sb.AppendLine("        global::System.Span<int> state,");
            sb.AppendLine("        global::System.Span<int> workspaceDegrees,");
            sb.Append("        global::System.Span<").Append(coefficientType).AppendLine("> workspaceCoefficients) =>");
            sb.AppendLine("        new(degrees, coefficients, counts, state, workspaceDegrees, workspaceCoefficients);");
            sb.AppendLine();
        }
        sb.AppendLine("    public ref struct Scope");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly global::System.Span<int> _degrees;");
        sb.Append("        private readonly global::System.Span<").Append(coefficientType).AppendLine("> _coefficients;");
        sb.AppendLine("        private readonly global::System.Span<int> _counts;");
        sb.AppendLine("        private readonly global::System.Span<int> _state;");
        sb.AppendLine("        private readonly global::System.Span<int> _workspaceDegrees;");
        sb.Append("        private readonly global::System.Span<").Append(coefficientType).AppendLine("> _workspaceCoefficients;");
        sb.AppendLine("        private int _returnedHandle;");
        sb.AppendLine();
        sb.AppendLine("        public Scope(");
        sb.AppendLine("            global::System.Span<int> degrees,");
        sb.Append("            global::System.Span<").Append(coefficientType).AppendLine("> coefficients,");
        sb.AppendLine("            global::System.Span<int> counts,");
        sb.AppendLine("            global::System.Span<int> state,");
        sb.AppendLine("            global::System.Span<int> workspaceDegrees,");
        sb.Append("            global::System.Span<").Append(coefficientType).AppendLine("> workspaceCoefficients)");
        sb.AppendLine("        {");
        sb.AppendLine("            _degrees = degrees;");
        sb.AppendLine("            _coefficients = coefficients;");
        sb.AppendLine("            _counts = counts;");
        sb.AppendLine("            _state = state;");
        sb.AppendLine("            _workspaceDegrees = workspaceDegrees;");
        sb.AppendLine("            _workspaceCoefficients = workspaceCoefficients;");
        sb.AppendLine("            _state[0] = 0;");
        sb.AppendLine("            _state[1] = (int)global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("            _returnedHandle = -1;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public readonly global::HPD.Math.Core.AlgebraStatus Status => (global::HPD.Math.Core.AlgebraStatus)_state[1];");
        sb.AppendLine();
        sb.AppendLine("        public Poly Variable() => Monomial(1, new " + coefficientOpsType + "().One);");
        sb.AppendLine();
        sb.AppendLine("        public Poly Const(int value)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (Status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return InvalidPoly();");
        sb.AppendLine();
        sb.AppendLine("            var ops = new " + coefficientOpsType + "();");
        sb.AppendLine("            var status = ops.TryFromInt(value, out var coefficient);");
        sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("            {");
        sb.AppendLine("                Fail(status);");
        sb.AppendLine("                return InvalidPoly();");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            return Const(coefficient);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.Append("        public Poly Const(").Append(coefficientType).AppendLine(" coefficient) => Monomial(0, coefficient);");
        sb.AppendLine();
        sb.Append("        public Poly Monomial(int degree, ").Append(coefficientType).AppendLine(" coefficient)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!TryAllocate(out var handle))");
        sb.AppendLine("                return InvalidPoly();");
        sb.AppendLine();
        sb.AppendLine("            var builder = Builder(handle);");
        sb.AppendLine("            var status = builder.TryAppendTermStatus(degree, coefficient, new " + coefficientOpsType + "());");
        sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("            {");
        sb.AppendLine("                Fail(status);");
        sb.AppendLine("                return InvalidPoly();");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            _counts[handle] = builder.Count;");
        sb.AppendLine("            return CreatePoly(handle);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public Poly Add(Poly left, Poly right) => left + right;");
        sb.AppendLine("        public Poly Mul(Poly left, Poly right) => left * right;");
        sb.AppendLine("        public Poly Derivative(Poly value) => value.Derivative;");
        sb.AppendLine();
        sb.AppendLine("        public void Return(Poly value)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!IsValid(value.Handle))");
        sb.AppendLine("            {");
        sb.AppendLine("                Fail(global::HPD.Math.Core.AlgebraStatus.InvalidInput);");
        sb.AppendLine("                return;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            _returnedHandle = value.Handle;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.Append("        public global::HPD.Math.Core.AlgebraStatus CopyReturned(global::System.Span<int> outputDegrees, global::System.Span<")
            .Append(coefficientType).AppendLine("> outputCoefficients, out int outputTermCount)");
        sb.AppendLine("        {");
        sb.AppendLine("            outputTermCount = 0;");
        sb.AppendLine("            if (Status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return Status;");
        sb.AppendLine("            if (!IsValid(_returnedHandle))");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine();
        sb.AppendLine("            var count = _counts[_returnedHandle];");
        sb.AppendLine("            if (outputDegrees.Length < count || outputCoefficients.Length < count)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InsufficientDestination;");
        sb.AppendLine();
        sb.AppendLine("            DegreeSlot(_returnedHandle)[..count].CopyTo(outputDegrees);");
        sb.AppendLine("            CoefficientSlot(_returnedHandle)[..count].CopyTo(outputCoefficients);");
        sb.AppendLine("            outputTermCount = count;");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private bool TryAllocate(out int handle)");
        sb.AppendLine("        {");
        sb.AppendLine("            handle = -1;");
        sb.AppendLine("            if (Status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return false;");
        sb.AppendLine("            if (_state[0] >= HandleCapacity)");
        sb.AppendLine("            {");
        sb.AppendLine("                Fail(global::HPD.Math.Core.AlgebraStatus.InsufficientDestination);");
        sb.AppendLine("                return false;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            handle = _state[0];");
        sb.AppendLine("            _state[0]++;");
        sb.AppendLine("            _counts[handle] = 0;");
        sb.AppendLine("            return true;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private bool IsValid(int handle) => handle >= 0 && handle < _state[0];");
        sb.AppendLine("        private void Fail(global::HPD.Math.Core.AlgebraStatus status)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (Status == global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                _state[1] = (int)status;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private Poly InvalidPoly() => CreatePoly(-1);");
        sb.AppendLine("        private Poly CreatePoly(int handle) => new(_degrees, _coefficients, _counts, _state, _workspaceDegrees, _workspaceCoefficients, handle);");
        sb.AppendLine("        private global::System.Span<int> DegreeSlot(int handle) => _degrees.Slice(handle * TermCapacity, TermCapacity);");
        sb.Append("        private global::System.Span<").Append(coefficientType).AppendLine("> CoefficientSlot(int handle) => _coefficients.Slice(handle * TermCapacity, TermCapacity);");
        sb.AppendLine("        private " + builderType + " Builder(int handle) => new(DegreeSlot(handle), CoefficientSlot(handle));");
        sb.AppendLine();
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public readonly ref struct Poly");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly global::System.Span<int> _degrees;");
        sb.Append("        private readonly global::System.Span<").Append(coefficientType).AppendLine("> _coefficients;");
        sb.AppendLine("        private readonly global::System.Span<int> _counts;");
        sb.AppendLine("        private readonly global::System.Span<int> _state;");
        sb.AppendLine("        private readonly global::System.Span<int> _workspaceDegrees;");
        sb.Append("        private readonly global::System.Span<").Append(coefficientType).AppendLine("> _workspaceCoefficients;");
        sb.AppendLine("        internal readonly int Handle;");
        sb.AppendLine();
        sb.AppendLine("        internal Poly(");
        sb.AppendLine("            global::System.Span<int> degrees,");
        sb.Append("            global::System.Span<").Append(coefficientType).AppendLine("> coefficients,");
        sb.AppendLine("            global::System.Span<int> counts,");
        sb.AppendLine("            global::System.Span<int> state,");
        sb.AppendLine("            global::System.Span<int> workspaceDegrees,");
        sb.Append("            global::System.Span<").Append(coefficientType).AppendLine("> workspaceCoefficients,");
        sb.AppendLine("            int handle)");
        sb.AppendLine("        {");
        sb.AppendLine("            _degrees = degrees;");
        sb.AppendLine("            _coefficients = coefficients;");
        sb.AppendLine("            _counts = counts;");
        sb.AppendLine("            _state = state;");
        sb.AppendLine("            _workspaceDegrees = workspaceDegrees;");
        sb.AppendLine("            _workspaceCoefficients = workspaceCoefficients;");
        sb.AppendLine("            Handle = handle;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public int TermCount => IsValid(Handle) ? _counts[Handle] : 0;");
        sb.AppendLine("        public int Degree => TermCount == 0 ? -1 : DegreeSlot(Handle)[TermCount - 1];");
        sb.AppendLine("        public int DegreeAt(int supportIndex) => DegreeSlot(Handle)[supportIndex];");
        sb.AppendLine("        public " + coefficientType + " CoefficientAt(int supportIndex) => CoefficientSlot(Handle)[supportIndex];");
        sb.AppendLine("        public Poly Derivative => Derive();");
        sb.AppendLine();
        sb.AppendLine("        public Poly Add(Poly other)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!CanOperate(other, out var handle))");
        sb.AppendLine("                return InvalidPoly();");
        sb.AppendLine();
        sb.AppendLine("            var destination = Builder(handle);");
        sb.AppendLine("            var status = global::HPD.Math.Algebra.StatusSparsePolynomialKernels.TryAdd(View(Handle), other.View(other.Handle), ref destination, new " + coefficientOpsType + "());");
        sb.AppendLine("            return Complete(handle, destination.Count, status);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public Poly Mul(Poly other)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!CanOperate(other, out var handle))");
        sb.AppendLine("                return InvalidPoly();");
        sb.AppendLine();
        sb.AppendLine("            var destination = Builder(handle);");
        sb.AppendLine("            var status = global::HPD.Math.Algebra.StatusSparsePolynomialKernels.TryMul(View(Handle), other.View(other.Handle), ref destination, _workspaceDegrees, _workspaceCoefficients, new " + coefficientOpsType + "());");
        sb.AppendLine("            return Complete(handle, destination.Count, status);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private Poly Derive()");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!CanOperate(out var handle))");
        sb.AppendLine("                return InvalidPoly();");
        sb.AppendLine();
        sb.AppendLine("            var source = View(Handle);");
        sb.AppendLine("            var destination = Builder(handle);");
        sb.AppendLine("            var ops = new " + coefficientOpsType + "();");
        sb.AppendLine("            for (var i = 0; i < source.TermCount; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                var degree = source.DegreeAt(i);");
        sb.AppendLine("                if (degree == 0)");
        sb.AppendLine("                    continue;");
        sb.AppendLine();
        sb.AppendLine("                var status = ops.TryFromInt(degree, out var scalar);");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                    return Complete(handle, destination.Count, status);");
        sb.AppendLine();
        sb.AppendLine("                var coefficient = ops.Zero;");
        sb.AppendLine("                status = ops.TryMul(ref coefficient, source.CoefficientAt(i), scalar);");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                    return Complete(handle, destination.Count, status);");
        sb.AppendLine();
        sb.AppendLine("                status = destination.TryAppendTermStatus(degree - 1, coefficient, ops);");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                    return Complete(handle, destination.Count, status);");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            return Complete(handle, destination.Count, global::HPD.Math.Core.AlgebraStatus.Ok);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private bool CanOperate(Poly other, out int handle)");
        sb.AppendLine("        {");
        sb.AppendLine("            handle = -1;");
        sb.AppendLine("            return IsValid(Handle) && other.IsValid(other.Handle) && TryAllocate(out handle);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private bool CanOperate(out int handle)");
        sb.AppendLine("        {");
        sb.AppendLine("            handle = -1;");
        sb.AppendLine("            return IsValid(Handle) && TryAllocate(out handle);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private bool TryAllocate(out int handle)");
        sb.AppendLine("        {");
        sb.AppendLine("            handle = -1;");
        sb.AppendLine("            if ((global::HPD.Math.Core.AlgebraStatus)_state[1] != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return false;");
        sb.AppendLine("            if (_state[0] >= HandleCapacity)");
        sb.AppendLine("            {");
        sb.AppendLine("                Fail(global::HPD.Math.Core.AlgebraStatus.InsufficientDestination);");
        sb.AppendLine("                return false;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            handle = _state[0];");
        sb.AppendLine("            _state[0]++;");
        sb.AppendLine("            _counts[handle] = 0;");
        sb.AppendLine("            return true;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private Poly Complete(int handle, int count, global::HPD.Math.Core.AlgebraStatus status)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("            {");
        sb.AppendLine("                Fail(status);");
        sb.AppendLine("                return InvalidPoly();");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            _counts[handle] = count;");
        sb.AppendLine("            return CreatePoly(handle);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private bool IsValid(int handle) => handle >= 0 && handle < _state[0];");
        sb.AppendLine("        private void Fail(global::HPD.Math.Core.AlgebraStatus status)");
        sb.AppendLine("        {");
        sb.AppendLine("            if ((global::HPD.Math.Core.AlgebraStatus)_state[1] == global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                _state[1] = (int)status;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private Poly InvalidPoly() => CreatePoly(-1);");
        sb.AppendLine("        private Poly CreatePoly(int handle) => new(_degrees, _coefficients, _counts, _state, _workspaceDegrees, _workspaceCoefficients, handle);");
        sb.AppendLine("        private global::System.Span<int> DegreeSlot(int handle) => _degrees.Slice(handle * TermCapacity, TermCapacity);");
        sb.Append("        private global::System.Span<").Append(coefficientType).AppendLine("> CoefficientSlot(int handle) => _coefficients.Slice(handle * TermCapacity, TermCapacity);");
        sb.AppendLine("        private " + builderType + " Builder(int handle) => new(DegreeSlot(handle), CoefficientSlot(handle));");
        sb.AppendLine("        private " + viewType + " View(int handle) => new(new " + finsuppViewType + "(DegreeSlot(handle)[.._counts[handle]], CoefficientSlot(handle)[.._counts[handle]]));");
        sb.AppendLine();
        sb.AppendLine("    }");
    }


    private static void GenerateMatrixScope(StringBuilder sb, MatrixScopeModel scope)
    {
        GenerateMatrixAuthoringSurface(sb, scope, emitRunner: true);
    }

    private static void GenerateMatrixContext(StringBuilder sb, string contextName, MatrixScopeModel context)
    {
        GenerateMatrixValueContext(sb, contextName, context);
    }

    private static void GenerateMatrixValueContext(StringBuilder sb, string contextName, MatrixScopeModel context)
    {
        var elementType = context.ElementType;
        var elementOpsType = context.ElementOpsType;

        sb.AppendLine();
        sb.Append("    public const int Rows = ").Append(context.Rows).AppendLine(";");
        sb.Append("    public const int Columns = ").Append(context.Columns).AppendLine(";");
        sb.Append("    public const int ElementCapacity = ").Append(context.Rows * context.Columns).AppendLine(";");
        sb.AppendLine("    public static bool IsSquare => Rows == Columns;");
        sb.AppendLine();
        sb.AppendLine("    public static Matrix Zero");
        sb.AppendLine("    {");
        sb.AppendLine("        get");
        sb.AppendLine("        {");
        sb.AppendLine("            var result = default(Matrix);");
        sb.AppendLine("            var ops = new " + elementOpsType + "();");
        sb.AppendLine("            for (var i = 0; i < ElementCapacity; i++)");
        sb.AppendLine("                result.SetValue(i, ops.Zero);");
        sb.AppendLine("            return result;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public static global::HPD.Math.Core.AlgebraStatus TryIdentity(out Matrix result)");
        sb.AppendLine("    {");
        sb.AppendLine("        result = default;");
        sb.AppendLine("        if (!IsSquare)");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.DimensionMismatch;");
        sb.AppendLine();
        sb.AppendLine("        var ops = new " + elementOpsType + "();");
        sb.AppendLine("        for (var i = 0; i < ElementCapacity; i++)");
        sb.AppendLine("            result.SetValue(i, ops.Zero);");
        sb.AppendLine("        for (var i = 0; i < Rows; i++)");
        sb.AppendLine("            result.SetValue((i * Columns) + i, ops.One);");
        sb.AppendLine("        return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public static global::HPD.Math.Core.AlgebraStatus TryFromValues(global::System.ReadOnlySpan<" + elementType + "> values, out Matrix result)");
        sb.AppendLine("    {");
        sb.AppendLine("        result = default;");
        sb.AppendLine("        if (values.Length != ElementCapacity)");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine();
        sb.AppendLine("        for (var i = 0; i < ElementCapacity; i++)");
        sb.AppendLine("            result.SetValue(i, values[i]);");
        sb.AppendLine("        return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public struct Matrix : global::System.IEquatable<Matrix>");
        sb.AppendLine("    {");
        sb.AppendLine("        private ValueBuffer _values;");
        sb.AppendLine();
        sb.AppendLine("        public int RowCount => Rows;");
        sb.AppendLine("        public int ColumnCount => Columns;");
        sb.Append("        public ").Append(elementType).AppendLine(" this[int row, int column] => ReadOnlyValues[(row * Columns) + column];");
        sb.Append("        public ").Append(elementType).AppendLine(" ValueAt(int index) => ReadOnlyValues[index];");
        sb.AppendLine();
        sb.AppendLine("        internal global::System.ReadOnlySpan<" + elementType + "> ReadOnlyValues => _values.AsSpan();");
        sb.AppendLine("        internal " + elementType + " GetValue(int index) => ReadOnlyValues[index];");
        sb.AppendLine("        internal void SetValue(int index, " + elementType + " value) => _values.AsSpan()[index] = value;");
        sb.AppendLine();
        sb.AppendLine("        public bool Equals(Matrix other)");
        sb.AppendLine("        {");
        sb.AppendLine("            var left = ReadOnlyValues;");
        sb.AppendLine("            var right = other.ReadOnlyValues;");
        sb.AppendLine("            var ops = new " + elementOpsType + "();");
        sb.AppendLine("            for (var i = 0; i < ElementCapacity; i++)");
        sb.AppendLine("                if (!ops.Eq(left[i], right[i]))");
        sb.AppendLine("                    return false;");
        sb.AppendLine("            return true;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public override bool Equals(object? obj) => obj is Matrix other && Equals(other);");
        sb.AppendLine();
        sb.AppendLine("        public override int GetHashCode()");
        sb.AppendLine("        {");
        sb.AppendLine("            var hash = new global::System.HashCode();");
        sb.AppendLine("            var values = ReadOnlyValues;");
        sb.AppendLine("            for (var i = 0; i < ElementCapacity; i++)");
        sb.AppendLine("                hash.Add(values[i]);");
        sb.AppendLine("            return hash.ToHashCode();");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public static bool operator ==(Matrix left, Matrix right) => left.Equals(right);");
        sb.AppendLine("        public static bool operator !=(Matrix left, Matrix right) => !left.Equals(right);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public readonly struct Ops");
        sb.AppendLine("    {");
        sb.Append("        public Matrix Zero => ").Append(contextName).AppendLine(".Zero;");
        sb.AppendLine();
        sb.Append("        public global::HPD.Math.Core.AlgebraStatus TryIdentity(out Matrix result) => ").Append(contextName).AppendLine(".TryIdentity(out result);");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryAdd(in Matrix left, in Matrix right, out Matrix result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            var ops = new " + elementOpsType + "();");
        sb.AppendLine("            for (var i = 0; i < ElementCapacity; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                var destination = ops.Zero;");
        sb.AppendLine("                ops.Add(ref destination, left.GetValue(i), right.GetValue(i));");
        sb.AppendLine("                result.SetValue(i, destination);");
        sb.AppendLine("            }");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TrySub(in Matrix left, in Matrix right, out Matrix result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            var ops = new " + elementOpsType + "();");
        sb.AppendLine("            for (var i = 0; i < ElementCapacity; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                var destination = ops.Zero;");
        sb.AppendLine("                ops.Sub(ref destination, left.GetValue(i), right.GetValue(i));");
        sb.AppendLine("                result.SetValue(i, destination);");
        sb.AppendLine("            }");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryScale(in Matrix value, in " + elementType + " scalar, out Matrix result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            var ops = new " + elementOpsType + "();");
        sb.AppendLine("            for (var i = 0; i < ElementCapacity; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                var destination = ops.Zero;");
        sb.AppendLine("                ops.Mul(ref destination, scalar, value.GetValue(i));");
        sb.AppendLine("                result.SetValue(i, destination);");
        sb.AppendLine("            }");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryMul(in Matrix left, in Matrix right, out Matrix result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            if (!IsSquare)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.DimensionMismatch;");
        sb.AppendLine();
        sb.AppendLine("            var ops = new " + elementOpsType + "();");
        sb.AppendLine("            for (var row = 0; row < Rows; row++)");
        sb.AppendLine("            {");
        sb.AppendLine("                for (var column = 0; column < Columns; column++)");
        sb.AppendLine("                {");
        sb.AppendLine("                    var sum = ops.Zero;");
        sb.AppendLine("                    for (var k = 0; k < Columns; k++)");
        sb.AppendLine("                    {");
        sb.AppendLine("                        var product = ops.Zero;");
        sb.AppendLine("                        ops.Mul(ref product, left.GetValue((row * Columns) + k), right.GetValue((k * Columns) + column));");
        sb.AppendLine("                        var next = ops.Zero;");
        sb.AppendLine("                        ops.Add(ref next, sum, product);");
        sb.AppendLine("                        sum = next;");
        sb.AppendLine("                    }");
        sb.AppendLine("                    result.SetValue((row * Columns) + column, sum);");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryTranspose(in Matrix value, out Matrix result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            if (!IsSquare)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.DimensionMismatch;");
        sb.AppendLine();
        sb.AppendLine("            for (var row = 0; row < Rows; row++)");
        sb.AppendLine("            {");
        sb.AppendLine("                for (var column = 0; column < Columns; column++)");
        sb.AppendLine("                    result.SetValue((column * Rows) + row, value.GetValue((row * Columns) + column));");
        sb.AppendLine("            }");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    [global::System.Runtime.CompilerServices.InlineArray(ElementCapacity)]");
        sb.AppendLine("    private struct ValueBuffer");
        sb.AppendLine("    {");
        sb.Append("        private ").Append(elementType).AppendLine(" _element0;");
        sb.Append("        public global::System.Span<").Append(elementType).AppendLine("> AsSpan() => global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref _element0, ElementCapacity);");
        sb.AppendLine("    }");
    }

    private static void GenerateMatrixAuthoringSurface(
        StringBuilder sb,
        MatrixScopeModel scope,
        bool emitRunner)
    {
        var elementType = scope.ElementType;
        var elementOpsType = scope.ElementOpsType;
        var viewType = "global::HPD.Math.LinearAlgebra.MatrixView<" + elementType + ">";
        var builderType = "global::HPD.Math.LinearAlgebra.MatrixBuilder<" + elementType + ">";

        sb.AppendLine();
        sb.Append("    public const int Rows = ").Append(scope.Rows).AppendLine(";");
        sb.Append("    public const int Columns = ").Append(scope.Columns).AppendLine(";");
        sb.Append("    public const int ElementCapacity = ").Append(scope.Rows * scope.Columns).AppendLine(";");
        sb.Append("    public const int HandleCapacity = ").Append(scope.Handles).AppendLine(";");
        sb.AppendLine();
        if (emitRunner)
        {
            sb.AppendLine("    public global::HPD.Math.Core.AlgebraStatus Run(ref Result result)");
            sb.AppendLine("    {");
            sb.AppendLine("        result.Clear();");
            sb.Append("        global::System.Span<").Append(elementType).AppendLine("> values = stackalloc " + elementType + "[ElementCapacity * HandleCapacity];");
            sb.AppendLine("        global::System.Span<int> state = stackalloc int[2];");
            sb.AppendLine();
            sb.AppendLine("        var scope = new Scope(values, state);");
            sb.AppendLine("        Build(ref scope);");
            sb.AppendLine("        var status = scope.CopyReturned(result.ValueStorage, out var rows, out var columns);");
            sb.AppendLine("        if (scope.Status != global::HPD.Math.Core.AlgebraStatus.Ok)");
            sb.AppendLine("            return scope.Status;");
            sb.AppendLine("        if (status == global::HPD.Math.Core.AlgebraStatus.Ok)");
            sb.AppendLine("            result.SetShape(rows, columns);");
            sb.AppendLine("        return status;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    public global::HPD.Math.Core.AlgebraStatus Run()");
            sb.AppendLine("    {");
            sb.Append("        global::System.Span<").Append(elementType).AppendLine("> values = stackalloc " + elementType + "[ElementCapacity * HandleCapacity];");
            sb.AppendLine("        global::System.Span<int> state = stackalloc int[2];");
            sb.AppendLine();
            sb.AppendLine("        var scope = new Scope(values, state);");
            sb.AppendLine("        Build(ref scope);");
            sb.AppendLine("        return scope.Status;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    partial void Build(ref Scope m);");
            sb.AppendLine();
            sb.AppendLine("    public struct Result");
            sb.AppendLine("    {");
            sb.AppendLine("        private ValueBuffer _values;");
            sb.AppendLine("        public int RowCount { get; private set; }");
            sb.AppendLine("        public int ColumnCount { get; private set; }");
            sb.Append("        public ").Append(elementType).AppendLine(" this[int row, int column] => _values[(row * Columns) + column];");
            sb.Append("        public ").Append(elementType).AppendLine(" ValueAt(int index) => _values[index];");
            sb.Append("        internal global::System.Span<").Append(elementType).AppendLine("> ValueStorage => _values.AsSpan();");
            sb.AppendLine("        internal void SetShape(int rows, int columns) { RowCount = rows; ColumnCount = columns; }");
            sb.AppendLine("        internal void Clear() { RowCount = 0; ColumnCount = 0; }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    [global::System.Runtime.CompilerServices.InlineArray(ElementCapacity)]");
            sb.AppendLine("    private struct ValueBuffer");
            sb.AppendLine("    {");
            sb.Append("        private ").Append(elementType).AppendLine(" _element0;");
            sb.Append("        public global::System.Span<").Append(elementType).AppendLine("> AsSpan() => global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref _element0, ElementCapacity);");
            sb.AppendLine("    }");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("    public static " + elementOpsType + " CreateOps() => new();");
            sb.AppendLine();
            sb.Append("    public static Scope CreateScope(global::System.Span<").Append(elementType)
                .AppendLine("> values, global::System.Span<int> state) =>");
            sb.AppendLine("        new(values, state);");
            sb.AppendLine();
        }
        sb.AppendLine("    public ref struct Scope");
        sb.AppendLine("    {");
        sb.Append("        private readonly global::System.Span<").Append(elementType).AppendLine("> _values;");
        sb.AppendLine("        private readonly global::System.Span<int> _state;");
        sb.AppendLine("        private int _returnedHandle;");
        sb.AppendLine();
        sb.Append("        public Scope(global::System.Span<").Append(elementType).AppendLine("> values, global::System.Span<int> state)");
        sb.AppendLine("        {");
        sb.AppendLine("            _values = values;");
        sb.AppendLine("            _state = state;");
        sb.AppendLine("            _state[0] = 0;");
        sb.AppendLine("            _state[1] = (int)global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("            _returnedHandle = -1;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public readonly global::HPD.Math.Core.AlgebraStatus Status => (global::HPD.Math.Core.AlgebraStatus)_state[1];");
        sb.AppendLine();
        sb.Append("        public Matrix FromValues(global::System.ReadOnlySpan<").Append(elementType).AppendLine("> values)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!TryAllocate(out var handle))");
        sb.AppendLine("                return InvalidMatrix();");
        sb.AppendLine("            if (values.Length != ElementCapacity)");
        sb.AppendLine("            {");
        sb.AppendLine("                Fail(global::HPD.Math.Core.AlgebraStatus.InvalidInput);");
        sb.AppendLine("                return InvalidMatrix();");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            values.CopyTo(ValueSlot(handle));");
        sb.AppendLine("            return CreateMatrix(handle);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public Matrix Zero()");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!TryAllocate(out var handle))");
        sb.AppendLine("                return InvalidMatrix();");
        sb.AppendLine();
        sb.AppendLine("            ValueSlot(handle).Fill(new " + elementOpsType + "().Zero);");
        sb.AppendLine("            return CreateMatrix(handle);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public Matrix Identity()");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!TryAllocate(out var handle))");
        sb.AppendLine("                return InvalidMatrix();");
        sb.AppendLine();
        sb.AppendLine("            var destination = Builder(handle);");
        sb.AppendLine("            var status = global::HPD.Math.LinearAlgebra.MatrixKernels.TryIdentity(Rows, ref destination, new " + elementOpsType + "());");
        sb.AppendLine("            return Complete(handle, status);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public Matrix Add(Matrix left, Matrix right) => left + right;");
        sb.AppendLine("        public Matrix Mul(Matrix left, Matrix right) => left * right;");
        sb.AppendLine("        public Matrix Transpose(Matrix value) => value.Transpose;");
        sb.AppendLine();
        sb.AppendLine("        public void Return(Matrix value)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!IsValid(value.Handle))");
        sb.AppendLine("            {");
        sb.AppendLine("                Fail(global::HPD.Math.Core.AlgebraStatus.InvalidInput);");
        sb.AppendLine("                return;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            _returnedHandle = value.Handle;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.Append("        public global::HPD.Math.Core.AlgebraStatus CopyReturned(global::System.Span<")
            .Append(elementType).AppendLine("> outputValues, out int outputRows, out int outputColumns)");
        sb.AppendLine("        {");
        sb.AppendLine("            outputRows = 0;");
        sb.AppendLine("            outputColumns = 0;");
        sb.AppendLine("            if (Status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return Status;");
        sb.AppendLine("            if (!IsValid(_returnedHandle))");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine("            if (outputValues.Length < ElementCapacity)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InsufficientDestination;");
        sb.AppendLine();
        sb.AppendLine("            ValueSlot(_returnedHandle).CopyTo(outputValues);");
        sb.AppendLine("            outputRows = Rows;");
        sb.AppendLine("            outputColumns = Columns;");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private bool TryAllocate(out int handle)");
        sb.AppendLine("        {");
        sb.AppendLine("            handle = -1;");
        sb.AppendLine("            if (Status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return false;");
        sb.AppendLine("            if (_state[0] >= HandleCapacity)");
        sb.AppendLine("            {");
        sb.AppendLine("                Fail(global::HPD.Math.Core.AlgebraStatus.InsufficientDestination);");
        sb.AppendLine("                return false;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            handle = _state[0];");
        sb.AppendLine("            _state[0]++;");
        sb.AppendLine("            return true;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private bool IsValid(int handle) => handle >= 0 && handle < _state[0];");
        sb.AppendLine("        private void Fail(global::HPD.Math.Core.AlgebraStatus status)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (Status == global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                _state[1] = (int)status;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private Matrix Complete(int handle, global::HPD.Math.Core.AlgebraStatus status)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("            {");
        sb.AppendLine("                Fail(status);");
        sb.AppendLine("                return InvalidMatrix();");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            return CreateMatrix(handle);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private Matrix InvalidMatrix() => CreateMatrix(-1);");
        sb.AppendLine("        private Matrix CreateMatrix(int handle) => new(_values, _state, handle);");
        sb.Append("        private global::System.Span<").Append(elementType).AppendLine("> ValueSlot(int handle) => _values.Slice(handle * ElementCapacity, ElementCapacity);");
        sb.AppendLine("        private " + builderType + " Builder(int handle) => new(ValueSlot(handle));");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public readonly ref struct Matrix");
        sb.AppendLine("    {");
        sb.Append("        private readonly global::System.Span<").Append(elementType).AppendLine("> _values;");
        sb.AppendLine("        private readonly global::System.Span<int> _state;");
        sb.AppendLine("        internal readonly int Handle;");
        sb.AppendLine();
        sb.Append("        internal Matrix(global::System.Span<").Append(elementType).AppendLine("> values, global::System.Span<int> state, int handle)");
        sb.AppendLine("        {");
        sb.AppendLine("            _values = values;");
        sb.AppendLine("            _state = state;");
        sb.AppendLine("            Handle = handle;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public int RowCount => Rows;");
        sb.AppendLine("        public int ColumnCount => Columns;");
        sb.Append("        public ").Append(elementType).AppendLine(" this[int row, int column] => ValueSlot(Handle)[(row * Columns) + column];");
        sb.AppendLine("        public Matrix Transpose => TransposeCore();");
        sb.AppendLine();
        sb.AppendLine("        public Matrix Add(Matrix other)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!CanOperate(other, out var handle))");
        sb.AppendLine("                return InvalidMatrix();");
        sb.AppendLine();
        sb.AppendLine("            var destination = Builder(handle);");
        sb.AppendLine("            var status = global::HPD.Math.LinearAlgebra.MatrixKernels.TryAdd(View(Handle), other.View(other.Handle), ref destination, new " + elementOpsType + "());");
        sb.AppendLine("            return Complete(handle, status);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public Matrix Mul(Matrix other)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!CanOperate(other, out var handle))");
        sb.AppendLine("                return InvalidMatrix();");
        sb.AppendLine();
        sb.AppendLine("            var destination = Builder(handle);");
        sb.AppendLine("            var status = global::HPD.Math.LinearAlgebra.MatrixKernels.TryMul(View(Handle), other.View(other.Handle), ref destination, new " + elementOpsType + "());");
        sb.AppendLine("            return Complete(handle, status);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private Matrix TransposeCore()");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!CanOperate(out var handle))");
        sb.AppendLine("                return InvalidMatrix();");
        sb.AppendLine();
        sb.AppendLine("            var destination = Builder(handle);");
        sb.AppendLine("            var status = global::HPD.Math.LinearAlgebra.MatrixKernels.TryTranspose(View(Handle), ref destination);");
        sb.AppendLine("            return Complete(handle, status);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private bool CanOperate(Matrix other, out int handle)");
        sb.AppendLine("        {");
        sb.AppendLine("            handle = -1;");
        sb.AppendLine("            if (Status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return false;");
        sb.AppendLine("            if (!IsValid(Handle) || !IsValid(other.Handle))");
        sb.AppendLine("            {");
        sb.AppendLine("                Fail(global::HPD.Math.Core.AlgebraStatus.InvalidInput);");
        sb.AppendLine("                return false;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            return TryAllocate(out handle);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private bool CanOperate(out int handle)");
        sb.AppendLine("        {");
        sb.AppendLine("            handle = -1;");
        sb.AppendLine("            if (Status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return false;");
        sb.AppendLine("            if (!IsValid(Handle))");
        sb.AppendLine("            {");
        sb.AppendLine("                Fail(global::HPD.Math.Core.AlgebraStatus.InvalidInput);");
        sb.AppendLine("                return false;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            return TryAllocate(out handle);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private bool TryAllocate(out int handle)");
        sb.AppendLine("        {");
        sb.AppendLine("            handle = -1;");
        sb.AppendLine("            if (_state[0] >= HandleCapacity)");
        sb.AppendLine("            {");
        sb.AppendLine("                Fail(global::HPD.Math.Core.AlgebraStatus.InsufficientDestination);");
        sb.AppendLine("                return false;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            handle = _state[0];");
        sb.AppendLine("            _state[0]++;");
        sb.AppendLine("            return true;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private Matrix Complete(int handle, global::HPD.Math.Core.AlgebraStatus status)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("            {");
        sb.AppendLine("                Fail(status);");
        sb.AppendLine("                return InvalidMatrix();");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            return new Matrix(_values, _state, handle);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private global::HPD.Math.Core.AlgebraStatus Status => (global::HPD.Math.Core.AlgebraStatus)_state[1];");
        sb.AppendLine("        private bool IsValid(int handle) => handle >= 0 && handle < _state[0];");
        sb.AppendLine("        private void Fail(global::HPD.Math.Core.AlgebraStatus status)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (Status == global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                _state[1] = (int)status;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private Matrix InvalidMatrix() => new(_values, _state, -1);");
        sb.Append("        private global::System.Span<").Append(elementType).AppendLine("> ValueSlot(int handle) => _values.Slice(handle * ElementCapacity, ElementCapacity);");
        sb.AppendLine("        private " + builderType + " Builder(int handle) => new(ValueSlot(handle));");
        sb.AppendLine("        private " + viewType + " View(int handle) => new(Rows, Columns, ValueSlot(handle));");
        sb.AppendLine("    }");
    }

    private static void GenerateReverseDiffScope(StringBuilder sb, ReverseDiffScopeModel scope)
    {
        GenerateReverseDiffAuthoringSurface(sb, scope, emitRunner: true);
    }

    private static void GenerateReverseDiffContext(StringBuilder sb, ReverseDiffScopeModel context)
    {
        GenerateReverseDiffAuthoringSurface(sb, context, emitRunner: true);
    }

    private static void GenerateReverseDiffAuthoringSurface(
        StringBuilder sb,
        ReverseDiffScopeModel scope,
        bool emitRunner)
    {
        var scalarType = scope.ScalarType;
        var scalarOpsType = scope.ScalarOpsType;
        var nodeType = "global::HPD.Math.Autodiff.ReverseNode<" + scalarType + ">";
        var tapeViewType = "global::HPD.Math.Autodiff.ReverseTapeView<" + scalarType + ">";

        sb.AppendLine();
        sb.Append("    public const int NodeCapacity = ").Append(scope.Nodes).AppendLine(";");
        sb.AppendLine();
        if (emitRunner)
        {
            sb.AppendLine("    public global::HPD.Math.Core.AlgebraStatus Run(ref Result result)");
            sb.AppendLine("    {");
            sb.AppendLine("        result.Clear();");
            sb.Append("        global::System.Span<").Append(nodeType).AppendLine("> nodes = stackalloc " + nodeType + "[NodeCapacity];");
            sb.Append("        global::System.Span<").Append(scalarType).AppendLine("> allGradients = stackalloc " + scalarType + "[NodeCapacity];");
            sb.AppendLine("        global::System.Span<int> state = stackalloc int[3];");
            sb.AppendLine();
            sb.AppendLine("        var scope = new Scope(nodes, allGradients, state);");
            sb.AppendLine("        Build(ref scope);");
            sb.AppendLine("        var status = scope.CopyReturned(out var primal, result.GradientStorage, out var gradientCount);");
            sb.AppendLine("        if (scope.Status != global::HPD.Math.Core.AlgebraStatus.Ok)");
            sb.AppendLine("            return scope.Status;");
            sb.AppendLine("        if (status == global::HPD.Math.Core.AlgebraStatus.Ok)");
            sb.AppendLine("            result.Set(primal, gradientCount);");
            sb.AppendLine("        return status;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    public global::HPD.Math.Core.AlgebraStatus Run()");
            sb.AppendLine("    {");
            sb.Append("        global::System.Span<").Append(nodeType).AppendLine("> nodes = stackalloc " + nodeType + "[NodeCapacity];");
            sb.Append("        global::System.Span<").Append(scalarType).AppendLine("> gradients = stackalloc " + scalarType + "[NodeCapacity];");
            sb.AppendLine("        global::System.Span<int> state = stackalloc int[3];");
            sb.AppendLine();
            sb.AppendLine("        var scope = new Scope(nodes, gradients, state);");
            sb.AppendLine("        Build(ref scope);");
            sb.AppendLine("        return scope.Status;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    partial void Build(ref Scope d);");
            sb.AppendLine();
            sb.AppendLine("    public struct Result");
            sb.AppendLine("    {");
            sb.AppendLine("        private GradientBuffer _gradients;");
            sb.Append("        public ").Append(scalarType).AppendLine(" Primal { get; private set; }");
            sb.AppendLine("        public int GradientCount { get; private set; }");
            sb.Append("        public ").Append(scalarType).AppendLine(" GradientAt(int index) => _gradients[index];");
            sb.Append("        internal global::System.Span<").Append(scalarType).AppendLine("> GradientStorage => _gradients.AsSpan();");
            sb.AppendLine("        internal void Set(" + scalarType + " primal, int gradientCount) { Primal = primal; GradientCount = gradientCount; }");
            sb.AppendLine("        internal void Clear() { Primal = new " + scalarOpsType + "().Zero; GradientCount = 0; }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    [global::System.Runtime.CompilerServices.InlineArray(NodeCapacity)]");
            sb.AppendLine("    private struct GradientBuffer");
            sb.AppendLine("    {");
            sb.Append("        private ").Append(scalarType).AppendLine(" _element0;");
            sb.Append("        public global::System.Span<").Append(scalarType).AppendLine("> AsSpan() => global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref _element0, NodeCapacity);");
            sb.AppendLine("    }");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("    public static " + scalarOpsType + " CreateOps() => new();");
            sb.AppendLine();
            sb.Append("    public static Scope CreateScope(global::System.Span<").Append(nodeType)
                .Append("> nodes, global::System.Span<").Append(scalarType)
                .AppendLine("> gradients, global::System.Span<int> state) =>");
            sb.AppendLine("        new(nodes, gradients, state);");
            sb.AppendLine();
        }
        sb.AppendLine("    public ref struct Scope");
        sb.AppendLine("    {");
        sb.Append("        private readonly global::System.Span<").Append(nodeType).AppendLine("> _nodes;");
        sb.Append("        private readonly global::System.Span<").Append(scalarType).AppendLine("> _gradients;");
        sb.AppendLine("        private readonly global::System.Span<int> _state;");
        sb.AppendLine();
        sb.Append("        public Scope(global::System.Span<").Append(nodeType)
            .Append("> nodes, global::System.Span<").Append(scalarType).AppendLine("> gradients, global::System.Span<int> state)");
        sb.AppendLine("        {");
        sb.AppendLine("            _nodes = nodes;");
        sb.AppendLine("            _gradients = gradients;");
        sb.AppendLine("            _state = state;");
        sb.AppendLine("            _state[0] = 0;");
        sb.AppendLine("            _state[1] = (int)global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("            _state[2] = -1;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public readonly global::HPD.Math.Core.AlgebraStatus Status => (global::HPD.Math.Core.AlgebraStatus)_state[1];");
        sb.AppendLine("        public readonly int Count => _state[0];");
        sb.AppendLine();
        sb.Append("        public Var Input(").Append(scalarType).AppendLine(" value) => Append(global::HPD.Math.Autodiff.ReverseOpCode.Input, -1, -1, value);");
        sb.Append("        public Var Const(").Append(scalarType).AppendLine(" value) => Append(global::HPD.Math.Autodiff.ReverseOpCode.Constant, -1, -1, value);");
        sb.AppendLine();
        sb.AppendLine("        public Var Const(int value)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (Status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return InvalidVar();");
        sb.AppendLine();
        sb.AppendLine("            var status = new " + scalarOpsType + "().TryFromInt(value, out var scalar);");
        sb.AppendLine("            return status == global::HPD.Math.Core.AlgebraStatus.Ok");
        sb.AppendLine("                ? Const(scalar)");
        sb.AppendLine("                : FailAndReturn(status);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public Var Add(Var left, Var right) => left + right;");
        sb.AppendLine("        public Var Sub(Var left, Var right) => left - right;");
        sb.AppendLine("        public Var Mul(Var left, Var right) => left * right;");
        sb.AppendLine("        public Var Neg(Var value) => -value;");
        sb.AppendLine("        public Var Inv(Var value) => value.Inv;");
        sb.AppendLine();
        sb.AppendLine("        public void Return(Var value)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!IsValid(value.Index))");
        sb.AppendLine("            {");
        sb.AppendLine("                Fail(global::HPD.Math.Core.AlgebraStatus.InvalidInput);");
        sb.AppendLine("                return;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            _state[2] = value.Index;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.Append("        public global::HPD.Math.Core.AlgebraStatus CopyReturned(out ").Append(scalarType)
            .AppendLine(" primal, global::System.Span<" + scalarType + "> gradients, out int gradientCount)");
        sb.AppendLine("        {");
        sb.AppendLine("            primal = new " + scalarOpsType + "().Zero;");
        sb.AppendLine("            gradientCount = 0;");
        sb.AppendLine("            if (Status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return Status;");
        sb.AppendLine("            if (!IsValid(_state[2]))");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine("            if (gradients.Length < Count)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InsufficientDestination;");
        sb.AppendLine();
        sb.AppendLine("            var status = global::HPD.Math.Autodiff.ReverseTapeKernels.TryBackward(new " + tapeViewType + "(_nodes[..Count]), _state[2], _gradients, new " + scalarOpsType + "());");
        sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return status;");
        sb.AppendLine();
        sb.AppendLine("            _gradients[..Count].CopyTo(gradients);");
        sb.AppendLine("            gradientCount = Count;");
        sb.AppendLine("            primal = _nodes[_state[2]].Primal;");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.Append("        private Var Append(global::HPD.Math.Autodiff.ReverseOpCode opCode, int left, int right, ").Append(scalarType).AppendLine(" primal)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (Status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return InvalidVar();");
        sb.AppendLine("            if (Count >= NodeCapacity)");
        sb.AppendLine("                return FailAndReturn(global::HPD.Math.Core.AlgebraStatus.InsufficientDestination);");
        sb.AppendLine();
        sb.AppendLine("            var index = Count;");
        sb.AppendLine("            _nodes[index] = new " + nodeType + "(opCode, left, right, primal);");
        sb.AppendLine("            _state[0]++;");
        sb.AppendLine("            return CreateVar(index);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private bool IsValid(int index) => index >= 0 && index < Count;");
        sb.AppendLine("        private void Fail(global::HPD.Math.Core.AlgebraStatus status)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (Status == global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                _state[1] = (int)status;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private Var FailAndReturn(global::HPD.Math.Core.AlgebraStatus status)");
        sb.AppendLine("        {");
        sb.AppendLine("            Fail(status);");
        sb.AppendLine("            return InvalidVar();");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private Var InvalidVar() => CreateVar(-1);");
        sb.AppendLine("        private Var CreateVar(int index) => new(_nodes, _state, index);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public readonly ref struct Var");
        sb.AppendLine("    {");
        sb.Append("        private readonly global::System.Span<").Append(nodeType).AppendLine("> _nodes;");
        sb.AppendLine("        private readonly global::System.Span<int> _state;");
        sb.AppendLine("        internal readonly int Index;");
        sb.AppendLine();
        sb.Append("        internal Var(global::System.Span<").Append(nodeType).AppendLine("> nodes, global::System.Span<int> state, int index)");
        sb.AppendLine("        {");
        sb.AppendLine("            _nodes = nodes;");
        sb.AppendLine("            _state = state;");
        sb.AppendLine("            Index = index;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public int NodeIndex => Index;");
        sb.Append("        public ").Append(scalarType).AppendLine(" Value => IsValid(Index) ? _nodes[Index].Primal : new " + scalarOpsType + "().Zero;");
        sb.AppendLine("        public Var Inv => Invert();");
        sb.AppendLine();
        sb.AppendLine("        public Var Add(Var other) => Binary(other, global::HPD.Math.Autodiff.ReverseOpCode.Add);");
        sb.AppendLine("        public Var Sub(Var other) => Binary(other, global::HPD.Math.Autodiff.ReverseOpCode.Sub);");
        sb.AppendLine("        public Var Mul(Var other) => Binary(other, global::HPD.Math.Autodiff.ReverseOpCode.Mul);");
        sb.AppendLine("        public Var Neg() => Unary(global::HPD.Math.Autodiff.ReverseOpCode.Neg);");
        sb.AppendLine();
        sb.AppendLine("        private Var Binary(Var other, global::HPD.Math.Autodiff.ReverseOpCode opCode)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!CanOperate(other, out var index))");
        sb.AppendLine("                return InvalidVar();");
        sb.AppendLine();
        sb.AppendLine("            var ops = new " + scalarOpsType + "();");
        sb.AppendLine("            var primal = ops.Zero;");
        sb.AppendLine("            var status = opCode switch");
        sb.AppendLine("            {");
        sb.AppendLine("                global::HPD.Math.Autodiff.ReverseOpCode.Add => ops.TryAdd(ref primal, Value, other.Value),");
        sb.AppendLine("                global::HPD.Math.Autodiff.ReverseOpCode.Sub => ops.TrySub(ref primal, Value, other.Value),");
        sb.AppendLine("                global::HPD.Math.Autodiff.ReverseOpCode.Mul => ops.TryMul(ref primal, Value, other.Value),");
        sb.AppendLine("                _ => global::HPD.Math.Core.AlgebraStatus.InvalidInput");
        sb.AppendLine("            };");
        sb.AppendLine();
        sb.AppendLine("            return status == global::HPD.Math.Core.AlgebraStatus.Ok");
        sb.AppendLine("                ? Complete(index, opCode, Index, other.Index, primal)");
        sb.AppendLine("                : FailAndReturn(status);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private Var Unary(global::HPD.Math.Autodiff.ReverseOpCode opCode)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!CanOperate(out var index))");
        sb.AppendLine("                return InvalidVar();");
        sb.AppendLine();
        sb.AppendLine("            var ops = new " + scalarOpsType + "();");
        sb.AppendLine("            var primal = ops.Zero;");
        sb.AppendLine("            var status = opCode == global::HPD.Math.Autodiff.ReverseOpCode.Neg");
        sb.AppendLine("                ? ops.TryNeg(ref primal, Value)");
        sb.AppendLine("                : global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine();
        sb.AppendLine("            return status == global::HPD.Math.Core.AlgebraStatus.Ok");
        sb.AppendLine("                ? Complete(index, opCode, Index, -1, primal)");
        sb.AppendLine("                : FailAndReturn(status);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private Var Invert()");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!CanOperate(out var index))");
        sb.AppendLine("                return InvalidVar();");
        sb.AppendLine();
        sb.AppendLine("            var ops = new " + scalarOpsType + "();");
        sb.AppendLine("            var primal = ops.Zero;");
        sb.AppendLine("            var status = ops.TryInvert(ref primal, Value);");
        sb.AppendLine("            return status == global::HPD.Math.Core.AlgebraStatus.Ok");
        sb.AppendLine("                ? Complete(index, global::HPD.Math.Autodiff.ReverseOpCode.Inv, Index, -1, primal)");
        sb.AppendLine("                : FailAndReturn(status);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private bool CanOperate(Var other, out int index)");
        sb.AppendLine("        {");
        sb.AppendLine("            index = -1;");
        sb.AppendLine("            if (Status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return false;");
        sb.AppendLine("            if (!IsValid(Index) || !IsValid(other.Index))");
        sb.AppendLine("                return Fail(global::HPD.Math.Core.AlgebraStatus.InvalidInput);");
        sb.AppendLine("            return TryAllocate(out index);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private bool CanOperate(out int index)");
        sb.AppendLine("        {");
        sb.AppendLine("            index = -1;");
        sb.AppendLine("            if (Status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return false;");
        sb.AppendLine("            if (!IsValid(Index))");
        sb.AppendLine("                return Fail(global::HPD.Math.Core.AlgebraStatus.InvalidInput);");
        sb.AppendLine("            return TryAllocate(out index);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private bool TryAllocate(out int index)");
        sb.AppendLine("        {");
        sb.AppendLine("            index = -1;");
        sb.AppendLine("            if (_state[0] >= NodeCapacity)");
        sb.AppendLine("                return Fail(global::HPD.Math.Core.AlgebraStatus.InsufficientDestination);");
        sb.AppendLine("            index = _state[0];");
        sb.AppendLine("            _state[0]++;");
        sb.AppendLine("            return true;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.Append("        private Var Complete(int index, global::HPD.Math.Autodiff.ReverseOpCode opCode, int left, int right, ").Append(scalarType).AppendLine(" primal)");
        sb.AppendLine("        {");
        sb.AppendLine("            _nodes[index] = new " + nodeType + "(opCode, left, right, primal);");
        sb.AppendLine("            return new Var(_nodes, _state, index);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private global::HPD.Math.Core.AlgebraStatus Status => (global::HPD.Math.Core.AlgebraStatus)_state[1];");
        sb.AppendLine("        private bool IsValid(int index) => index >= 0 && index < _state[0];");
        sb.AppendLine("        private bool Fail(global::HPD.Math.Core.AlgebraStatus status)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (Status == global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                _state[1] = (int)status;");
        sb.AppendLine("            return false;");
        sb.AppendLine("        }");
        sb.AppendLine("        private Var FailAndReturn(global::HPD.Math.Core.AlgebraStatus status)");
        sb.AppendLine("        {");
        sb.AppendLine("            Fail(status);");
        sb.AppendLine("            return InvalidVar();");
        sb.AppendLine("        }");
        sb.AppendLine("        private Var InvalidVar() => new(_nodes, _state, -1);");
        sb.AppendLine("    }");
    }

    private static void GeneratePolynomialQuotientScope(StringBuilder sb, PolynomialQuotientScopeModel scope)
    {
        GeneratePolynomialQuotientAuthoringSurface(sb, scope, emitRunner: true);
    }

    private static void GeneratePolynomialQuotientContext(StringBuilder sb, PolynomialQuotientScopeModel context)
    {
        GeneratePolynomialQuotientValueContext(sb, context);
    }

    private static void GeneratePolynomialQuotientValueContext(StringBuilder sb, PolynomialQuotientScopeModel context)
    {
        var coefficientType = context.CoefficientType;
        var coefficientOpsType = context.CoefficientOpsType;
        var appendMethod = context.UsesStatusFieldOps ? "TryAppendTermStatus" : "TryAppendTerm";
        var validateContextMethod = context.UsesStatusFieldOps ? "ValidateContextStatus" : "ValidateContext";
        var reduceMethod = context.UsesStatusFieldOps ? "TryReduceStatus" : "TryReduce";
        var addMethod = context.UsesStatusFieldOps ? "TryAddStatus" : "TryAdd";
        var mulMethod = context.UsesStatusFieldOps ? "TryMulStatus" : "TryMul";
        var quotientViewType = "global::HPD.Math.Algebra.PolynomialQuotientView<" + coefficientType + ">";
        var quotientBuilderType = "global::HPD.Math.Algebra.PolynomialQuotientBuilder<" + coefficientType + ">";
        var polynomialViewType = "global::HPD.Math.Algebra.SparsePolynomialView<" + coefficientType + ">";
        var polynomialBuilderType = "global::HPD.Math.Algebra.SparsePolynomialBuilder<" + coefficientType + ">";
        var finsuppViewType = "global::HPD.Math.Finite.FinsuppView<int, " + coefficientType + ">";

        sb.AppendLine();
        sb.Append("    public const int TermCapacity = ").Append(context.Terms).AppendLine(";");
        sb.Append("    public const int WorkspaceCapacity = ").Append(context.Workspace).AppendLine(";");
        sb.AppendLine();
        sb.AppendLine("    public static global::HPD.Math.Core.AlgebraStatus TryCreateOps(scoped global::System.ReadOnlySpan<int> modulusDegrees, scoped global::System.ReadOnlySpan<" + coefficientType + "> modulusCoefficients, out Ops ops)");
        sb.AppendLine("    {");
        sb.AppendLine("        ops = default;");
        sb.AppendLine("        if (modulusDegrees.Length != modulusCoefficients.Length)");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine("        if (modulusDegrees.Length > TermCapacity)");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.InsufficientDestination;");
        sb.AppendLine();
        sb.AppendLine("        var modulus = default(Element);");
        sb.AppendLine("        var builder = new " + polynomialBuilderType + "(modulus.DegreeStorage, modulus.CoefficientStorage);");
        sb.AppendLine("        var coefficientOps = new " + coefficientOpsType + "();");
        sb.AppendLine("        for (var i = 0; i < modulusDegrees.Length; i++)");
        sb.AppendLine("        {");
        sb.AppendLine("            var status = builder." + appendMethod + "(modulusDegrees[i], modulusCoefficients[i], coefficientOps);");
        sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return status;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        modulus.SetTermCount(builder.Count);");
        sb.AppendLine("        var modulusView = new " + polynomialViewType + "(new " + finsuppViewType + "(modulus.DegreeStorage[..modulus.TermCount], modulus.CoefficientStorage[..modulus.TermCount]));");
        sb.AppendLine("        var contextStatus = global::HPD.Math.Algebra.PolynomialQuotientKernels." + validateContextMethod + "(modulusView, coefficientOps);");
        sb.AppendLine("        if (contextStatus != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("            return contextStatus;");
        sb.AppendLine();
        sb.AppendLine("        ops = new Ops(modulus);");
        sb.AppendLine("        return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public struct Element : global::System.IEquatable<Element>");
        sb.AppendLine("    {");
        sb.AppendLine("        private DegreeBuffer _degrees;");
        sb.AppendLine("        private CoefficientBuffer _coefficients;");
        sb.AppendLine("        private int _termCount;");
        sb.AppendLine();
        sb.AppendLine("        public int TermCount => _termCount;");
        sb.AppendLine("        public bool IsZero => _termCount == 0;");
        sb.AppendLine("        public int Degree => _termCount == 0 ? -1 : _degrees[_termCount - 1];");
        sb.AppendLine("        public int DegreeAt(int supportIndex) => _degrees[supportIndex];");
        sb.AppendLine("        public " + coefficientType + " CoefficientAt(int supportIndex) => _coefficients[supportIndex];");
        sb.AppendLine("        internal global::System.Span<int> DegreeStorage => _degrees.AsSpan();");
        sb.AppendLine("        internal global::System.Span<" + coefficientType + "> CoefficientStorage => _coefficients.AsSpan();");
        sb.AppendLine("        internal void SetTermCount(int count) => _termCount = count;");
        sb.AppendLine();
        sb.AppendLine("        public bool Equals(Element other)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (_termCount != other._termCount)");
        sb.AppendLine("                return false;");
        sb.AppendLine("            var ops = new " + coefficientOpsType + "();");
        sb.AppendLine("            for (var i = 0; i < _termCount; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                if (_degrees[i] != other._degrees[i] || !ops.Eq(_coefficients[i], other._coefficients[i]))");
        sb.AppendLine("                    return false;");
        sb.AppendLine("            }");
        sb.AppendLine("            return true;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public override bool Equals(object? obj) => obj is Element other && Equals(other);");
        sb.AppendLine("        public override int GetHashCode()");
        sb.AppendLine("        {");
        sb.AppendLine("            var hash = new global::System.HashCode();");
        sb.AppendLine("            for (var i = 0; i < _termCount; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                hash.Add(_degrees[i]);");
        sb.AppendLine("                hash.Add(_coefficients[i]);");
        sb.AppendLine("            }");
        sb.AppendLine("            return hash.ToHashCode();");
        sb.AppendLine("        }");
        sb.AppendLine("        public static bool operator ==(Element left, Element right) => left.Equals(right);");
        sb.AppendLine("        public static bool operator !=(Element left, Element right) => !left.Equals(right);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public readonly struct Ops");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly Element _modulus;");
        sb.AppendLine("        private readonly bool _hasModulus;");
        sb.AppendLine("        internal Ops(Element modulus)");
        sb.AppendLine("        {");
        sb.AppendLine("            _modulus = modulus;");
        sb.AppendLine("            _hasModulus = true;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public bool HasModulus => _hasModulus;");
        sb.AppendLine("        public Element Modulus => _modulus;");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryGenerator(out Element result) =>");
        sb.AppendLine("            TryMonomial(1, new " + coefficientOpsType + "().One, out result);");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryConst(" + coefficientType + " coefficient, out Element result) =>");
        sb.AppendLine("            TryMonomial(0, coefficient, out result);");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryMonomial(int degree, " + coefficientType + " coefficient, out Element result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            if (!_hasModulus)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine("            var builder = new " + polynomialBuilderType + "(result.DegreeStorage, result.CoefficientStorage);");
        sb.AppendLine("            var coefficientOps = new " + coefficientOpsType + "();");
        sb.AppendLine("            var status = builder." + appendMethod + "(degree, coefficient, coefficientOps);");
        sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return status;");
        sb.AppendLine("            result.SetTermCount(builder.Count);");
        sb.AppendLine("            return TryReduceInPlace(ref result);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryFromTerms(scoped global::System.ReadOnlySpan<int> degrees, scoped global::System.ReadOnlySpan<" + coefficientType + "> coefficients, out Element result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            if (!_hasModulus)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine("            if (degrees.Length != coefficients.Length)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine("            if (degrees.Length > TermCapacity)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InsufficientDestination;");
        sb.AppendLine("            var builder = new " + polynomialBuilderType + "(result.DegreeStorage, result.CoefficientStorage);");
        sb.AppendLine("            var coefficientOps = new " + coefficientOpsType + "();");
        sb.AppendLine("            for (var i = 0; i < degrees.Length; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                var status = builder." + appendMethod + "(degrees[i], coefficients[i], coefficientOps);");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                    return status;");
        sb.AppendLine("            }");
        sb.AppendLine("            result.SetTermCount(builder.Count);");
        sb.AppendLine("            return TryReduceInPlace(ref result);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryAdd(Element left, Element right, out Element result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            if (!_hasModulus)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine("            var builder = new " + quotientBuilderType + "(result.DegreeStorage, result.CoefficientStorage);");
        sb.AppendLine("            global::System.Span<int> workspaceDegrees = stackalloc int[WorkspaceCapacity * 4];");
        sb.AppendLine("            global::System.Span<" + coefficientType + "> workspaceCoefficients = stackalloc " + coefficientType + "[WorkspaceCapacity * 6];");
        sb.AppendLine("            var workspace = new global::HPD.Math.Algebra.PolynomialQuotientArithmeticWorkspace<" + coefficientType + ">(workspaceDegrees.Slice(0, WorkspaceCapacity), workspaceCoefficients.Slice(0, WorkspaceCapacity), workspaceDegrees.Slice(WorkspaceCapacity, WorkspaceCapacity), workspaceCoefficients.Slice(WorkspaceCapacity, WorkspaceCapacity), new global::HPD.Math.Algebra.PolynomialQuotientReductionWorkspace<" + coefficientType + ">(workspaceDegrees.Slice(WorkspaceCapacity * 2, WorkspaceCapacity), workspaceCoefficients.Slice(WorkspaceCapacity * 2, WorkspaceCapacity), workspaceDegrees.Slice(WorkspaceCapacity * 3, WorkspaceCapacity), workspaceCoefficients.Slice(WorkspaceCapacity * 3, WorkspaceCapacity), workspaceCoefficients.Slice(WorkspaceCapacity * 4, WorkspaceCapacity), workspaceCoefficients.Slice(WorkspaceCapacity * 5, WorkspaceCapacity)));");
        sb.AppendLine("            var modulus = _modulus;");
        sb.AppendLine("            var leftPolynomial = new " + polynomialViewType + "(new " + finsuppViewType + "(left.DegreeStorage[..left.TermCount], left.CoefficientStorage[..left.TermCount]));");
        sb.AppendLine("            var rightPolynomial = new " + polynomialViewType + "(new " + finsuppViewType + "(right.DegreeStorage[..right.TermCount], right.CoefficientStorage[..right.TermCount]));");
        sb.AppendLine("            var modulusPolynomial = new " + polynomialViewType + "(new " + finsuppViewType + "(modulus.DegreeStorage[..modulus.TermCount], modulus.CoefficientStorage[..modulus.TermCount]));");
        sb.AppendLine("            var leftView = new " + quotientViewType + "(leftPolynomial);");
        sb.AppendLine("            var rightView = new " + quotientViewType + "(rightPolynomial);");
        sb.AppendLine("            var status = global::HPD.Math.Algebra.PolynomialQuotientKernels." + addMethod + "(leftView, rightView, modulusPolynomial, ref builder, workspace, new " + coefficientOpsType + "());");
        sb.AppendLine("            if (status == global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                result.SetTermCount(builder.Representative.Count);");
        sb.AppendLine("            return status;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryMul(Element left, Element right, out Element result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            if (!_hasModulus)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine("            var builder = new " + quotientBuilderType + "(result.DegreeStorage, result.CoefficientStorage);");
        sb.AppendLine("            global::System.Span<int> workspaceDegrees = stackalloc int[WorkspaceCapacity * 4];");
        sb.AppendLine("            global::System.Span<" + coefficientType + "> workspaceCoefficients = stackalloc " + coefficientType + "[WorkspaceCapacity * 6];");
        sb.AppendLine("            var workspace = new global::HPD.Math.Algebra.PolynomialQuotientArithmeticWorkspace<" + coefficientType + ">(workspaceDegrees.Slice(0, WorkspaceCapacity), workspaceCoefficients.Slice(0, WorkspaceCapacity), workspaceDegrees.Slice(WorkspaceCapacity, WorkspaceCapacity), workspaceCoefficients.Slice(WorkspaceCapacity, WorkspaceCapacity), new global::HPD.Math.Algebra.PolynomialQuotientReductionWorkspace<" + coefficientType + ">(workspaceDegrees.Slice(WorkspaceCapacity * 2, WorkspaceCapacity), workspaceCoefficients.Slice(WorkspaceCapacity * 2, WorkspaceCapacity), workspaceDegrees.Slice(WorkspaceCapacity * 3, WorkspaceCapacity), workspaceCoefficients.Slice(WorkspaceCapacity * 3, WorkspaceCapacity), workspaceCoefficients.Slice(WorkspaceCapacity * 4, WorkspaceCapacity), workspaceCoefficients.Slice(WorkspaceCapacity * 5, WorkspaceCapacity)));");
        sb.AppendLine("            var modulus = _modulus;");
        sb.AppendLine("            var leftPolynomial = new " + polynomialViewType + "(new " + finsuppViewType + "(left.DegreeStorage[..left.TermCount], left.CoefficientStorage[..left.TermCount]));");
        sb.AppendLine("            var rightPolynomial = new " + polynomialViewType + "(new " + finsuppViewType + "(right.DegreeStorage[..right.TermCount], right.CoefficientStorage[..right.TermCount]));");
        sb.AppendLine("            var modulusPolynomial = new " + polynomialViewType + "(new " + finsuppViewType + "(modulus.DegreeStorage[..modulus.TermCount], modulus.CoefficientStorage[..modulus.TermCount]));");
        sb.AppendLine("            var leftView = new " + quotientViewType + "(leftPolynomial);");
        sb.AppendLine("            var rightView = new " + quotientViewType + "(rightPolynomial);");
        sb.AppendLine("            var status = global::HPD.Math.Algebra.PolynomialQuotientKernels." + mulMethod + "(leftView, rightView, modulusPolynomial, ref builder, workspace, new " + coefficientOpsType + "());");
        sb.AppendLine("            if (status == global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                result.SetTermCount(builder.Representative.Count);");
        sb.AppendLine("            return status;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private global::HPD.Math.Core.AlgebraStatus TryReduceInPlace(ref Element value)");
        sb.AppendLine("        {");
        sb.AppendLine("            var sourceElement = value;");
        sb.AppendLine("            var modulus = _modulus;");
        sb.AppendLine("            var source = new " + polynomialViewType + "(new " + finsuppViewType + "(sourceElement.DegreeStorage[..sourceElement.TermCount], sourceElement.CoefficientStorage[..sourceElement.TermCount]));");
        sb.AppendLine("            var modulusPolynomial = new " + polynomialViewType + "(new " + finsuppViewType + "(modulus.DegreeStorage[..modulus.TermCount], modulus.CoefficientStorage[..modulus.TermCount]));");
        sb.AppendLine("            var destination = new " + quotientBuilderType + "(value.DegreeStorage, value.CoefficientStorage);");
        sb.AppendLine("            global::System.Span<int> workspaceDegrees = stackalloc int[WorkspaceCapacity * 4];");
        sb.AppendLine("            global::System.Span<" + coefficientType + "> workspaceCoefficients = stackalloc " + coefficientType + "[WorkspaceCapacity * 6];");
        sb.AppendLine("            var workspace = new global::HPD.Math.Algebra.PolynomialQuotientReductionWorkspace<" + coefficientType + ">(workspaceDegrees.Slice(WorkspaceCapacity * 2, WorkspaceCapacity), workspaceCoefficients.Slice(WorkspaceCapacity * 2, WorkspaceCapacity), workspaceDegrees.Slice(WorkspaceCapacity * 3, WorkspaceCapacity), workspaceCoefficients.Slice(WorkspaceCapacity * 3, WorkspaceCapacity), workspaceCoefficients.Slice(WorkspaceCapacity * 4, WorkspaceCapacity), workspaceCoefficients.Slice(WorkspaceCapacity * 5, WorkspaceCapacity));");
        sb.AppendLine("            var status = global::HPD.Math.Algebra.PolynomialQuotientKernels." + reduceMethod + "(source, modulusPolynomial, ref destination, workspace, new " + coefficientOpsType + "());");
        sb.AppendLine("            if (status == global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                value.SetTermCount(destination.Representative.Count);");
        sb.AppendLine("            return status;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    [global::System.Runtime.CompilerServices.InlineArray(TermCapacity)]");
        sb.AppendLine("    private struct DegreeBuffer");
        sb.AppendLine("    {");
        sb.AppendLine("        private int _element0;");
        sb.AppendLine("        public global::System.Span<int> AsSpan() => global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref _element0, TermCapacity);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    [global::System.Runtime.CompilerServices.InlineArray(TermCapacity)]");
        sb.AppendLine("    private struct CoefficientBuffer");
        sb.AppendLine("    {");
        sb.AppendLine("        private " + coefficientType + " _element0;");
        sb.AppendLine("        public global::System.Span<" + coefficientType + "> AsSpan() => global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref _element0, TermCapacity);");
        sb.AppendLine("    }");
    }

    private static void GeneratePolynomialQuotientAuthoringSurface(
        StringBuilder sb,
        PolynomialQuotientScopeModel scope,
        bool emitRunner)
    {
        var coefficientType = scope.CoefficientType;
        var coefficientOpsType = scope.CoefficientOpsType;
        var appendMethod = scope.UsesStatusFieldOps ? "TryAppendTermStatus" : "TryAppendTerm";
        var validateContextMethod = scope.UsesStatusFieldOps ? "ValidateContextStatus" : "ValidateContext";
        var reduceMethod = scope.UsesStatusFieldOps ? "TryReduceStatus" : "TryReduce";
        var addMethod = scope.UsesStatusFieldOps ? "TryAddStatus" : "TryAdd";
        var mulMethod = scope.UsesStatusFieldOps ? "TryMulStatus" : "TryMul";
        var quotientViewType = "global::HPD.Math.Algebra.PolynomialQuotientView<" + coefficientType + ">";
        var quotientBuilderType = "global::HPD.Math.Algebra.PolynomialQuotientBuilder<" + coefficientType + ">";
        var polynomialViewType = "global::HPD.Math.Algebra.SparsePolynomialView<" + coefficientType + ">";
        var polynomialBuilderType = "global::HPD.Math.Algebra.SparsePolynomialBuilder<" + coefficientType + ">";
        var finsuppViewType = "global::HPD.Math.Finite.FinsuppView<int, " + coefficientType + ">";

        sb.AppendLine();
        sb.Append("    public const int TermCapacity = ").Append(scope.Terms).AppendLine(";");
        sb.Append("    public const int HandleCapacity = ").Append(scope.Handles).AppendLine(";");
        sb.Append("    public const int WorkspaceCapacity = ").Append(scope.Workspace).AppendLine(";");
        sb.AppendLine();
        if (emitRunner)
        {
            sb.AppendLine("    public global::HPD.Math.Core.AlgebraStatus Run(ref Result result)");
            sb.AppendLine("    {");
            sb.AppendLine("        result.Clear();");
            sb.AppendLine("        global::System.Span<int> degrees = stackalloc int[TermCapacity * HandleCapacity];");
            sb.Append("        global::System.Span<").Append(coefficientType).AppendLine("> coefficients = stackalloc " + coefficientType + "[TermCapacity * HandleCapacity];");
            sb.AppendLine("        global::System.Span<int> counts = stackalloc int[HandleCapacity];");
            sb.AppendLine("        global::System.Span<int> modulusDegrees = stackalloc int[TermCapacity];");
            sb.Append("        global::System.Span<").Append(coefficientType).AppendLine("> modulusCoefficients = stackalloc " + coefficientType + "[TermCapacity];");
            sb.AppendLine("        global::System.Span<int> state = stackalloc int[4];");
            sb.AppendLine("        global::System.Span<int> workspaceDegrees = stackalloc int[WorkspaceCapacity * 4];");
            sb.Append("        global::System.Span<").Append(coefficientType).AppendLine("> workspaceCoefficients = stackalloc " + coefficientType + "[WorkspaceCapacity * 6];");
            sb.AppendLine();
            sb.AppendLine("        var scope = new Scope(degrees, coefficients, counts, modulusDegrees, modulusCoefficients, state, workspaceDegrees, workspaceCoefficients);");
            sb.AppendLine("        Build(ref scope);");
            sb.AppendLine("        var status = scope.CopyReturned(result.DegreeStorage, result.CoefficientStorage, out var termCount);");
            sb.AppendLine("        if (scope.Status != global::HPD.Math.Core.AlgebraStatus.Ok)");
            sb.AppendLine("            return scope.Status;");
            sb.AppendLine("        if (status == global::HPD.Math.Core.AlgebraStatus.Ok)");
            sb.AppendLine("            result.SetTermCount(termCount);");
            sb.AppendLine("        return status;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    public global::HPD.Math.Core.AlgebraStatus Run()");
            sb.AppendLine("    {");
            sb.AppendLine("        global::System.Span<int> degrees = stackalloc int[TermCapacity * HandleCapacity];");
            sb.Append("        global::System.Span<").Append(coefficientType).AppendLine("> coefficients = stackalloc " + coefficientType + "[TermCapacity * HandleCapacity];");
            sb.AppendLine("        global::System.Span<int> counts = stackalloc int[HandleCapacity];");
            sb.AppendLine("        global::System.Span<int> modulusDegrees = stackalloc int[TermCapacity];");
            sb.Append("        global::System.Span<").Append(coefficientType).AppendLine("> modulusCoefficients = stackalloc " + coefficientType + "[TermCapacity];");
            sb.AppendLine("        global::System.Span<int> state = stackalloc int[4];");
            sb.AppendLine("        global::System.Span<int> workspaceDegrees = stackalloc int[WorkspaceCapacity * 4];");
            sb.Append("        global::System.Span<").Append(coefficientType).AppendLine("> workspaceCoefficients = stackalloc " + coefficientType + "[WorkspaceCapacity * 6];");
            sb.AppendLine();
            sb.AppendLine("        var scope = new Scope(degrees, coefficients, counts, modulusDegrees, modulusCoefficients, state, workspaceDegrees, workspaceCoefficients);");
            sb.AppendLine("        Build(ref scope);");
            sb.AppendLine("        return scope.Status;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    partial void Build(ref Scope q);");
            sb.AppendLine();
            sb.AppendLine("    public struct Result");
            sb.AppendLine("    {");
            sb.AppendLine("        private DegreeBuffer _degrees;");
            sb.AppendLine("        private CoefficientBuffer _coefficients;");
            sb.AppendLine("        public int TermCount { get; private set; }");
            sb.AppendLine("        public int DegreeAt(int index) => _degrees[index];");
            sb.Append("        public ").Append(coefficientType).AppendLine(" CoefficientAt(int index) => _coefficients[index];");
            sb.AppendLine("        internal global::System.Span<int> DegreeStorage => _degrees.AsSpan();");
            sb.Append("        internal global::System.Span<").Append(coefficientType).AppendLine("> CoefficientStorage => _coefficients.AsSpan();");
            sb.AppendLine("        internal void SetTermCount(int count) => TermCount = count;");
            sb.AppendLine("        internal void Clear() => TermCount = 0;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    [global::System.Runtime.CompilerServices.InlineArray(TermCapacity)]");
            sb.AppendLine("    private struct DegreeBuffer");
            sb.AppendLine("    {");
            sb.AppendLine("        private int _element0;");
            sb.AppendLine("        public global::System.Span<int> AsSpan() => global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref _element0, TermCapacity);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    [global::System.Runtime.CompilerServices.InlineArray(TermCapacity)]");
            sb.AppendLine("    private struct CoefficientBuffer");
            sb.AppendLine("    {");
            sb.Append("        private ").Append(coefficientType).AppendLine(" _element0;");
            sb.Append("        public global::System.Span<").Append(coefficientType).AppendLine("> AsSpan() => global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref _element0, TermCapacity);");
            sb.AppendLine("    }");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("    public static " + coefficientOpsType + " CreateOps() => new();");
            sb.AppendLine();
            sb.AppendLine("    public static Scope CreateScope(");
            sb.AppendLine("        global::System.Span<int> degrees,");
            sb.Append("        global::System.Span<").Append(coefficientType).AppendLine("> coefficients,");
            sb.AppendLine("        global::System.Span<int> counts,");
            sb.AppendLine("        global::System.Span<int> modulusDegrees,");
            sb.Append("        global::System.Span<").Append(coefficientType).AppendLine("> modulusCoefficients,");
            sb.AppendLine("        global::System.Span<int> state,");
            sb.AppendLine("        global::System.Span<int> workspaceDegrees,");
            sb.Append("        global::System.Span<").Append(coefficientType).AppendLine("> workspaceCoefficients) =>");
            sb.AppendLine("        new(degrees, coefficients, counts, modulusDegrees, modulusCoefficients, state, workspaceDegrees, workspaceCoefficients);");
            sb.AppendLine();
        }
        sb.AppendLine("    public ref struct Scope");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly global::System.Span<int> _degrees;");
        sb.Append("        private readonly global::System.Span<").Append(coefficientType).AppendLine("> _coefficients;");
        sb.AppendLine("        private readonly global::System.Span<int> _counts;");
        sb.AppendLine("        private readonly global::System.Span<int> _modulusDegrees;");
        sb.Append("        private readonly global::System.Span<").Append(coefficientType).AppendLine("> _modulusCoefficients;");
        sb.AppendLine("        private readonly global::System.Span<int> _state;");
        sb.AppendLine("        private readonly global::System.Span<int> _workspaceDegrees;");
        sb.Append("        private readonly global::System.Span<").Append(coefficientType).AppendLine("> _workspaceCoefficients;");
        sb.AppendLine();
        sb.AppendLine("        public Scope(");
        sb.AppendLine("            global::System.Span<int> degrees,");
        sb.Append("            global::System.Span<").Append(coefficientType).AppendLine("> coefficients,");
        sb.AppendLine("            global::System.Span<int> counts,");
        sb.AppendLine("            global::System.Span<int> modulusDegrees,");
        sb.Append("            global::System.Span<").Append(coefficientType).AppendLine("> modulusCoefficients,");
        sb.AppendLine("            global::System.Span<int> state,");
        sb.AppendLine("            global::System.Span<int> workspaceDegrees,");
        sb.Append("            global::System.Span<").Append(coefficientType).AppendLine("> workspaceCoefficients)");
        sb.AppendLine("        {");
        sb.AppendLine("            _degrees = degrees;");
        sb.AppendLine("            _coefficients = coefficients;");
        sb.AppendLine("            _counts = counts;");
        sb.AppendLine("            _modulusDegrees = modulusDegrees;");
        sb.AppendLine("            _modulusCoefficients = modulusCoefficients;");
        sb.AppendLine("            _state = state;");
        sb.AppendLine("            _workspaceDegrees = workspaceDegrees;");
        sb.AppendLine("            _workspaceCoefficients = workspaceCoefficients;");
        sb.AppendLine("            _state[0] = 0;");
        sb.AppendLine("            _state[1] = (int)global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("            _state[2] = -1;");
        sb.AppendLine("            _state[3] = 0;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public readonly global::HPD.Math.Core.AlgebraStatus Status => (global::HPD.Math.Core.AlgebraStatus)_state[1];");
        sb.AppendLine();
        sb.Append("        public global::HPD.Math.Core.AlgebraStatus SetModulus(scoped global::System.ReadOnlySpan<int> degrees, scoped global::System.ReadOnlySpan<")
            .Append(coefficientType).AppendLine("> coefficients)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (degrees.Length != coefficients.Length)");
        sb.AppendLine("                return FailAndReturn(global::HPD.Math.Core.AlgebraStatus.InvalidInput);");
        sb.AppendLine("            if (degrees.Length > TermCapacity)");
        sb.AppendLine("                return FailAndReturn(global::HPD.Math.Core.AlgebraStatus.InsufficientDestination);");
        sb.AppendLine();
        sb.AppendLine("            var builder = new " + polynomialBuilderType + "(_modulusDegrees, _modulusCoefficients);");
        sb.AppendLine("            builder.Clear();");
        sb.AppendLine("            var ops = new " + coefficientOpsType + "();");
        sb.AppendLine("            for (var i = 0; i < degrees.Length; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                var status = builder." + appendMethod + "(degrees[i], coefficients[i], ops);");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                    return FailAndReturn(status);");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            var contextStatus = global::HPD.Math.Algebra.PolynomialQuotientKernels." + validateContextMethod + "(builder.AsView(), ops);");
        sb.AppendLine("            if (contextStatus != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return FailAndReturn(contextStatus);");
        sb.AppendLine();
        sb.AppendLine("            _state[3] = builder.Count;");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus SetDefiningPolynomial(scoped global::System.ReadOnlySpan<int> degrees, scoped global::System.ReadOnlySpan<" + coefficientType + "> coefficients) => SetModulus(degrees, coefficients);");
        sb.AppendLine();
        sb.AppendLine("        public Element Generator() => Monomial(1, new " + coefficientOpsType + "().One);");
        if (coefficientType is not "int" and not "global::System.Int32")
        {
            sb.AppendLine("        public Element Const(int value)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (Status != global::HPD.Math.Core.AlgebraStatus.Ok)");
            sb.AppendLine("                return InvalidElement();");
            sb.AppendLine("            var status = new " + coefficientOpsType + "().TryFromInt(value, out var coefficient);");
            sb.AppendLine("            return status == global::HPD.Math.Core.AlgebraStatus.Ok ? Const(coefficient) : FailAndReturnElement(status);");
            sb.AppendLine("        }");
        }
        sb.Append("        public Element Const(").Append(coefficientType).AppendLine(" coefficient) => Monomial(0, coefficient);");
        sb.Append("        public Element Monomial(int degree, ").Append(coefficientType).AppendLine(" coefficient)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!TryAllocate(out var handle))");
        sb.AppendLine("                return InvalidElement();");
        sb.AppendLine("            var builder = Builder(handle);");
        sb.AppendLine("            var status = builder.Representative." + appendMethod + "(degree, coefficient, new " + coefficientOpsType + "());");
        sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return Complete(handle, builder.Representative.Count, status);");
        sb.AppendLine("            return Complete(handle, builder.Representative.Count, Reduce(handle));");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public void Return(Element value)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!IsValid(value.Handle))");
        sb.AppendLine("            {");
        sb.AppendLine("                Fail(global::HPD.Math.Core.AlgebraStatus.InvalidInput);");
        sb.AppendLine("                return;");
        sb.AppendLine("            }");
        sb.AppendLine("            _state[2] = value.Handle;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.Append("        public global::HPD.Math.Core.AlgebraStatus CopyReturned(global::System.Span<int> outputDegrees, global::System.Span<")
            .Append(coefficientType).AppendLine("> outputCoefficients, out int outputTermCount)");
        sb.AppendLine("        {");
        sb.AppendLine("            outputTermCount = 0;");
        sb.AppendLine("            if (Status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return Status;");
        sb.AppendLine("            if (!IsValid(_state[2]))");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine("            var count = _counts[_state[2]];");
        sb.AppendLine("            if (outputDegrees.Length < count || outputCoefficients.Length < count)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InsufficientDestination;");
        sb.AppendLine("            DegreeSlot(_state[2])[..count].CopyTo(outputDegrees);");
        sb.AppendLine("            CoefficientSlot(_state[2])[..count].CopyTo(outputCoefficients);");
        sb.AppendLine("            outputTermCount = count;");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private global::HPD.Math.Core.AlgebraStatus Reduce(int handle)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (_state[3] == 0)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine("            var source = View(handle).Representative;");
        sb.AppendLine("            var destination = Builder(handle);");
        sb.AppendLine("            var status = global::HPD.Math.Algebra.PolynomialQuotientKernels." + reduceMethod + "(source, Modulus(), ref destination, ReductionWorkspace(), new " + coefficientOpsType + "());");
        sb.AppendLine("            if (status == global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                _counts[handle] = destination.Representative.Count;");
        sb.AppendLine("            return status;");
        sb.AppendLine("        }");
        sb.AppendLine("        private global::HPD.Math.Core.AlgebraStatus FailAndReturn(global::HPD.Math.Core.AlgebraStatus status)");
        sb.AppendLine("        {");
        sb.AppendLine("            Fail(status);");
        sb.AppendLine("            return status;");
        sb.AppendLine("        }");
        sb.AppendLine("        private Element FailAndReturnElement(global::HPD.Math.Core.AlgebraStatus status)");
        sb.AppendLine("        {");
        sb.AppendLine("            Fail(status);");
        sb.AppendLine("            return InvalidElement();");
        sb.AppendLine("        }");
        sb.AppendLine("        private bool TryAllocate(out int handle)");
        sb.AppendLine("        {");
        sb.AppendLine("            handle = -1;");
        sb.AppendLine("            if (Status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return false;");
        sb.AppendLine("            if (_state[0] >= HandleCapacity)");
        sb.AppendLine("            {");
        sb.AppendLine("                Fail(global::HPD.Math.Core.AlgebraStatus.InsufficientDestination);");
        sb.AppendLine("                return false;");
        sb.AppendLine("            }");
        sb.AppendLine("            handle = _state[0];");
        sb.AppendLine("            _state[0]++;");
        sb.AppendLine("            _counts[handle] = 0;");
        sb.AppendLine("            return true;");
        sb.AppendLine("        }");
        sb.AppendLine("        private Element Complete(int handle, int count, global::HPD.Math.Core.AlgebraStatus status)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("            {");
        sb.AppendLine("                Fail(status);");
        sb.AppendLine("                return InvalidElement();");
        sb.AppendLine("            }");
        sb.AppendLine("            _counts[handle] = count;");
        sb.AppendLine("            return CreateElement(handle);");
        sb.AppendLine("        }");
        sb.AppendLine("        private bool IsValid(int handle) => handle >= 0 && handle < _state[0];");
        sb.AppendLine("        private void Fail(global::HPD.Math.Core.AlgebraStatus status)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (Status == global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                _state[1] = (int)status;");
        sb.AppendLine("        }");
        sb.AppendLine("        private Element InvalidElement() => CreateElement(-1);");
        sb.AppendLine("        private Element CreateElement(int handle) => new(_degrees, _coefficients, _counts, _modulusDegrees, _modulusCoefficients, _state, _workspaceDegrees, _workspaceCoefficients, handle);");
        sb.AppendLine("        private global::System.Span<int> DegreeSlot(int handle) => _degrees.Slice(handle * TermCapacity, TermCapacity);");
        sb.Append("        private global::System.Span<").Append(coefficientType).AppendLine("> CoefficientSlot(int handle) => _coefficients.Slice(handle * TermCapacity, TermCapacity);");
        sb.AppendLine("        private " + quotientBuilderType + " Builder(int handle) => new(DegreeSlot(handle), CoefficientSlot(handle));");
        sb.AppendLine("        private " + quotientViewType + " View(int handle) => new(new " + polynomialViewType + "(new " + finsuppViewType + "(DegreeSlot(handle)[.._counts[handle]], CoefficientSlot(handle)[.._counts[handle]])));");
        sb.AppendLine("        private " + polynomialViewType + " Modulus() => new(new " + finsuppViewType + "(_modulusDegrees[.._state[3]], _modulusCoefficients[.._state[3]]));");
        sb.AppendLine("        private global::HPD.Math.Algebra.PolynomialQuotientReductionWorkspace<" + coefficientType + "> ReductionWorkspace() => new(_workspaceDegrees.Slice(WorkspaceCapacity * 2, WorkspaceCapacity), _workspaceCoefficients.Slice(WorkspaceCapacity * 2, WorkspaceCapacity), _workspaceDegrees.Slice(WorkspaceCapacity * 3, WorkspaceCapacity), _workspaceCoefficients.Slice(WorkspaceCapacity * 3, WorkspaceCapacity), _workspaceCoefficients.Slice(WorkspaceCapacity * 4, WorkspaceCapacity), _workspaceCoefficients.Slice(WorkspaceCapacity * 5, WorkspaceCapacity));");
        sb.AppendLine("        private global::HPD.Math.Algebra.PolynomialQuotientArithmeticWorkspace<" + coefficientType + "> ArithmeticWorkspace() => new(_workspaceDegrees.Slice(0, WorkspaceCapacity), _workspaceCoefficients.Slice(0, WorkspaceCapacity), _workspaceDegrees.Slice(WorkspaceCapacity, WorkspaceCapacity), _workspaceCoefficients.Slice(WorkspaceCapacity, WorkspaceCapacity), ReductionWorkspace());");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public readonly ref struct Element");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly global::System.Span<int> _degrees;");
        sb.Append("        private readonly global::System.Span<").Append(coefficientType).AppendLine("> _coefficients;");
        sb.AppendLine("        private readonly global::System.Span<int> _counts;");
        sb.AppendLine("        private readonly global::System.Span<int> _modulusDegrees;");
        sb.Append("        private readonly global::System.Span<").Append(coefficientType).AppendLine("> _modulusCoefficients;");
        sb.AppendLine("        private readonly global::System.Span<int> _state;");
        sb.AppendLine("        private readonly global::System.Span<int> _workspaceDegrees;");
        sb.Append("        private readonly global::System.Span<").Append(coefficientType).AppendLine("> _workspaceCoefficients;");
        sb.AppendLine("        internal readonly int Handle;");
        sb.AppendLine("        internal Element(global::System.Span<int> degrees, global::System.Span<" + coefficientType + "> coefficients, global::System.Span<int> counts, global::System.Span<int> modulusDegrees, global::System.Span<" + coefficientType + "> modulusCoefficients, global::System.Span<int> state, global::System.Span<int> workspaceDegrees, global::System.Span<" + coefficientType + "> workspaceCoefficients, int handle)");
        sb.AppendLine("        {");
        sb.AppendLine("            _degrees = degrees; _coefficients = coefficients; _counts = counts; _modulusDegrees = modulusDegrees; _modulusCoefficients = modulusCoefficients; _state = state; _workspaceDegrees = workspaceDegrees; _workspaceCoefficients = workspaceCoefficients; Handle = handle;");
        sb.AppendLine("        }");
        sb.AppendLine("        public int TermCount => IsValid(Handle) ? _counts[Handle] : 0;");
        sb.AppendLine("        public int DegreeAt(int supportIndex) => DegreeSlot(Handle)[supportIndex];");
        sb.AppendLine("        public " + coefficientType + " CoefficientAt(int supportIndex) => CoefficientSlot(Handle)[supportIndex];");
        sb.AppendLine("        public Element Add(Element other) => Binary(other, true);");
        sb.AppendLine("        public Element Mul(Element other) => Binary(other, false);");
        sb.AppendLine("        private Element Binary(Element other, bool add)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!CanOperate(other, out var handle))");
        sb.AppendLine("                return InvalidElement();");
        sb.AppendLine("            var destination = Builder(handle);");
        sb.AppendLine("            var status = add");
        sb.AppendLine("                ? global::HPD.Math.Algebra.PolynomialQuotientKernels." + addMethod + "(View(Handle), other.View(other.Handle), Modulus(), ref destination, ArithmeticWorkspace(), new " + coefficientOpsType + "())");
        sb.AppendLine("                : global::HPD.Math.Algebra.PolynomialQuotientKernels." + mulMethod + "(View(Handle), other.View(other.Handle), Modulus(), ref destination, ArithmeticWorkspace(), new " + coefficientOpsType + "());");
        sb.AppendLine("            return Complete(handle, destination.Representative.Count, status);");
        sb.AppendLine("        }");
        sb.AppendLine("        private bool CanOperate(Element other, out int handle)");
        sb.AppendLine("        {");
        sb.AppendLine("            handle = -1;");
        sb.AppendLine("            if (Status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return false;");
        sb.AppendLine("            if (_state[3] == 0 || !IsValid(Handle) || !IsValid(other.Handle))");
        sb.AppendLine("                return Fail(global::HPD.Math.Core.AlgebraStatus.InvalidInput);");
        sb.AppendLine("            return TryAllocate(out handle);");
        sb.AppendLine("        }");
        sb.AppendLine("        private bool TryAllocate(out int handle)");
        sb.AppendLine("        {");
        sb.AppendLine("            handle = -1;");
        sb.AppendLine("            if (_state[0] >= HandleCapacity)");
        sb.AppendLine("                return Fail(global::HPD.Math.Core.AlgebraStatus.InsufficientDestination);");
        sb.AppendLine("            handle = _state[0];");
        sb.AppendLine("            _state[0]++;");
        sb.AppendLine("            _counts[handle] = 0;");
        sb.AppendLine("            return true;");
        sb.AppendLine("        }");
        sb.AppendLine("        private Element Complete(int handle, int count, global::HPD.Math.Core.AlgebraStatus status)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("            {");
        sb.AppendLine("                Fail(status);");
        sb.AppendLine("                return InvalidElement();");
        sb.AppendLine("            }");
        sb.AppendLine("            _counts[handle] = count;");
        sb.AppendLine("            return new Element(_degrees, _coefficients, _counts, _modulusDegrees, _modulusCoefficients, _state, _workspaceDegrees, _workspaceCoefficients, handle);");
        sb.AppendLine("        }");
        sb.AppendLine("        private global::HPD.Math.Core.AlgebraStatus Status => (global::HPD.Math.Core.AlgebraStatus)_state[1];");
        sb.AppendLine("        private bool IsValid(int handle) => handle >= 0 && handle < _state[0];");
        sb.AppendLine("        private bool Fail(global::HPD.Math.Core.AlgebraStatus status) { if (Status == global::HPD.Math.Core.AlgebraStatus.Ok) _state[1] = (int)status; return false; }");
        sb.AppendLine("        private Element InvalidElement() => new(_degrees, _coefficients, _counts, _modulusDegrees, _modulusCoefficients, _state, _workspaceDegrees, _workspaceCoefficients, -1);");
        sb.AppendLine("        private global::System.Span<int> DegreeSlot(int handle) => _degrees.Slice(handle * TermCapacity, TermCapacity);");
        sb.Append("        private global::System.Span<").Append(coefficientType).AppendLine("> CoefficientSlot(int handle) => _coefficients.Slice(handle * TermCapacity, TermCapacity);");
        sb.AppendLine("        private " + quotientBuilderType + " Builder(int handle) => new(DegreeSlot(handle), CoefficientSlot(handle));");
        sb.AppendLine("        private " + quotientViewType + " View(int handle) => new(new " + polynomialViewType + "(new " + finsuppViewType + "(DegreeSlot(handle)[.._counts[handle]], CoefficientSlot(handle)[.._counts[handle]])));");
        sb.AppendLine("        private " + polynomialViewType + " Modulus() => new(new " + finsuppViewType + "(_modulusDegrees[.._state[3]], _modulusCoefficients[.._state[3]]));");
        sb.AppendLine("        private global::HPD.Math.Algebra.PolynomialQuotientReductionWorkspace<" + coefficientType + "> ReductionWorkspace() => new(_workspaceDegrees.Slice(WorkspaceCapacity * 2, WorkspaceCapacity), _workspaceCoefficients.Slice(WorkspaceCapacity * 2, WorkspaceCapacity), _workspaceDegrees.Slice(WorkspaceCapacity * 3, WorkspaceCapacity), _workspaceCoefficients.Slice(WorkspaceCapacity * 3, WorkspaceCapacity), _workspaceCoefficients.Slice(WorkspaceCapacity * 4, WorkspaceCapacity), _workspaceCoefficients.Slice(WorkspaceCapacity * 5, WorkspaceCapacity));");
        sb.AppendLine("        private global::HPD.Math.Algebra.PolynomialQuotientArithmeticWorkspace<" + coefficientType + "> ArithmeticWorkspace() => new(_workspaceDegrees.Slice(0, WorkspaceCapacity), _workspaceCoefficients.Slice(0, WorkspaceCapacity), _workspaceDegrees.Slice(WorkspaceCapacity, WorkspaceCapacity), _workspaceCoefficients.Slice(WorkspaceCapacity, WorkspaceCapacity), ReductionWorkspace());");
        sb.AppendLine("    }");
    }

    private static void GenerateRationalFunctionScope(StringBuilder sb, RationalFunctionScopeModel scope)
    {
        GenerateRationalFunctionAuthoringSurface(sb, scope, emitRunner: true);
    }

    private static void GenerateRationalFunctionContext(StringBuilder sb, RationalFunctionScopeModel context)
    {
        GenerateRationalFunctionValueContext(sb, context);
    }

    private static void GenerateRationalFunctionValueContext(StringBuilder sb, RationalFunctionScopeModel context)
    {
        var coefficientType = context.CoefficientType;
        var coefficientOpsType = context.CoefficientOpsType;
        var rationalBuilderType = "global::HPD.Math.Algebra.RationalFunctionBuilder<" + coefficientType + ">";
        var rationalViewType = "global::HPD.Math.Algebra.RationalFunctionView<" + coefficientType + ">";
        var polynomialViewType = "global::HPD.Math.Algebra.SparsePolynomialView<" + coefficientType + ">";
        var polynomialBuilderType = "global::HPD.Math.Algebra.SparsePolynomialBuilder<" + coefficientType + ">";
        var finsuppViewType = "global::HPD.Math.Finite.FinsuppView<int, " + coefficientType + ">";

        sb.AppendLine();
        sb.Append("    public const int TermCapacity = ").Append(context.Terms).AppendLine(";");
        sb.Append("    public const int WorkspaceCapacity = ").Append(context.Workspace).AppendLine(";");
        sb.AppendLine();
        sb.AppendLine("    public static Ops CreateOps() => default;");
        sb.AppendLine();
        sb.AppendLine("    public struct Value : global::System.IEquatable<Value>");
        sb.AppendLine("    {");
        sb.AppendLine("        private DegreeBuffer _numeratorDegrees;");
        sb.AppendLine("        private CoefficientBuffer _numeratorCoefficients;");
        sb.AppendLine("        private DegreeBuffer _denominatorDegrees;");
        sb.AppendLine("        private CoefficientBuffer _denominatorCoefficients;");
        sb.AppendLine("        private int _numeratorTermCount;");
        sb.AppendLine("        private int _denominatorTermCount;");
        sb.AppendLine();
        sb.AppendLine("        public int NumeratorTermCount => _numeratorTermCount;");
        sb.AppendLine("        public int DenominatorTermCount => _denominatorTermCount;");
        sb.AppendLine("        public int NumeratorDegreeAt(int index) => _numeratorDegrees[index];");
        sb.AppendLine("        public " + coefficientType + " NumeratorCoefficientAt(int index) => _numeratorCoefficients[index];");
        sb.AppendLine("        public int DenominatorDegreeAt(int index) => _denominatorDegrees[index];");
        sb.AppendLine("        public " + coefficientType + " DenominatorCoefficientAt(int index) => _denominatorCoefficients[index];");
        sb.AppendLine("        internal global::System.Span<int> NumeratorDegreeStorage => _numeratorDegrees.AsSpan();");
        sb.AppendLine("        internal global::System.Span<" + coefficientType + "> NumeratorCoefficientStorage => _numeratorCoefficients.AsSpan();");
        sb.AppendLine("        internal global::System.Span<int> DenominatorDegreeStorage => _denominatorDegrees.AsSpan();");
        sb.AppendLine("        internal global::System.Span<" + coefficientType + "> DenominatorCoefficientStorage => _denominatorCoefficients.AsSpan();");
        sb.AppendLine("        internal void SetTermCounts(int numeratorTermCount, int denominatorTermCount)");
        sb.AppendLine("        {");
        sb.AppendLine("            _numeratorTermCount = numeratorTermCount;");
        sb.AppendLine("            _denominatorTermCount = denominatorTermCount;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public bool Equals(Value other)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (_numeratorTermCount != other._numeratorTermCount || _denominatorTermCount != other._denominatorTermCount)");
        sb.AppendLine("                return false;");
        sb.AppendLine("            var ops = new " + coefficientOpsType + "();");
        sb.AppendLine("            for (var i = 0; i < _numeratorTermCount; i++)");
        sb.AppendLine("                if (_numeratorDegrees[i] != other._numeratorDegrees[i] || !ops.Eq(_numeratorCoefficients[i], other._numeratorCoefficients[i]))");
        sb.AppendLine("                    return false;");
        sb.AppendLine("            for (var i = 0; i < _denominatorTermCount; i++)");
        sb.AppendLine("                if (_denominatorDegrees[i] != other._denominatorDegrees[i] || !ops.Eq(_denominatorCoefficients[i], other._denominatorCoefficients[i]))");
        sb.AppendLine("                    return false;");
        sb.AppendLine("            return true;");
        sb.AppendLine("        }");
        sb.AppendLine("        public override bool Equals(object? obj) => obj is Value other && Equals(other);");
        sb.AppendLine("        public override int GetHashCode()");
        sb.AppendLine("        {");
        sb.AppendLine("            var hash = new global::System.HashCode();");
        sb.AppendLine("            for (var i = 0; i < _numeratorTermCount; i++) { hash.Add(_numeratorDegrees[i]); hash.Add(_numeratorCoefficients[i]); }");
        sb.AppendLine("            for (var i = 0; i < _denominatorTermCount; i++) { hash.Add(_denominatorDegrees[i]); hash.Add(_denominatorCoefficients[i]); }");
        sb.AppendLine("            return hash.ToHashCode();");
        sb.AppendLine("        }");
        sb.AppendLine("        public static bool operator ==(Value left, Value right) => left.Equals(right);");
        sb.AppendLine("        public static bool operator !=(Value left, Value right) => !left.Equals(right);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public readonly struct Ops");
        sb.AppendLine("    {");
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryVariable(out Value result) =>");
        sb.AppendLine("            TryMonomial(1, new " + coefficientOpsType + "().One, out result);");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryConst(" + coefficientType + " coefficient, out Value result) =>");
        sb.AppendLine("            TryMonomial(0, coefficient, out result);");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryMonomial(int degree, " + coefficientType + " coefficient, out Value result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            var coefficientOps = new " + coefficientOpsType + "();");
        sb.AppendLine("            var builder = new " + rationalBuilderType + "(result.NumeratorDegreeStorage, result.NumeratorCoefficientStorage, result.DenominatorDegreeStorage, result.DenominatorCoefficientStorage);");
        sb.AppendLine("            var status = builder.Numerator.TryAppendTerm(degree, coefficient, coefficientOps);");
        sb.AppendLine("            if (status == global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                status = global::HPD.Math.Algebra.SparsePolynomialKernels.TryMonomial(0, coefficientOps.One, ref builder.Denominator, coefficientOps);");
        sb.AppendLine("            if (status == global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                result.SetTermCounts(builder.Numerator.Count, builder.Denominator.Count);");
        sb.AppendLine("            return status;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryFromPolynomials(scoped global::System.ReadOnlySpan<int> numeratorDegrees, scoped global::System.ReadOnlySpan<" + coefficientType + "> numeratorCoefficients, scoped global::System.ReadOnlySpan<int> denominatorDegrees, scoped global::System.ReadOnlySpan<" + coefficientType + "> denominatorCoefficients, out Value result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            if (numeratorDegrees.Length != numeratorCoefficients.Length || denominatorDegrees.Length != denominatorCoefficients.Length)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine("            if (numeratorDegrees.Length > TermCapacity || denominatorDegrees.Length > TermCapacity)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InsufficientDestination;");
        sb.AppendLine("            var coefficientOps = new " + coefficientOpsType + "();");
        sb.AppendLine("            var builder = new " + rationalBuilderType + "(result.NumeratorDegreeStorage, result.NumeratorCoefficientStorage, result.DenominatorDegreeStorage, result.DenominatorCoefficientStorage);");
        sb.AppendLine("            for (var i = 0; i < numeratorDegrees.Length; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                var status = builder.Numerator.TryAppendTerm(numeratorDegrees[i], numeratorCoefficients[i], coefficientOps);");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                    return status;");
        sb.AppendLine("            }");
        sb.AppendLine("            for (var i = 0; i < denominatorDegrees.Length; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                var status = builder.Denominator.TryAppendTerm(denominatorDegrees[i], denominatorCoefficients[i], coefficientOps);");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                    return status;");
        sb.AppendLine("            }");
        sb.AppendLine("            result.SetTermCounts(builder.Numerator.Count, builder.Denominator.Count);");
        sb.AppendLine("            var view = new " + rationalViewType + "(new " + polynomialViewType + "(new " + finsuppViewType + "(result.NumeratorDegreeStorage[..result.NumeratorTermCount], result.NumeratorCoefficientStorage[..result.NumeratorTermCount])), new " + polynomialViewType + "(new " + finsuppViewType + "(result.DenominatorDegreeStorage[..result.DenominatorTermCount], result.DenominatorCoefficientStorage[..result.DenominatorTermCount])));");
        sb.AppendLine("            return global::HPD.Math.Algebra.RationalFunctionKernels.Validate(view, coefficientOps);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryNormalize(Value value, out Value result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            var source = value;");
        sb.AppendLine("            var sourceView = new " + rationalViewType + "(new " + polynomialViewType + "(new " + finsuppViewType + "(source.NumeratorDegreeStorage[..source.NumeratorTermCount], source.NumeratorCoefficientStorage[..source.NumeratorTermCount])), new " + polynomialViewType + "(new " + finsuppViewType + "(source.DenominatorDegreeStorage[..source.DenominatorTermCount], source.DenominatorCoefficientStorage[..source.DenominatorTermCount])));");
        sb.AppendLine("            var destination = new " + rationalBuilderType + "(result.NumeratorDegreeStorage, result.NumeratorCoefficientStorage, result.DenominatorDegreeStorage, result.DenominatorCoefficientStorage);");
        sb.AppendLine("            global::System.Span<int> workspaceDegrees = stackalloc int[WorkspaceCapacity * 5];");
        sb.AppendLine("            global::System.Span<" + coefficientType + "> workspaceCoefficients = stackalloc " + coefficientType + "[WorkspaceCapacity * 8];");
        sb.AppendLine("            var workspace = new global::HPD.Math.Algebra.RationalFunctionNormalizationWorkspace<" + coefficientType + ">(workspaceDegrees.Slice(0, WorkspaceCapacity), workspaceCoefficients.Slice(0, WorkspaceCapacity), workspaceDegrees.Slice(WorkspaceCapacity, WorkspaceCapacity), workspaceCoefficients.Slice(WorkspaceCapacity, WorkspaceCapacity), workspaceDegrees.Slice(WorkspaceCapacity * 2, WorkspaceCapacity), workspaceCoefficients.Slice(WorkspaceCapacity * 2, WorkspaceCapacity), workspaceCoefficients.Slice(WorkspaceCapacity * 3, WorkspaceCapacity), workspaceCoefficients.Slice(WorkspaceCapacity * 4, WorkspaceCapacity), workspaceCoefficients.Slice(WorkspaceCapacity * 5, WorkspaceCapacity), workspaceCoefficients.Slice(WorkspaceCapacity * 6, WorkspaceCapacity), workspaceCoefficients.Slice(WorkspaceCapacity * 7, WorkspaceCapacity));");
        sb.AppendLine("            var status = global::HPD.Math.Algebra.RationalFunctionKernels.TryNormalize(sourceView, ref destination, workspace, new " + coefficientOpsType + "());");
        sb.AppendLine("            if (status == global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                result.SetTermCounts(destination.Numerator.Count, destination.Denominator.Count);");
        sb.AppendLine("            return status;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryMul(Value left, Value right, out Value result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            var leftValue = left;");
        sb.AppendLine("            var rightValue = right;");
        sb.AppendLine("            var leftView = new " + rationalViewType + "(new " + polynomialViewType + "(new " + finsuppViewType + "(leftValue.NumeratorDegreeStorage[..leftValue.NumeratorTermCount], leftValue.NumeratorCoefficientStorage[..leftValue.NumeratorTermCount])), new " + polynomialViewType + "(new " + finsuppViewType + "(leftValue.DenominatorDegreeStorage[..leftValue.DenominatorTermCount], leftValue.DenominatorCoefficientStorage[..leftValue.DenominatorTermCount])));");
        sb.AppendLine("            var rightView = new " + rationalViewType + "(new " + polynomialViewType + "(new " + finsuppViewType + "(rightValue.NumeratorDegreeStorage[..rightValue.NumeratorTermCount], rightValue.NumeratorCoefficientStorage[..rightValue.NumeratorTermCount])), new " + polynomialViewType + "(new " + finsuppViewType + "(rightValue.DenominatorDegreeStorage[..rightValue.DenominatorTermCount], rightValue.DenominatorCoefficientStorage[..rightValue.DenominatorTermCount])));");
        sb.AppendLine("            var destination = new " + rationalBuilderType + "(result.NumeratorDegreeStorage, result.NumeratorCoefficientStorage, result.DenominatorDegreeStorage, result.DenominatorCoefficientStorage);");
        sb.AppendLine("            global::System.Span<int> workspaceDegrees = stackalloc int[WorkspaceCapacity];");
        sb.AppendLine("            global::System.Span<" + coefficientType + "> workspaceCoefficients = stackalloc " + coefficientType + "[WorkspaceCapacity];");
        sb.AppendLine("            var status = global::HPD.Math.Algebra.RationalFunctionKernels.TryMul(leftView, rightView, ref destination, workspaceDegrees, workspaceCoefficients, new " + coefficientOpsType + "());");
        sb.AppendLine("            if (status == global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                result.SetTermCounts(destination.Numerator.Count, destination.Denominator.Count);");
        sb.AppendLine("            return status;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    [global::System.Runtime.CompilerServices.InlineArray(TermCapacity)]");
        sb.AppendLine("    private struct DegreeBuffer");
        sb.AppendLine("    {");
        sb.AppendLine("        private int _element0;");
        sb.AppendLine("        public global::System.Span<int> AsSpan() => global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref _element0, TermCapacity);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    [global::System.Runtime.CompilerServices.InlineArray(TermCapacity)]");
        sb.AppendLine("    private struct CoefficientBuffer");
        sb.AppendLine("    {");
        sb.AppendLine("        private " + coefficientType + " _element0;");
        sb.AppendLine("        public global::System.Span<" + coefficientType + "> AsSpan() => global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref _element0, TermCapacity);");
        sb.AppendLine("    }");
    }

    private static void GenerateRationalFunctionAuthoringSurface(
        StringBuilder sb,
        RationalFunctionScopeModel scope,
        bool emitRunner)
    {
        var coefficientType = scope.CoefficientType;
        var coefficientOpsType = scope.CoefficientOpsType;
        var rationalBuilderType = "global::HPD.Math.Algebra.RationalFunctionBuilder<" + coefficientType + ">";
        var rationalViewType = "global::HPD.Math.Algebra.RationalFunctionView<" + coefficientType + ">";
        var polynomialViewType = "global::HPD.Math.Algebra.SparsePolynomialView<" + coefficientType + ">";
        var polynomialBuilderType = "global::HPD.Math.Algebra.SparsePolynomialBuilder<" + coefficientType + ">";
        var finsuppViewType = "global::HPD.Math.Finite.FinsuppView<int, " + coefficientType + ">";

        sb.AppendLine();
        sb.Append("    public const int TermCapacity = ").Append(scope.Terms).AppendLine(";");
        sb.Append("    public const int HandleCapacity = ").Append(scope.Handles).AppendLine(";");
        sb.Append("    public const int WorkspaceCapacity = ").Append(scope.Workspace).AppendLine(";");
        sb.AppendLine();
        if (emitRunner)
        {
            sb.AppendLine("    public global::HPD.Math.Core.AlgebraStatus Run(ref Result result)");
            sb.AppendLine("    {");
            sb.AppendLine("        result.Clear();");
            sb.AppendLine("        global::System.Span<int> numeratorDegrees = stackalloc int[TermCapacity * HandleCapacity];");
            sb.Append("        global::System.Span<").Append(coefficientType).AppendLine("> numeratorCoefficients = stackalloc " + coefficientType + "[TermCapacity * HandleCapacity];");
            sb.AppendLine("        global::System.Span<int> denominatorDegrees = stackalloc int[TermCapacity * HandleCapacity];");
            sb.Append("        global::System.Span<").Append(coefficientType).AppendLine("> denominatorCoefficients = stackalloc " + coefficientType + "[TermCapacity * HandleCapacity];");
            sb.AppendLine("        global::System.Span<int> numeratorCounts = stackalloc int[HandleCapacity];");
            sb.AppendLine("        global::System.Span<int> denominatorCounts = stackalloc int[HandleCapacity];");
            sb.AppendLine("        global::System.Span<int> state = stackalloc int[3];");
            sb.AppendLine("        global::System.Span<int> workspaceDegrees = stackalloc int[WorkspaceCapacity * 5];");
            sb.Append("        global::System.Span<").Append(coefficientType).AppendLine("> workspaceCoefficients = stackalloc " + coefficientType + "[WorkspaceCapacity * 8];");
            sb.AppendLine();
            sb.AppendLine("        var scope = new Scope(numeratorDegrees, numeratorCoefficients, denominatorDegrees, denominatorCoefficients, numeratorCounts, denominatorCounts, state, workspaceDegrees, workspaceCoefficients);");
            sb.AppendLine("        Build(ref scope);");
            sb.AppendLine("        var status = scope.CopyReturned(result.NumeratorDegreeStorage, result.NumeratorCoefficientStorage, result.DenominatorDegreeStorage, result.DenominatorCoefficientStorage, out var numeratorTermCount, out var denominatorTermCount);");
            sb.AppendLine("        if (scope.Status != global::HPD.Math.Core.AlgebraStatus.Ok)");
            sb.AppendLine("            return scope.Status;");
            sb.AppendLine("        if (status == global::HPD.Math.Core.AlgebraStatus.Ok)");
            sb.AppendLine("            result.SetTermCounts(numeratorTermCount, denominatorTermCount);");
            sb.AppendLine("        return status;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    partial void Build(ref Scope r);");
            sb.AppendLine();
            sb.AppendLine("    public struct Result");
            sb.AppendLine("    {");
            sb.AppendLine("        private DegreeBuffer _numeratorDegrees;");
            sb.AppendLine("        private CoefficientBuffer _numeratorCoefficients;");
            sb.AppendLine("        private DegreeBuffer _denominatorDegrees;");
            sb.AppendLine("        private CoefficientBuffer _denominatorCoefficients;");
            sb.AppendLine("        public int NumeratorTermCount { get; private set; }");
            sb.AppendLine("        public int DenominatorTermCount { get; private set; }");
            sb.AppendLine("        public int NumeratorDegreeAt(int index) => _numeratorDegrees[index];");
            sb.Append("        public ").Append(coefficientType).AppendLine(" NumeratorCoefficientAt(int index) => _numeratorCoefficients[index];");
            sb.AppendLine("        public int DenominatorDegreeAt(int index) => _denominatorDegrees[index];");
            sb.Append("        public ").Append(coefficientType).AppendLine(" DenominatorCoefficientAt(int index) => _denominatorCoefficients[index];");
            sb.AppendLine("        internal global::System.Span<int> NumeratorDegreeStorage => _numeratorDegrees.AsSpan();");
            sb.Append("        internal global::System.Span<").Append(coefficientType).AppendLine("> NumeratorCoefficientStorage => _numeratorCoefficients.AsSpan();");
            sb.AppendLine("        internal global::System.Span<int> DenominatorDegreeStorage => _denominatorDegrees.AsSpan();");
            sb.Append("        internal global::System.Span<").Append(coefficientType).AppendLine("> DenominatorCoefficientStorage => _denominatorCoefficients.AsSpan();");
            sb.AppendLine("        internal void SetTermCounts(int numeratorTermCount, int denominatorTermCount) { NumeratorTermCount = numeratorTermCount; DenominatorTermCount = denominatorTermCount; }");
            sb.AppendLine("        internal void Clear() { NumeratorTermCount = 0; DenominatorTermCount = 0; }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    [global::System.Runtime.CompilerServices.InlineArray(TermCapacity)]");
            sb.AppendLine("    private struct DegreeBuffer");
            sb.AppendLine("    {");
            sb.AppendLine("        private int _element0;");
            sb.AppendLine("        public global::System.Span<int> AsSpan() => global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref _element0, TermCapacity);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    [global::System.Runtime.CompilerServices.InlineArray(TermCapacity)]");
            sb.AppendLine("    private struct CoefficientBuffer");
            sb.AppendLine("    {");
            sb.Append("        private ").Append(coefficientType).AppendLine(" _element0;");
            sb.Append("        public global::System.Span<").Append(coefficientType).AppendLine("> AsSpan() => global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref _element0, TermCapacity);");
            sb.AppendLine("    }");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("    public static " + coefficientOpsType + " CreateOps() => new();");
            sb.AppendLine();
            sb.AppendLine("    public static Scope CreateScope(");
            sb.AppendLine("        global::System.Span<int> numeratorDegrees,");
            sb.Append("        global::System.Span<").Append(coefficientType).AppendLine("> numeratorCoefficients,");
            sb.AppendLine("        global::System.Span<int> denominatorDegrees,");
            sb.Append("        global::System.Span<").Append(coefficientType).AppendLine("> denominatorCoefficients,");
            sb.AppendLine("        global::System.Span<int> numeratorCounts,");
            sb.AppendLine("        global::System.Span<int> denominatorCounts,");
            sb.AppendLine("        global::System.Span<int> state,");
            sb.AppendLine("        global::System.Span<int> workspaceDegrees,");
            sb.Append("        global::System.Span<").Append(coefficientType).AppendLine("> workspaceCoefficients) =>");
            sb.AppendLine("        new(numeratorDegrees, numeratorCoefficients, denominatorDegrees, denominatorCoefficients, numeratorCounts, denominatorCounts, state, workspaceDegrees, workspaceCoefficients);");
            sb.AppendLine();
        }
        sb.AppendLine("    public ref struct Scope");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly global::System.Span<int> _numeratorDegrees;");
        sb.Append("        private readonly global::System.Span<").Append(coefficientType).AppendLine("> _numeratorCoefficients;");
        sb.AppendLine("        private readonly global::System.Span<int> _denominatorDegrees;");
        sb.Append("        private readonly global::System.Span<").Append(coefficientType).AppendLine("> _denominatorCoefficients;");
        sb.AppendLine("        private readonly global::System.Span<int> _numeratorCounts;");
        sb.AppendLine("        private readonly global::System.Span<int> _denominatorCounts;");
        sb.AppendLine("        private readonly global::System.Span<int> _state;");
        sb.AppendLine("        private readonly global::System.Span<int> _workspaceDegrees;");
        sb.Append("        private readonly global::System.Span<").Append(coefficientType).AppendLine("> _workspaceCoefficients;");
        sb.AppendLine();
        sb.AppendLine("        public Scope(global::System.Span<int> numeratorDegrees, global::System.Span<" + coefficientType + "> numeratorCoefficients, global::System.Span<int> denominatorDegrees, global::System.Span<" + coefficientType + "> denominatorCoefficients, global::System.Span<int> numeratorCounts, global::System.Span<int> denominatorCounts, global::System.Span<int> state, global::System.Span<int> workspaceDegrees, global::System.Span<" + coefficientType + "> workspaceCoefficients)");
        sb.AppendLine("        {");
        sb.AppendLine("            _numeratorDegrees = numeratorDegrees; _numeratorCoefficients = numeratorCoefficients; _denominatorDegrees = denominatorDegrees; _denominatorCoefficients = denominatorCoefficients; _numeratorCounts = numeratorCounts; _denominatorCounts = denominatorCounts; _state = state; _workspaceDegrees = workspaceDegrees; _workspaceCoefficients = workspaceCoefficients;");
        sb.AppendLine("            _state[0] = 0; _state[1] = (int)global::HPD.Math.Core.AlgebraStatus.Ok; _state[2] = -1;");
        sb.AppendLine("        }");
        sb.AppendLine("        public readonly global::HPD.Math.Core.AlgebraStatus Status => (global::HPD.Math.Core.AlgebraStatus)_state[1];");
        sb.AppendLine("        public Value Variable() => Monomial(1, new " + coefficientOpsType + "().One);");
        if (coefficientType is not "int" and not "global::System.Int32")
        {
            sb.AppendLine("        public Value Const(int value)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (Status != global::HPD.Math.Core.AlgebraStatus.Ok) return InvalidValue();");
            sb.AppendLine("            var status = new " + coefficientOpsType + "().TryFromInt(value, out var coefficient);");
            sb.AppendLine("            return status == global::HPD.Math.Core.AlgebraStatus.Ok ? Const(coefficient) : FailAndReturnValue(status);");
            sb.AppendLine("        }");
        }
        sb.Append("        public Value Const(").Append(coefficientType).AppendLine(" coefficient) => Monomial(0, coefficient);");
        sb.Append("        public Value Monomial(int degree, ").Append(coefficientType).AppendLine(" coefficient)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!TryAllocate(out var handle)) return InvalidValue();");
        sb.AppendLine("            var builder = Builder(handle);");
        sb.AppendLine("            var ops = new " + coefficientOpsType + "();");
        sb.AppendLine("            var status = builder.Numerator.TryAppendTerm(degree, coefficient, ops);");
        sb.AppendLine("            if (status == global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                status = global::HPD.Math.Algebra.SparsePolynomialKernels.TryMonomial(0, ops.One, ref builder.Denominator, ops);");
        sb.AppendLine("            return Complete(handle, builder.Numerator.Count, builder.Denominator.Count, status);");
        sb.AppendLine("        }");
        sb.AppendLine("        public Value FromPolynomials(scoped global::System.ReadOnlySpan<int> numeratorDegrees, scoped global::System.ReadOnlySpan<" + coefficientType + "> numeratorCoefficients, scoped global::System.ReadOnlySpan<int> denominatorDegrees, scoped global::System.ReadOnlySpan<" + coefficientType + "> denominatorCoefficients)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!TryAllocate(out var handle)) return InvalidValue();");
        sb.AppendLine("            if (numeratorDegrees.Length != numeratorCoefficients.Length || denominatorDegrees.Length != denominatorCoefficients.Length)");
        sb.AppendLine("                return FailAndReturnValue(global::HPD.Math.Core.AlgebraStatus.InvalidInput);");
        sb.AppendLine("            if (numeratorDegrees.Length > TermCapacity || denominatorDegrees.Length > TermCapacity)");
        sb.AppendLine("                return FailAndReturnValue(global::HPD.Math.Core.AlgebraStatus.InsufficientDestination);");
        sb.AppendLine("            var builder = Builder(handle);");
        sb.AppendLine("            var ops = new " + coefficientOpsType + "();");
        sb.AppendLine("            for (var i = 0; i < numeratorDegrees.Length; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                var status = builder.Numerator.TryAppendTerm(numeratorDegrees[i], numeratorCoefficients[i], ops);");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok) return Complete(handle, builder.Numerator.Count, builder.Denominator.Count, status);");
        sb.AppendLine("            }");
        sb.AppendLine("            for (var i = 0; i < denominatorDegrees.Length; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                var status = builder.Denominator.TryAppendTerm(denominatorDegrees[i], denominatorCoefficients[i], ops);");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok) return Complete(handle, builder.Numerator.Count, builder.Denominator.Count, status);");
        sb.AppendLine("            }");
        sb.AppendLine("            var validateStatus = global::HPD.Math.Algebra.RationalFunctionKernels.Validate(builder.AsView(), ops);");
        sb.AppendLine("            return Complete(handle, builder.Numerator.Count, builder.Denominator.Count, validateStatus);");
        sb.AppendLine("        }");
        sb.AppendLine("        public Value Normalize(Value value) => value.Normalize();");
        sb.AppendLine("        public void Return(Value value)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!IsValid(value.Handle)) { Fail(global::HPD.Math.Core.AlgebraStatus.InvalidInput); return; }");
        sb.AppendLine("            _state[2] = value.Handle;");
        sb.AppendLine("        }");
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus CopyReturned(global::System.Span<int> outputNumeratorDegrees, global::System.Span<" + coefficientType + "> outputNumeratorCoefficients, global::System.Span<int> outputDenominatorDegrees, global::System.Span<" + coefficientType + "> outputDenominatorCoefficients, out int numeratorTermCount, out int denominatorTermCount)");
        sb.AppendLine("        {");
        sb.AppendLine("            numeratorTermCount = 0; denominatorTermCount = 0;");
        sb.AppendLine("            if (Status != global::HPD.Math.Core.AlgebraStatus.Ok) return Status;");
        sb.AppendLine("            if (!IsValid(_state[2])) return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine("            var n = _numeratorCounts[_state[2]]; var d = _denominatorCounts[_state[2]];");
        sb.AppendLine("            if (outputNumeratorDegrees.Length < n || outputNumeratorCoefficients.Length < n || outputDenominatorDegrees.Length < d || outputDenominatorCoefficients.Length < d) return global::HPD.Math.Core.AlgebraStatus.InsufficientDestination;");
        sb.AppendLine("            NumeratorDegreeSlot(_state[2])[..n].CopyTo(outputNumeratorDegrees); NumeratorCoefficientSlot(_state[2])[..n].CopyTo(outputNumeratorCoefficients);");
        sb.AppendLine("            DenominatorDegreeSlot(_state[2])[..d].CopyTo(outputDenominatorDegrees); DenominatorCoefficientSlot(_state[2])[..d].CopyTo(outputDenominatorCoefficients);");
        sb.AppendLine("            numeratorTermCount = n; denominatorTermCount = d; return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine("        private bool TryAllocate(out int handle)");
        sb.AppendLine("        {");
        sb.AppendLine("            handle = -1; if (Status != global::HPD.Math.Core.AlgebraStatus.Ok) return false;");
        sb.AppendLine("            if (_state[0] >= HandleCapacity) { Fail(global::HPD.Math.Core.AlgebraStatus.InsufficientDestination); return false; }");
        sb.AppendLine("            handle = _state[0]++; _numeratorCounts[handle] = 0; _denominatorCounts[handle] = 0; return true;");
        sb.AppendLine("        }");
        sb.AppendLine("        private Value Complete(int handle, int numeratorCount, int denominatorCount, global::HPD.Math.Core.AlgebraStatus status)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok) { Fail(status); return InvalidValue(); }");
        sb.AppendLine("            _numeratorCounts[handle] = numeratorCount; _denominatorCounts[handle] = denominatorCount; return CreateValue(handle);");
        sb.AppendLine("        }");
        sb.AppendLine("        private void Fail(global::HPD.Math.Core.AlgebraStatus status) { if (Status == global::HPD.Math.Core.AlgebraStatus.Ok) _state[1] = (int)status; }");
        sb.AppendLine("        private Value FailAndReturnValue(global::HPD.Math.Core.AlgebraStatus status) { Fail(status); return InvalidValue(); }");
        sb.AppendLine("        private bool IsValid(int handle) => handle >= 0 && handle < _state[0];");
        sb.AppendLine("        private Value InvalidValue() => CreateValue(-1);");
        sb.AppendLine("        private Value CreateValue(int handle) => new(_numeratorDegrees, _numeratorCoefficients, _denominatorDegrees, _denominatorCoefficients, _numeratorCounts, _denominatorCounts, _state, _workspaceDegrees, _workspaceCoefficients, handle);");
        sb.AppendLine("        private global::System.Span<int> NumeratorDegreeSlot(int handle) => _numeratorDegrees.Slice(handle * TermCapacity, TermCapacity);");
        sb.AppendLine("        private global::System.Span<" + coefficientType + "> NumeratorCoefficientSlot(int handle) => _numeratorCoefficients.Slice(handle * TermCapacity, TermCapacity);");
        sb.AppendLine("        private global::System.Span<int> DenominatorDegreeSlot(int handle) => _denominatorDegrees.Slice(handle * TermCapacity, TermCapacity);");
        sb.AppendLine("        private global::System.Span<" + coefficientType + "> DenominatorCoefficientSlot(int handle) => _denominatorCoefficients.Slice(handle * TermCapacity, TermCapacity);");
        sb.AppendLine("        private " + rationalBuilderType + " Builder(int handle) => new(NumeratorDegreeSlot(handle), NumeratorCoefficientSlot(handle), DenominatorDegreeSlot(handle), DenominatorCoefficientSlot(handle));");
        sb.AppendLine("    }");
        sb.AppendLine("    public readonly ref struct Value");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly global::System.Span<int> _numeratorDegrees; private readonly global::System.Span<" + coefficientType + "> _numeratorCoefficients; private readonly global::System.Span<int> _denominatorDegrees; private readonly global::System.Span<" + coefficientType + "> _denominatorCoefficients;");
        sb.AppendLine("        private readonly global::System.Span<int> _numeratorCounts; private readonly global::System.Span<int> _denominatorCounts; private readonly global::System.Span<int> _state; private readonly global::System.Span<int> _workspaceDegrees; private readonly global::System.Span<" + coefficientType + "> _workspaceCoefficients; internal readonly int Handle;");
        sb.AppendLine("        internal Value(global::System.Span<int> numeratorDegrees, global::System.Span<" + coefficientType + "> numeratorCoefficients, global::System.Span<int> denominatorDegrees, global::System.Span<" + coefficientType + "> denominatorCoefficients, global::System.Span<int> numeratorCounts, global::System.Span<int> denominatorCounts, global::System.Span<int> state, global::System.Span<int> workspaceDegrees, global::System.Span<" + coefficientType + "> workspaceCoefficients, int handle)");
        sb.AppendLine("        { _numeratorDegrees = numeratorDegrees; _numeratorCoefficients = numeratorCoefficients; _denominatorDegrees = denominatorDegrees; _denominatorCoefficients = denominatorCoefficients; _numeratorCounts = numeratorCounts; _denominatorCounts = denominatorCounts; _state = state; _workspaceDegrees = workspaceDegrees; _workspaceCoefficients = workspaceCoefficients; Handle = handle; }");
        sb.AppendLine("        public int NumeratorTermCount => IsValid(Handle) ? _numeratorCounts[Handle] : 0;");
        sb.AppendLine("        public int DenominatorTermCount => IsValid(Handle) ? _denominatorCounts[Handle] : 0;");
        sb.AppendLine("        public Value Normalize() { if (!CanOperate(out var handle)) return InvalidValue(); var destination = Builder(handle); var status = global::HPD.Math.Algebra.RationalFunctionKernels.TryNormalize(View(Handle), ref destination, NormalizationWorkspace(), new " + coefficientOpsType + "()); return Complete(handle, destination.Numerator.Count, destination.Denominator.Count, status); }");
        sb.AppendLine("        public Value Mul(Value other) { if (!CanOperate(other, out var handle)) return InvalidValue(); var destination = Builder(handle); var status = global::HPD.Math.Algebra.RationalFunctionKernels.TryMul(View(Handle), other.View(other.Handle), ref destination, _workspaceDegrees.Slice(0, WorkspaceCapacity), _workspaceCoefficients.Slice(0, WorkspaceCapacity), new " + coefficientOpsType + "()); return Complete(handle, destination.Numerator.Count, destination.Denominator.Count, status); }");
        sb.AppendLine("        private bool CanOperate(Value other, out int handle) { handle = -1; if (Status != global::HPD.Math.Core.AlgebraStatus.Ok) return false; if (!IsValid(Handle) || !IsValid(other.Handle)) return Fail(global::HPD.Math.Core.AlgebraStatus.InvalidInput); return TryAllocate(out handle); }");
        sb.AppendLine("        private bool CanOperate(out int handle) { handle = -1; if (Status != global::HPD.Math.Core.AlgebraStatus.Ok) return false; if (!IsValid(Handle)) return Fail(global::HPD.Math.Core.AlgebraStatus.InvalidInput); return TryAllocate(out handle); }");
        sb.AppendLine("        private bool TryAllocate(out int handle) { handle = -1; if (_state[0] >= HandleCapacity) return Fail(global::HPD.Math.Core.AlgebraStatus.InsufficientDestination); handle = _state[0]++; _numeratorCounts[handle] = 0; _denominatorCounts[handle] = 0; return true; }");
        sb.AppendLine("        private Value Complete(int handle, int numeratorCount, int denominatorCount, global::HPD.Math.Core.AlgebraStatus status) { if (status != global::HPD.Math.Core.AlgebraStatus.Ok) { Fail(status); return InvalidValue(); } _numeratorCounts[handle] = numeratorCount; _denominatorCounts[handle] = denominatorCount; return new Value(_numeratorDegrees, _numeratorCoefficients, _denominatorDegrees, _denominatorCoefficients, _numeratorCounts, _denominatorCounts, _state, _workspaceDegrees, _workspaceCoefficients, handle); }");
        sb.AppendLine("        private global::HPD.Math.Core.AlgebraStatus Status => (global::HPD.Math.Core.AlgebraStatus)_state[1];");
        sb.AppendLine("        private bool IsValid(int handle) => handle >= 0 && handle < _state[0];");
        sb.AppendLine("        private bool Fail(global::HPD.Math.Core.AlgebraStatus status) { if (Status == global::HPD.Math.Core.AlgebraStatus.Ok) _state[1] = (int)status; return false; }");
        sb.AppendLine("        private Value InvalidValue() => new(_numeratorDegrees, _numeratorCoefficients, _denominatorDegrees, _denominatorCoefficients, _numeratorCounts, _denominatorCounts, _state, _workspaceDegrees, _workspaceCoefficients, -1);");
        sb.AppendLine("        private global::System.Span<int> NumeratorDegreeSlot(int handle) => _numeratorDegrees.Slice(handle * TermCapacity, TermCapacity);");
        sb.AppendLine("        private global::System.Span<" + coefficientType + "> NumeratorCoefficientSlot(int handle) => _numeratorCoefficients.Slice(handle * TermCapacity, TermCapacity);");
        sb.AppendLine("        private global::System.Span<int> DenominatorDegreeSlot(int handle) => _denominatorDegrees.Slice(handle * TermCapacity, TermCapacity);");
        sb.AppendLine("        private global::System.Span<" + coefficientType + "> DenominatorCoefficientSlot(int handle) => _denominatorCoefficients.Slice(handle * TermCapacity, TermCapacity);");
        sb.AppendLine("        private " + rationalBuilderType + " Builder(int handle) => new(NumeratorDegreeSlot(handle), NumeratorCoefficientSlot(handle), DenominatorDegreeSlot(handle), DenominatorCoefficientSlot(handle));");
        sb.AppendLine("        private " + rationalViewType + " View(int handle) => new(new " + polynomialViewType + "(new " + finsuppViewType + "(NumeratorDegreeSlot(handle)[.._numeratorCounts[handle]], NumeratorCoefficientSlot(handle)[.._numeratorCounts[handle]])), new " + polynomialViewType + "(new " + finsuppViewType + "(DenominatorDegreeSlot(handle)[.._denominatorCounts[handle]], DenominatorCoefficientSlot(handle)[.._denominatorCounts[handle]])));");
        sb.AppendLine("        private global::HPD.Math.Algebra.RationalFunctionNormalizationWorkspace<" + coefficientType + "> NormalizationWorkspace() => new(_workspaceDegrees.Slice(0, WorkspaceCapacity), _workspaceCoefficients.Slice(0, WorkspaceCapacity), _workspaceDegrees.Slice(WorkspaceCapacity, WorkspaceCapacity), _workspaceCoefficients.Slice(WorkspaceCapacity, WorkspaceCapacity), _workspaceDegrees.Slice(WorkspaceCapacity * 2, WorkspaceCapacity), _workspaceCoefficients.Slice(WorkspaceCapacity * 2, WorkspaceCapacity), _workspaceCoefficients.Slice(WorkspaceCapacity * 3, WorkspaceCapacity), _workspaceCoefficients.Slice(WorkspaceCapacity * 4, WorkspaceCapacity), _workspaceCoefficients.Slice(WorkspaceCapacity * 5, WorkspaceCapacity), _workspaceCoefficients.Slice(WorkspaceCapacity * 6, WorkspaceCapacity), _workspaceCoefficients.Slice(WorkspaceCapacity * 7, WorkspaceCapacity));");
        sb.AppendLine("    }");
    }

    private static void GeneratePadicScope(StringBuilder sb, PadicScopeModel scope)
    {
        GeneratePadicAuthoringSurface(sb, scope, emitRunner: true);
    }

    private static void GeneratePadicContext(StringBuilder sb, PadicScopeModel context)
    {
        GeneratePadicValueContext(sb, context);
    }

    private static void GeneratePadicValueContext(StringBuilder sb, PadicScopeModel context)
    {
        var valueType = "global::HPD.Math.Numerics.Padic32<" + context.PrimeType + ", " + context.PrecisionType + ">";
        var opsType = "global::HPD.Math.Numerics.Padic32Ops<" + context.PrimeType + ", " + context.PrecisionType + ">";

        sb.AppendLine();
        sb.Append("    public static int Prime => ").Append(context.PrimeType).AppendLine(".Value;");
        sb.Append("    public static int Precision => ").Append(context.PrecisionType).AppendLine(".Value;");
        sb.AppendLine("    public static Value Zero => new(" + valueType + ".Zero);");
        sb.AppendLine("    public static Value One => new(" + valueType + ".One);");
        sb.AppendLine("    public static Ops CreateOps() => default;");
        sb.AppendLine();
        sb.AppendLine("    public static global::HPD.Math.Core.AlgebraStatus TryConst(int value, out Value result)");
        sb.AppendLine("    {");
        sb.AppendLine("        result = default;");
        sb.AppendLine("        var status = global::HPD.Math.Numerics.Padic32Kernels.TryCreate<" + context.PrimeType + ", " + context.PrecisionType + ">(value, out var raw);");
        sb.AppendLine("        if (status == global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("            result = new Value(raw);");
        sb.AppendLine("        return status;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public readonly struct Value : global::System.IEquatable<Value>");
        sb.AppendLine("    {");
        sb.AppendLine("        internal Value(" + valueType + " raw) => Raw = raw;");
        sb.AppendLine("        public " + valueType + " Raw { get; }");
        sb.AppendLine("        public int Residue => Raw.Value;");
        sb.AppendLine("        public bool IsUnit => Raw.IsUnit;");
        sb.AppendLine("        public bool Equals(Value other) => Raw == other.Raw;");
        sb.AppendLine("        public override bool Equals(object? obj) => obj is Value other && Equals(other);");
        sb.AppendLine("        public override int GetHashCode() => Raw.GetHashCode();");
        sb.AppendLine("        public static bool operator ==(Value left, Value right) => left.Equals(right);");
        sb.AppendLine("        public static bool operator !=(Value left, Value right) => !left.Equals(right);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public readonly struct Ops");
        sb.AppendLine("    {");
        sb.AppendLine("        public Value Zero => new(" + valueType + ".Zero);");
        sb.AppendLine("        public Value One => new(" + valueType + ".One);");
        sb.AppendLine("        public bool Eq(in Value left, in Value right) => left.Raw == right.Raw;");
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryConst(int value, out Value result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            var status = global::HPD.Math.Numerics.Padic32Kernels.TryCreate<" + context.PrimeType + ", " + context.PrecisionType + ">(value, out var raw);");
        sb.AppendLine("            if (status == global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                result = new Value(raw);");
        sb.AppendLine("            return status;");
        sb.AppendLine("        }");
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryAdd(Value left, Value right, out Value result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            var raw = " + valueType + ".Zero;");
        sb.AppendLine("            var status = new " + opsType + "().TryAdd(ref raw, left.Raw, right.Raw);");
        sb.AppendLine("            if (status == global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                result = new Value(raw);");
        sb.AppendLine("            return status;");
        sb.AppendLine("        }");
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryMul(Value left, Value right, out Value result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            var raw = " + valueType + ".Zero;");
        sb.AppendLine("            var status = new " + opsType + "().TryMul(ref raw, left.Raw, right.Raw);");
        sb.AppendLine("            if (status == global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                result = new Value(raw);");
        sb.AppendLine("            return status;");
        sb.AppendLine("        }");
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryInv(Value value, out Value result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            var raw = " + valueType + ".Zero;");
        sb.AppendLine("            var status = new " + opsType + "().TryInvert(ref raw, value.Raw);");
        sb.AppendLine("            if (status == global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                result = new Value(raw);");
        sb.AppendLine("            return status;");
        sb.AppendLine("        }");
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryValuation(Value value, out int valuation) =>");
        sb.AppendLine("            value.Raw.TryValuation(out valuation);");
        sb.AppendLine("    }");
    }

    private static void GeneratePadicAuthoringSurface(
        StringBuilder sb,
        PadicScopeModel scope,
        bool emitRunner)
    {
        var valueType = "global::HPD.Math.Numerics.Padic32<" + scope.PrimeType + ", " + scope.PrecisionType + ">";
        var opsType = "global::HPD.Math.Numerics.Padic32Ops<" + scope.PrimeType + ", " + scope.PrecisionType + ">";

        sb.AppendLine();
        sb.Append("    public const int HandleCapacity = ").Append(scope.Handles).AppendLine(";");
        sb.Append("    public static int Prime => ").Append(scope.PrimeType).AppendLine(".Value;");
        sb.Append("    public static int Precision => ").Append(scope.PrecisionType).AppendLine(".Value;");
        sb.AppendLine();
        if (emitRunner)
        {
            sb.AppendLine("    public global::HPD.Math.Core.AlgebraStatus Run(ref Result result)");
            sb.AppendLine("    {");
            sb.AppendLine("        result.Clear();");
            sb.Append("        global::System.Span<").Append(valueType).AppendLine("> values = stackalloc " + valueType + "[HandleCapacity];");
            sb.AppendLine("        global::System.Span<int> state = stackalloc int[3];");
            sb.AppendLine("        var scope = new Scope(values, state);");
            sb.AppendLine("        Build(ref scope);");
            sb.AppendLine("        var status = scope.CopyReturned(out var value);");
            sb.AppendLine("        if (scope.Status != global::HPD.Math.Core.AlgebraStatus.Ok)");
            sb.AppendLine("            return scope.Status;");
            sb.AppendLine("        if (status == global::HPD.Math.Core.AlgebraStatus.Ok)");
            sb.AppendLine("            result.SetValue(value);");
            sb.AppendLine("        return status;");
            sb.AppendLine("    }");
            sb.AppendLine("    partial void Build(ref Scope z);");
            sb.AppendLine();
            sb.AppendLine("    public struct Result");
            sb.AppendLine("    {");
            sb.AppendLine("        public " + valueType + " Value { get; private set; }");
            sb.AppendLine("        public int Residue => Value.Value;");
            sb.AppendLine("        internal void SetValue(" + valueType + " value) => Value = value;");
            sb.AppendLine("        internal void Clear() => Value = " + valueType + ".Zero;");
            sb.AppendLine("    }");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("    public static " + opsType + " CreateOps() => new();");
            sb.AppendLine();
            sb.Append("    public static Scope CreateScope(global::System.Span<").Append(valueType)
                .AppendLine("> values, global::System.Span<int> state) =>");
            sb.AppendLine("        new(values, state);");
            sb.AppendLine();
        }
        sb.AppendLine("    public ref struct Scope");
        sb.AppendLine("    {");
        sb.Append("        private readonly global::System.Span<").Append(valueType).AppendLine("> _values;");
        sb.AppendLine("        private readonly global::System.Span<int> _state;");
        sb.AppendLine("        public Scope(global::System.Span<" + valueType + "> values, global::System.Span<int> state)");
        sb.AppendLine("        {");
        sb.AppendLine("            _values = values; _state = state; _state[0] = 0; _state[1] = (int)global::HPD.Math.Core.AlgebraStatus.Ok; _state[2] = -1;");
        sb.AppendLine("        }");
        sb.AppendLine("        public readonly global::HPD.Math.Core.AlgebraStatus Status => (global::HPD.Math.Core.AlgebraStatus)_state[1];");
        sb.AppendLine("        public Value Zero() => Store(" + valueType + ".Zero);");
        sb.AppendLine("        public Value One() => Store(" + valueType + ".One);");
        sb.AppendLine("        public Value Const(int value)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (Status != global::HPD.Math.Core.AlgebraStatus.Ok) return InvalidValue();");
        sb.AppendLine("            var status = global::HPD.Math.Numerics.Padic32Kernels.TryCreate<" + scope.PrimeType + ", " + scope.PrecisionType + ">(value, out var result);");
        sb.AppendLine("            return status == global::HPD.Math.Core.AlgebraStatus.Ok ? Store(result) : FailAndReturn(status);");
        sb.AppendLine("        }");
        sb.AppendLine("        public Value Inv(Value value) => value.Inv;");
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus Valuation(Value value, out int valuation)");
        sb.AppendLine("        {");
        sb.AppendLine("            valuation = 0;");
        sb.AppendLine("            if (!IsValid(value.Handle)) return FailStatus(global::HPD.Math.Core.AlgebraStatus.InvalidInput);");
        sb.AppendLine("            return _values[value.Handle].TryValuation(out valuation);");
        sb.AppendLine("        }");
        sb.AppendLine("        public void Return(Value value)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!IsValid(value.Handle)) { Fail(global::HPD.Math.Core.AlgebraStatus.InvalidInput); return; }");
        sb.AppendLine("            _state[2] = value.Handle;");
        sb.AppendLine("        }");
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus CopyReturned(out " + valueType + " value)");
        sb.AppendLine("        {");
        sb.AppendLine("            value = " + valueType + ".Zero;");
        sb.AppendLine("            if (Status != global::HPD.Math.Core.AlgebraStatus.Ok) return Status;");
        sb.AppendLine("            if (!IsValid(_state[2])) return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine("            value = _values[_state[2]];");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine("        private Value Store(" + valueType + " value)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!TryAllocate(out var handle)) return InvalidValue();");
        sb.AppendLine("            _values[handle] = value;");
        sb.AppendLine("            return CreateValue(handle);");
        sb.AppendLine("        }");
        sb.AppendLine("        private bool TryAllocate(out int handle)");
        sb.AppendLine("        {");
        sb.AppendLine("            handle = -1; if (Status != global::HPD.Math.Core.AlgebraStatus.Ok) return false;");
        sb.AppendLine("            if (_state[0] >= HandleCapacity) { Fail(global::HPD.Math.Core.AlgebraStatus.InsufficientDestination); return false; }");
        sb.AppendLine("            handle = _state[0]++; return true;");
        sb.AppendLine("        }");
        sb.AppendLine("        private bool IsValid(int handle) => handle >= 0 && handle < _state[0];");
        sb.AppendLine("        private void Fail(global::HPD.Math.Core.AlgebraStatus status) { if (Status == global::HPD.Math.Core.AlgebraStatus.Ok) _state[1] = (int)status; }");
        sb.AppendLine("        private global::HPD.Math.Core.AlgebraStatus FailStatus(global::HPD.Math.Core.AlgebraStatus status) { Fail(status); return status; }");
        sb.AppendLine("        private Value FailAndReturn(global::HPD.Math.Core.AlgebraStatus status) { Fail(status); return InvalidValue(); }");
        sb.AppendLine("        private Value InvalidValue() => CreateValue(-1);");
        sb.AppendLine("        private Value CreateValue(int handle) => new(_values, _state, handle);");
        sb.AppendLine("    }");
        sb.AppendLine("    public readonly ref struct Value");
        sb.AppendLine("    {");
        sb.Append("        private readonly global::System.Span<").Append(valueType).AppendLine("> _values;");
        sb.AppendLine("        private readonly global::System.Span<int> _state;");
        sb.AppendLine("        internal readonly int Handle;");
        sb.AppendLine("        internal Value(global::System.Span<" + valueType + "> values, global::System.Span<int> state, int handle) { _values = values; _state = state; Handle = handle; }");
        sb.AppendLine("        public " + valueType + " Raw => IsValid(Handle) ? _values[Handle] : " + valueType + ".Zero;");
        sb.AppendLine("        public int Residue => Raw.Value;");
        sb.AppendLine("        public bool IsUnit => Raw.IsUnit;");
        sb.AppendLine("        public Value Inv => Invert();");
        sb.AppendLine("        public Value Add(Value other) => Binary(other, true);");
        sb.AppendLine("        public Value Mul(Value other) => Binary(other, false);");
        sb.AppendLine("        private Value Binary(Value other, bool add)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!CanOperate(other, out var handle)) return InvalidValue();");
        sb.AppendLine("            var ops = new " + opsType + "();");
        sb.AppendLine("            var result = " + valueType + ".Zero;");
        sb.AppendLine("            var status = add ? ops.TryAdd(ref result, Raw, other.Raw) : ops.TryMul(ref result, Raw, other.Raw);");
        sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok) { Fail(status); return InvalidValue(); }");
        sb.AppendLine("            _values[handle] = result;");
        sb.AppendLine("            return new Value(_values, _state, handle);");
        sb.AppendLine("        }");
        sb.AppendLine("        private Value Invert()");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!CanOperate(out var handle)) return InvalidValue();");
        sb.AppendLine("            var result = " + valueType + ".Zero;");
        sb.AppendLine("            var status = new " + opsType + "().TryInvert(ref result, Raw);");
        sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok) { Fail(status); return InvalidValue(); }");
        sb.AppendLine("            _values[handle] = result;");
        sb.AppendLine("            return new Value(_values, _state, handle);");
        sb.AppendLine("        }");
        sb.AppendLine("        private bool CanOperate(Value other, out int handle) { handle = -1; if (Status != global::HPD.Math.Core.AlgebraStatus.Ok) return false; if (!IsValid(Handle) || !IsValid(other.Handle)) return Fail(global::HPD.Math.Core.AlgebraStatus.InvalidInput); return TryAllocate(out handle); }");
        sb.AppendLine("        private bool CanOperate(out int handle) { handle = -1; if (Status != global::HPD.Math.Core.AlgebraStatus.Ok) return false; if (!IsValid(Handle)) return Fail(global::HPD.Math.Core.AlgebraStatus.InvalidInput); return TryAllocate(out handle); }");
        sb.AppendLine("        private bool TryAllocate(out int handle) { handle = -1; if (_state[0] >= HandleCapacity) return Fail(global::HPD.Math.Core.AlgebraStatus.InsufficientDestination); handle = _state[0]++; return true; }");
        sb.AppendLine("        private global::HPD.Math.Core.AlgebraStatus Status => (global::HPD.Math.Core.AlgebraStatus)_state[1];");
        sb.AppendLine("        private bool IsValid(int handle) => handle >= 0 && handle < _state[0];");
        sb.AppendLine("        private bool Fail(global::HPD.Math.Core.AlgebraStatus status) { if (Status == global::HPD.Math.Core.AlgebraStatus.Ok) _state[1] = (int)status; return false; }");
        sb.AppendLine("        private Value InvalidValue() => new(_values, _state, -1);");
        sb.AppendLine("    }");
    }

    private static void GenerateWittVectorScope(StringBuilder sb, WittVectorScopeModel scope)
    {
        GenerateWittVectorAuthoringSurface(sb, scope, emitRunner: true);
    }

    private static void GenerateWittVectorContext(StringBuilder sb, string contextName, WittVectorContextModel context)
    {
        var componentType = context.ComponentType;
        var componentOpsType = context.ComponentOpsType;

        sb.AppendLine();
        sb.Append("    public const int Length = ").Append(context.Length).AppendLine(";");
        sb.Append("    public static int Prime => ").Append(context.PrimeType).AppendLine(".Value;");
        sb.AppendLine("    public static bool IsValidContext => Prime > 1 && Length > 0;");
        sb.AppendLine("    public static bool HasSupportedArithmetic => IsValidContext && Length <= 2;");
        sb.AppendLine();
        sb.AppendLine("    public static global::HPD.Math.Core.AlgebraStatus TryZero(out Vector result) => new Ops().TryZero(out result);");
        sb.AppendLine();
        sb.AppendLine("    public static global::HPD.Math.Core.AlgebraStatus TryOne(out Vector result) => new Ops().TryOne(out result);");
        sb.AppendLine();
        sb.AppendLine("    public static global::HPD.Math.Core.AlgebraStatus TryFromComponents(global::System.ReadOnlySpan<" + componentType + "> components, out Vector result)");
        sb.AppendLine("    {");
        sb.AppendLine("        result = default;");
        sb.AppendLine("        if (components.Length != Length)");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.DimensionMismatch;");
        sb.AppendLine("        if (!IsValidContext)");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine("        for (var i = 0; i < Length; i++)");
        sb.AppendLine("            result.SetComponent(i, components[i]);");
        sb.AppendLine("        return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public struct Vector : global::System.IEquatable<Vector>");
        sb.AppendLine("    {");
        sb.AppendLine("        private ComponentBuffer _components;");
        sb.AppendLine();
        sb.AppendLine("        public int ComponentCount => Length;");
        sb.Append("        public ").Append(componentType).AppendLine(" this[int index] => GetComponent(index);");
        sb.Append("        public ").Append(componentType).AppendLine(" ComponentAt(int index) => GetComponent(index);");
        sb.AppendLine();
        sb.AppendLine("        internal global::System.ReadOnlySpan<" + componentType + "> ReadOnlyComponents => _components.AsSpan();");
        sb.AppendLine("        internal " + componentType + " GetComponent(int index) => ReadOnlyComponents[index];");
        sb.AppendLine("        internal void SetComponent(int index, " + componentType + " value) => _components.AsSpan()[index] = value;");
        sb.AppendLine();
        sb.AppendLine("        public bool Equals(Vector other)");
        sb.AppendLine("        {");
        sb.AppendLine("            var ops = new " + componentOpsType + "();");
        sb.AppendLine("            for (var i = 0; i < Length; i++)");
        sb.AppendLine("                if (!ops.Eq(GetComponent(i), other.GetComponent(i)))");
        sb.AppendLine("                    return false;");
        sb.AppendLine("            return true;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public override bool Equals(object? obj) => obj is Vector other && Equals(other);");
        sb.AppendLine();
        sb.AppendLine("        public override int GetHashCode()");
        sb.AppendLine("        {");
        sb.AppendLine("            var hash = new global::System.HashCode();");
        sb.AppendLine("            for (var i = 0; i < Length; i++)");
        sb.AppendLine("                hash.Add(GetComponent(i));");
        sb.AppendLine("            return hash.ToHashCode();");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public static bool operator ==(Vector left, Vector right) => left.Equals(right);");
        sb.AppendLine("        public static bool operator !=(Vector left, Vector right) => !left.Equals(right);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public readonly struct Ops");
        sb.AppendLine("    {");
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryZero(out Vector result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            var status = ValidateContext();");
        sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return status;");
        sb.AppendLine("            var ops = new " + componentOpsType + "();");
        sb.AppendLine("            for (var i = 0; i < Length; i++)");
        sb.AppendLine("                result.SetComponent(i, ops.Zero);");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryOne(out Vector result)");
        sb.AppendLine("        {");
        sb.AppendLine("            var status = TryZero(out result);");
        sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return status;");
        sb.AppendLine("            result.SetComponent(0, new " + componentOpsType + "().One);");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryAdd(in Vector left, in Vector right, out Vector result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        if (context.Length > 2)
        {
            sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryNeg(in Vector value, out Vector result)");
            sb.AppendLine("        {");
            sb.AppendLine("            result = default;");
            sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TrySub(in Vector left, in Vector right, out Vector result)");
            sb.AppendLine("        {");
            sb.AppendLine("            result = default;");
            sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryMul(in Vector left, in Vector right, out Vector result)");
            sb.AppendLine("        {");
            sb.AppendLine("            result = default;");
            sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
            sb.AppendLine("        }");
        }
        else
        {
        sb.AppendLine("            var status = ValidateArithmetic();");
        sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return status;");
        sb.AppendLine("            var ops = new " + componentOpsType + "();");
        sb.AppendLine("            var first = ops.Zero;");
        sb.AppendLine("            ops.Add(ref first, left.GetComponent(0), right.GetComponent(0));");
        sb.AppendLine("            result.SetComponent(0, first);");
            if (context.Length == 2)
            {
                sb.AppendLine("            var second = ops.Zero;");
                sb.AppendLine("            ops.Add(ref second, left.GetComponent(1), right.GetComponent(1));");
                sb.AppendLine("            status = WittAdditionCorrection(left.GetComponent(0), right.GetComponent(0), out var correction, ops);");
                sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
                sb.AppendLine("                return status;");
                sb.AppendLine("            ops.Sub(ref second, second, correction);");
                sb.AppendLine("            result.SetComponent(1, second);");
            }
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryNeg(in Vector value, out Vector result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            var status = ValidateArithmetic();");
        sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return status;");
        sb.AppendLine("            var ops = new " + componentOpsType + "();");
        sb.AppendLine("            var first = ops.Zero;");
        sb.AppendLine("            ops.Neg(ref first, value.GetComponent(0));");
        sb.AppendLine("            result.SetComponent(0, first);");
            if (context.Length == 2)
            {
                sb.AppendLine("            var second = ops.Zero;");
                sb.AppendLine("            ops.Neg(ref second, value.GetComponent(1));");
                sb.AppendLine("            result.SetComponent(1, second);");
                sb.AppendLine("            status = WittAdditionCorrection(value.GetComponent(0), result.GetComponent(0), out var correction, ops);");
                sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
                sb.AppendLine("                return status;");
                sb.AppendLine("            ops.Add(ref second, second, correction);");
                sb.AppendLine("            result.SetComponent(1, second);");
            }
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TrySub(in Vector left, in Vector right, out Vector result)");
        sb.AppendLine("        {");
        sb.AppendLine("            var status = TryNeg(right, out var negRight);");
        sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("            {");
        sb.AppendLine("                result = default;");
        sb.AppendLine("                return status;");
        sb.AppendLine("            }");
        sb.AppendLine("            return TryAdd(left, negRight, out result);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryMul(in Vector left, in Vector right, out Vector result)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = default;");
        sb.AppendLine("            var status = ValidateArithmetic();");
        sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return status;");
        sb.AppendLine("            var ops = new " + componentOpsType + "();");
        sb.AppendLine("            var first = ops.Zero;");
        sb.AppendLine("            ops.Mul(ref first, left.GetComponent(0), right.GetComponent(0));");
        sb.AppendLine("            result.SetComponent(0, first);");
            if (context.Length == 2)
            {
                sb.AppendLine("            status = Pow(left.GetComponent(0), Prime, out var leftPow, ops);");
                sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
                sb.AppendLine("                return status;");
                sb.AppendLine("            var second = ops.Zero;");
                sb.AppendLine("            ops.Mul(ref second, leftPow, right.GetComponent(1));");
                sb.AppendLine("            status = Pow(right.GetComponent(0), Prime, out var rightPow, ops);");
                sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
                sb.AppendLine("                return status;");
                sb.AppendLine("            ops.Mul(ref rightPow, rightPow, left.GetComponent(1));");
                sb.AppendLine("            ops.Add(ref second, second, rightPow);");
                sb.AppendLine("            status = ops.TryFromInt(Prime, out var cross);");
                sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
                sb.AppendLine("                return status;");
                sb.AppendLine("            ops.Mul(ref cross, cross, left.GetComponent(1));");
                sb.AppendLine("            ops.Mul(ref cross, cross, right.GetComponent(1));");
                sb.AppendLine("            ops.Add(ref second, second, cross);");
                sb.AppendLine("            result.SetComponent(1, second);");
            }
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        }
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryGhostComponent(in Vector value, int index, ref " + componentType + " destination)");
        sb.AppendLine("        {");
        sb.AppendLine("            var status = ValidateContext();");
        sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                return status;");
        sb.AppendLine("            if (index < 0 || index >= Length)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine("            var ops = new " + componentOpsType + "();");
        sb.AppendLine("            destination = ops.Zero;");
        sb.AppendLine("            var primePower = 1;");
        sb.AppendLine("            for (var j = 0; j <= index; j++)");
        sb.AppendLine("            {");
        sb.AppendLine("                status = ops.TryFromInt(primePower, out var scale);");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                    return status;");
        sb.AppendLine("                status = Pow(value.GetComponent(j), PowInt(Prime, index - j), out var componentPower, ops);");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                    return status;");
        sb.AppendLine("                ops.Mul(ref scale, scale, componentPower);");
        sb.AppendLine("                ops.Add(ref destination, destination, scale);");
        sb.AppendLine("                primePower = checked(primePower * Prime);");
        sb.AppendLine("            }");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private static global::HPD.Math.Core.AlgebraStatus ValidateContext() =>");
        sb.AppendLine("            IsValidContext");
        sb.AppendLine("                ? global::HPD.Math.Core.AlgebraStatus.Ok");
        sb.AppendLine("                : global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine();
        sb.AppendLine("        private static global::HPD.Math.Core.AlgebraStatus ValidateArithmetic() =>");
        sb.AppendLine("            HasSupportedArithmetic");
        sb.AppendLine("                ? global::HPD.Math.Core.AlgebraStatus.Ok");
        sb.AppendLine("                : global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine();
        sb.AppendLine("        private static global::HPD.Math.Core.AlgebraStatus WittAdditionCorrection(");
        sb.AppendLine("            in " + componentType + " x,");
        sb.AppendLine("            in " + componentType + " y,");
        sb.AppendLine("            out " + componentType + " correction,");
        sb.AppendLine("            " + componentOpsType + " ops)");
        sb.AppendLine("        {");
        sb.AppendLine("            correction = ops.Zero;");
        sb.AppendLine("            for (var i = 1; i < Prime; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                var status = ops.TryFromInt(Binomial(Prime, i) / Prime, out var term);");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                    return status;");
        sb.AppendLine("                status = Pow(x, i, out var xPower, ops);");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                    return status;");
        sb.AppendLine("                status = Pow(y, Prime - i, out var yPower, ops);");
        sb.AppendLine("                if (status != global::HPD.Math.Core.AlgebraStatus.Ok)");
        sb.AppendLine("                    return status;");
        sb.AppendLine("                ops.Mul(ref term, term, xPower);");
        sb.AppendLine("                ops.Mul(ref term, term, yPower);");
        sb.AppendLine("                ops.Add(ref correction, correction, term);");
        sb.AppendLine("            }");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private static global::HPD.Math.Core.AlgebraStatus Pow(in " + componentType + " value, int exponent, out " + componentType + " destination, " + componentOpsType + " ops)");
        sb.AppendLine("        {");
        sb.AppendLine("            destination = ops.One;");
        sb.AppendLine("            if (exponent < 0)");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine("            for (var i = 0; i < exponent; i++)");
        sb.AppendLine("                ops.Mul(ref destination, destination, value);");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private static int Binomial(int n, int k)");
        sb.AppendLine("        {");
        sb.AppendLine("            var result = 1;");
        sb.AppendLine("            for (var i = 1; i <= k; i++)");
        sb.AppendLine("                result = checked(result * (n - (k - i)) / i);");
        sb.AppendLine("            return result;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private static int PowInt(int value, int exponent)");
        sb.AppendLine("        {");
        sb.AppendLine("            var result = 1;");
        sb.AppendLine("            for (var i = 0; i < exponent; i++)");
        sb.AppendLine("                result = checked(result * value);");
        sb.AppendLine("            return result;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    [global::System.Runtime.CompilerServices.InlineArray(Length)]");
        sb.AppendLine("    private struct ComponentBuffer");
        sb.AppendLine("    {");
        sb.Append("        private ").Append(componentType).AppendLine(" _element0;");
        sb.Append("        public global::System.Span<").Append(componentType).AppendLine("> AsSpan() => global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref _element0, Length);");
        sb.AppendLine("    }");
    }

    private static void GenerateFinitePowerSetContext(StringBuilder sb, FinitePowerSetContextModel context)
    {
        var wordCount = (context.Cardinality + 63) / 64;
        var finalWordBits = context.Cardinality % 64;

        sb.AppendLine();
        sb.Append("    public const int Cardinality = ").Append(context.Cardinality).AppendLine(";");
        sb.Append("    public const int WordCount = ").Append(wordCount).AppendLine(";");
        sb.AppendLine();
        sb.AppendLine("    public static Set Empty => default;");
        sb.AppendLine();
        sb.AppendLine("    public static Set Top");
        sb.AppendLine("    {");
        sb.AppendLine("        get");
        sb.AppendLine("        {");
        sb.AppendLine("            var result = default(Set);");
        sb.AppendLine("            result.FillTop();");
        sb.AppendLine("            return result;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public static global::HPD.Math.Core.AlgebraStatus TrySingletonIndex(int index, out Set result)");
        sb.AppendLine("    {");
        sb.AppendLine("        result = default;");
        sb.AppendLine("        if (index < 0 || index >= Cardinality)");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine();
        sb.AppendLine("        result.SetIndexUnchecked(index);");
        sb.AppendLine("        return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public static global::HPD.Math.Core.AlgebraStatus TryFromIndices(global::System.ReadOnlySpan<int> indices, out Set result)");
        sb.AppendLine("    {");
        sb.AppendLine("        result = default;");
        sb.AppendLine("        for (var i = 0; i < indices.Length; i++)");
        sb.AppendLine("        {");
        sb.AppendLine("            var index = indices[i];");
        sb.AppendLine("            if (index < 0 || index >= Cardinality)");
        sb.AppendLine("            {");
        sb.AppendLine("                result = default;");
        sb.AppendLine("                return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            result.SetIndexUnchecked(index);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public struct Set : global::System.IEquatable<Set>");
        sb.AppendLine("    {");
        sb.AppendLine("        private WordBuffer _words;");
        sb.AppendLine();
        sb.AppendLine("        public bool IsEmpty");
        sb.AppendLine("        {");
        sb.AppendLine("            get");
        sb.AppendLine("            {");
        sb.AppendLine("                var words = Words;");
        sb.AppendLine("                for (var i = 0; i < WordCount; i++)");
        sb.AppendLine("                    if (words[i] != 0)");
        sb.AppendLine("                        return false;");
        sb.AppendLine("                return true;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public bool ContainsIndex(int index) =>");
        sb.AppendLine("            index >= 0 && index < Cardinality && (GetWord(index >> 6) & (1UL << (index & 63))) != 0;");
        sb.AppendLine();
        sb.AppendLine("        internal global::System.Span<ulong> Words => _words.AsSpan();");
        sb.AppendLine();
        sb.AppendLine("        internal global::System.ReadOnlySpan<ulong> ReadOnlyWords => _words.AsSpan();");
        sb.AppendLine();
        sb.AppendLine("        internal ulong GetWord(int wordIndex) => ReadOnlyWords[wordIndex];");
        sb.AppendLine();
        sb.AppendLine("        internal void SetWord(int wordIndex, ulong value) => Words[wordIndex] = value;");
        sb.AppendLine();
        sb.AppendLine("        internal void SetIndexUnchecked(int index)");
        sb.AppendLine("        {");
        sb.AppendLine("            var wordIndex = index >> 6;");
        sb.AppendLine("            SetWord(wordIndex, GetWord(wordIndex) | (1UL << (index & 63)));");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        internal void FillTop()");
        sb.AppendLine("        {");
        sb.AppendLine("            for (var i = 0; i < WordCount; i++)");
        sb.AppendLine("                SetWord(i, ulong.MaxValue);");
        if (finalWordBits != 0)
            sb.Append("            SetWord(WordCount - 1, (1UL << ").Append(finalWordBits).AppendLine(") - 1UL);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public bool Equals(Set other) => new Ops().Eq(this, other);");
        sb.AppendLine();
        sb.AppendLine("        public override bool Equals(object? obj) => obj is Set other && Equals(other);");
        sb.AppendLine();
        sb.AppendLine("        public override int GetHashCode()");
        sb.AppendLine("        {");
        sb.AppendLine("            var hash = new global::System.HashCode();");
        sb.AppendLine("            var words = ReadOnlyWords;");
        sb.AppendLine("            for (var i = 0; i < WordCount; i++)");
        sb.AppendLine("                hash.Add(words[i] & Ops.MaskForWord(i));");
        sb.AppendLine("            return hash.ToHashCode();");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public static bool operator ==(Set left, Set right) => left.Equals(right);");
        sb.AppendLine();
        sb.AppendLine("        public static bool operator !=(Set left, Set right) => !left.Equals(right);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public readonly struct Ops :");
        sb.AppendLine("        global::HPD.Math.Core.IBooleanAlgebraOps<Set>,");
        sb.AppendLine("        global::HPD.Math.Core.ICompleteFiniteLatticeOps<Set>");
        sb.AppendLine("    {");
        sb.AppendLine("        public Set Top");
        sb.AppendLine("        {");
        sb.AppendLine("            get");
        sb.AppendLine("            {");
        sb.AppendLine("                var result = default(Set);");
        sb.AppendLine("                result.FillTop();");
        sb.AppendLine("                return result;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public Set Bottom => default;");
        sb.AppendLine();
        sb.AppendLine("        public bool Eq(in Set left, in Set right)");
        sb.AppendLine("        {");
        sb.AppendLine("            for (var i = 0; i < WordCount; i++)");
        sb.AppendLine("                if ((left.GetWord(i) & MaskForWord(i)) != (right.GetWord(i) & MaskForWord(i)))");
        sb.AppendLine("                    return false;");
        sb.AppendLine("            return true;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public bool LessEqual(in Set left, in Set right)");
        sb.AppendLine("        {");
        sb.AppendLine("            for (var i = 0; i < WordCount; i++)");
        sb.AppendLine("                if ((left.GetWord(i) & ~right.GetWord(i) & MaskForWord(i)) != 0)");
        sb.AppendLine("                    return false;");
        sb.AppendLine("            return true;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public void Join(ref Set destination, in Set left, in Set right)");
        sb.AppendLine("        {");
        sb.AppendLine("            for (var i = 0; i < WordCount; i++)");
        sb.AppendLine("                destination.SetWord(i, (left.GetWord(i) | right.GetWord(i)) & MaskForWord(i));");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public void Meet(ref Set destination, in Set left, in Set right)");
        sb.AppendLine("        {");
        sb.AppendLine("            for (var i = 0; i < WordCount; i++)");
        sb.AppendLine("                destination.SetWord(i, (left.GetWord(i) & right.GetWord(i)) & MaskForWord(i));");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public void Complement(ref Set destination, in Set value)");
        sb.AppendLine("        {");
        sb.AppendLine("            for (var i = 0; i < WordCount; i++)");
        sb.AppendLine("                destination.SetWord(i, ~value.GetWord(i) & MaskForWord(i));");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TrySupremum(ref Set destination, global::System.ReadOnlySpan<Set> values)");
        sb.AppendLine("        {");
        sb.AppendLine("            destination = default;");
        sb.AppendLine("            for (var i = 0; i < values.Length; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                for (var word = 0; word < WordCount; word++)");
        sb.AppendLine("                    destination.SetWord(word, (destination.GetWord(word) | values[i].GetWord(word)) & MaskForWord(word));");
        sb.AppendLine("            }");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus TryInfimum(ref Set destination, global::System.ReadOnlySpan<Set> values)");
        sb.AppendLine("        {");
        sb.AppendLine("            destination = Top;");
        sb.AppendLine("            for (var i = 0; i < values.Length; i++)");
        sb.AppendLine("            {");
        sb.AppendLine("                for (var word = 0; word < WordCount; word++)");
        sb.AppendLine("                    destination.SetWord(word, (destination.GetWord(word) & values[i].GetWord(word)) & MaskForWord(word));");
        sb.AppendLine("            }");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        internal static ulong MaskForWord(int wordIndex)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (wordIndex < WordCount - 1)");
        sb.AppendLine("                return ulong.MaxValue;");
        if (finalWordBits == 0)
            sb.AppendLine("            return ulong.MaxValue;");
        else
            sb.Append("            return (1UL << ").Append(finalWordBits).AppendLine(") - 1UL;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    [global::System.Runtime.CompilerServices.InlineArray(WordCount)]");
        sb.AppendLine("    private struct WordBuffer");
        sb.AppendLine("    {");
        sb.AppendLine("        private ulong _element0;");
        sb.AppendLine("        public global::System.Span<ulong> AsSpan() => global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref _element0, WordCount);");
        sb.AppendLine("    }");
    }

    private static void GenerateWittVectorAuthoringSurface(
        StringBuilder sb,
        WittVectorScopeModel scope,
        bool emitRunner)
    {
        var componentType = scope.ComponentType;
        var componentOpsType = scope.ComponentOpsType;
        var viewType = "global::HPD.Math.Numerics.WittVectorView<" + componentType + ">";
        var builderType = "global::HPD.Math.Numerics.WittVectorBuilder<" + componentType + ">";

        sb.AppendLine();
        sb.Append("    public const int HandleCapacity = ").Append(scope.Handles).AppendLine(";");
        sb.Append("    public static int Prime => ").Append(scope.PrimeType).AppendLine(".Value;");
        sb.Append("    public static int Length => ").Append(scope.LengthType).AppendLine(".Value;");
        sb.AppendLine();
        if (emitRunner)
        {
            sb.AppendLine("    public global::HPD.Math.Core.AlgebraStatus Run(ref Result result)");
            sb.AppendLine("    {");
            sb.AppendLine("        result.Clear();");
            sb.Append("        global::System.Span<").Append(componentType).AppendLine("> components = stackalloc " + componentType + "[Length * HandleCapacity];");
            sb.AppendLine("        global::System.Span<int> state = stackalloc int[3];");
            sb.AppendLine("        var scope = new Scope(components, state);");
            sb.AppendLine("        Build(ref scope);");
            sb.AppendLine("        var status = scope.CopyReturned(result.ComponentStorage, out var componentCount);");
            sb.AppendLine("        if (scope.Status != global::HPD.Math.Core.AlgebraStatus.Ok)");
            sb.AppendLine("            return scope.Status;");
            sb.AppendLine("        if (status == global::HPD.Math.Core.AlgebraStatus.Ok)");
            sb.AppendLine("            result.SetComponentCount(componentCount);");
            sb.AppendLine("        return status;");
            sb.AppendLine("    }");
            sb.AppendLine("    partial void Build(ref Scope w);");
            sb.AppendLine();
            sb.AppendLine("    public struct Result");
            sb.AppendLine("    {");
            sb.AppendLine("        private ComponentBuffer _components;");
            sb.AppendLine("        public int ComponentCount { get; private set; }");
            sb.Append("        public ").Append(componentType).AppendLine(" ComponentAt(int index) => _components[index];");
            sb.Append("        internal global::System.Span<").Append(componentType).AppendLine("> ComponentStorage => _components.AsSpan();");
            sb.AppendLine("        internal void SetComponentCount(int count) => ComponentCount = count;");
            sb.AppendLine("        internal void Clear() => ComponentCount = 0;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    [global::System.Runtime.CompilerServices.InlineArray(HandleCapacity)]");
            sb.AppendLine("    private struct ComponentBuffer");
            sb.AppendLine("    {");
            sb.Append("        private ").Append(componentType).AppendLine(" _element0;");
            sb.Append("        public global::System.Span<").Append(componentType).AppendLine("> AsSpan() => global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref _element0, HandleCapacity);");
            sb.AppendLine("    }");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("    public static " + componentOpsType + " CreateOps() => new();");
            sb.AppendLine();
            sb.Append("    public static Scope CreateScope(global::System.Span<").Append(componentType)
                .AppendLine("> components, global::System.Span<int> state) =>");
            sb.AppendLine("        new(components, state);");
            sb.AppendLine();
        }
        sb.AppendLine("    public ref struct Scope");
        sb.AppendLine("    {");
        sb.Append("        private readonly global::System.Span<").Append(componentType).AppendLine("> _components;");
        sb.AppendLine("        private readonly global::System.Span<int> _state;");
        sb.AppendLine("        public Scope(global::System.Span<" + componentType + "> components, global::System.Span<int> state)");
        sb.AppendLine("        {");
        sb.AppendLine("            _components = components; _state = state; _state[0] = 0; _state[1] = (int)global::HPD.Math.Core.AlgebraStatus.Ok; _state[2] = -1;");
        sb.AppendLine("        }");
        sb.AppendLine("        public readonly global::HPD.Math.Core.AlgebraStatus Status => (global::HPD.Math.Core.AlgebraStatus)_state[1];");
        sb.AppendLine("        public Vector Zero() => KernelUnaryZero(one: false);");
        sb.AppendLine("        public Vector One() => KernelUnaryZero(one: true);");
        sb.AppendLine("        public Vector FromComponents(scoped global::System.ReadOnlySpan<" + componentType + "> components)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!TryAllocate(out var handle)) return InvalidVector();");
        sb.AppendLine("            if (components.Length != Length) return FailAndReturn(global::HPD.Math.Core.AlgebraStatus.DimensionMismatch);");
        sb.AppendLine("            components.CopyTo(ComponentSlot(handle));");
        sb.AppendLine("            return CreateVector(handle);");
        sb.AppendLine("        }");
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus Ghost(Vector value, int index, ref " + componentType + " destination)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!IsValid(value.Handle)) return FailStatus(global::HPD.Math.Core.AlgebraStatus.InvalidInput);");
        sb.AppendLine("            return global::HPD.Math.Numerics.WittVectorKernels.TryGhostComponent<" + scope.PrimeType + ", " + scope.LengthType + ", " + componentType + ", " + componentOpsType + ", " + componentOpsType + ">(View(value.Handle), index, ref destination, new " + componentOpsType + "(), new " + componentOpsType + "());");
        sb.AppendLine("        }");
        sb.AppendLine("        public void Return(Vector value)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!IsValid(value.Handle)) { Fail(global::HPD.Math.Core.AlgebraStatus.InvalidInput); return; }");
        sb.AppendLine("            _state[2] = value.Handle;");
        sb.AppendLine("        }");
        sb.AppendLine("        public global::HPD.Math.Core.AlgebraStatus CopyReturned(global::System.Span<" + componentType + "> outputComponents, out int componentCount)");
        sb.AppendLine("        {");
        sb.AppendLine("            componentCount = 0;");
        sb.AppendLine("            if (Status != global::HPD.Math.Core.AlgebraStatus.Ok) return Status;");
        sb.AppendLine("            if (!IsValid(_state[2])) return global::HPD.Math.Core.AlgebraStatus.InvalidInput;");
        sb.AppendLine("            if (outputComponents.Length < Length) return global::HPD.Math.Core.AlgebraStatus.InsufficientDestination;");
        sb.AppendLine("            ComponentSlot(_state[2]).CopyTo(outputComponents);");
        sb.AppendLine("            componentCount = Length;");
        sb.AppendLine("            return global::HPD.Math.Core.AlgebraStatus.Ok;");
        sb.AppendLine("        }");
        sb.AppendLine("        private Vector KernelUnaryZero(bool one)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!TryAllocate(out var handle)) return InvalidVector();");
        sb.AppendLine("            var builder = Builder(handle);");
        sb.AppendLine("            var status = one");
        sb.AppendLine("                ? global::HPD.Math.Numerics.WittVectorKernels.TryOne<" + scope.PrimeType + ", " + scope.LengthType + ", " + componentType + ", " + componentOpsType + ">(ref builder, new " + componentOpsType + "())");
        sb.AppendLine("                : global::HPD.Math.Numerics.WittVectorKernels.TryZero<" + scope.PrimeType + ", " + scope.LengthType + ", " + componentType + ", " + componentOpsType + ">(ref builder, new " + componentOpsType + "());");
        sb.AppendLine("            return status == global::HPD.Math.Core.AlgebraStatus.Ok ? CreateVector(handle) : FailAndReturn(status);");
        sb.AppendLine("        }");
        sb.AppendLine("        private bool TryAllocate(out int handle)");
        sb.AppendLine("        {");
        sb.AppendLine("            handle = -1; if (Status != global::HPD.Math.Core.AlgebraStatus.Ok) return false;");
        sb.AppendLine("            if (_state[0] >= HandleCapacity) { Fail(global::HPD.Math.Core.AlgebraStatus.InsufficientDestination); return false; }");
        sb.AppendLine("            handle = _state[0]++; return true;");
        sb.AppendLine("        }");
        sb.AppendLine("        private bool IsValid(int handle) => handle >= 0 && handle < _state[0];");
        sb.AppendLine("        private void Fail(global::HPD.Math.Core.AlgebraStatus status) { if (Status == global::HPD.Math.Core.AlgebraStatus.Ok) _state[1] = (int)status; }");
        sb.AppendLine("        private global::HPD.Math.Core.AlgebraStatus FailStatus(global::HPD.Math.Core.AlgebraStatus status) { Fail(status); return status; }");
        sb.AppendLine("        private Vector FailAndReturn(global::HPD.Math.Core.AlgebraStatus status) { Fail(status); return InvalidVector(); }");
        sb.AppendLine("        private Vector InvalidVector() => CreateVector(-1);");
        sb.AppendLine("        private Vector CreateVector(int handle) => new(_components, _state, handle);");
        sb.AppendLine("        private global::System.Span<" + componentType + "> ComponentSlot(int handle) => _components.Slice(handle * Length, Length);");
        sb.AppendLine("        private " + builderType + " Builder(int handle) => new(ComponentSlot(handle));");
        sb.AppendLine("        private " + viewType + " View(int handle) => new(ComponentSlot(handle));");
        sb.AppendLine("    }");
        sb.AppendLine("    public readonly ref struct Vector");
        sb.AppendLine("    {");
        sb.Append("        private readonly global::System.Span<").Append(componentType).AppendLine("> _components;");
        sb.AppendLine("        private readonly global::System.Span<int> _state;");
        sb.AppendLine("        internal readonly int Handle;");
        sb.AppendLine("        internal Vector(global::System.Span<" + componentType + "> components, global::System.Span<int> state, int handle) { _components = components; _state = state; Handle = handle; }");
        sb.AppendLine("        public " + componentType + " this[int index] => ComponentSlot(Handle)[index];");
        sb.AppendLine("        public Vector Add(Vector other) => Binary(other, add: true);");
        sb.AppendLine("        public Vector Mul(Vector other) => Binary(other, add: false);");
        sb.AppendLine("        private Vector Binary(Vector other, bool add)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!CanOperate(other, out var handle)) return InvalidVector();");
        sb.AppendLine("            var builder = Builder(handle);");
        sb.AppendLine("            var status = add");
        sb.AppendLine("                ? global::HPD.Math.Numerics.WittVectorKernels.TryAdd<" + scope.PrimeType + ", " + scope.LengthType + ", " + componentType + ", " + componentOpsType + ", " + componentOpsType + ">(View(Handle), other.View(other.Handle), ref builder, new " + componentOpsType + "(), new " + componentOpsType + "())");
        sb.AppendLine("                : global::HPD.Math.Numerics.WittVectorKernels.TryMul<" + scope.PrimeType + ", " + scope.LengthType + ", " + componentType + ", " + componentOpsType + ", " + componentOpsType + ">(View(Handle), other.View(other.Handle), ref builder, new " + componentOpsType + "(), new " + componentOpsType + "());");
        sb.AppendLine("            if (status != global::HPD.Math.Core.AlgebraStatus.Ok) { Fail(status); return InvalidVector(); }");
        sb.AppendLine("            return new Vector(_components, _state, handle);");
        sb.AppendLine("        }");
        sb.AppendLine("        private bool CanOperate(Vector other, out int handle) { handle = -1; if (Status != global::HPD.Math.Core.AlgebraStatus.Ok) return false; if (!IsValid(Handle) || !IsValid(other.Handle)) return Fail(global::HPD.Math.Core.AlgebraStatus.InvalidInput); return TryAllocate(out handle); }");
        sb.AppendLine("        private bool TryAllocate(out int handle) { handle = -1; if (_state[0] >= HandleCapacity) return Fail(global::HPD.Math.Core.AlgebraStatus.InsufficientDestination); handle = _state[0]++; return true; }");
        sb.AppendLine("        private global::HPD.Math.Core.AlgebraStatus Status => (global::HPD.Math.Core.AlgebraStatus)_state[1];");
        sb.AppendLine("        private bool IsValid(int handle) => handle >= 0 && handle < _state[0];");
        sb.AppendLine("        private bool Fail(global::HPD.Math.Core.AlgebraStatus status) { if (Status == global::HPD.Math.Core.AlgebraStatus.Ok) _state[1] = (int)status; return false; }");
        sb.AppendLine("        private Vector InvalidVector() => new(_components, _state, -1);");
        sb.AppendLine("        private global::System.Span<" + componentType + "> ComponentSlot(int handle) => _components.Slice(handle * Length, Length);");
        sb.AppendLine("        private " + builderType + " Builder(int handle) => new(ComponentSlot(handle));");
        sb.AppendLine("        private " + viewType + " View(int handle) => new(ComponentSlot(handle));");
        sb.AppendLine("    }");
    }

    private static bool HasGeneratedExtensionMembers(GeneratedType model) =>
        model.PolynomialContext is not null ||
        model.PolynomialScope is not null ||
        model.MatrixContext is not null ||
        model.MatrixScope is not null ||
        model.ReverseDiffContext is not null ||
        model.ReverseDiffScope is not null ||
        model.PolynomialQuotientContext is not null ||
        model.PolynomialQuotientScope is not null ||
        model.FieldExtensionContext is not null ||
        model.FieldExtensionScope is not null ||
        model.RationalFunctionContext is not null ||
        model.RationalFunctionScope is not null ||
        model.PadicContext is not null ||
        model.PadicScope is not null ||
        model.WittVectorContext is not null ||
        model.WittVectorScope is not null;

    private static void GenerateExtensionMembers(StringBuilder sb, GeneratedType model)
    {
        var hostType = model.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var accessibility = model.Modifiers.Contains("public") ? "public" : "internal";
        var className = SanitizeIdentifier(
            model.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", "")) + "Extensions";

        sb.AppendLine();
        sb.Append(accessibility).Append(" static class ").Append(className).AppendLine();
        sb.AppendLine("{");
        if (model.PolynomialScope is not null)
            GenerateBinaryExtensionBlock(sb, hostType + ".Poly", add: true, sub: false, mul: true, neg: false);
        if (model.MatrixScope is not null)
            GenerateBinaryExtensionBlock(sb, hostType + ".Matrix", add: true, sub: false, mul: true, neg: false);
        if (model.ReverseDiffContext is not null || model.ReverseDiffScope is not null)
            GenerateBinaryExtensionBlock(sb, hostType + ".Var", add: true, sub: true, mul: true, neg: true);
        if (model.PolynomialQuotientScope is not null ||
            model.FieldExtensionScope is not null)
            GenerateBinaryExtensionBlock(sb, hostType + ".Element", add: true, sub: false, mul: true, neg: false);
        if (model.RationalFunctionScope is not null)
            GenerateBinaryExtensionBlock(sb, hostType + ".Value", add: false, sub: false, mul: true, neg: false);
        if (model.PadicScope is not null)
            GenerateBinaryExtensionBlock(sb, hostType + ".Value", add: true, sub: false, mul: true, neg: false);
        if (model.WittVectorScope is not null)
            GenerateBinaryExtensionBlock(sb, hostType + ".Vector", add: true, sub: false, mul: true, neg: false);
        sb.AppendLine("}");
    }

    private static void GenerateBinaryExtensionBlock(
        StringBuilder sb,
        string handleType,
        bool add,
        bool sub,
        bool mul,
        bool neg)
    {
        sb.Append("    extension(").Append(handleType).AppendLine(" receiver)");
        sb.AppendLine("    {");
        if (add)
            sb.Append("        public static ").Append(handleType).Append(" operator +(").Append(handleType)
                .Append(" left, ").Append(handleType).AppendLine(" right) => left.Add(right);");
        if (sub)
            sb.Append("        public static ").Append(handleType).Append(" operator -(").Append(handleType)
                .Append(" left, ").Append(handleType).AppendLine(" right) => left.Sub(right);");
        if (mul)
            sb.Append("        public static ").Append(handleType).Append(" operator *(").Append(handleType)
                .Append(" left, ").Append(handleType).AppendLine(" right) => left.Mul(right);");
        if (neg)
            sb.Append("        public static ").Append(handleType).Append(" operator -(").Append(handleType)
                .AppendLine(" value) => value.Neg();");
        sb.AppendLine("    }");
    }

    private static string SanitizeIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
            builder.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');

        return builder.Length == 0 || char.IsDigit(builder[0])
            ? "_" + builder
            : builder.ToString();
    }

    private static int? GetIntAttribute(
        ImmutableArray<AttributeData> attributes,
        string fullName,
        string shortName)
    {
        foreach (var attribute in attributes)
        {
            var name = attribute.AttributeClass?.ToDisplayString();
            if (name != fullName && name != shortName)
                continue;

            if (attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is int value)
                return value;
        }

        return null;
    }

    private static PolynomialScopeModel? GetPolynomialContextAttribute(ImmutableArray<AttributeData> attributes) =>
        GetPolynomialAuthoringAttribute(
            attributes,
            "HPD.Math.Core.PolynomialContextAttribute",
            "PolynomialContextAttribute");

    private static PolynomialScopeModel? GetSparsePolynomialContextAttribute(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            var name = attribute.AttributeClass?.ToDisplayString();
            if (name != "HPD.Math.Core.SparsePolynomialContextAttribute" &&
                name != "SparsePolynomialContextAttribute")
                continue;

            if (attribute.ConstructorArguments.Length != 2)
                return null;

            var coefficientType = attribute.ConstructorArguments[0].Value as ITypeSymbol;
            var coefficientOpsType = attribute.ConstructorArguments[1].Value as ITypeSymbol;

            if (coefficientType is null || coefficientOpsType is null)
                return null;

            var terms = 32;
            foreach (var namedArgument in attribute.NamedArguments)
            {
                if (namedArgument.Key == "Terms" && namedArgument.Value.Value is int termValue)
                    terms = termValue;
            }

            return new PolynomialScopeModel(
                coefficientType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                coefficientOpsType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                terms,
                workspace: 0,
                handles: 0);
        }

        return null;
    }

    private static PolynomialScopeModel? GetPolynomialScopeAttribute(ImmutableArray<AttributeData> attributes) =>
        GetPolynomialAuthoringAttribute(
            attributes,
            "HPD.Math.Core.PolynomialScopeAttribute",
            "PolynomialScopeAttribute");

    private static PolynomialScopeModel? GetPolynomialAuthoringAttribute(
        ImmutableArray<AttributeData> attributes,
        string fullName,
        string shortName)
    {
        foreach (var attribute in attributes)
        {
            var name = attribute.AttributeClass?.ToDisplayString();
            if (name != fullName && name != shortName)
                continue;

            if (attribute.ConstructorArguments.Length != 2)
                return null;

            var coefficientType = attribute.ConstructorArguments[0].Value as ITypeSymbol;
            var coefficientOpsType = attribute.ConstructorArguments[1].Value as ITypeSymbol;

            if (coefficientType is null || coefficientOpsType is null)
                return null;

            var terms = 32;
            var workspace = 64;
            var handles = 16;
            foreach (var namedArgument in attribute.NamedArguments)
            {
                if (namedArgument.Key == "Terms" && namedArgument.Value.Value is int termValue)
                    terms = termValue;
                else if (namedArgument.Key == "Workspace" && namedArgument.Value.Value is int workspaceValue)
                    workspace = workspaceValue;
                else if (namedArgument.Key == "Handles" && namedArgument.Value.Value is int handlesValue)
                    handles = handlesValue;
            }

            return new PolynomialScopeModel(
                coefficientType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                coefficientOpsType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                terms,
                workspace,
                handles);
        }

        return null;
    }

    private static MatrixScopeModel? GetMatrixContextAttribute(ImmutableArray<AttributeData> attributes) =>
        GetMatrixAuthoringAttribute(
            attributes,
            "HPD.Math.Core.MatrixContextAttribute",
            "MatrixContextAttribute");

    private static MatrixScopeModel? GetMatrixScopeAttribute(ImmutableArray<AttributeData> attributes) =>
        GetMatrixAuthoringAttribute(
            attributes,
            "HPD.Math.Core.MatrixScopeAttribute",
            "MatrixScopeAttribute");

    private static MatrixScopeModel? GetMatrixAuthoringAttribute(
        ImmutableArray<AttributeData> attributes,
        string fullName,
        string shortName)
    {
        foreach (var attribute in attributes)
        {
            var name = attribute.AttributeClass?.ToDisplayString();
            if (name != fullName && name != shortName)
                continue;

            if (attribute.ConstructorArguments.Length != 2)
                return null;

            var elementType = attribute.ConstructorArguments[0].Value as ITypeSymbol;
            var elementOpsType = attribute.ConstructorArguments[1].Value as ITypeSymbol;

            if (elementType is null || elementOpsType is null)
                return null;

            var rows = 2;
            var columns = 2;
            var handles = 16;

            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.Value.Value is not int value)
                    continue;

                switch (argument.Key)
                {
                    case "Rows":
                        rows = value;
                        break;
                    case "Columns":
                        columns = value;
                        break;
                    case "Handles":
                        handles = value;
                        break;
                }
            }

            return new MatrixScopeModel(
                elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                elementOpsType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                rows,
                columns,
                handles);
        }

        return null;
    }

    private static ReverseDiffScopeModel? GetReverseDiffContextAttribute(ImmutableArray<AttributeData> attributes) =>
        GetReverseDiffAuthoringAttribute(
            attributes,
            "HPD.Math.Core.ReverseDiffContextAttribute",
            "ReverseDiffContextAttribute");

    private static ReverseDiffScopeModel? GetReverseDiffScopeAttribute(ImmutableArray<AttributeData> attributes) =>
        GetReverseDiffAuthoringAttribute(
            attributes,
            "HPD.Math.Core.ReverseDiffScopeAttribute",
            "ReverseDiffScopeAttribute");

    private static ReverseDiffScopeModel? GetReverseDiffAuthoringAttribute(
        ImmutableArray<AttributeData> attributes,
        string fullName,
        string shortName)
    {
        foreach (var attribute in attributes)
        {
            var name = attribute.AttributeClass?.ToDisplayString();
            if (name != fullName && name != shortName)
                continue;

            if (attribute.ConstructorArguments.Length != 2)
                return null;

            var scalarType = attribute.ConstructorArguments[0].Value as ITypeSymbol;
            var scalarOpsType = attribute.ConstructorArguments[1].Value as ITypeSymbol;

            if (scalarType is null || scalarOpsType is null)
                return null;

            var nodes = 32;
            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.Key == "Nodes" && argument.Value.Value is int value)
                    nodes = value;
            }

            return new ReverseDiffScopeModel(
                scalarType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                scalarOpsType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                nodes);
        }

        return null;
    }

    private static PolynomialQuotientScopeModel? GetPolynomialQuotientContextAttribute(ImmutableArray<AttributeData> attributes) =>
        GetPolynomialQuotientAuthoringAttribute(
            attributes,
            "HPD.Math.Core.PolynomialQuotientContextAttribute",
            "PolynomialQuotientContextAttribute");

    private static PolynomialQuotientScopeModel? GetPolynomialQuotientScopeAttribute(ImmutableArray<AttributeData> attributes) =>
        GetPolynomialQuotientAuthoringAttribute(
            attributes,
            "HPD.Math.Core.PolynomialQuotientScopeAttribute",
            "PolynomialQuotientScopeAttribute");

    private static PolynomialQuotientScopeModel? GetPolynomialQuotientAuthoringAttribute(
        ImmutableArray<AttributeData> attributes,
        string fullName,
        string shortName)
    {
        foreach (var attribute in attributes)
        {
            var name = attribute.AttributeClass?.ToDisplayString();
            if (name != fullName && name != shortName)
                continue;

            if (attribute.ConstructorArguments.Length != 2)
                return null;

            var coefficientType = attribute.ConstructorArguments[0].Value as ITypeSymbol;
            var coefficientOpsType = attribute.ConstructorArguments[1].Value as ITypeSymbol;

            if (coefficientType is null || coefficientOpsType is null)
                return null;

            var terms = 8;
            var handles = 16;
            var workspace = 16;
            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.Value.Value is not int value)
                    continue;

                switch (argument.Key)
                {
                    case "Terms":
                        terms = value;
                        break;
                    case "Handles":
                        handles = value;
                        break;
                    case "Workspace":
                        workspace = value;
                        break;
                }
            }

            return new PolynomialQuotientScopeModel(
                coefficientType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                coefficientOpsType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                terms,
                handles,
                workspace,
                ImplementsStatusFieldOps(coefficientOpsType, coefficientType));
        }

        return null;
    }

    private static RationalFunctionScopeModel? GetRationalFunctionContextAttribute(ImmutableArray<AttributeData> attributes) =>
        GetRationalFunctionAuthoringAttribute(
            attributes,
            "HPD.Math.Core.RationalFunctionContextAttribute",
            "RationalFunctionContextAttribute");

    private static RationalFunctionScopeModel? GetRationalFunctionScopeAttribute(ImmutableArray<AttributeData> attributes) =>
        GetRationalFunctionAuthoringAttribute(
            attributes,
            "HPD.Math.Core.RationalFunctionScopeAttribute",
            "RationalFunctionScopeAttribute");

    private static RationalFunctionScopeModel? GetRationalFunctionAuthoringAttribute(
        ImmutableArray<AttributeData> attributes,
        string fullName,
        string shortName)
    {
        foreach (var attribute in attributes)
        {
            var name = attribute.AttributeClass?.ToDisplayString();
            if (name != fullName && name != shortName)
                continue;

            if (attribute.ConstructorArguments.Length != 2)
                return null;

            var coefficientType = attribute.ConstructorArguments[0].Value as ITypeSymbol;
            var coefficientOpsType = attribute.ConstructorArguments[1].Value as ITypeSymbol;

            if (coefficientType is null || coefficientOpsType is null)
                return null;

            var terms = 8;
            var handles = 16;
            var workspace = 16;
            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.Value.Value is not int value)
                    continue;

                switch (argument.Key)
                {
                    case "Terms":
                        terms = value;
                        break;
                    case "Handles":
                        handles = value;
                        break;
                    case "Workspace":
                        workspace = value;
                        break;
                }
            }

            return new RationalFunctionScopeModel(
                coefficientType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                coefficientOpsType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                terms,
                handles,
                workspace);
        }

        return null;
    }

    private static PolynomialQuotientScopeModel? GetFieldExtensionContextAttribute(ImmutableArray<AttributeData> attributes) =>
        GetFieldExtensionAuthoringAttribute(
            attributes,
            "HPD.Math.Core.FieldExtensionContextAttribute",
            "FieldExtensionContextAttribute");

    private static PolynomialQuotientScopeModel? GetFieldExtensionScopeAttribute(ImmutableArray<AttributeData> attributes) =>
        GetFieldExtensionAuthoringAttribute(
            attributes,
            "HPD.Math.Core.FieldExtensionScopeAttribute",
            "FieldExtensionScopeAttribute");

    private static PolynomialQuotientScopeModel? GetFieldExtensionAuthoringAttribute(
        ImmutableArray<AttributeData> attributes,
        string fullName,
        string shortName)
    {
        foreach (var attribute in attributes)
        {
            var name = attribute.AttributeClass?.ToDisplayString();
            if (name != fullName && name != shortName)
                continue;

            if (attribute.ConstructorArguments.Length != 2)
                return null;

            var coefficientType = attribute.ConstructorArguments[0].Value as ITypeSymbol;
            var coefficientOpsType = attribute.ConstructorArguments[1].Value as ITypeSymbol;

            if (coefficientType is null || coefficientOpsType is null)
                return null;

            var terms = 8;
            var handles = 16;
            var workspace = 16;
            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.Value.Value is not int value)
                    continue;

                switch (argument.Key)
                {
                    case "Terms":
                        terms = value;
                        break;
                    case "Handles":
                        handles = value;
                        break;
                    case "Workspace":
                        workspace = value;
                        break;
                }
            }

            return new PolynomialQuotientScopeModel(
                coefficientType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                coefficientOpsType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                terms,
                handles,
                workspace,
                ImplementsStatusFieldOps(coefficientOpsType, coefficientType));
        }

        return null;
    }

    private static bool ImplementsStatusFieldOps(ITypeSymbol opsType, ITypeSymbol coefficientType)
    {
        foreach (var iface in opsType.AllInterfaces)
        {
            if (iface.Name == "IStatusFieldOps" &&
                iface.TypeArguments.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(iface.TypeArguments[0], coefficientType))
                return true;
        }

        return false;
    }

    private static PadicScopeModel? GetPadicContextAttribute(ImmutableArray<AttributeData> attributes) =>
        GetPadicAuthoringAttribute(
            attributes,
            "HPD.Math.Core.PadicContextAttribute",
            "PadicContextAttribute");

    private static PadicScopeModel? GetPadicScopeAttribute(ImmutableArray<AttributeData> attributes) =>
        GetPadicAuthoringAttribute(
            attributes,
            "HPD.Math.Core.PadicScopeAttribute",
            "PadicScopeAttribute");

    private static PadicScopeModel? GetPadicAuthoringAttribute(
        ImmutableArray<AttributeData> attributes,
        string fullName,
        string shortName)
    {
        foreach (var attribute in attributes)
        {
            var name = attribute.AttributeClass?.ToDisplayString();
            if (name != fullName && name != shortName)
                continue;

            if (attribute.ConstructorArguments.Length != 2)
                return null;

            var primeType = attribute.ConstructorArguments[0].Value as ITypeSymbol;
            var precisionType = attribute.ConstructorArguments[1].Value as ITypeSymbol;

            if (primeType is null || precisionType is null)
                return null;

            var handles = 16;
            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.Key == "Handles" && argument.Value.Value is int value)
                    handles = value;
            }

            return new PadicScopeModel(
                primeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                precisionType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                handles);
        }

        return null;
    }

    private static WittVectorContextModel? GetWittVectorContextAttribute(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            var name = attribute.AttributeClass?.ToDisplayString();
            if (name != "HPD.Math.Core.WittVectorContextAttribute" &&
                name != "WittVectorContextAttribute")
                continue;

            if (attribute.ConstructorArguments.Length != 4 ||
                attribute.ConstructorArguments[3].Value is not int length)
                return null;

            var componentType = attribute.ConstructorArguments[0].Value as ITypeSymbol;
            var componentOpsType = attribute.ConstructorArguments[1].Value as ITypeSymbol;
            var primeType = attribute.ConstructorArguments[2].Value as ITypeSymbol;

            if (componentType is null || componentOpsType is null || primeType is null)
                return null;

            return new WittVectorContextModel(
                componentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                componentOpsType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                primeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                length);
        }

        return null;
    }

    private static WittVectorScopeModel? GetWittVectorScopeAttribute(ImmutableArray<AttributeData> attributes) =>
        GetWittVectorAuthoringAttribute(
            attributes,
            "HPD.Math.Core.WittVectorScopeAttribute",
            "WittVectorScopeAttribute");

    private static WittVectorScopeModel? GetWittVectorAuthoringAttribute(
        ImmutableArray<AttributeData> attributes,
        string fullName,
        string shortName)
    {
        foreach (var attribute in attributes)
        {
            var name = attribute.AttributeClass?.ToDisplayString();
            if (name != fullName && name != shortName)
                continue;

            if (attribute.ConstructorArguments.Length != 4)
                return null;

            var componentType = attribute.ConstructorArguments[0].Value as ITypeSymbol;
            var componentOpsType = attribute.ConstructorArguments[1].Value as ITypeSymbol;
            var primeType = attribute.ConstructorArguments[2].Value as ITypeSymbol;
            var lengthType = attribute.ConstructorArguments[3].Value as ITypeSymbol;

            if (componentType is null || componentOpsType is null || primeType is null || lengthType is null)
                return null;

            var handles = 16;
            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.Key == "Handles" && argument.Value.Value is int value)
                    handles = value;
            }

            return new WittVectorScopeModel(
                componentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                componentOpsType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                primeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                lengthType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                handles);
        }

        return null;
    }

    private static FinitePowerSetContextModel? GetFinitePowerSetContextAttribute(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            var name = attribute.AttributeClass?.ToDisplayString();
            if (name != "HPD.Math.Core.FinitePowerSetContextAttribute" &&
                name != "FinitePowerSetContextAttribute")
                continue;

            if (attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not int cardinality)
                return null;

            return new FinitePowerSetContextModel(cardinality);
        }

        return null;
    }


    private static string GetNamespace(INamedTypeSymbol symbol)
    {
        var ns = symbol.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace ? "" : ns.ToDisplayString();
    }

    private static ImmutableArray<ContainingType> GetContainingTypes(INamedTypeSymbol symbol)
    {
        var stack = new Stack<ContainingType>();
        var current = symbol.ContainingType;
        while (current is not null)
        {
            var kind = current.TypeKind == TypeKind.Struct ? "struct" :
                current.TypeKind == TypeKind.Interface ? "interface" :
                current.TypeKind == TypeKind.Class ? "class" :
                "class";

            stack.Push(new ContainingType(
                GetAccessibilityAndModifiers(current),
                kind,
                current.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            current = current.ContainingType;
        }

        return stack.ToImmutableArray();
    }

    private static string GetAccessibilityAndModifiers(INamedTypeSymbol symbol)
    {
        var accessibility = symbol.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            _ => "internal"
        };

        return symbol.IsStatic
            ? accessibility + " static partial"
            : accessibility + " partial";
    }

    private static string GetDeclarationModifiers(TypeDeclarationSyntax syntax)
    {
        var modifiers = new List<string>();
        foreach (var modifier in syntax.Modifiers)
        {
            if (modifier.IsKind(SyntaxKind.PartialKeyword))
                continue;
            modifiers.Add(modifier.Text);
        }

        return string.Join(" ", modifiers);
    }

    private sealed class GeneratedType
    {
        public GeneratedType(
            INamedTypeSymbol symbol,
            string @namespace,
            ImmutableArray<ContainingType> containingTypes,
            string modifiers,
            int? dimension,
            int? precision,
            int? primeModulus,
            PolynomialScopeModel? polynomialContext,
            PolynomialScopeModel? sparsePolynomialContext,
            PolynomialScopeModel? polynomialScope,
            MatrixScopeModel? matrixContext,
            MatrixScopeModel? matrixScope,
            ReverseDiffScopeModel? reverseDiffContext,
            ReverseDiffScopeModel? reverseDiffScope,
            PolynomialQuotientScopeModel? polynomialQuotientContext,
            PolynomialQuotientScopeModel? polynomialQuotientScope,
            RationalFunctionScopeModel? rationalFunctionContext,
            RationalFunctionScopeModel? rationalFunctionScope,
            PolynomialQuotientScopeModel? fieldExtensionContext,
            PolynomialQuotientScopeModel? fieldExtensionScope,
            PadicScopeModel? padicContext,
            PadicScopeModel? padicScope,
            WittVectorContextModel? wittVectorContext,
            WittVectorScopeModel? wittVectorScope,
            FinitePowerSetContextModel? finitePowerSetContext,
            ImmutableArray<Diagnostic> diagnostics,
            bool canGenerate)
        {
            Symbol = symbol;
            Namespace = @namespace;
            ContainingTypes = containingTypes;
            Modifiers = modifiers;
            Dimension = dimension;
            Precision = precision;
            PrimeModulus = primeModulus;
            PolynomialContext = polynomialContext;
            SparsePolynomialContext = sparsePolynomialContext;
            PolynomialScope = polynomialScope;
            MatrixContext = matrixContext;
            MatrixScope = matrixScope;
            ReverseDiffContext = reverseDiffContext;
            ReverseDiffScope = reverseDiffScope;
            PolynomialQuotientContext = polynomialQuotientContext;
            PolynomialQuotientScope = polynomialQuotientScope;
            RationalFunctionContext = rationalFunctionContext;
            RationalFunctionScope = rationalFunctionScope;
            FieldExtensionContext = fieldExtensionContext;
            FieldExtensionScope = fieldExtensionScope;
            PadicContext = padicContext;
            PadicScope = padicScope;
            WittVectorContext = wittVectorContext;
            WittVectorScope = wittVectorScope;
            FinitePowerSetContext = finitePowerSetContext;
            Diagnostics = diagnostics;
            CanGenerate = canGenerate;
        }

        public INamedTypeSymbol Symbol { get; }

        public string Namespace { get; }

        public ImmutableArray<ContainingType> ContainingTypes { get; }

        public string Modifiers { get; }

        public int? Dimension { get; }

        public int? Precision { get; }

        public int? PrimeModulus { get; }

        public PolynomialScopeModel? PolynomialContext { get; }

        public PolynomialScopeModel? SparsePolynomialContext { get; }

        public PolynomialScopeModel? PolynomialScope { get; }

        public MatrixScopeModel? MatrixContext { get; }

        public MatrixScopeModel? MatrixScope { get; }

        public ReverseDiffScopeModel? ReverseDiffContext { get; }

        public ReverseDiffScopeModel? ReverseDiffScope { get; }

        public PolynomialQuotientScopeModel? PolynomialQuotientContext { get; }

        public PolynomialQuotientScopeModel? PolynomialQuotientScope { get; }

        public RationalFunctionScopeModel? RationalFunctionContext { get; }

        public RationalFunctionScopeModel? RationalFunctionScope { get; }

        public PolynomialQuotientScopeModel? FieldExtensionContext { get; }

        public PolynomialQuotientScopeModel? FieldExtensionScope { get; }

        public PadicScopeModel? PadicContext { get; }

        public PadicScopeModel? PadicScope { get; }

        public WittVectorContextModel? WittVectorContext { get; }

        public WittVectorScopeModel? WittVectorScope { get; }

        public FinitePowerSetContextModel? FinitePowerSetContext { get; }

        public ImmutableArray<Diagnostic> Diagnostics { get; }

        public bool CanGenerate { get; }

        public string DeclarationName => Symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        public string HintName =>
            Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", "")
                .Replace('<', '_')
                .Replace('>', '_')
                .Replace('.', '_') + ".g.cs";
    }

    private sealed class PolynomialScopeModel
    {
        public PolynomialScopeModel(
            string coefficientType,
            string coefficientOpsType,
            int terms,
            int workspace,
            int handles)
        {
            CoefficientType = coefficientType;
            CoefficientOpsType = coefficientOpsType;
            Terms = terms;
            Workspace = workspace;
            Handles = handles;
        }

        public string CoefficientType { get; }

        public string CoefficientOpsType { get; }

        public int Terms { get; }

        public int Workspace { get; }

        public int Handles { get; }
    }

    private sealed class MatrixScopeModel
    {
        public MatrixScopeModel(
            string elementType,
            string elementOpsType,
            int rows,
            int columns,
            int handles)
        {
            ElementType = elementType;
            ElementOpsType = elementOpsType;
            Rows = rows;
            Columns = columns;
            Handles = handles;
        }

        public string ElementType { get; }

        public string ElementOpsType { get; }

        public int Rows { get; }

        public int Columns { get; }

        public int Handles { get; }
    }

    private sealed class ReverseDiffScopeModel
    {
        public ReverseDiffScopeModel(string scalarType, string scalarOpsType, int nodes)
        {
            ScalarType = scalarType;
            ScalarOpsType = scalarOpsType;
            Nodes = nodes;
        }

        public string ScalarType { get; }

        public string ScalarOpsType { get; }

        public int Nodes { get; }
    }

    private sealed class PolynomialQuotientScopeModel
    {
        public PolynomialQuotientScopeModel(
            string coefficientType,
            string coefficientOpsType,
            int terms,
            int handles,
            int workspace,
            bool usesStatusFieldOps)
        {
            CoefficientType = coefficientType;
            CoefficientOpsType = coefficientOpsType;
            Terms = terms;
            Handles = handles;
            Workspace = workspace;
            UsesStatusFieldOps = usesStatusFieldOps;
        }

        public string CoefficientType { get; }

        public string CoefficientOpsType { get; }

        public int Terms { get; }

        public int Handles { get; }

        public int Workspace { get; }

        public bool UsesStatusFieldOps { get; }
    }

    private sealed class RationalFunctionScopeModel
    {
        public RationalFunctionScopeModel(
            string coefficientType,
            string coefficientOpsType,
            int terms,
            int handles,
            int workspace)
        {
            CoefficientType = coefficientType;
            CoefficientOpsType = coefficientOpsType;
            Terms = terms;
            Handles = handles;
            Workspace = workspace;
        }

        public string CoefficientType { get; }

        public string CoefficientOpsType { get; }

        public int Terms { get; }

        public int Handles { get; }

        public int Workspace { get; }
    }

    private sealed class PadicScopeModel
    {
        public PadicScopeModel(string primeType, string precisionType, int handles)
        {
            PrimeType = primeType;
            PrecisionType = precisionType;
            Handles = handles;
        }

        public string PrimeType { get; }

        public string PrecisionType { get; }

        public int Handles { get; }
    }

    private sealed class WittVectorScopeModel
    {
        public WittVectorScopeModel(
            string componentType,
            string componentOpsType,
            string primeType,
            string lengthType,
            int handles)
        {
            ComponentType = componentType;
            ComponentOpsType = componentOpsType;
            PrimeType = primeType;
            LengthType = lengthType;
            Handles = handles;
        }

        public string ComponentType { get; }

        public string ComponentOpsType { get; }

        public string PrimeType { get; }

        public string LengthType { get; }

        public int Handles { get; }
    }

    private sealed class WittVectorContextModel
    {
        public WittVectorContextModel(
            string componentType,
            string componentOpsType,
            string primeType,
            int length)
        {
            ComponentType = componentType;
            ComponentOpsType = componentOpsType;
            PrimeType = primeType;
            Length = length;
        }

        public string ComponentType { get; }

        public string ComponentOpsType { get; }

        public string PrimeType { get; }

        public int Length { get; }
    }

    private sealed class FinitePowerSetContextModel
    {
        public FinitePowerSetContextModel(int cardinality)
        {
            Cardinality = cardinality;
        }

        public int Cardinality { get; }
    }

    private readonly struct ContainingType
    {
        public ContainingType(string modifiers, string kind, string declarationName)
        {
            Modifiers = modifiers;
            Kind = kind;
            DeclarationName = declarationName;
        }

        public string Modifiers { get; }

        public string Kind { get; }

        public string DeclarationName { get; }
    }
}
