using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Helium.Generators;

[Generator]
public sealed class HeliumGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor InvalidDerivativeMethod = new(
        "CS_HELIUM_001",
        "CompileTimeDerivative supports expression-bodied polynomial methods only",
        "Method '{0}' is not supported by CompileTimeDerivative",
        "Helium.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidQuantizedBits = new(
        "CS_HELIUM_004",
        "Invalid QuantizedType bit width",
        "QuantizedType Bits must be a positive multiple of 64 and no larger than 4096",
        "Helium.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
            ctx.AddSource("HeliumGeneratorAttributes.g.cs", SourceText.From(AttributeSource, Encoding.UTF8)));

        var structs = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is StructDeclarationSyntax { AttributeLists.Count: > 0 },
                static (ctx, _) => GetStructModel(ctx))
            .Where(static model => model is not null)
            .Collect();

        context.RegisterSourceOutput(structs, static (ctx, models) =>
        {
            foreach (var model in models)
            {
                if (model is null)
                    continue;

                if (model.QuantizedBits is { } bits)
                {
                    if (!IsValidQuantizedBits(bits))
                    {
                        ctx.ReportDiagnostic(Diagnostic.Create(InvalidQuantizedBits, model.IdentifierLocation));
                        continue;
                    }

                    ctx.AddSource($"{model.MetadataName}.Quantized.g.cs", SourceText.From(GenerateQuantized(model, bits), Encoding.UTF8));
                }

                if (model.RnsModuli is { Length: > 0 } moduli)
                {
                    var error = ValidatePairwiseCoprime(moduli);
                    if (error is not null)
                    {
                        ctx.ReportDiagnostic(Diagnostic.Create(error, model.IdentifierLocation));
                        continue;
                    }

                    ctx.AddSource($"{model.MetadataName}.Rns.g.cs", SourceText.From(GenerateRns(model, moduli), Encoding.UTF8));
                }
            }
        });

        var methods = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                static (ctx, _) => GetDerivativeMethod(ctx))
            .Where(static model => model is not null);

        context.RegisterSourceOutput(methods, static (ctx, model) =>
        {
            if (model is null)
                return;

            if (!model.IsSupported)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(InvalidDerivativeMethod, model.IdentifierLocation, model.Name));
                return;
            }

            ctx.AddSource($"{model.ContainerMetadataName}.{model.Name}.Derivative.g.cs",
                SourceText.From(GenerateDerivative(model), Encoding.UTF8));
        });
    }

    private static StructModel? GetStructModel(GeneratorSyntaxContext context)
    {
        var syntax = (StructDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(syntax);
        if (symbol is null)
            return null;

        int? quantizedBits = null;
        ulong[]? rnsModuli = null;

        foreach (var attr in symbol.GetAttributes())
        {
            var name = attr.AttributeClass?.ToDisplayString();
            if (name is "Helium.Generators.QuantizedTypeAttribute" or "QuantizedTypeAttribute")
            {
                var bitsArg = attr.NamedArguments.FirstOrDefault(kv => kv.Key == "Bits").Value;
                if (bitsArg.Value is int namedBits)
                    quantizedBits = namedBits;
                else if (attr.ConstructorArguments.Length == 1 && attr.ConstructorArguments[0].Value is int ctorBits)
                    quantizedBits = ctorBits;
            }
            else if (name is "Helium.Generators.RnsTypeAttribute" or "RnsTypeAttribute")
            {
                if (attr.ConstructorArguments.Length == 1)
                    rnsModuli = attr.ConstructorArguments[0].Values
                        .Select(v => Convert.ToUInt64(v.Value))
                        .ToArray();
            }
        }

        if (quantizedBits is null && rnsModuli is null)
            return null;

        return new StructModel(
            symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.ToDisplayString(),
            symbol.Name,
            symbol.MetadataName,
            syntax.Identifier.GetLocation(),
            quantizedBits,
            rnsModuli);
    }

    private static DerivativeMethodModel? GetDerivativeMethod(GeneratorSyntaxContext context)
    {
        var syntax = (MethodDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(syntax);
        if (symbol is null)
            return null;

        var hasAttribute = symbol.GetAttributes().Any(attr =>
            attr.AttributeClass?.ToDisplayString() is "Helium.Generators.CompileTimeDerivativeAttribute" or "CompileTimeDerivativeAttribute");
        if (!hasAttribute)
            return null;

        if (syntax.ExpressionBody is null || syntax.Body is not null || syntax.ParameterList.Parameters.Count != 1)
        {
            return new DerivativeMethodModel(
                symbol.ContainingType.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingType.ContainingNamespace.ToDisplayString(),
                symbol.ContainingType.Name,
                symbol.ContainingType.MetadataName,
                symbol.Name,
                syntax.Identifier.GetLocation(),
                false,
                Array.Empty<long>());
        }

        var parameterName = syntax.ParameterList.Parameters[0].Identifier.ValueText;
        if (!TryParsePolynomial(syntax.ExpressionBody.Expression, parameterName, out var coefficients))
            coefficients = Array.Empty<long>();

        return new DerivativeMethodModel(
            symbol.ContainingType.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingType.ContainingNamespace.ToDisplayString(),
            symbol.ContainingType.Name,
            symbol.ContainingType.MetadataName,
            symbol.Name,
            syntax.Identifier.GetLocation(),
            coefficients.Length > 0,
            Derivative(coefficients));
    }

    private static DiagnosticDescriptor? ValidatePairwiseCoprime(IReadOnlyList<ulong> moduli)
    {
        for (var i = 0; i < moduli.Count; i++)
        {
            if (moduli[i] < 2)
                return new DiagnosticDescriptor("CS_HELIUM_002", "Invalid RNS modulus", "RNS modulus must be at least 2", "Helium.Generators", DiagnosticSeverity.Error, true);

            for (var j = i + 1; j < moduli.Count; j++)
            {
                if (Gcd(moduli[i], moduli[j]) != 1)
                    return new DiagnosticDescriptor("CS_HELIUM_003", "RNS moduli must be coprime", "RNS moduli must be pairwise coprime", "Helium.Generators", DiagnosticSeverity.Error, true);
            }
        }

        return null;
    }

    private static ulong Gcd(ulong a, ulong b)
    {
        while (b != 0)
            (a, b) = (b, a % b);
        return a;
    }

    private static ulong Barrett64(ulong modulus)
    {
        var q = ulong.MaxValue / modulus;
        var r = ulong.MaxValue % modulus;
        return r == modulus - 1 ? q + 1 : q;
    }

    private static string GenerateQuantized(StructModel model, int bits)
    {
        var limbs = bits / 64;
        var sb = new StringBuilder();
        AppendNamespaceStart(sb, model.Namespace);
        sb.Append("public partial struct ").Append(model.Name).AppendLine();
        sb.AppendLine("{");
        for (var i = 0; i < limbs; i++)
            sb.Append("    public ulong L").Append(i).AppendLine(" { get; }");
        sb.AppendLine();
        sb.Append("    public ").Append(model.Name).Append("(ulong l0");
        for (var i = 1; i < limbs; i++)
            sb.Append(", ulong l").Append(i).Append(" = 0");
        sb.AppendLine(")");
        sb.AppendLine("    {");
        for (var i = 0; i < limbs; i++)
            sb.Append("        L").Append(i).Append(" = l").Append(i).AppendLine(";");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.Append("    public static ").Append(model.Name).Append(" Zero => new ").Append(model.Name).AppendLine("(0);");
        sb.Append("    public static ").Append(model.Name).Append(" One => new ").Append(model.Name).AppendLine("(1);");
        sb.AppendLine();
        sb.Append("    public static ").Append(model.Name).Append(" operator +(").Append(model.Name).Append(" left, ").Append(model.Name).AppendLine(" right)");
        sb.AppendLine("    {");
        sb.AppendLine("        ulong carry = 0;");
        for (var i = 0; i < limbs; i++)
        {
            sb.Append("        var s").Append(i).Append(" = (System.UInt128)left.L").Append(i).Append(" + right.L").Append(i).AppendLine(" + carry;");
            sb.Append("        var r").Append(i).Append(" = (ulong)s").Append(i).AppendLine(";");
            sb.Append("        carry = (ulong)(s").Append(i).AppendLine(" >> 64);");
        }
        sb.Append("        return new ").Append(model.Name).Append("(");
        AppendArgs(sb, "r", limbs);
        sb.AppendLine(");");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.Append("    public static ").Append(model.Name).Append(" operator -(").Append(model.Name).Append(" left, ").Append(model.Name).AppendLine(" right)");
        sb.AppendLine("    {");
        sb.AppendLine("        ulong borrow = 0;");
        for (var i = 0; i < limbs; i++)
        {
            sb.Append("        var sub").Append(i).Append(" = (System.UInt128)right.L").Append(i).AppendLine(" + borrow;");
            sb.Append("        borrow = (System.UInt128)left.L").Append(i).Append(" < sub").Append(i).AppendLine(" ? 1UL : 0UL;");
            sb.Append("        var r").Append(i).Append(" = (ulong)((System.UInt128)left.L").Append(i).Append(" - sub").Append(i).AppendLine(");");
        }
        sb.Append("        return new ").Append(model.Name).Append("(");
        AppendArgs(sb, "r", limbs);
        sb.AppendLine(");");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.Append("    public static ").Append(model.Name).Append(" operator *(").Append(model.Name).Append(" left, ").Append(model.Name).AppendLine(" right)");
        sb.AppendLine("    {");
        for (var i = 0; i < limbs; i++)
            sb.Append("        ulong r").Append(i).AppendLine(" = 0;");
        for (var i = 0; i < limbs; i++)
        {
            sb.AppendLine("        {");
            sb.AppendLine("            ulong carry = 0;");
            for (var j = 0; i + j < limbs; j++)
            {
                sb.Append("            var p").Append(i).Append('_').Append(j)
                    .Append(" = (System.UInt128)left.L").Append(i)
                    .Append(" * right.L").Append(j)
                    .Append(" + r").Append(i + j)
                    .AppendLine(" + carry;");
                sb.Append("            r").Append(i + j).Append(" = (ulong)p").Append(i).Append('_').Append(j).AppendLine(";");
                sb.Append("            carry = (ulong)(p").Append(i).Append('_').Append(j).AppendLine(" >> 64);");
            }
            sb.AppendLine("        }");
        }
        sb.Append("        return new ").Append(model.Name).Append("(");
        AppendArgs(sb, "r", limbs);
        sb.AppendLine(");");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        AppendNamespaceEnd(sb, model.Namespace);
        return sb.ToString();
    }

    private static bool IsValidQuantizedBits(int bits) =>
        bits > 0 && bits <= 4096 && bits % 64 == 0;

    private static string GenerateRns(StructModel model, IReadOnlyList<ulong> moduli)
    {
        var sb = new StringBuilder();
        AppendNamespaceStart(sb, model.Namespace);
        sb.Append("public partial struct ").Append(model.Name).AppendLine();
        sb.AppendLine("{");
        for (var i = 0; i < moduli.Count; i++)
        {
            sb.Append("    public const ulong Modulus").Append(i).Append(" = ").Append(moduli[i]).AppendLine("UL;");
            sb.Append("    private const ulong Barrett").Append(i).Append(" = ").Append(Barrett64(moduli[i])).AppendLine("UL;");
            sb.Append("    public ulong R").Append(i).AppendLine(" { get; }");
        }
        sb.AppendLine();
        sb.Append("    public ").Append(model.Name).Append("(ulong value)");
        sb.AppendLine();
        sb.AppendLine("    {");
        for (var i = 0; i < moduli.Count; i++)
            sb.Append("        R").Append(i).Append(" = Reduce").Append(i).AppendLine("(value);");
        sb.AppendLine("    }");
        sb.AppendLine();
        if (moduli.Count > 1)
        {
            sb.Append("    private ").Append(model.Name).Append("(");
            AppendRnsResidueParameters(sb, moduli.Count);
            sb.AppendLine(")");
            sb.AppendLine("    {");
            for (var i = 0; i < moduli.Count; i++)
                sb.Append("        R").Append(i).Append(" = r").Append(i).AppendLine(";");
            sb.AppendLine("    }");
            sb.AppendLine();
        }
        sb.Append("    public static ").Append(model.Name).Append(" Zero => new ").Append(model.Name).AppendLine("(0UL);");
        sb.Append("    public static ").Append(model.Name).Append(" One => new ").Append(model.Name).AppendLine("(1UL);");
        sb.AppendLine();
        sb.Append("    public static ").Append(model.Name).Append(" operator +(").Append(model.Name).Append(" left, ").Append(model.Name).AppendLine(" right) =>");
        sb.Append("        new ").Append(model.Name).Append("(");
        for (var i = 0; i < moduli.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            AppendRnsReduceCall(sb, i, "((System.UInt128)left.R" + i + " + right.R" + i + ")", moduli[i]);
        }
        sb.AppendLine(");");
        sb.AppendLine();
        sb.Append("    public static ").Append(model.Name).Append(" operator -(").Append(model.Name).Append(" left, ").Append(model.Name).AppendLine(" right) =>");
        sb.Append("        new ").Append(model.Name).Append("(");
        for (var i = 0; i < moduli.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append("left.R").Append(i).Append(" >= right.R").Append(i)
                .Append(" ? left.R").Append(i).Append(" - right.R").Append(i)
                .Append(" : Modulus").Append(i).Append(" - (right.R").Append(i).Append(" - left.R").Append(i).Append(")");
        }
        sb.AppendLine(");");
        sb.AppendLine();
        sb.Append("    public static ").Append(model.Name).Append(" operator *(").Append(model.Name).Append(" left, ").Append(model.Name).AppendLine(" right) =>");
        sb.Append("        new ").Append(model.Name).Append("(");
        for (var i = 0; i < moduli.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            AppendRnsReduceCall(sb, i, "((System.UInt128)left.R" + i + " * right.R" + i + ")", moduli[i]);
        }
        sb.AppendLine(");");
        for (var i = 0; i < moduli.Count; i++)
        {
            sb.AppendLine();
            sb.Append("    private static ulong Reduce").Append(i).AppendLine("(System.UInt128 value)");
            sb.AppendLine("    {");
            sb.AppendLine("        System.UInt128 remainder = 0;");
            sb.AppendLine("        const int highestBit = 127;");
            sb.AppendLine("        for (var bit = highestBit; bit >= 0; bit--)");
            sb.AppendLine("        {");
            sb.AppendLine("            remainder = (remainder << 1) | ((value >> bit) & 1);");
            sb.Append("            if (remainder >= Modulus").Append(i).AppendLine(")");
            sb.Append("                remainder -= Modulus").Append(i).AppendLine(";");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        return (ulong)remainder;");
            sb.AppendLine("    }");
        }
        sb.AppendLine("}");
        AppendNamespaceEnd(sb, model.Namespace);
        return sb.ToString();
    }

    private static string GenerateDerivative(DerivativeMethodModel model)
    {
        var sb = new StringBuilder();
        AppendNamespaceStart(sb, model.Namespace);
        sb.Append("public static partial class ").Append(model.ContainerName).AppendLine();
        sb.AppendLine("{");
        sb.Append("    public static System.ReadOnlySpan<long> ").Append(model.Name).Append("_DerivativeCoeffs => [");
        for (var i = 0; i < model.DerivativeCoefficients.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(model.DerivativeCoefficients[i]).Append('L');
        }
        sb.AppendLine("];");
        sb.AppendLine("}");
        AppendNamespaceEnd(sb, model.Namespace);
        return sb.ToString();
    }

    private static bool TryParsePolynomial(ExpressionSyntax expression, string variableName, out long[] coefficients)
    {
        try
        {
            var terms = ParsePolynomial(expression, variableName);
            if (terms is null)
            {
                coefficients = Array.Empty<long>();
                return false;
            }

            coefficients = ToCoefficientArray(terms);
            return true;
        }
        catch (OverflowException)
        {
            coefficients = Array.Empty<long>();
            return false;
        }
    }

    private static Dictionary<int, long>? ParsePolynomial(ExpressionSyntax expression, string variableName)
    {
        expression = expression is ParenthesizedExpressionSyntax p ? p.Expression : expression;

        if (expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.NumericLiteralExpression) &&
            literal.Token.Value is int or long)
        {
            return Constant(Convert.ToInt64(literal.Token.Value));
        }

        if (expression is IdentifierNameSyntax id)
            return id.Identifier.ValueText == variableName ? Variable() : null;

        if (expression is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.UnaryMinusExpression))
        {
            var operand = ParsePolynomial(unary.Operand, variableName);
            return operand is null ? null : Scale(operand, -1);
        }

        if (expression is BinaryExpressionSyntax binary)
        {
            var left = ParsePolynomial(binary.Left, variableName);
            var right = ParsePolynomial(binary.Right, variableName);
            if (left is null || right is null)
                return null;

            if (binary.IsKind(SyntaxKind.AddExpression))
                return Add(left, right);
            if (binary.IsKind(SyntaxKind.SubtractExpression))
                return Add(left, Scale(right, -1));
            if (binary.IsKind(SyntaxKind.MultiplyExpression))
                return Multiply(left, right);
        }

        return null;
    }

    private static Dictionary<int, long> Constant(long value)
    {
        var result = new Dictionary<int, long>();
        if (value != 0)
            result[0] = value;
        return result;
    }

    private static Dictionary<int, long> Variable() => new Dictionary<int, long> { [1] = 1 };

    private static Dictionary<int, long> Add(Dictionary<int, long> left, Dictionary<int, long> right)
    {
        var result = new Dictionary<int, long>(left);
        foreach (var term in right)
            result[term.Key] = checked((result.TryGetValue(term.Key, out var existing) ? existing : 0) + term.Value);
        RemoveZeros(result);
        return result;
    }

    private static Dictionary<int, long> Scale(Dictionary<int, long> source, long scalar)
    {
        var result = new Dictionary<int, long>();
        foreach (var term in source)
            result[term.Key] = checked(term.Value * scalar);
        RemoveZeros(result);
        return result;
    }

    private static Dictionary<int, long> Multiply(Dictionary<int, long> left, Dictionary<int, long> right)
    {
        var result = new Dictionary<int, long>();
        foreach (var l in left)
        foreach (var r in right)
        {
            var degree = checked(l.Key + r.Key);
            var coeff = checked(l.Value * r.Value);
            result[degree] = checked((result.TryGetValue(degree, out var existing) ? existing : 0) + coeff);
        }
        RemoveZeros(result);
        return result;
    }

    private static void RemoveZeros(Dictionary<int, long> terms)
    {
        foreach (var key in terms.Where(kv => kv.Value == 0).Select(kv => kv.Key).ToArray())
            terms.Remove(key);
    }

    private static long[] ToCoefficientArray(Dictionary<int, long> terms)
    {
        if (terms.Count == 0)
            return new[] { 0L };

        var degree = terms.Keys.Max();
        var result = new long[degree + 1];
        foreach (var term in terms)
            result[term.Key] = term.Value;
        return result;
    }

    private static long[] Derivative(long[] coefficients)
    {
        if (coefficients.Length <= 1)
            return new[] { 0L };

        var result = new long[coefficients.Length - 1];
        for (var i = 1; i < coefficients.Length; i++)
            result[i - 1] = checked(coefficients[i] * i);

        var length = result.Length;
        while (length > 1 && result[length - 1] == 0)
            length--;
        if (length == result.Length)
            return result;
        var trimmed = new long[length];
        Array.Copy(result, trimmed, length);
        return trimmed;
    }

    private static void AppendNamespaceStart(StringBuilder sb, string? ns)
    {
        if (ns is null)
            return;
        sb.Append("namespace ").Append(ns).AppendLine(";");
        sb.AppendLine();
    }

    private static void AppendNamespaceEnd(StringBuilder sb, string? ns)
    {
    }

    private static void AppendArgs(StringBuilder sb, string prefix, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(prefix).Append(i);
        }
    }

    private static void AppendRnsResidueParameters(StringBuilder sb, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append("ulong r").Append(i);
        }
    }

    private static void AppendRnsReduceCall(StringBuilder sb, int index, string expression, ulong modulus)
    {
        sb.Append("Reduce").Append(index).Append('(').Append(expression).Append(')');
    }

    private const string AttributeSource = """
// <auto-generated/>
namespace Helium.Generators;

[System.AttributeUsage(System.AttributeTargets.Struct)]
public sealed class QuantizedTypeAttribute : System.Attribute
{
    public int Bits { get; set; }
    public QuantizedTypeAttribute() { }
    public QuantizedTypeAttribute(int bits) => Bits = bits;
}

[System.AttributeUsage(System.AttributeTargets.Struct)]
public sealed class RnsTypeAttribute : System.Attribute
{
    public ulong[] Moduli { get; }
    public RnsTypeAttribute(params ulong[] moduli) => Moduli = moduli;
}

[System.AttributeUsage(System.AttributeTargets.Method)]
public sealed class CompileTimeDerivativeAttribute : System.Attribute
{
}
""";

    private sealed class StructModel
    {
        public StructModel(string? ns, string name, string metadataName, Location identifierLocation, int? quantizedBits, ulong[]? rnsModuli)
        {
            Namespace = ns;
            Name = name;
            MetadataName = metadataName;
            IdentifierLocation = identifierLocation;
            QuantizedBits = quantizedBits;
            RnsModuli = rnsModuli;
        }

        public string? Namespace { get; }
        public string Name { get; }
        public string MetadataName { get; }
        public Location IdentifierLocation { get; }
        public int? QuantizedBits { get; }
        public ulong[]? RnsModuli { get; }
    }

    private sealed class DerivativeMethodModel
    {
        public DerivativeMethodModel(string? ns, string containerName, string containerMetadataName, string name, Location identifierLocation, bool isSupported, long[] derivativeCoefficients)
        {
            Namespace = ns;
            ContainerName = containerName;
            ContainerMetadataName = containerMetadataName;
            Name = name;
            IdentifierLocation = identifierLocation;
            IsSupported = isSupported;
            DerivativeCoefficients = derivativeCoefficients;
        }

        public string? Namespace { get; }
        public string ContainerName { get; }
        public string ContainerMetadataName { get; }
        public string Name { get; }
        public Location IdentifierLocation { get; }
        public bool IsSupported { get; }
        public long[] DerivativeCoefficients { get; }
    }
}
