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

    public static List<ParameterInfo> Analyze(
        IMethodSymbol method, Compilation compilation, out List<Diagnostic> diagnostics)
    {
        diagnostics = new List<Diagnostic>();
        var result = new List<ParameterInfo>();
        foreach (var parameter in method.Parameters)
        {
            var attributes = new List<AttributeData>();
            IParameterSymbol? defaultDeclaration = null;
            for (var current = method; current is not null; current = current.OverriddenMethod)
            {
                if (parameter.Ordinal < current.Parameters.Length)
                {
                    var declared = current.Parameters[parameter.Ordinal];
                    attributes.AddRange(declared.GetAttributes());
                    if (defaultDeclaration is null && declared.HasExplicitDefaultValue)
                        defaultDeclaration = declared;
                }
            }
            string? Text(string metadataName) => attributes.FirstOrDefault(attribute =>
                SymbolEqualityComparer.Default.Equals(attribute.AttributeClass,
                    compilation.GetTypeByMetadataName(metadataName)))
                ?.ConstructorArguments.FirstOrDefault().Value as string;
            var description = Text("AIDescriptionAttribute") ??
                Text("System.ComponentModel.DescriptionAttribute") ?? string.Empty;
            var kind = FunctionParameterClassifier.Classify(parameter.Type);
            var location = parameter.Locations.FirstOrDefault() ?? Location.None;
            if (kind == FunctionParameterKind.Unsupported)
                diagnostics.Add(Diagnostic.Create(FunctionParameterClassifier.UnsupportedRuntimeParameter,
                    location, parameter.Name, FunctionParameterClassifier.GetMetadataName(parameter.Type)));
            AIContractNode? contract = null;
            if (kind == FunctionParameterKind.ModelFacing)
            {
                var analysis = AIContractAnalyzer.Analyze(parameter.Type, parameter.Name,
                    description.Contains("{metadata.") ? null : description, location);
                contract = analysis.Contract;
                diagnostics.AddRange(analysis.Diagnostics);
            }
            result.Add(new ParameterInfo
            {
                Symbol = parameter,
                Contract = contract,
                Name = parameter.Name,
                JsonName = parameter.Name,
                Type = parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                MetadataTypeName = FunctionParameterClassifier.GetMetadataName(parameter.Type),
                Kind = kind,
                Description = description,
                HasDefaultValue = defaultDeclaration is not null,
                DefaultValue = defaultDeclaration is not null
                    ? ToCSharpLiteral(defaultDeclaration.ExplicitDefaultValue, parameter.Type) : null,
                ConditionalExpression = Text("ConditionalParameterAttribute")
            });
        }
        return result;
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
            return type is { IsValueType: true } && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
                ? $"default({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})"
                : "null";
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
