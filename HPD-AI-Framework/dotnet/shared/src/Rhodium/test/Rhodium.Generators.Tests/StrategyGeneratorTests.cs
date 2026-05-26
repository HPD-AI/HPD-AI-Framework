using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rhodium.Analyzers;
using Rhodium.Generators;

namespace Rhodium.Generators.Tests;

public sealed class StrategyGeneratorTests
{
    [Fact]
    public void BarIndicatorWithoutReadonlyBarField_ReportsRhd002()
    {
        var diagnostics = RunGenerator("""
            using Rhodium.Indicators.Streaming;
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class BadStrategy : Strategy
            {
                [BarIndicator(typeof(RSI), 14)]
                public partial double Rsi { get; }
            }
            """);

        Assert.Contains(diagnostics, static d => d.Id == "RHD002");
    }

    [Fact]
    public void MultiOutputIndicator_ReportsRhd003()
    {
        var diagnostics = RunGenerator("""
            using Rhodium.Indicators.Streaming;
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class BadStrategy : Strategy
            {
                [BarField(ReadOnly = true)]
                [BarIndicator(typeof(MACD), 12, 26, 9)]
                public partial double Macd { get; }
            }
            """);

        Assert.Contains(diagnostics, static d => d.Id == "RHD003");
    }

    [Fact]
    public void BarIndicatorGroup_GeneratesCompilingMultiOutputView()
    {
        var diagnostics = RunGeneratorAndCompilation("""
            using Rhodium.Indicators.Streaming;
            using Rhodium.Kernel;
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;
            using Rhodium.Primitives;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class MacdStrategy : Strategy
            {
                private AssetId _spy;
                private AssetId _qqq;

                [BarIndicatorGroup(typeof(MACD), 12, 26, 9)]
                public partial MacdView Macd { get; }

                protected override void OnInitialize(in SetupContext setup)
                {
                    _spy = setup.AddEquity("SPY");
                    _qqq = setup.AddEquity("QQQ");
                }

                partial void OnBar(ref BarContext bar)
                {
                    if (!bar.Macd.IsReady) return;
                    if (bar.Macd.Histogram > 0 && bar.MacdFor(_spy).Value > bar.MacdFor(_qqq).Signal)
                        bar.Buy(_spy, new Qty(1m));
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void BarFieldWithoutPartialProperty_ReportsRhd005()
    {
        var diagnostics = RunGenerator("""
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class BadStrategy : Strategy
            {
                [BarField(ReadOnly = false)]
                public double Signal { get; set; }
            }
            """);

        Assert.Contains(diagnostics, static d => d.Id == "RHD005");
    }

    [Fact]
    public void GeneratedFieldInNonPartialStrategy_ReportsRhd004()
    {
        var diagnostics = RunGenerator("""
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;

            namespace Rhodium.Platform.TestSubject;

            public sealed class BadStrategy : Strategy
            {
                [BarField(ReadOnly = false)]
                public partial double Signal { get; set; }

                partial void OnBar(ref BarContext bar);
            }
            """);

        Assert.Contains(diagnostics, static d => d.Id == "RHD004");
    }

    [Fact]
    public void BarIndicatorSource_GeneratesCompilingCode()
    {
        var diagnostics = RunGeneratorAndCompilation("""
            using Rhodium.Indicators.Streaming;
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class AtrStrategy : Strategy
            {
                [BarField(ReadOnly = true)]
                [BarIndicator(typeof(ATR), 14, Source = BarSource.Bar)]
                public partial double Atr { get; }

                partial void OnBar(ref BarContext bar)
                {
                    _ = bar.Atr;
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void BarIndicator_GeneratesCompilingContextAccessor()
    {
        var diagnostics = RunGeneratorAndCompilation("""
            using Rhodium.Indicators.Streaming;
            using Rhodium.Kernel;
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;
            using Rhodium.Primitives;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class RsiStrategy : Strategy
            {
                [BarField(Name = "RSI_14", ReadOnly = true)]
                [BarIndicator(typeof(RSI), 14)]
                public partial double Rsi { get; }

                partial void OnBar(ref BarContext bar)
                {
                    _ = bar.Rsi;
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void HookOnlyStrategy_GeneratesCompilingTypedContextsWithoutGeneratedFields()
    {
        var diagnostics = RunGeneratorAndCompilation("""
            using Rhodium.Kernel;
            using Rhodium.Platform;
            using Rhodium.Primitives;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class HookOnlyStrategy : Strategy
            {
                private AssetId _spy;

                protected override void OnInitialize(in SetupContext setup)
                {
                    _spy = setup.AddEquity("SPY");
                }

                partial void OnBar(ref BarContext bar)
                {
                    if (bar.AssetId == _spy)
                        bar.Buy(new Qty(1m));
                }

                partial void OnTick(ref TickContext tick)
                {
                    _ = tick.BookSpreadTicks;
                    tick.Buy(new Qty(1m));
                }

                partial void OnQuote(ref QuoteContext quote)
                {
                    _ = quote.Bid;
                    quote.Buy(new Qty(1m));
                }

                partial void OnTrade(ref TradeContext trade)
                {
                    _ = trade.Price;
                    trade.Sell(new Qty(1m));
                }

                partial void OnBookSnapshot(ref BookSnapshotContext book)
                {
                    _ = book.BestBid;
                    book.Flatten();
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void HookOnlyStrategy_NonPartialGeneratedHookReportsRhd018()
    {
        var diagnostics = RunGenerator("""
            using Rhodium.Platform;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class BadStrategy : Strategy
            {
                void OnBar(ref BarContext bar)
                {
                }
            }
            """);

        Assert.Contains(diagnostics, static d => d.Id == "RHD018");
    }

    [Fact]
    public void HookOnlyStrategy_OutsideStrategyReportsRhd012()
    {
        var diagnostics = RunGenerator("""
            using Rhodium.Platform;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class BadStrategy
            {
                partial void OnBar(ref BarContext bar);
            }
            """);

        Assert.Contains(diagnostics, static d => d.Id == "RHD012");
    }

    [Fact]
    public void BarContextForAccessors_GenerateCompilingCrossAssetReads()
    {
        var diagnostics = RunGeneratorAndCompilation("""
            using Rhodium.Indicators.Streaming;
            using Rhodium.Kernel;
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;
            using Rhodium.Primitives;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class PairStrategy : Strategy
            {
                private AssetId _spy;
                private AssetId _qqq;

                [BarField(Name = "RSI_14", ReadOnly = true)]
                [BarIndicator(typeof(RSI), 14)]
                public partial double Rsi { get; }

                protected override void OnInitialize(in SetupContext setup)
                {
                    _spy = setup.AddEquity("SPY");
                    _qqq = setup.AddEquity("QQQ");
                }

                partial void OnBar(ref BarContext bar)
                {
                    if (bar.RsiFor(_spy) > bar.RsiFor(_qqq))
                        bar.Buy(_spy, new Qty(1m));
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void WindowedBarField_GeneratesCompilingWindowAccessor()
    {
        var diagnostics = RunGeneratorAndCompilation("""
            using Rhodium.Kernel;
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;
            using Rhodium.Primitives;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class WindowStrategy : Strategy
            {
                [BarField(ReadOnly = true)]
                [Window(3, 5)]
                public partial double Close { get; }

                partial void OnBar(ref BarContext bar)
                {
                    var closes = bar.Close.Window(3);
                    var z = ((double)bar.Close - closes.Mean()) / closes.StdDev();
                    if (z < -2.0)
                        bar.Buy(new Qty(1m));
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void WindowOnNonReadonlyBarDouble_ReportsRhd015()
    {
        var diagnostics = RunGenerator("""
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class BadWindowStrategy : Strategy
            {
                [QuoteField(ReadOnly = true)]
                [Window(5)]
                public partial double Mid { get; }
            }
            """);

        Assert.Contains(diagnostics, static d => d.Id == "RHD015");
    }

    [Fact]
    public void WindowWithNonPositiveLength_ReportsRhd016()
    {
        var diagnostics = RunGenerator("""
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class BadWindowStrategy : Strategy
            {
                [BarField(ReadOnly = true)]
                [Window(0)]
                public partial double Close { get; }
            }
            """);

        Assert.Contains(diagnostics, static d => d.Id == "RHD016");
    }

    [Fact]
    public void TickIndicator_GeneratesCompilingTickContext()
    {
        var diagnostics = RunGeneratorAndCompilation("""
            using Rhodium.Indicators.Streaming;
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class SpreadStrategy : Strategy
            {
                [TickField(ReadOnly = true)]
                [TickIndicator(typeof(Spread))]
                public partial long SpreadTicks { get; }

                partial void OnTick(ref TickContext tick)
                {
                    if (tick.SpreadTicks <= 1)
                        tick.Buy(new Rhodium.Primitives.Qty(1m));
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void TickContextForAccessors_GenerateCompilingCrossAssetReads()
    {
        var diagnostics = RunGeneratorAndCompilation("""
            using Rhodium.Indicators.Streaming;
            using Rhodium.Kernel;
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;
            using Rhodium.Primitives;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class PairScalper : Strategy
            {
                private AssetId _spy;
                private AssetId _qqq;

                [TickField(ReadOnly = true)]
                [TickIndicator(typeof(Spread))]
                public partial long SpreadTicks { get; }

                protected override void OnInitialize(in SetupContext setup)
                {
                    _spy = setup.AddEquity("SPY");
                    _qqq = setup.AddEquity("QQQ");
                }

                partial void OnTick(ref TickContext tick)
                {
                    if (tick.SpreadTicksFor(_spy) <= tick.SpreadTicksFor(_qqq))
                        tick.Buy(_spy, new Qty(1m));
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void TickContextExecutionSpec_GeneratesCompilingOrderIntentHelpers()
    {
        var diagnostics = RunGeneratorAndCompilation("""
            using Rhodium.Indicators.Streaming;
            using Rhodium.Kernel;
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;
            using Rhodium.Primitives;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class ExecutionStrategy : Strategy
            {
                private AssetId _spy;

                [TickField(ReadOnly = true)]
                [TickIndicator(typeof(Spread))]
                public partial long SpreadTicks { get; }

                protected override void OnInitialize(in SetupContext setup)
                {
                    _spy = setup.AddEquity("SPY");
                }

                partial void OnTick(ref TickContext tick)
                {
                    if (tick.SpreadTicks <= 1)
                        tick.Buy(_spy, new Qty(1m), Execution.Limit().AtBid().WithPostOnly());
                    else
                        tick.Sell(new Qty(1m), Execution.Twap().Over(Duration.FromSeconds(30)));
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void QuoteField_GeneratesCompilingQuoteContext()
    {
        var diagnostics = RunGeneratorAndCompilation("""
            using Rhodium.Kernel;
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;
            using Rhodium.Primitives;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class QuoteStrategy : Strategy
            {
                private AssetId _spy;

                [QuoteField(ReadOnly = true)]
                public partial double Close { get; }

                protected override void OnInitialize(in SetupContext setup)
                {
                    _spy = setup.AddEquity("SPY");
                }

                partial void OnQuote(ref QuoteContext quote)
                {
                    if (quote.AssetId == _spy && quote.SpreadTicks <= 1)
                        quote.Buy(new Qty(1m), Execution.Limit().AtBid().WithPostOnly());
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void TradeField_GeneratesCompilingTradeContext()
    {
        var diagnostics = RunGeneratorAndCompilation("""
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;
            using Rhodium.Primitives;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class TradeStrategy : Strategy
            {
                [TradeField(ReadOnly = true)]
                public partial double Close { get; }

                partial void OnTrade(ref TradeContext trade)
                {
                    if (trade.Price.Value > 100m && trade.AggressorSide == Side.Buy)
                        trade.Sell(new Qty(1m), Execution.Market());
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void BookField_GeneratesCompilingBookSnapshotContext()
    {
        var diagnostics = RunGeneratorAndCompilation("""
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;
            using Rhodium.Primitives;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class BookStrategy : Strategy
            {
                [BookField(ReadOnly = true)]
                public partial double Close { get; }

                partial void OnBookSnapshot(ref BookSnapshotContext book)
                {
                    if (book.TopLevelImbalance > 0.5m)
                        book.Buy(new Qty(1m), Execution.Limit().AtBid());
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void BookLevelDeltaHooks_GenerateCompilingContexts()
    {
        var diagnostics = RunGeneratorAndCompilation("""
            using Rhodium.Platform;
            using Rhodium.Primitives;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class BookLevelDeltaStrategy : Strategy
            {
                partial void OnBookLevelDelta(ref BookLevelDeltaContext book)
                {
                    if (book.Action == BookAction.Add && book.Side == Side.Buy)
                        book.Buy(new Qty(1m), Execution.Limit().At(book.Price));
                }

                partial void OnBookLevelDeltas(ref BookLevelDeltasContext book)
                {
                    if (book.Count > 0)
                        book.Sell(new Qty(1m), Execution.Market());
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void GeneratedMarketContexts_UseByrefSafePortfolioFrame()
    {
        var generated = RunGeneratorAndGetOutput("""
            using Rhodium.Indicators.Streaming;
            using Rhodium.Kernel;
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;
            using Rhodium.Primitives;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class ContextStrategy : Strategy
            {
                [TickField(ReadOnly = true)]
                [TickIndicator(typeof(Spread))]
                public partial long SpreadTicks { get; }

                [QuoteField(ReadOnly = true)]
                public partial double QuoteClose { get; }

                [TradeField(ReadOnly = true)]
                public partial double TradeClose { get; }

                [BookField(ReadOnly = true)]
                public partial double BookClose { get; }

                [BarField]
                public partial double Signal { get; set; }

                partial void OnTick(ref TickContext tick) => tick.Buy(new Qty(1m), Execution.Market());
                partial void OnQuote(ref QuoteContext quote) => quote.Buy(new Qty(1m), Execution.Market());
                partial void OnTrade(ref TradeContext trade) => trade.Buy(new Qty(1m), Execution.Market());
                partial void OnBookSnapshot(ref BookSnapshotContext book) => book.Buy(new Qty(1m), Execution.Market());
                partial void OnBar(ref BarContext bar) => bar.Buy(new Qty(1m), Execution.Market());
            }
            """);

        Assert.Contains("private PortfolioContextFrame _portfolio;", generated);
        Assert.Contains("ref PortfolioContext portfolio", generated);
        Assert.Contains("_portfolio = portfolio.AsFrame();", generated);
        Assert.Contains("new TickContext(id, this, in market, ref portfolio, in frame)", generated);
        Assert.Contains("new QuoteContext(id, this, in market, ref portfolio, in evt)", generated);
        Assert.Contains("new TradeContext(id, this, in market, ref portfolio, in evt)", generated);
        Assert.Contains("new BookSnapshotContext(id, this, in market, ref portfolio, in evt)", generated);
        Assert.Contains("new BarContext(id, this, in market, ref portfolio)", generated);
        Assert.DoesNotContain("private PortfolioContext _portfolio;", generated);
        Assert.DoesNotContain("public MarketKernel Market", generated);
    }

    [Fact]
    public void ParamBoundBarIndicator_EmitsDirectPropertyConstructorArgument()
    {
        var generated = RunGeneratorAndGetOutput("""
            using Rhodium.Indicators.Streaming;
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class ParamStrategy : Strategy
            {
                [Param] public int RsiPeriod { get; init; }

                [BarField(ReadOnly = true)]
                [BarIndicator(typeof(RSI), Param = nameof(RsiPeriod))]
                public partial double Rsi { get; }
            }
            """);

        Assert.Contains("new global::Rhodium.Indicators.Streaming.RSI(RsiPeriod)", generated);
        Assert.Contains("global::Rhodium.Platform.IStrategyParameterFactory<ParamStrategy>", generated);
        Assert.Contains("public static ParamStrategy CreateVariant(global::Rhodium.Platform.ParameterSet parameters)", generated);
        Assert.Contains("RsiPeriod = parameters.GetRequired<int>(@\"RsiPeriod\", @\"RsiPeriod\")", generated);
        Assert.DoesNotContain("ParameterSet.Get", generated);
    }

    [Fact]
    public void ParamOnlyStrategy_EmitsStaticVariantFactory()
    {
        var generated = RunGeneratorAndGetOutput("""
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class ParamStrategy : Strategy
            {
                [Param] public int Fast { get; init; }
                [Param(Name = "slow-period")] public int Slow { get; init; }
            }
            """);

        Assert.Contains("partial class ParamStrategy : global::Rhodium.Platform.IStrategyParameterFactory<ParamStrategy>", generated);
        Assert.Contains("Fast = parameters.GetRequired<int>(@\"Fast\", @\"Fast\")", generated);
        Assert.Contains("Slow = parameters.GetRequired<int>(@\"slow-period\", @\"Slow\")", generated);
    }

    [Fact]
    public void ParamBoundIndicator_MissingParameterReportsRhd013()
    {
        var diagnostics = RunGenerator("""
            using Rhodium.Indicators.Streaming;
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class ParamStrategy : Strategy
            {
                [BarField(ReadOnly = true)]
                [BarIndicator(typeof(RSI), Param = "Missing")]
                public partial double Rsi { get; }
            }
            """);

        Assert.Contains(diagnostics, static d => d.Id == "RHD013");
    }

    [Fact]
    public void ParamBoundIndicator_UnsupportedParameterTypeReportsRhd014()
    {
        var diagnostics = RunGenerator("""
            using System;
            using Rhodium.Indicators.Streaming;
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class ParamStrategy : Strategy
            {
                [Param] public DateTime RsiPeriod { get; init; }

                [BarField(ReadOnly = true)]
                [BarIndicator(typeof(RSI), Param = nameof(RsiPeriod))]
                public partial double Rsi { get; }
            }
            """);

        Assert.Contains(diagnostics, static d => d.Id == "RHD014");
    }

    [Fact]
    public void ParamProperty_UnsupportedTypeReportsRhd014WithoutIndicatorReference()
    {
        var diagnostics = RunGenerator("""
            using System;
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class ParamStrategy : Strategy
            {
                [Param] public DateTime Start { get; init; }
            }
            """);

        Assert.Contains(diagnostics, static d => d.Id == "RHD014");
    }

    [Fact]
    public void ParamProperty_MutableSetterReportsRhd017()
    {
        var diagnostics = RunGenerator("""
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class ParamStrategy : Strategy
            {
                [Param] public int RsiPeriod { get; set; }
            }
            """);

        Assert.Contains(diagnostics, static d => d.Id == "RHD017");
    }

    [Fact]
    public void GeneratedFieldOutsideStrategy_ReportsRhd012()
    {
        var diagnostics = RunGenerator("""
            using Rhodium.Platform;
            using Rhodium.Platform.Attributes;

            namespace Rhodium.Platform.TestSubject;

            public sealed partial class BadStrategy
            {
                [BarField(ReadOnly = false)]
                public partial double Signal { get; set; }
            }
            """);

        Assert.Contains(diagnostics, static d => d.Id == "RHD012");
    }

    [Fact]
    public async Task SafeAssemblyReferencingUnsafeType_ReportsRhd001()
    {
        var compilation = CreateCompilation("""
            namespace Rhodium.Platform.TestSubject;

            public sealed class UnsafeConsumer
            {
                private readonly Rhodium.Unsafe.GlobalMemoryTracker _tracker = new();
            }
            """, assemblyName: "Rhodium.Platform.TestSubject");

        var analyzerDiagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new UnsafeAccessAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        Assert.Contains(analyzerDiagnostics, static d => d.Id == "RHD001");
    }

    [Fact]
    public async Task ManualGeneratedRegistrationCall_ReportsRhd019()
    {
        var compilation = CreateCompilation("""
            using Rhodium.Platform;
            using Rhodium.Platform.Extensions;

            namespace Rhodium.Platform.TestSubject;

            public sealed class ManualRegistrationStrategy : Strategy
            {
                protected override void OnInitialize(in SetupContext setup)
                {
                    setup.AddEquity("SPY");
                    __GeneratedRegisterIndicator(Fields.RSI_14);
                }
            }
            """);

        var analyzerDiagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new GeneratedRegistrationAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        Assert.Contains(analyzerDiagnostics, static d => d.Id == "RHD019");
    }

    private static ImmutableArray<Diagnostic> RunGenerator(string source)
    {
        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(
            [new StrategyGenerator().AsSourceGenerator()],
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        return diagnostics;
    }

    private static ImmutableArray<Diagnostic> RunGeneratorAndCompilation(string source)
    {
        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(
            [new StrategyGenerator().AsSourceGenerator()],
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var generatorDiagnostics);

        return generatorDiagnostics
            .AddRange(updatedCompilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
    }

    private static string RunGeneratorAndGetOutput(string source)
    {
        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(
            [new StrategyGenerator().AsSourceGenerator()],
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var result = driver.GetRunResult();
        return string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static tree => tree.GetText().ToString()));
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        string assemblyName = "Rhodium.Platform.TestSubject")
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        return CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IEnumerable<MetadataReference> References()
    {
        var assemblies = new[]
        {
            typeof(object).Assembly,
            typeof(Attribute).Assembly,
            typeof(Enumerable).Assembly,
            typeof(Rhodium.Platform.Strategy).Assembly,
            typeof(Rhodium.Platform.Attributes.BarFieldAttribute).Assembly,
            typeof(Rhodium.Events.QuoteReceived).Assembly,
            typeof(Rhodium.Kernel.MarketKernel).Assembly,
            typeof(Rhodium.Primitives.AssetId).Assembly,
            typeof(Rhodium.Tensor.Field).Assembly,
            typeof(Rhodium.Indicators.IPriceIndicator).Assembly,
            typeof(Rhodium.Indicators.Streaming.RSI).Assembly,
            typeof(Rhodium.Unsafe.GlobalMemoryTracker).Assembly
        };

        foreach (var assembly in assemblies.DistinctBy(static a => a.Location))
            yield return MetadataReference.CreateFromFile(assembly.Location);

        var trustedPlatformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator) ?? [];

        foreach (var path in trustedPlatformAssemblies.Where(static p => Path.GetFileName(p) is "System.Runtime.dll" or "netstandard.dll"))
            yield return MetadataReference.CreateFromFile(path);
    }
}
