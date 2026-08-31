using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HPD.Agent.SourceGenerator.Contracts;

namespace HPD.Agent.SourceGenerator.Capabilities;

internal static class ParameterAnalyzer
{
    public static List<ParameterInfo> Analyze(ParameterListSyntax parameterList, SemanticModel semanticModel)
        => Analyze(parameterList, semanticModel, out _);

    public static List<ParameterInfo> Analyze(
        ParameterListSyntax parameterList,
        SemanticModel semanticModel,
        out List<Diagnostic> diagnostics)
    {
        diagnostics = new List<Diagnostic>();

        var parameters = new List<ParameterInfo>();

        foreach (var param in parameterList.Parameters)
        {
            var typeSymbol = param.Type is null
                ? null
                : semanticModel.GetTypeInfo(param.Type).Type;
            var kind = FunctionParameterClassifier.Classify(typeSymbol);
            var parameterSymbol = semanticModel.GetDeclaredSymbol(param);

            if (kind == FunctionParameterKind.Unsupported)
            {
                diagnostics.Add(Diagnostic.Create(
                    FunctionParameterClassifier.UnsupportedRuntimeParameter,
                    param.GetLocation(),
                    param.Identifier.ValueText,
                    FunctionParameterClassifier.GetMetadataName(typeSymbol)));
            }

            AIContractNode? contract = null;
            if (kind == FunctionParameterKind.ModelFacing && typeSymbol is not null)
            {
                var parameterDescription = GetDescription(param);
                var analysis = AIContractAnalyzer.Analyze(
                    typeSymbol,
                    param.Identifier.ValueText,
                    parameterDescription.Contains("{metadata.") ? null : parameterDescription,
                    param.GetLocation());
                contract = analysis.Contract;
                diagnostics.AddRange(analysis.Diagnostics);
            }

            parameters.Add(new ParameterInfo
            {
                Symbol = parameterSymbol,
                Contract = contract,
                Name = param.Identifier.ValueText,
                JsonName = parameterSymbol?.Name ?? param.Identifier.ValueText,
                Type = param.Type?.ToString() ?? "object",
                MetadataTypeName = FunctionParameterClassifier.GetMetadataName(typeSymbol),
                Kind = kind,
                Description = GetDescription(param),
                HasDefaultValue = param.Default != null,
                DefaultValue = GetDefaultValue(param, semanticModel),
                ConditionalExpression = GetConditionalExpression(param)
            });
        }

        return parameters;
    }

    public static string GetDefaultInitializer(ParameterInfo param)
    {
        if (param.HasDefaultValue)
        {
            var defaultValue = param.DefaultValue ?? "default";
            return $" = {defaultValue};";
        }

        return " = default!;";
    }

    private static string GetDescription(ParameterSyntax param)
    {
        var attrs = param.AttributeLists.SelectMany(al => al.Attributes).ToList();

        var aiDescription = attrs.FirstOrDefault(a => a.Name.ToString().Contains("AIDescription"));
        if (aiDescription?.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax aiLiteral)
        {
            return aiLiteral.Token.ValueText;
        }

        var description = attrs.FirstOrDefault(a =>
            a.Name.ToString().Contains("Description") &&
            !a.Name.ToString().Contains("AIDescription"));
        if (description?.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal)
        {
            return literal.Token.ValueText;
        }

        return string.Empty;
    }

    private static string? GetConditionalExpression(ParameterSyntax param)
    {
        var attrs = param.AttributeLists.SelectMany(al => al.Attributes).ToList();
        var conditional = attrs.FirstOrDefault(a => a.Name.ToString().Contains("ConditionalParameter"));
        if (conditional?.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal)
        {
            return literal.Token.ValueText;
        }

        return null;
    }

    private static string? GetDefaultValue(ParameterSyntax param, SemanticModel semanticModel)
    {
        if (param.Default is null)
        {
            return null;
        }

        var parameterSymbol = semanticModel.GetDeclaredSymbol(param);
        if (parameterSymbol?.HasExplicitDefaultValue == true)
        {
            return ToCSharpLiteral(parameterSymbol.ExplicitDefaultValue, parameterSymbol.Type);
        }

        var constant = semanticModel.GetConstantValue(param.Default.Value);
        if (constant.HasValue)
        {
            var type = semanticModel.GetTypeInfo(param.Default.Value).ConvertedType
                ?? semanticModel.GetTypeInfo(param.Default.Value).Type;
            return ToCSharpLiteral(constant.Value, type);
        }

        return param.Default.Value.ToString();
    }

    private static string ToCSharpLiteral(object? value, ITypeSymbol? type)
    {
        if (value is null)
        {
            return "null";
        }

        if (type?.TypeKind == TypeKind.Enum)
        {
            return $"({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}";
        }

        return value switch
        {
            string text => SymbolDisplay.FormatLiteral(text, quote: true),
            char ch => SymbolDisplay.FormatLiteral(ch, quote: true),
            bool boolean => boolean ? "true" : "false",
            byte number => number.ToString(CultureInfo.InvariantCulture),
            sbyte number => number.ToString(CultureInfo.InvariantCulture),
            short number => number.ToString(CultureInfo.InvariantCulture),
            ushort number => number.ToString(CultureInfo.InvariantCulture),
            int number => number.ToString(CultureInfo.InvariantCulture),
            uint number => number.ToString(CultureInfo.InvariantCulture) + "u",
            long number => number.ToString(CultureInfo.InvariantCulture) + "L",
            ulong number => number.ToString(CultureInfo.InvariantCulture) + "UL",
            float number => number.ToString("R", CultureInfo.InvariantCulture) + "f",
            double number => number.ToString("R", CultureInfo.InvariantCulture) + "d",
            decimal number => number.ToString(CultureInfo.InvariantCulture) + "m",
            _ => value.ToString() ?? "default"
        };
    }
}
