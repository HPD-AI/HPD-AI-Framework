using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Rhodium.Generators;

[Generator]
public sealed class StrategyGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor IndicatorWithoutField = new(
        "RHD002",
        "Generated indicators require a matching generated field",
        "Property '{0}' uses [{1}] without [{2}(ReadOnly = true)]",
        "Rhodium.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MultiOutputIndicator = new(
        "RHD003",
        "Multi-output indicators require [BarIndicatorGroup]",
        "Indicator type '{0}' is multi-output; declare a [BarIndicatorGroup] property so the generator can share one instance across outputs",
        "Rhodium.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ContainingTypeMustBePartial = new(
        "RHD004",
        "Generated strategies must be partial",
        "Type '{0}' declares generated fields and must be partial",
        "Rhodium.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor GeneratedPropertyMustBePartial = new(
        "RHD005",
        "Generated properties must be partial",
        "Property '{0}' declares a generated field and must be partial",
        "Rhodium.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor GeneratedStrategyRequired = new(
        "RHD012",
        "Generated hooks require Strategy",
        "Type '{0}' declares generated fields; derive from Strategy",
        "Rhodium.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingParameter = new(
        "RHD013",
        "Param-bound indicator references a missing parameter",
        "Indicator parameter reference '{0}' does not match a [Param] property on type '{1}'",
        "Rhodium.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedParameterType = new(
        "RHD014",
        "Unsupported generated parameter type",
        "Parameter property '{0}' has unsupported type '{1}'",
        "Rhodium.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedWindowField = new(
        "RHD015",
        "Generated windows require a read-only bar double field",
        "Property '{0}' uses [Window] and must be declared as [BarField(ReadOnly = true)] double",
        "Rhodium.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidWindowLength = new(
        "RHD016",
        "Generated windows require positive lengths",
        "Property '{0}' declares [Window] with a non-positive length",
        "Rhodium.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ParameterMustBeInitOnly = new(
        "RHD017",
        "Generated parameters must be init-only",
        "Parameter property '{0}' must be init-only",
        "Rhodium.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor GeneratedHookMustBePartial = new(
        "RHD018",
        "Generated hooks must be partial",
        "Generated hook '{0}' must be declared partial",
        "Rhodium.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var properties = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is PropertyDeclarationSyntax { AttributeLists.Count: > 0 },
                static (ctx, _) => GetPropertyModel(ctx))
            .Where(static model => model is not null)
            .Collect();

        var hooks = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is MethodDeclarationSyntax { Identifier.ValueText: "OnBar" or "OnTick" or "OnQuote" or "OnTrade" or "OnBook" or "OnBookDelta" or "OnBookDeltas" },
                static (ctx, _) => GetHookModel(ctx))
            .Where(static model => model is not null)
            .Collect();

        context.RegisterSourceOutput(properties.Combine(hooks), static (ctx, input) =>
        {
            var grouped = new Dictionary<INamedTypeSymbol, List<GeneratedProperty>>(SymbolEqualityComparer.Default);

            foreach (var model in input.Left.Concat(input.Right))
            {
                if (model is null) continue;

                foreach (var diagnostic in model.Diagnostics)
                    ctx.ReportDiagnostic(diagnostic);

                if (!model.CanGenerate) continue;
                if (!grouped.TryGetValue(model.ContainingType, out var list))
                {
                    list = new List<GeneratedProperty>();
                    grouped[model.ContainingType] = list;
                }

                list.Add(model);
            }

            foreach (var entry in grouped)
            {
                var syntax = entry.Key.DeclaringSyntaxReferences
                    .Select(static r => r.GetSyntax())
                    .OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault();

                if (syntax is null || !syntax.Modifiers.Any(static m => m.ValueText == "partial"))
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        ContainingTypeMustBePartial,
                        syntax?.Identifier.GetLocation(),
                        entry.Key.Name));
                    continue;
                }

                ctx.AddSource(
                    $"{entry.Key.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "").Replace('.', '_')}.Strategy.g.cs",
                    SourceText.From(Generate(entry.Key, entry.Value), Encoding.UTF8));
            }
        });
    }

    private static GeneratedProperty? GetPropertyModel(GeneratorSyntaxContext context)
    {
        var syntax = (PropertyDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(syntax) as IPropertySymbol;
        if (symbol is null) return null;

        AttributeData? barField = null;
        AttributeData? tickField = null;
        AttributeData? quoteField = null;
        AttributeData? tradeField = null;
        AttributeData? bookField = null;
        AttributeData? barIndicator = null;
        AttributeData? tickIndicator = null;
        AttributeData? barIndicatorGroup = null;
        AttributeData? window = null;
        AttributeData? param = null;

        foreach (var attr in symbol.GetAttributes())
        {
            var name = attr.AttributeClass?.ToDisplayString();
            switch (name)
            {
                case "Rhodium.Platform.Attributes.BarFieldAttribute":
                case "BarFieldAttribute":
                    barField = attr;
                    break;
                case "Rhodium.Platform.Attributes.TickFieldAttribute":
                case "TickFieldAttribute":
                    tickField = attr;
                    break;
                case "Rhodium.Platform.Attributes.QuoteFieldAttribute":
                case "QuoteFieldAttribute":
                    quoteField = attr;
                    break;
                case "Rhodium.Platform.Attributes.TradeFieldAttribute":
                case "TradeFieldAttribute":
                    tradeField = attr;
                    break;
                case "Rhodium.Platform.Attributes.BookFieldAttribute":
                case "BookFieldAttribute":
                    bookField = attr;
                    break;
                case "Rhodium.Platform.Attributes.BarIndicatorAttribute":
                case "BarIndicatorAttribute":
                    barIndicator = attr;
                    break;
                case "Rhodium.Platform.Attributes.TickIndicatorAttribute":
                case "TickIndicatorAttribute":
                    tickIndicator = attr;
                    break;
                case "Rhodium.Platform.Attributes.BarIndicatorGroupAttribute":
                case "BarIndicatorGroupAttribute":
                    barIndicatorGroup = attr;
                    break;
                case "Rhodium.Platform.Attributes.WindowAttribute":
                case "WindowAttribute":
                    window = attr;
                    break;
                case "Rhodium.Platform.Attributes.ParamAttribute":
                case "ParamAttribute":
                    param = attr;
                    break;
            }
        }

        if (barField is null && tickField is null && quoteField is null && tradeField is null && bookField is null &&
            barIndicator is null && tickIndicator is null && barIndicatorGroup is null && window is null && param is null)
            return null;

        var diagnostics = new List<Diagnostic>();
        var canGenerate = true;
        if (param is not null)
            ValidateParameterProperty(symbol, syntax, diagnostics);

        if (barField is null && tickField is null && quoteField is null && tradeField is null && bookField is null &&
            barIndicator is null && tickIndicator is null && barIndicatorGroup is null && window is null)
        {
            return GeneratedProperty.DiagnosticsOnly(symbol.ContainingType, symbol.Name, diagnostics);
        }

        if (barIndicatorGroup is not null)
        {
            if (!InheritsFrom(symbol.ContainingType, "Rhodium.Platform.Strategy"))
            {
                diagnostics.Add(Diagnostic.Create(GeneratedStrategyRequired, syntax.Identifier.GetLocation(), symbol.ContainingType.Name));
                canGenerate = false;
            }

            if (!syntax.Modifiers.Any(static m => m.ValueText == "partial"))
            {
                diagnostics.Add(Diagnostic.Create(GeneratedPropertyMustBePartial, syntax.Identifier.GetLocation(), symbol.Name));
                canGenerate = false;
            }

            var groupTypeName = GetIndicatorTypeName(barIndicatorGroup);
            if (groupTypeName is null || !IsMultiOutputIndicator(groupTypeName))
            {
                diagnostics.Add(Diagnostic.Create(
                    MultiOutputIndicator,
                    syntax.Identifier.GetLocation(),
                    groupTypeName ?? symbol.Name));
                canGenerate = false;
            }

            return GeneratedProperty.Group(
                symbol.ContainingType,
                symbol.Name,
                symbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                groupTypeName,
                GetIndicatorArguments(barIndicatorGroup, symbol.ContainingType, syntax, diagnostics, ref canGenerate),
                GetSource(barIndicatorGroup, FieldFrequency.Bar),
                diagnostics,
                canGenerate);
        }

        var frequency = FieldFrequency.Bar;
        if (quoteField is not null)
            frequency = FieldFrequency.Quote;
        else if (tradeField is not null)
            frequency = FieldFrequency.Trade;
        else if (bookField is not null)
            frequency = FieldFrequency.Book;
        else if (tickField is not null || tickIndicator is not null)
            frequency = FieldFrequency.Tick;

        var fieldAttr = frequency switch
        {
            FieldFrequency.Tick => tickField,
            FieldFrequency.Quote => quoteField,
            FieldFrequency.Trade => tradeField,
            FieldFrequency.Book => bookField,
            _ => barField
        };
        var indicatorAttr = frequency == FieldFrequency.Tick ? tickIndicator : barIndicator;
        var fieldAttrName = frequency switch
        {
            FieldFrequency.Tick => "TickField",
            FieldFrequency.Quote => "QuoteField",
            FieldFrequency.Trade => "TradeField",
            FieldFrequency.Book => "BookField",
            _ => "BarField"
        };
        var indicatorAttrName = frequency == FieldFrequency.Tick ? "TickIndicator" : "BarIndicator";
        var readOnly = GetBoolNamedArgument(fieldAttr, "ReadOnly", defaultValue: false);

        if (!InheritsFrom(symbol.ContainingType, "Rhodium.Platform.Strategy"))
        {
            diagnostics.Add(Diagnostic.Create(GeneratedStrategyRequired, syntax.Identifier.GetLocation(), symbol.ContainingType.Name));
            canGenerate = false;
        }

        if (!syntax.Modifiers.Any(static m => m.ValueText == "partial"))
        {
            diagnostics.Add(Diagnostic.Create(GeneratedPropertyMustBePartial, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        if (indicatorAttr is not null && (fieldAttr is null || !readOnly))
        {
            diagnostics.Add(Diagnostic.Create(
                IndicatorWithoutField,
                syntax.Identifier.GetLocation(),
                symbol.Name,
                indicatorAttrName,
                fieldAttrName));
            canGenerate = false;
        }

        var indicatorTypeName = GetIndicatorTypeName(indicatorAttr);
        if (indicatorTypeName is not null && IsMultiOutputIndicator(indicatorTypeName))
        {
            diagnostics.Add(Diagnostic.Create(MultiOutputIndicator, syntax.Identifier.GetLocation(), indicatorTypeName));
            canGenerate = false;
        }

        if (fieldAttr is null)
            canGenerate = false;

        var windowLengths = GetWindowLengths(window, syntax, diagnostics, ref canGenerate);
        if (window is not null &&
            (frequency != FieldFrequency.Bar || fieldAttr is null || !readOnly || !IsDouble(symbol.Type)))
        {
            diagnostics.Add(Diagnostic.Create(UnsupportedWindowField, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        return new GeneratedProperty(
            symbol.ContainingType,
            symbol.Name,
            symbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            GetStringNamedArgument(fieldAttr, "Name") ?? symbol.Name,
            readOnly,
            frequency,
            indicatorAttr is not null,
            indicatorTypeName,
            GetIndicatorArguments(indicatorAttr, symbol.ContainingType, syntax, diagnostics, ref canGenerate),
            windowLengths,
            GetSource(indicatorAttr, frequency),
            diagnostics,
            canGenerate);
    }

    private static GeneratedProperty? GetHookModel(GeneratorSyntaxContext context)
    {
        var syntax = (MethodDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(syntax) as IMethodSymbol;
        if (symbol is null) return null;

        var frequency = symbol.Name switch
        {
            "OnTick" => FieldFrequency.Tick,
            "OnQuote" => FieldFrequency.Quote,
            "OnTrade" => FieldFrequency.Trade,
            "OnBook" => FieldFrequency.Book,
            "OnBookDelta" => FieldFrequency.Book,
            "OnBookDeltas" => FieldFrequency.Book,
            "OnBar" => FieldFrequency.Bar,
            _ => (FieldFrequency?)null
        };
        if (frequency is null)
            return null;

        var expectedContextName = symbol.Name switch
        {
            "OnBookDelta" => "BookDeltaContext",
            "OnBookDeltas" => "BookDeltasContext",
            _ => frequency.Value + "Context"
        };

        if (symbol.Parameters.Length != 1 ||
            symbol.Parameters[0].RefKind != RefKind.Ref ||
            symbol.Parameters[0].Type.Name != expectedContextName)
            return null;

        var diagnostics = new List<Diagnostic>();
        var canGenerate = true;
        if (!InheritsFrom(symbol.ContainingType, "Rhodium.Platform.Strategy"))
        {
            diagnostics.Add(Diagnostic.Create(GeneratedStrategyRequired, syntax.Identifier.GetLocation(), symbol.ContainingType.Name));
            canGenerate = false;
        }

        if (!syntax.Modifiers.Any(static m => m.ValueText == "partial"))
        {
            diagnostics.Add(Diagnostic.Create(GeneratedHookMustBePartial, syntax.Identifier.GetLocation(), symbol.Name));
            canGenerate = false;
        }

        return GeneratedProperty.HookTrigger(symbol.ContainingType, symbol.Name, frequency.Value, diagnostics, canGenerate);
    }

    private static string Generate(INamedTypeSymbol type, IReadOnlyList<GeneratedProperty> properties)
    {
        var ns = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();
        var typeName = type.Name;
        var hookTriggers = properties.Where(static p => p.IsHookTrigger).ToArray();
        var generatedProperties = properties.Where(static p => !p.IsHookTrigger).ToArray();
        var barProperties = generatedProperties.Where(static p => p.Frequency == FieldFrequency.Bar).ToArray();
        var tickProperties = generatedProperties.Where(static p => p.Frequency == FieldFrequency.Tick).ToArray();
        var quoteProperties = generatedProperties.Where(static p => p.Frequency == FieldFrequency.Quote).ToArray();
        var tradeProperties = generatedProperties.Where(static p => p.Frequency == FieldFrequency.Trade).ToArray();
        var bookProperties = generatedProperties.Where(static p => p.Frequency == FieldFrequency.Book).ToArray();
        var groupProperties = generatedProperties.Where(static p => p.IsGroup).ToArray();
        barProperties = barProperties.Where(static p => !p.IsGroup).ToArray();
        tickProperties = tickProperties.Where(static p => !p.IsGroup).ToArray();
        quoteProperties = quoteProperties.Where(static p => !p.IsGroup).ToArray();
        tradeProperties = tradeProperties.Where(static p => !p.IsGroup).ToArray();
        bookProperties = bookProperties.Where(static p => !p.IsGroup).ToArray();
        var hasBarHook = hookTriggers.Any(static p => p.Frequency == FieldFrequency.Bar);
        var hasTickHook = hookTriggers.Any(static p => p.Frequency == FieldFrequency.Tick);
        var hasQuoteHook = hookTriggers.Any(static p => p.Frequency == FieldFrequency.Quote);
        var hasTradeHook = hookTriggers.Any(static p => p.Frequency == FieldFrequency.Trade);
        var hasBookHook = hookTriggers.Any(static p => p.PropertyName == "OnBook");
        var hasBookDeltaHook = hookTriggers.Any(static p => p.PropertyName == "OnBookDelta");
        var hasBookDeltasHook = hookTriggers.Any(static p => p.PropertyName == "OnBookDeltas");
        var hasAnyBookHook = hasBookHook || hasBookDeltaHook || hasBookDeltasHook;
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        if (quoteProperties.Length > 0 || tradeProperties.Length > 0 || bookProperties.Length > 0 ||
            hasQuoteHook || hasTradeHook || hasAnyBookHook)
            sb.AppendLine("using Rhodium.Events;");
        sb.AppendLine("using Rhodium.Tensor;");
        sb.AppendLine("using Rhodium.Kernel;");
        sb.AppendLine("using Rhodium.Primitives;");
        sb.AppendLine("using Rhodium.Platform.Extensions;");
        if (generatedProperties.Any(static p => p.HasIndicator || p.IsGroup))
            sb.AppendLine("using Rhodium.Indicators;");
        if (ns is not null)
        {
            sb.Append("namespace ").Append(ns).AppendLine(";");
            sb.AppendLine();
        }

        sb.Append("partial class ").Append(typeName).AppendLine();
        sb.AppendLine("{");

        foreach (var property in generatedProperties)
        {
            sb.Append("    private static readonly ").Append(FieldTypeFor(property)).Append(" __Rhodium_Field_")
                .Append(property.PropertyName).Append(" = ").Append(FieldInitializerFor(property)).AppendLine(";");
        }

        foreach (var property in barProperties.Where(static p => p.HasIndicator))
        {
            sb.Append("    private ").Append(BarIndicatorInterfaceFor(property)).Append("[] __Rhodium_BarIndicator_")
                .Append(property.PropertyName).Append(" = global::System.Array.Empty<").Append(BarIndicatorInterfaceFor(property)).AppendLine(">();");
        }

        foreach (var property in tickProperties.Where(static p => p.HasIndicator))
        {
            sb.Append("    private ITickIndicator[] __Rhodium_TickIndicator_").Append(property.PropertyName)
                .AppendLine(" = global::System.Array.Empty<ITickIndicator>();");
        }

        foreach (var property in groupProperties)
        {
            sb.Append("    private global::").Append(property.IndicatorTypeName).Append("[] __Rhodium_BarGroup_")
                .Append(property.PropertyName).Append(" = global::System.Array.Empty<global::")
                .Append(property.IndicatorTypeName).AppendLine(">();");
        }

        foreach (var property in generatedProperties.Where(static p => p.HasWindow))
        {
            sb.Append("    private RollingTensorHistory[] __Rhodium_Window_").Append(property.PropertyName)
                .AppendLine(" = global::System.Array.Empty<RollingTensorHistory>();");
        }

        GenerateInitialize(sb, generatedProperties, barProperties, tickProperties, groupProperties);
        GenerateTick(sb, typeName, tickProperties, hasTickHook);
        GenerateQuotes(sb, typeName, quoteProperties, hasQuoteHook);
        GenerateTrades(sb, typeName, tradeProperties, hasTradeHook);
        GenerateBooks(sb, typeName, bookProperties, hasBookHook, hasBookDeltaHook, hasBookDeltasHook);
        GenerateBars(sb, typeName, barProperties, groupProperties, hasBarHook);
        GenerateContextOnlyProperties(sb, generatedProperties);
        GenerateTickContext(sb, typeName, tickProperties, hasTickHook);
        GenerateQuoteContext(sb, typeName, quoteProperties, hasQuoteHook);
        GenerateTradeContext(sb, typeName, tradeProperties, hasTradeHook);
        GenerateBookContext(sb, typeName, bookProperties, hasBookHook);
        GenerateBookDeltaContext(sb, typeName, hasBookDeltaHook);
        GenerateBookDeltasContext(sb, typeName, hasBookDeltasHook);
        GenerateBarContext(sb, typeName, barProperties, groupProperties, hasBarHook);
        GenerateExplicitAccessors(sb, generatedProperties);
        GenerateGroupViews(sb, groupProperties);

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void GenerateInitialize(
        StringBuilder sb,
        IReadOnlyList<GeneratedProperty> properties,
        IReadOnlyList<GeneratedProperty> barProperties,
        IReadOnlyList<GeneratedProperty> tickProperties,
        IReadOnlyList<GeneratedProperty> groupProperties)
    {
        sb.AppendLine();
        sb.AppendLine("    protected override void __GeneratedInitialize(in MarketKernel market)");
        sb.AppendLine("    {");
        sb.AppendLine("        base.__GeneratedInitialize(in market);");

        foreach (var property in properties)
        {
            if (property.IsGroup)
                continue;

            if (property.HasIndicator || !property.ReadOnly)
                sb.Append("        __GeneratedRegisterPortfolioField(__Rhodium_Field_").Append(property.PropertyName).AppendLine(");");
            else if (ShouldRegister(property))
                sb.Append("        __GeneratedRegisterIndicator(__Rhodium_Field_").Append(property.PropertyName).AppendLine(");");
        }

        foreach (var property in barProperties.Where(static p => p.HasIndicator))
        {
            sb.Append("        __Rhodium_BarIndicator_").Append(property.PropertyName).Append(" = new ")
                .Append(BarIndicatorInterfaceFor(property)).AppendLine("[market.UniverseSize];");
            sb.AppendLine("        for (var i = 0; i < market.UniverseSize; i++)");
            sb.Append("            __Rhodium_BarIndicator_").Append(property.PropertyName).Append("[i] = new global::")
                .Append(property.IndicatorTypeName).Append("(").Append(IndicatorArguments(property)).AppendLine(");");
        }

        foreach (var property in tickProperties.Where(static p => p.HasIndicator))
        {
            sb.Append("        __Rhodium_TickIndicator_").Append(property.PropertyName).AppendLine(" = new ITickIndicator[market.UniverseSize];");
            sb.AppendLine("        for (var i = 0; i < market.UniverseSize; i++)");
            sb.Append("            __Rhodium_TickIndicator_").Append(property.PropertyName).Append("[i] = new global::")
                .Append(property.IndicatorTypeName).Append("(").Append(IndicatorArguments(property)).AppendLine(");");
        }

        foreach (var property in groupProperties)
        {
            sb.Append("        __Rhodium_BarGroup_").Append(property.PropertyName).Append(" = new global::")
                .Append(property.IndicatorTypeName).AppendLine("[market.UniverseSize];");
            sb.AppendLine("        for (var i = 0; i < market.UniverseSize; i++)");
            sb.Append("            __Rhodium_BarGroup_").Append(property.PropertyName).Append("[i] = new global::")
                .Append(property.IndicatorTypeName).Append("(").Append(IndicatorArguments(property)).AppendLine(");");
        }

        foreach (var property in properties.Where(static p => p.HasWindow))
        {
            sb.Append("        __Rhodium_Window_").Append(property.PropertyName).AppendLine(" = new RollingTensorHistory[market.UniverseSize];");
            sb.AppendLine("        for (var i = 0; i < market.UniverseSize; i++)");
            sb.Append("            __Rhodium_Window_").Append(property.PropertyName).Append("[i] = new RollingTensorHistory(")
                .Append(property.WindowCapacity.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine(");");
        }

        sb.AppendLine("    }");
    }

    private static void GenerateTick(
        StringBuilder sb,
        string typeName,
        IReadOnlyList<GeneratedProperty> tickProperties,
        bool force)
    {
        if (tickProperties.Count == 0 && !force)
            return;

        sb.AppendLine();
        sb.AppendLine("    protected override void __GeneratedRunTick(in MarketKernel market, ref PortfolioContext portfolio)");
        sb.AppendLine("    {");
        sb.AppendLine("        base.__GeneratedRunTick(in market, ref portfolio);");
        sb.AppendLine("        var assets = RegisteredAssets;");
        sb.AppendLine("        for (var i = 0; i < assets.Length; i++)");
        sb.AppendLine("            __Rhodium_OnTickAsset(assets[i], in market, ref portfolio);");
        sb.AppendLine("    }");

        sb.AppendLine();
        sb.AppendLine("    private void __Rhodium_OnTickAsset(AssetId id, in MarketKernel market, ref PortfolioContext portfolio)");
        sb.AppendLine("    {");
        sb.AppendLine("        var bidTick = market.GetBestBidTick(id);");
        sb.AppendLine("        var askTick = market.GetBestAskTick(id);");
        sb.AppendLine("        var metadata = market.GetMetadata(id);");
        sb.AppendLine("        var bidSize = bidTick.HasValue ? market.GetQtyAtTick(id, Side.Buy, bidTick.Value) : 0m;");
        sb.AppendLine("        var askSize = askTick.HasValue ? market.GetQtyAtTick(id, Side.Sell, askTick.Value) : 0m;");
        sb.AppendLine("        var frame = new TickFrame(id, TickEventType.Snapshot, bidTick, askTick, bidSize, askSize, metadata.TickSize, default);");

        foreach (var property in tickProperties.Where(static p => p.HasIndicator))
        {
            sb.Append("        __Rhodium_TickIndicator_").Append(property.PropertyName).AppendLine("[id.VirtualIndex].Update(in frame);");
            sb.Append("        portfolio.SetScalar(__Rhodium_Field_").Append(property.PropertyName).Append(", id, ")
                .Append(TensorValueExpression(property, "(double)__Rhodium_TickIndicator_" + property.PropertyName + "[id.VirtualIndex].Value")).AppendLine(");");
        }

        sb.AppendLine();
        sb.AppendLine("        var tick = new TickContext(id, this, in market, ref portfolio, in frame);");
        sb.AppendLine("        OnTick(ref tick);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    partial void OnTick(ref TickContext tick);");
    }

    private static void GenerateQuotes(
        StringBuilder sb,
        string typeName,
        IReadOnlyList<GeneratedProperty> quoteProperties,
        bool force)
    {
        if (quoteProperties.Count == 0 && !force)
            return;

        sb.AppendLine();
        sb.AppendLine("    protected override void __GeneratedRunQuote(in MarketKernel market, ref PortfolioContext portfolio, QuoteReceived evt, int assetRangeStart, int assetRangeLength)");
        sb.AppendLine("    {");
        sb.AppendLine("        base.__GeneratedRunQuote(in market, ref portfolio, evt, assetRangeStart, assetRangeLength);");
        sb.AppendLine("        var assets = RegisteredAssets;");
        sb.AppendLine("        var assetRangeEnd = assetRangeStart + assetRangeLength;");
        sb.AppendLine("        for (var i = 0; i < assets.Length; i++)");
        sb.AppendLine("        {");
        sb.AppendLine("            var id = assets[i];");
        sb.AppendLine("            if (id.VirtualIndex < assetRangeStart || id.VirtualIndex >= assetRangeEnd) continue;");
        sb.AppendLine("            __Rhodium_OnQuoteAsset(id, in market, ref portfolio, in evt);");
        sb.AppendLine("        }");
        sb.AppendLine("    }");

        sb.AppendLine();
        sb.AppendLine("    private void __Rhodium_OnQuoteAsset(AssetId id, in MarketKernel market, ref PortfolioContext portfolio, in QuoteReceived evt)");
        sb.AppendLine("    {");
        sb.AppendLine("        var quote = new QuoteContext(id, this, in market, ref portfolio, in evt);");
        sb.AppendLine("        OnQuote(ref quote);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    partial void OnQuote(ref QuoteContext quote);");
    }

    private static void GenerateTrades(
        StringBuilder sb,
        string typeName,
        IReadOnlyList<GeneratedProperty> tradeProperties,
        bool force)
    {
        if (tradeProperties.Count == 0 && !force)
            return;

        sb.AppendLine();
        sb.AppendLine("    protected override void __GeneratedRunTrade(in MarketKernel market, ref PortfolioContext portfolio, TradeOccurred evt, int assetRangeStart, int assetRangeLength)");
        sb.AppendLine("    {");
        sb.AppendLine("        base.__GeneratedRunTrade(in market, ref portfolio, evt, assetRangeStart, assetRangeLength);");
        sb.AppendLine("        var assets = RegisteredAssets;");
        sb.AppendLine("        var assetRangeEnd = assetRangeStart + assetRangeLength;");
        sb.AppendLine("        for (var i = 0; i < assets.Length; i++)");
        sb.AppendLine("        {");
        sb.AppendLine("            var id = assets[i];");
        sb.AppendLine("            if (id.VirtualIndex < assetRangeStart || id.VirtualIndex >= assetRangeEnd) continue;");
        sb.AppendLine("            __Rhodium_OnTradeAsset(id, in market, ref portfolio, in evt);");
        sb.AppendLine("        }");
        sb.AppendLine("    }");

        sb.AppendLine();
        sb.AppendLine("    private void __Rhodium_OnTradeAsset(AssetId id, in MarketKernel market, ref PortfolioContext portfolio, in TradeOccurred evt)");
        sb.AppendLine("    {");
        sb.AppendLine("        var trade = new TradeContext(id, this, in market, ref portfolio, in evt);");
        sb.AppendLine("        OnTrade(ref trade);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    partial void OnTrade(ref TradeContext trade);");
    }

    private static void GenerateBooks(
        StringBuilder sb,
        string typeName,
        IReadOnlyList<GeneratedProperty> bookProperties,
        bool force,
        bool forceDelta,
        bool forceDeltas)
    {
        if (bookProperties.Count == 0 && !force && !forceDelta && !forceDeltas)
            return;

        if (bookProperties.Count > 0 || force)
        {
            sb.AppendLine();
            sb.AppendLine("    protected override void __GeneratedRunBook(in MarketKernel market, ref PortfolioContext portfolio, BookUpdated evt, int assetRangeStart, int assetRangeLength)");
            sb.AppendLine("    {");
            sb.AppendLine("        base.__GeneratedRunBook(in market, ref portfolio, evt, assetRangeStart, assetRangeLength);");
            sb.AppendLine("        var assets = RegisteredAssets;");
            sb.AppendLine("        var assetRangeEnd = assetRangeStart + assetRangeLength;");
            sb.AppendLine("        for (var i = 0; i < assets.Length; i++)");
            sb.AppendLine("        {");
            sb.AppendLine("            var id = assets[i];");
            sb.AppendLine("            if (id.VirtualIndex < assetRangeStart || id.VirtualIndex >= assetRangeEnd) continue;");
            sb.AppendLine("            __Rhodium_OnBookAsset(id, in market, ref portfolio, in evt);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");

            sb.AppendLine();
            sb.AppendLine("    private void __Rhodium_OnBookAsset(AssetId id, in MarketKernel market, ref PortfolioContext portfolio, in BookUpdated evt)");
            sb.AppendLine("    {");
            sb.AppendLine("        var book = new BookContext(id, this, in market, ref portfolio, in evt);");
            sb.AppendLine("        OnBook(ref book);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    partial void OnBook(ref BookContext book);");
        }

        if (forceDelta)
        {
            sb.AppendLine();
            sb.AppendLine("    protected override void __GeneratedRunBookDelta(in MarketKernel market, ref PortfolioContext portfolio, BookDeltaReceived evt, int assetRangeStart, int assetRangeLength)");
            sb.AppendLine("    {");
            sb.AppendLine("        base.__GeneratedRunBookDelta(in market, ref portfolio, evt, assetRangeStart, assetRangeLength);");
            sb.AppendLine("        var assets = RegisteredAssets;");
            sb.AppendLine("        var assetRangeEnd = assetRangeStart + assetRangeLength;");
            sb.AppendLine("        for (var i = 0; i < assets.Length; i++)");
            sb.AppendLine("        {");
            sb.AppendLine("            var id = assets[i];");
            sb.AppendLine("            if (id.VirtualIndex < assetRangeStart || id.VirtualIndex >= assetRangeEnd) continue;");
            sb.AppendLine("            var book = new BookDeltaContext(id, this, in market, ref portfolio, in evt);");
            sb.AppendLine("            OnBookDelta(ref book);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    partial void OnBookDelta(ref BookDeltaContext book);");
        }

        if (forceDeltas)
        {
            sb.AppendLine();
            sb.AppendLine("    protected override void __GeneratedRunBookDeltas(in MarketKernel market, ref PortfolioContext portfolio, BookDeltasReceived evt, int assetRangeStart, int assetRangeLength)");
            sb.AppendLine("    {");
            sb.AppendLine("        base.__GeneratedRunBookDeltas(in market, ref portfolio, evt, assetRangeStart, assetRangeLength);");
            sb.AppendLine("        var assets = RegisteredAssets;");
            sb.AppendLine("        var assetRangeEnd = assetRangeStart + assetRangeLength;");
            sb.AppendLine("        for (var i = 0; i < assets.Length; i++)");
            sb.AppendLine("        {");
            sb.AppendLine("            var id = assets[i];");
            sb.AppendLine("            if (id.VirtualIndex < assetRangeStart || id.VirtualIndex >= assetRangeEnd) continue;");
            sb.AppendLine("            var book = new BookDeltasContext(id, this, in market, ref portfolio, in evt);");
            sb.AppendLine("            OnBookDeltas(ref book);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    partial void OnBookDeltas(ref BookDeltasContext book);");
        }
    }

    private static void GenerateBars(
        StringBuilder sb,
        string typeName,
        IReadOnlyList<GeneratedProperty> barProperties,
        IReadOnlyList<GeneratedProperty> groupProperties,
        bool force)
    {
        if (barProperties.Count == 0 && groupProperties.Count == 0 && !force)
            return;

        sb.AppendLine();
        sb.AppendLine("    protected override void __GeneratedRunBars(in MarketKernel market, ref PortfolioContext portfolio)");
        sb.AppendLine("    {");
        sb.AppendLine("        base.__GeneratedRunBars(in market, ref portfolio);");
        sb.AppendLine("        var assets = RegisteredAssets;");
        sb.AppendLine("        for (var i = 0; i < assets.Length; i++)");
        sb.AppendLine("            __Rhodium_OnBarAsset(assets[i], in market, ref portfolio);");
        sb.AppendLine("    }");

        sb.AppendLine();
        sb.AppendLine("    private void __Rhodium_OnBarAsset(AssetId id, in MarketKernel market, ref PortfolioContext portfolio)");
        sb.AppendLine("    {");
        foreach (var property in barProperties.Where(static p => p.HasIndicator))
        {
            sb.Append("        __Rhodium_BarIndicator_").Append(property.PropertyName)
                .Append("[id.VirtualIndex].Update(").Append(BarIndicatorUpdateArgument(property, "id")).AppendLine(");");
            sb.Append("        portfolio.SetScalar(__Rhodium_Field_").Append(property.PropertyName).Append(", id, ")
                .Append(TensorValueExpression(property, "(double)__Rhodium_BarIndicator_" + property.PropertyName + "[id.VirtualIndex].Value")).AppendLine(");");
        }
        foreach (var property in groupProperties)
        {
            sb.Append("        __Rhodium_BarGroup_").Append(property.PropertyName)
                .Append("[id.VirtualIndex].Update(").Append(BarIndicatorUpdateArgument(property, "id")).AppendLine(");");
        }
        foreach (var property in barProperties.Where(static p => p.HasWindow))
        {
            sb.Append("        __Rhodium_Window_").Append(property.PropertyName).Append("[id.VirtualIndex].Push((double)(")
                .Append(ExplicitReadExpression(property, "id")).AppendLine("));");
        }
        sb.AppendLine();
        sb.AppendLine("        var bar = new BarContext(id, this, in market, ref portfolio);");
        sb.AppendLine("        OnBar(ref bar);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    partial void OnBar(ref BarContext bar);");
    }

    private static void GenerateContextOnlyProperties(StringBuilder sb, IReadOnlyList<GeneratedProperty> properties)
    {
        foreach (var property in properties)
        {
            sb.AppendLine();
            sb.Append("    public partial ").Append(property.PropertyType).Append(' ').Append(property.PropertyName).AppendLine();
            sb.AppendLine("    {");
            sb.Append("        get => throw __Rhodium_ContextOnly(nameof(").Append(property.PropertyName).AppendLine("));");
            if (!property.ReadOnly)
            {
                sb.AppendLine("        set");
                sb.Append("            => throw __Rhodium_ContextOnly(nameof(").Append(property.PropertyName).AppendLine("));");
            }
            sb.AppendLine("    }");
        }

        sb.AppendLine();
        sb.AppendLine("    private static global::System.InvalidOperationException __Rhodium_ContextOnly(string propertyName)");
        sb.AppendLine("        => new($\"Generated strategy property '{propertyName}' is context-only. Use tick.{propertyName} or bar.{propertyName} inside generated hooks.\");");
    }

    private static void GenerateTickContext(
        StringBuilder sb,
        string typeName,
        IReadOnlyList<GeneratedProperty> tickProperties,
        bool force)
    {
        if (tickProperties.Count == 0 && !force)
            return;

        GenerateContextHeader(sb, typeName, "TickContext", "TickFrame frame", "frame");
        sb.AppendLine("        public TickFrame Frame => _frame;");
        sb.AppendLine("        public long? BidTick => _frame.BidTick;");
        sb.AppendLine("        public long? AskTick => _frame.AskTick;");
        sb.AppendLine("        public decimal BidSize => _frame.BidSize;");
        sb.AppendLine("        public decimal AskSize => _frame.AskSize;");
        sb.AppendLine("        public long BookSpreadTicks => _frame.SpreadTicks;");
        sb.AppendLine("        public decimal MidPrice => _frame.MidPrice;");
        sb.AppendLine("        public decimal MicroPrice => _frame.MicroPrice;");
        sb.AppendLine();
        GenerateContextProperties(sb, tickProperties, "Tick");
        GenerateContextAssetAccessors(sb, tickProperties);
        GenerateOrderHelpers(sb);
        sb.AppendLine("    }");
    }

    private static void GenerateQuoteContext(
        StringBuilder sb,
        string typeName,
        IReadOnlyList<GeneratedProperty> quoteProperties,
        bool force)
    {
        if (quoteProperties.Count == 0 && !force)
            return;

        GenerateEventContextHeader(sb, typeName, "QuoteContext", "QuoteReceived", "quote");
        sb.AppendLine("        public QuoteReceived Event => _quote;");
        sb.AppendLine("        public Quote Quote => _quote.Quote;");
        sb.AppendLine("        public Price Bid => _quote.Quote.Bid;");
        sb.AppendLine("        public Price Ask => _quote.Quote.Ask;");
        sb.AppendLine("        public Qty BidSize => _quote.Quote.BidSize;");
        sb.AppendLine("        public Qty AskSize => _quote.Quote.AskSize;");
        sb.AppendLine("        public Price Mid => _quote.Quote.Mid;");
        sb.AppendLine("        public Price Spread => _quote.Quote.Spread;");
        sb.AppendLine("        public decimal SpreadBps => _quote.Quote.SpreadBps;");
        sb.AppendLine("        public long BidTick => _quote.Quote.BidTick(_market.GetMetadata(_assetId).TickSize).Ticks;");
        sb.AppendLine("        public long AskTick => _quote.Quote.AskTick(_market.GetMetadata(_assetId).TickSize).Ticks;");
        sb.AppendLine("        public long SpreadTicks => AskTick - BidTick;");
        sb.AppendLine();
        GenerateContextProperties(sb, quoteProperties, "Quote");
        GenerateContextAssetAccessors(sb, quoteProperties);
        GenerateOrderHelpers(sb);
        sb.AppendLine("    }");
    }

    private static void GenerateTradeContext(
        StringBuilder sb,
        string typeName,
        IReadOnlyList<GeneratedProperty> tradeProperties,
        bool force)
    {
        if (tradeProperties.Count == 0 && !force)
            return;

        GenerateEventContextHeader(sb, typeName, "TradeContext", "TradeOccurred", "trade");
        sb.AppendLine("        public TradeOccurred Event => _trade;");
        sb.AppendLine("        public Trade Trade => _trade.Trade;");
        sb.AppendLine("        public Price Price => _trade.Trade.Price;");
        sb.AppendLine("        public Qty Size => _trade.Trade.Size;");
        sb.AppendLine("        public Side AggressorSide => _trade.Trade.AggressorSide;");
        sb.AppendLine("        public long PriceTick => _trade.Trade.PriceTick(_market.GetMetadata(_assetId).TickSize).Ticks;");
        sb.AppendLine();
        GenerateContextProperties(sb, tradeProperties, "Trade");
        GenerateContextAssetAccessors(sb, tradeProperties);
        GenerateOrderHelpers(sb);
        sb.AppendLine("    }");
    }

    private static void GenerateBookContext(
        StringBuilder sb,
        string typeName,
        IReadOnlyList<GeneratedProperty> bookProperties,
        bool force)
    {
        if (bookProperties.Count == 0 && !force)
            return;

        GenerateEventContextHeader(sb, typeName, "BookContext", "BookUpdated", "book");
        sb.AppendLine("        public BookUpdated Event => _book;");
        sb.AppendLine("        public Book Book => _book.Book;");
        sb.AppendLine("        public Level? BestBid => _book.Book.BestBid;");
        sb.AppendLine("        public Level? BestAsk => _book.Book.BestAsk;");
        sb.AppendLine("        public Price? Bid => _book.Book.Bid;");
        sb.AppendLine("        public Price? Ask => _book.Book.Ask;");
        sb.AppendLine("        public Price? Mid => _book.Book.Mid;");
        sb.AppendLine("        public Price? Spread => _book.Book.Spread;");
        sb.AppendLine("        public decimal TopLevelImbalance");
        sb.AppendLine("        {");
        sb.AppendLine("            get");
        sb.AppendLine("            {");
        sb.AppendLine("                var bid = _book.Book.BestBid?.Size.Value ?? 0m;");
        sb.AppendLine("                var ask = _book.Book.BestAsk?.Size.Value ?? 0m;");
        sb.AppendLine("                var total = bid + ask;");
        sb.AppendLine("                return total > 0m ? (bid - ask) / total : 0m;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        GenerateContextProperties(sb, bookProperties, "Book");
        GenerateContextAssetAccessors(sb, bookProperties);
        GenerateOrderHelpers(sb);
        sb.AppendLine("    }");
    }

    private static void GenerateBookDeltaContext(
        StringBuilder sb,
        string typeName,
        bool force)
    {
        if (!force)
            return;

        GenerateEventContextHeader(sb, typeName, "BookDeltaContext", "BookDeltaReceived", "book");
        sb.AppendLine("        public BookDeltaReceived Event => _book;");
        sb.AppendLine("        public BookDelta Delta => _book.Delta;");
        sb.AppendLine("        public Side Side => _book.Delta.Side;");
        sb.AppendLine("        public Price Price => _book.Delta.Price;");
        sb.AppendLine("        public Qty Size => _book.Delta.Size;");
        sb.AppendLine("        public BookAction Action => _book.Delta.Action;");
        sb.AppendLine("        public long Sequence => _book.Delta.Sequence;");
        sb.AppendLine();
        GenerateOrderHelpers(sb);
        sb.AppendLine("    }");
    }

    private static void GenerateBookDeltasContext(
        StringBuilder sb,
        string typeName,
        bool force)
    {
        if (!force)
            return;

        GenerateEventContextHeader(sb, typeName, "BookDeltasContext", "BookDeltasReceived", "book");
        sb.AppendLine("        public BookDeltasReceived Event => _book;");
        sb.AppendLine("        public global::System.Collections.Generic.IReadOnlyList<BookDelta> Deltas => _book.Deltas;");
        sb.AppendLine("        public int Count => _book.Deltas.Count;");
        sb.AppendLine();
        GenerateOrderHelpers(sb);
        sb.AppendLine("    }");
    }

    private static void GenerateBarContext(
        StringBuilder sb,
        string typeName,
        IReadOnlyList<GeneratedProperty> barProperties,
        IReadOnlyList<GeneratedProperty> groupProperties,
        bool force)
    {
        if (barProperties.Count == 0 && groupProperties.Count == 0 && !force)
            return;

        GenerateContextHeader(sb, typeName, "BarContext", null, null);
        GenerateContextProperties(sb, barProperties, "Bar");
        GenerateContextGroupProperties(sb, groupProperties);
        GenerateContextAssetAccessors(sb, barProperties);
        GenerateOrderHelpers(sb);
        sb.AppendLine("    }");
    }

    private static void GenerateContextHeader(StringBuilder sb, string typeName, string contextName, string? extraCtorParameter, string? extraFieldAssignment)
    {
        sb.AppendLine();
        sb.Append("    public ref struct ").Append(contextName).AppendLine();
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly AssetId _assetId;");
        sb.Append("        private readonly ").Append(typeName).AppendLine(" _strategy;");
        sb.AppendLine("        private readonly MarketKernel _market;");
        sb.AppendLine("        private PortfolioContextFrame _portfolio;");
        if (extraCtorParameter is not null)
            sb.AppendLine("        private readonly TickFrame _frame;");
        sb.AppendLine();
        sb.Append("        internal ").Append(contextName).Append("(AssetId assetId, ").Append(typeName)
            .Append(" strategy, in MarketKernel market, ref PortfolioContext portfolio");
        if (extraCtorParameter is not null)
            sb.Append(", in ").Append(extraCtorParameter);
        sb.AppendLine(")");
        sb.AppendLine("        {");
        sb.AppendLine("            _assetId = assetId;");
        sb.AppendLine("            _strategy = strategy;");
        sb.AppendLine("            _market = market;");
        sb.AppendLine("            _portfolio = portfolio.AsFrame();");
        if (extraFieldAssignment is not null)
            sb.Append("            _frame = ").Append(extraFieldAssignment).AppendLine(";");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public AssetId AssetId => _assetId;");
        sb.AppendLine("        public StrategyId StrategyId => _portfolio.StrategyId;");
        sb.AppendLine("        public decimal PositionQuantity => _portfolio.GetPositionQty(_assetId);");
        sb.AppendLine();
    }

    private static void GenerateEventContextHeader(
        StringBuilder sb,
        string typeName,
        string contextName,
        string eventType,
        string eventFieldName)
    {
        sb.AppendLine();
        sb.Append("    public ref struct ").Append(contextName).AppendLine();
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly AssetId _assetId;");
        sb.Append("        private readonly ").Append(typeName).AppendLine(" _strategy;");
        sb.AppendLine("        private readonly MarketKernel _market;");
        sb.AppendLine("        private PortfolioContextFrame _portfolio;");
        sb.Append("        private readonly ").Append(eventType).Append(" _").Append(eventFieldName).AppendLine(";");
        sb.AppendLine();
        sb.Append("        internal ").Append(contextName).Append("(AssetId assetId, ").Append(typeName)
            .Append(" strategy, in MarketKernel market, ref PortfolioContext portfolio, in ").Append(eventType)
            .Append(' ').Append(eventFieldName).AppendLine(")");
        sb.AppendLine("        {");
        sb.AppendLine("            _assetId = assetId;");
        sb.AppendLine("            _strategy = strategy;");
        sb.AppendLine("            _market = market;");
        sb.AppendLine("            _portfolio = portfolio.AsFrame();");
        sb.Append("            _").Append(eventFieldName).Append(" = ").Append(eventFieldName).AppendLine(";");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public AssetId AssetId => _assetId;");
        sb.AppendLine("        public StrategyId StrategyId => _portfolio.StrategyId;");
        sb.AppendLine("        public decimal PositionQuantity => _portfolio.GetPositionQty(_assetId);");
        sb.AppendLine();
    }

    private static void GenerateContextProperties(StringBuilder sb, IReadOnlyList<GeneratedProperty> properties, string indicatorPrefix)
    {
        foreach (var property in properties)
        {
            sb.Append("        public ").Append(ContextPropertyType(property)).Append(' ').Append(property.PropertyName).AppendLine();
            sb.AppendLine("        {");
            sb.AppendLine("            get");
            sb.AppendLine("            {");
            if (property.ReadOnly && !property.HasIndicator)
                sb.Append("                _strategy.ValidateAssetBounds(_assetId, __Rhodium_Field_").Append(property.PropertyName).AppendLine(", in _market);");
            sb.Append("                return ").Append(ContextReadExpression(property)).AppendLine(";");
            sb.AppendLine("            }");
            if (!property.ReadOnly)
            {
                sb.AppendLine("            set");
                sb.AppendLine("            {");
                sb.Append("                _portfolio.SetScalar(__Rhodium_Field_").Append(property.PropertyName).Append(", _assetId, ")
                    .Append(TensorValueExpression(property, "value")).AppendLine(");");
                sb.AppendLine("            }");
            }
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        foreach (var property in properties.Where(static p => p.HasIndicator))
        {
            var storagePrefix = indicatorPrefix == "Tick" ? "TickIndicator" : "BarIndicator";
            sb.Append("        public bool ").Append(property.PropertyName).Append("IsReady => _strategy.__Rhodium_")
                .Append(storagePrefix).Append('_').Append(property.PropertyName)
                .Append(".Length > 0 && _strategy.__Rhodium_").Append(storagePrefix).Append('_').Append(property.PropertyName)
                .AppendLine("[_assetId.VirtualIndex].IsReady;");
            sb.AppendLine();
        }
    }

    private static void GenerateContextGroupProperties(StringBuilder sb, IReadOnlyList<GeneratedProperty> groupProperties)
    {
        foreach (var property in groupProperties)
        {
            sb.Append("        public ").Append(property.PropertyType).Append(' ').Append(property.PropertyName)
                .Append(" => new(_strategy.__Rhodium_BarGroup_").Append(property.PropertyName)
                .AppendLine("[_assetId.VirtualIndex]);");
            sb.AppendLine();
            sb.Append("        public ").Append(property.PropertyType).Append(' ').Append(property.PropertyName)
                .Append("For(AssetId id) => new(_strategy.__Rhodium_BarGroup_").Append(property.PropertyName)
                .AppendLine("[id.VirtualIndex]);");
            sb.AppendLine();
        }
    }

    private static void GenerateContextAssetAccessors(StringBuilder sb, IReadOnlyList<GeneratedProperty> properties)
    {
        foreach (var property in properties)
        {
            sb.Append("        public ").Append(ContextPropertyType(property)).Append(' ').Append(property.PropertyName).AppendLine("For(AssetId id)");
            sb.AppendLine("        {");
            if (property.ReadOnly && !property.HasIndicator)
                sb.Append("            _strategy.ValidateAssetBounds(id, __Rhodium_Field_").Append(property.PropertyName).AppendLine(", in _market);");
            sb.Append("            return ").Append(ContextReadExpression(property, "id")).AppendLine(";");
            sb.AppendLine("        }");
            sb.AppendLine();

            if (!property.ReadOnly)
            {
                sb.Append("        public void Set").Append(property.PropertyName).Append("For(AssetId id, ").Append(property.PropertyType).AppendLine(" value)");
                sb.AppendLine("        {");
                sb.Append("            _portfolio.SetScalar(__Rhodium_Field_").Append(property.PropertyName).Append(", id, ")
                    .Append(TensorValueExpression(property, "value")).AppendLine(");");
                sb.AppendLine("        }");
                sb.AppendLine();
            }
        }
    }

    private static void GenerateOrderHelpers(StringBuilder sb)
    {
        sb.AppendLine("        public void Buy(Qty quantity, ExecutionPolicy policy = ExecutionPolicy.Safe)");
        sb.AppendLine("            => _portfolio.Buy(_assetId, quantity, in _market);");
        sb.AppendLine();
        sb.AppendLine("        public void Buy(Qty quantity, ExecutionSpec execution)");
        sb.AppendLine("            => _portfolio.Buy(_assetId, quantity, execution);");
        sb.AppendLine();
        sb.AppendLine("        public void Buy(AssetId id, Qty quantity, ExecutionPolicy policy = ExecutionPolicy.Safe)");
        sb.AppendLine("            => _portfolio.Buy(id, quantity, in _market);");
        sb.AppendLine();
        sb.AppendLine("        public void Buy(AssetId id, Qty quantity, ExecutionSpec execution)");
        sb.AppendLine("            => _portfolio.Buy(id, quantity, execution);");
        sb.AppendLine();
        sb.AppendLine("        public void Sell(Qty quantity, ExecutionPolicy policy = ExecutionPolicy.Safe)");
        sb.AppendLine("            => _portfolio.Sell(_assetId, quantity, in _market);");
        sb.AppendLine();
        sb.AppendLine("        public void Sell(Qty quantity, ExecutionSpec execution)");
        sb.AppendLine("            => _portfolio.Sell(_assetId, quantity, execution);");
        sb.AppendLine();
        sb.AppendLine("        public void Sell(AssetId id, Qty quantity, ExecutionPolicy policy = ExecutionPolicy.Safe)");
        sb.AppendLine("            => _portfolio.Sell(id, quantity, in _market);");
        sb.AppendLine();
        sb.AppendLine("        public void Sell(AssetId id, Qty quantity, ExecutionSpec execution)");
        sb.AppendLine("            => _portfolio.Sell(id, quantity, execution);");
        sb.AppendLine();
        sb.AppendLine("        public void Flatten(ExecutionPolicy policy = ExecutionPolicy.Safe)");
        sb.AppendLine("            => _portfolio.Flatten(_assetId, in _market);");
        sb.AppendLine();
        sb.AppendLine("        public void Flatten(AssetId id, ExecutionPolicy policy = ExecutionPolicy.Safe)");
        sb.AppendLine("            => _portfolio.Flatten(id, in _market);");
        sb.AppendLine();
        sb.AppendLine("        public void TargetQuantity(Qty quantity, ExecutionPolicy policy = ExecutionPolicy.Safe)");
        sb.AppendLine("        {");
        sb.AppendLine("            var current = _portfolio.GetPositionQty(_assetId);");
        sb.AppendLine("            var delta = quantity.Value - current;");
        sb.AppendLine("            if (delta == 0m) return;");
        sb.AppendLine("            if (delta > 0m)");
        sb.AppendLine("                _portfolio.Buy(_assetId, new Qty(delta), in _market);");
        sb.AppendLine("            else");
        sb.AppendLine("                _portfolio.Sell(_assetId, new Qty(global::System.Math.Abs(delta)), in _market);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public void TargetQuantity(AssetId id, Qty quantity, ExecutionPolicy policy = ExecutionPolicy.Safe)");
        sb.AppendLine("        {");
        sb.AppendLine("            var current = _portfolio.GetPositionQty(id);");
        sb.AppendLine("            var delta = quantity.Value - current;");
        sb.AppendLine("            if (delta == 0m) return;");
        sb.AppendLine("            if (delta > 0m)");
        sb.AppendLine("                _portfolio.Buy(id, new Qty(delta), in _market);");
        sb.AppendLine("            else");
        sb.AppendLine("                _portfolio.Sell(id, new Qty(global::System.Math.Abs(delta)), in _market);");
        sb.AppendLine("        }");
    }

    private static void GenerateExplicitAccessors(StringBuilder sb, IReadOnlyList<GeneratedProperty> properties)
    {
        foreach (var property in properties.Where(static p => !p.IsGroup))
        {
            sb.AppendLine();
            sb.Append("    public ").Append(property.PropertyType).Append(" Get").Append(property.PropertyName).Append("(AssetId id, ");
            sb.Append(property.HasIndicator || !property.ReadOnly ? "ref PortfolioContext portfolio" : "in MarketKernel market").AppendLine(")");
            sb.AppendLine("    {");
            if (property.ReadOnly && !property.HasIndicator)
                sb.Append("        ValidateAssetBounds(id, __Rhodium_Field_").Append(property.PropertyName).AppendLine(", in market);");
            sb.Append("        return ").Append(ExplicitReadExpression(property, "id")).AppendLine(";");
            sb.AppendLine("    }");

            if (!property.ReadOnly)
            {
                sb.AppendLine();
                sb.Append("    public void Set").Append(property.PropertyName).Append("(AssetId id, ").Append(property.PropertyType).AppendLine(" value, ref PortfolioContext portfolio)");
                sb.Append("        => portfolio.SetScalar(__Rhodium_Field_").Append(property.PropertyName).Append(", id, ")
                    .Append(TensorValueExpression(property, "(double)value")).AppendLine(");");
            }
        }
    }

    private static void GenerateGroupViews(StringBuilder sb, IReadOnlyList<GeneratedProperty> groupProperties)
    {
        foreach (var property in groupProperties)
        {
            var outputs = GroupOutputsFor(property.IndicatorTypeName);
            if (outputs.Length == 0) continue;

            sb.AppendLine();
            sb.Append("    public readonly ref struct ").Append(property.PropertyType.Replace("global::", "")).AppendLine();
            sb.AppendLine("    {");
            sb.Append("        private readonly global::").Append(property.IndicatorTypeName).AppendLine(" _indicator;");
            sb.AppendLine();
            sb.Append("        internal ").Append(property.PropertyType.Replace("global::", "")).Append("(global::")
                .Append(property.IndicatorTypeName).AppendLine(" indicator)");
            sb.AppendLine("        {");
            sb.AppendLine("            _indicator = indicator;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public bool IsReady => _indicator.IsReady;");
            foreach (var output in outputs)
            {
                sb.Append("        public double ").Append(output.Name).Append(" => (double)_indicator.")
                    .Append(output.SourceProperty).AppendLine(";");
            }
            sb.AppendLine("    }");
        }
    }

    private static string ContextReadExpression(GeneratedProperty property)
        => ContextReadExpression(property, "_assetId");

    private static string ContextReadExpression(GeneratedProperty property, string assetExpression)
        => property.HasWindow
            ? $"new WindowedDouble((double)({ContextScalarReadExpression(property, assetExpression)}), _strategy.__Rhodium_Window_{property.PropertyName}[{assetExpression}.VirtualIndex])"
            : ContextScalarReadExpression(property, assetExpression);

    private static string ContextScalarReadExpression(GeneratedProperty property, string assetExpression)
        => property.HasIndicator || !property.ReadOnly
            ? CastFromDouble(property, $"_portfolio.GetScalar(__Rhodium_Field_{property.PropertyName}, {assetExpression}).Value")
            : CastFromDouble(property, $"_market.GetScalar(__Rhodium_Field_{property.PropertyName}, {assetExpression})");

    private static string ContextPropertyType(GeneratedProperty property)
        => property.HasWindow ? "WindowedDouble" : property.PropertyType;

    private static string ExplicitReadExpression(GeneratedProperty property, string assetExpression)
        => property.HasIndicator || !property.ReadOnly
            ? CastFromDouble(property, $"portfolio.GetScalar(__Rhodium_Field_{property.PropertyName}, {assetExpression}).Value")
            : CastFromDouble(property, $"market.GetScalar(__Rhodium_Field_{property.PropertyName}, {assetExpression})");

    private static string FieldTypeFor(GeneratedProperty property)
        => IsStandardMarketField(property) && property.ReadOnly && !property.HasIndicator
            ? property.FieldName switch
            {
                "Volume" => "VectorField<SizeF64>",
                _ => "VectorField<PriceF64>"
            }
            : "VectorField<FactorF64>";

    private static string FieldInitializerFor(GeneratedProperty property)
        => IsStandardMarketField(property) && property.ReadOnly && !property.HasIndicator
            ? "Field." + property.FieldName
            : "new VectorField<FactorF64>(\"" + property.FieldName + "\")";

    private static bool IsStandardMarketField(GeneratedProperty property)
        => property.FieldName is "Close" or "Open" or "High" or "Low" or "Volume";

    private static bool ShouldRegister(GeneratedProperty property)
        => property.HasIndicator || !IsStandardMarketField(property);

    private static string TensorValueExpression(GeneratedProperty property, string valueExpression)
        => "new FactorF64((double)(" + valueExpression + "))";

    private static string CastFromDouble(GeneratedProperty property, string expression)
        => property.PropertyType switch
        {
            "long" or "global::System.Int64" => "(long)(" + expression + ")",
            "int" or "global::System.Int32" => "(int)(" + expression + ")",
            "decimal" or "global::System.Decimal" => "(decimal)(" + expression + ")",
            _ => expression
        };

    private static string BarIndicatorSourceExpression(GeneratedProperty property, string assetExpression)
    {
        var field = property.Source switch
        {
            "Open" => "Field.Open",
            "High" => "Field.High",
            "Low" => "Field.Low",
            "Volume" => "Field.Volume",
            _ => "Field.Close"
        };

        return "market.GetScalar(" + field + ", " + assetExpression + ")";
    }

    private static string BarIndicatorUpdateArgument(GeneratedProperty property, string assetExpression)
        => IsBarIndicator(property)
            ? "new Bar(new Price((decimal)market.GetScalar(Field.Open, " + assetExpression + "), Currency.USD), new Price((decimal)market.GetScalar(Field.High, " + assetExpression + "), Currency.USD), new Price((decimal)market.GetScalar(Field.Low, " + assetExpression + "), Currency.USD), new Price((decimal)market.GetScalar(Field.Close, " + assetExpression + "), Currency.USD), new Qty((decimal)market.GetScalar(Field.Volume, " + assetExpression + ")), default, default)"
            : "(decimal)" + BarIndicatorSourceExpression(property, assetExpression);

    private static string BarIndicatorInterfaceFor(GeneratedProperty property)
        => IsBarIndicator(property) ? "IBarIndicator" : "IPriceIndicator";

    private static bool IsBarIndicator(GeneratedProperty property)
        => property.Source == "Bar" ||
           property.IndicatorTypeName?.EndsWith(".ATR", StringComparison.Ordinal) == true ||
           property.IndicatorTypeName?.EndsWith(".AverageTrueRange", StringComparison.Ordinal) == true;

    private static GroupOutput[] GroupOutputsFor(string? typeName)
    {
        if (typeName is null) return [];
        if (typeName.EndsWith(".MACD", StringComparison.Ordinal))
        {
            return
            [
                new("Value", "Value"),
                new("Signal", "Signal"),
                new("Histogram", "Histogram")
            ];
        }

        if (typeName.EndsWith(".BollingerBands", StringComparison.Ordinal))
        {
            return
            [
                new("Upper", "Upper"),
                new("Middle", "Middle"),
                new("Lower", "Lower")
            ];
        }

        if (typeName.EndsWith(".Stochastic", StringComparison.Ordinal))
        {
            return
            [
                new("K", "K"),
                new("D", "D")
            ];
        }

        return [];
    }

    private static string IndicatorArguments(GeneratedProperty property)
        => string.Join(", ", property.IndicatorParameters.Select(static arg => arg.Expression));

    private static string TypedConstantExpression(TypedConstant constant)
    {
        if (constant.Value is null) return "null";
        return constant.Value switch
        {
            string s => "@\"" + s.Replace("\"", "\"\"") + "\"",
            char c => "'" + c + "'",
            bool b => b ? "true" : "false",
            decimal d => d.ToString(System.Globalization.CultureInfo.InvariantCulture) + "m",
            float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture) + "f",
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture) + "d",
            long l => l.ToString(System.Globalization.CultureInfo.InvariantCulture) + "L",
            int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => constant.Value.ToString() ?? ""
        };
    }

    private static bool GetBoolNamedArgument(AttributeData? attr, string name, bool defaultValue)
    {
        if (attr is null) return defaultValue;
        foreach (var kv in attr.NamedArguments)
        {
            if (kv.Key == name && kv.Value.Value is bool value)
                return value;
        }

        return defaultValue;
    }

    private static string? GetStringNamedArgument(AttributeData? attr, string name)
    {
        if (attr is null) return null;
        foreach (var kv in attr.NamedArguments)
        {
            if (kv.Key == name)
                return kv.Value.Value as string;
        }

        return null;
    }

    private static string? GetIndicatorTypeName(AttributeData? attr)
    {
        if (attr is null || attr.ConstructorArguments.Length == 0)
            return null;

        return attr.ConstructorArguments[0].Value is INamedTypeSymbol type
            ? type.ToDisplayString()
            : null;
    }

    private static ImmutableArray<int> GetWindowLengths(
        AttributeData? attr,
        PropertyDeclarationSyntax syntax,
        List<Diagnostic> diagnostics,
        ref bool canGenerate)
    {
        if (attr is null)
            return ImmutableArray<int>.Empty;

        var builder = ImmutableArray.CreateBuilder<int>();
        if (attr.ConstructorArguments.Length > 0)
        {
            foreach (var value in attr.ConstructorArguments[0].Values)
            {
                if (value.Value is int length)
                    builder.Add(length);
            }
        }

        if (builder.Count == 0 || builder.Any(static length => length <= 0))
        {
            diagnostics.Add(Diagnostic.Create(InvalidWindowLength, syntax.Identifier.GetLocation(), syntax.Identifier.ValueText));
            canGenerate = false;
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<IndicatorArgument> GetIndicatorArguments(
        AttributeData? attr,
        INamedTypeSymbol containingType,
        PropertyDeclarationSyntax syntax,
        List<Diagnostic> diagnostics,
        ref bool canGenerate)
    {
        if (attr is null)
            return ImmutableArray<IndicatorArgument>.Empty;

        var args = attr.ConstructorArguments.Length >= 2
            ? attr.ConstructorArguments[1].Values.Select(static value => IndicatorArgument.Literal(TypedConstantExpression(value))).ToList()
            : [];

        ApplyParamArgument(attr, containingType, syntax, diagnostics, ref canGenerate, args, "Param", 0);
        for (var i = 0; i <= 7; i++)
            ApplyParamArgument(attr, containingType, syntax, diagnostics, ref canGenerate, args, "Param" + i, i);

        return args.ToImmutableArray();
    }

    private static void ApplyParamArgument(
        AttributeData attr,
        INamedTypeSymbol containingType,
        PropertyDeclarationSyntax syntax,
        List<Diagnostic> diagnostics,
        ref bool canGenerate,
        List<IndicatorArgument> args,
        string name,
        int index)
    {
        var reference = GetStringNamedArgument(attr, name);
        if (string.IsNullOrWhiteSpace(reference))
            return;

        var expression = ResolveParameterExpression(containingType, reference!, syntax, diagnostics, ref canGenerate);
        while (args.Count <= index)
            args.Add(IndicatorArgument.Literal("default"));

        args[index] = IndicatorArgument.Parameter(expression);
    }

    private static string ResolveParameterExpression(
        INamedTypeSymbol containingType,
        string reference,
        PropertyDeclarationSyntax syntax,
        List<Diagnostic> diagnostics,
        ref bool canGenerate)
    {
        foreach (var member in containingType.GetMembers().OfType<IPropertySymbol>())
        {
            var paramAttr = member.GetAttributes().FirstOrDefault(static attr =>
                attr.AttributeClass?.ToDisplayString() is "Rhodium.Platform.Attributes.ParamAttribute" or "ParamAttribute");
            if (paramAttr is null)
                continue;

            var publicName = GetStringNamedArgument(paramAttr, "Name") ?? member.Name;
            if (!string.Equals(member.Name, reference, StringComparison.Ordinal) &&
                !string.Equals(publicName, reference, StringComparison.Ordinal))
                continue;

            if (!IsSupportedParameterType(member.Type))
            {
                diagnostics.Add(Diagnostic.Create(
                    UnsupportedParameterType,
                    syntax.Identifier.GetLocation(),
                    member.Name,
                    member.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                canGenerate = false;
            }

            return member.Name;
        }

        diagnostics.Add(Diagnostic.Create(MissingParameter, syntax.Identifier.GetLocation(), reference, containingType.Name));
        canGenerate = false;
        return reference;
    }

    private static bool IsSupportedParameterType(ITypeSymbol type)
        => type.SpecialType is
            SpecialType.System_Int32 or
            SpecialType.System_Int64 or
            SpecialType.System_Double or
            SpecialType.System_Decimal or
            SpecialType.System_Boolean or
            SpecialType.System_String ||
           type.TypeKind == TypeKind.Enum;

    private static void ValidateParameterProperty(
        IPropertySymbol symbol,
        PropertyDeclarationSyntax syntax,
        List<Diagnostic> diagnostics)
    {
        if (!IsSupportedParameterType(symbol.Type))
        {
            diagnostics.Add(Diagnostic.Create(
                UnsupportedParameterType,
                syntax.Identifier.GetLocation(),
                symbol.Name,
                symbol.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }

        if (syntax.AccessorList?.Accessors.Any(static accessor => accessor.Keyword.ValueText == "init") != true)
        {
            diagnostics.Add(Diagnostic.Create(
                ParameterMustBeInitOnly,
                syntax.Identifier.GetLocation(),
                symbol.Name));
        }
    }

    private static bool IsDouble(ITypeSymbol type)
        => type.SpecialType == SpecialType.System_Double;

    private static string GetSource(AttributeData? attr, FieldFrequency frequency)
    {
        var defaultValue = frequency == FieldFrequency.Tick ? "Book" : "Close";
        if (attr is null) return defaultValue;
        foreach (var kv in attr.NamedArguments)
        {
            if (kv.Key == "Source")
                return kv.Value.Value?.ToString() ?? defaultValue;
        }

        return defaultValue;
    }

    private static bool IsMultiOutputIndicator(string typeName)
        => typeName.EndsWith(".MACD", StringComparison.Ordinal) ||
           typeName.EndsWith(".BollingerBands", StringComparison.Ordinal) ||
           typeName.EndsWith(".Stochastic", StringComparison.Ordinal) ||
           typeName.EndsWith(".Aroon", StringComparison.Ordinal) ||
           typeName.EndsWith(".DonchianChannel", StringComparison.Ordinal) ||
           typeName.EndsWith(".KeltnerChannel", StringComparison.Ordinal);

    private static bool InheritsFrom(INamedTypeSymbol type, string metadataName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == metadataName)
                return true;
        }

        return false;
    }

    private enum FieldFrequency
    {
        Bar,
        Tick,
        Quote,
        Trade,
        Book
    }

    private sealed class GeneratedProperty
    {
        public GeneratedProperty(
            INamedTypeSymbol containingType,
            string propertyName,
            string propertyType,
            string fieldName,
            bool readOnly,
            FieldFrequency frequency,
            bool hasIndicator,
            string? indicatorTypeName,
            ImmutableArray<IndicatorArgument> indicatorParameters,
            ImmutableArray<int> windowLengths,
            string source,
            IReadOnlyList<Diagnostic> diagnostics,
            bool canGenerate)
        {
            ContainingType = containingType;
            PropertyName = propertyName;
            PropertyType = propertyType;
            FieldName = fieldName;
            ReadOnly = readOnly;
            Frequency = frequency;
            HasIndicator = hasIndicator;
            IndicatorTypeName = indicatorTypeName;
            IndicatorParameters = indicatorParameters;
            WindowLengths = windowLengths;
            Source = source;
            Diagnostics = diagnostics;
            CanGenerate = canGenerate;
        }

        private GeneratedProperty(
            INamedTypeSymbol containingType,
            string propertyName,
            string propertyType,
            string? indicatorTypeName,
            ImmutableArray<IndicatorArgument> indicatorParameters,
            string source,
            IReadOnlyList<Diagnostic> diagnostics,
            bool canGenerate)
            : this(
                containingType,
                propertyName,
                propertyType,
                propertyName,
                readOnly: true,
                FieldFrequency.Bar,
                hasIndicator: true,
                indicatorTypeName,
                indicatorParameters,
                ImmutableArray<int>.Empty,
                source,
                diagnostics,
                canGenerate)
        {
            IsGroup = true;
        }

        private GeneratedProperty(
            INamedTypeSymbol containingType,
            string hookName,
            FieldFrequency frequency,
            IReadOnlyList<Diagnostic> diagnostics,
            bool canGenerate)
            : this(
                containingType,
                hookName,
                "void",
                hookName,
                readOnly: true,
                frequency,
                hasIndicator: false,
                indicatorTypeName: null,
                ImmutableArray<IndicatorArgument>.Empty,
                ImmutableArray<int>.Empty,
                source: "",
                diagnostics,
                canGenerate)
        {
            IsHookTrigger = true;
        }

        public static GeneratedProperty Group(
            INamedTypeSymbol containingType,
            string propertyName,
            string propertyType,
            string? indicatorTypeName,
            ImmutableArray<IndicatorArgument> indicatorParameters,
            string source,
            IReadOnlyList<Diagnostic> diagnostics,
            bool canGenerate)
            => new(containingType, propertyName, propertyType, indicatorTypeName, indicatorParameters, source, diagnostics, canGenerate);

        public static GeneratedProperty DiagnosticsOnly(
            INamedTypeSymbol containingType,
            string propertyName,
            IReadOnlyList<Diagnostic> diagnostics)
            => new(
                containingType,
                propertyName,
                "void",
                propertyName,
                readOnly: true,
                FieldFrequency.Bar,
                hasIndicator: false,
                indicatorTypeName: null,
                ImmutableArray<IndicatorArgument>.Empty,
                ImmutableArray<int>.Empty,
                source: "",
                diagnostics,
                canGenerate: false);

        public static GeneratedProperty HookTrigger(
            INamedTypeSymbol containingType,
            string hookName,
            FieldFrequency frequency,
            IReadOnlyList<Diagnostic> diagnostics,
            bool canGenerate)
            => new(containingType, hookName, frequency, diagnostics, canGenerate);

        public INamedTypeSymbol ContainingType { get; }
        public string PropertyName { get; }
        public string PropertyType { get; }
        public string FieldName { get; }
        public bool ReadOnly { get; }
        public FieldFrequency Frequency { get; }
        public bool HasIndicator { get; }
        public string? IndicatorTypeName { get; }
        public ImmutableArray<IndicatorArgument> IndicatorParameters { get; }
        public ImmutableArray<int> WindowLengths { get; }
        public string Source { get; }
        public IReadOnlyList<Diagnostic> Diagnostics { get; }
        public bool CanGenerate { get; }
        public bool IsGroup { get; }
        public bool IsHookTrigger { get; }
        public bool HasWindow => WindowLengths.Length > 0;
        public int WindowCapacity => HasWindow ? WindowLengths.Max() : 0;
    }

    private readonly struct GroupOutput
    {
        public GroupOutput(string name, string sourceProperty)
        {
            Name = name;
            SourceProperty = sourceProperty;
        }

        public string Name { get; }
        public string SourceProperty { get; }
    }

    private readonly struct IndicatorArgument
    {
        private IndicatorArgument(string expression)
        {
            Expression = expression;
        }

        public string Expression { get; }

        public static IndicatorArgument Literal(string expression) => new(expression);

        public static IndicatorArgument Parameter(string expression) => new(expression);
    }
}
