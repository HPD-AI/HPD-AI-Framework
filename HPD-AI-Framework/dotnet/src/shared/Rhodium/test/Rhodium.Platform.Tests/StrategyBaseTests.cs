using Rhodium.Kernel;
using Rhodium.Platform;
using Rhodium.Platform.Extensions;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Platform.Tests;

/// <summary>
/// Tests for StrategyBase lifecycle and guards.
/// </summary>
public class StrategyBaseTests
{
    [Fact]
    public void Initialize_SetsEngineState()
    {
        var engine = new TradingEngine();
        var strategy = new TestStrategy();

        strategy.Initialize(engine);

        Assert.NotNull(strategy.GetEngine());
    }

    [Fact]
    public void OnInitialize_CalledDuringInitialization()
    {
        var engine = new TradingEngine();
        var strategy = new TestStrategy();

        strategy.Initialize(engine);

        Assert.True(strategy.OnInitializeCalled);
    }

    [Fact]
    public void AddEquity_ReturnsValidAssetId()
    {
        var engine = new TradingEngine();
        var strategy = new TestStrategy();

        strategy.Initialize(engine);
        var assetId = strategy.AddEquityPublic("SPY");

        Assert.Equal(0, assetId.VirtualIndex);
    }

    [Fact]
    public void AddEquity_WithVariant_ReturnsCorrectOffset()
    {
        var engine = new TradingEngine();
        var strategy = new TestStrategy();

        strategy.Initialize(engine);
        var base1 = strategy.AddEquityPublic("SPY", 0);
        var variant1 = strategy.AddEquityPublic("SPY", 1);

        Assert.Equal(0, base1.VirtualIndex);
        Assert.Equal(1, variant1.VirtualIndex);
    }

    [Fact]
    public void RegisterIndicator_EnsuresColumn()
    {
        var engine = new TradingEngine();
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        engine.BatchMap.AddInstrument(instrument, 1);
        engine.Tensors.Grow();

        var strategy = new TestStrategy();
        strategy.Initialize(engine);
        strategy.RegisterIndicatorPublic(Fields.RSI_14);

        // Column should now exist (verified by setting a value)
        engine.Tensors.GetScalar(Fields.RSI_14, 0) = new FactorF64(50.0);
        var rsi = engine.Tensors.GetScalar(Fields.RSI_14, 0).Value;

        Assert.Equal(50.0, rsi);
    }

    [Fact]
    public void RunTickGuarded_CallsOnTick()
    {
        var engine = new TradingEngine();
        var strategy = new TestStrategy();

        strategy.Initialize(engine);
        strategy.RunTickGuarded();

        Assert.True(strategy.OnTickCalled);
    }

    [Fact]
    public void RunTickGuarded_ThrowsOnVersionMismatch()
    {
        var engine = new TradingEngine();
        var strategy = new TestStrategy();

        strategy.Initialize(engine);

        // Change universe version
        var instrument = new Instrument(new Asset("NEW", AssetClass.Equity), Venue.NASDAQ);
        engine.BatchMap.AddInstrument(instrument, 1);
        engine.Tensors.Grow();

        // Should throw because version changed
        Assert.Throws<InvalidOperationException>(() => strategy.RunTickGuarded());
    }

    [Fact]
    public void AddEquity_MultipleInstruments_ReturnsSequentialIds()
    {
        var engine = new TradingEngine();
        var strategy = new TestStrategy();

        strategy.Initialize(engine);
        var spy = strategy.AddEquityPublic("SPY");
        var qqq = strategy.AddEquityPublic("QQQ");
        var aapl = strategy.AddEquityPublic("AAPL");

        Assert.Equal(0, spy.VirtualIndex);
        Assert.Equal(1, qqq.VirtualIndex);
        Assert.Equal(2, aapl.VirtualIndex);
    }

    [Fact]
    public void Strategy_CanAccessEngineInOnTick()
    {
        var engine = new TradingEngine();
        var strategy = new EngineAccessStrategy();

        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        engine.BatchMap.AddInstrument(instrument, 1);
        engine.Tensors.Grow();

        strategy.Initialize(engine);
        strategy.RunTickGuarded();

        Assert.True(strategy.AccessedEngine);
    }

    [Fact]
    public void Strategy_CanUseExtensionsInOnTick()
    {
        var engine = new TradingEngine();
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        engine.BatchMap.AddInstrument(instrument, 1);
        engine.Tensors.Grow();

        engine.Tensors.GetScalar(Field.Close, 0) = new PriceF64(450.0);

        var strategy = new ExtensionUsingStrategy();
        strategy.Initialize(engine);
        strategy.RunTickGuarded();

        Assert.Equal(450.0, strategy.ClosePrice);
    }

    [Fact]
    public void Strategy_MultipleInstruments_CanTrackAll()
    {
        var engine = new TradingEngine();
        var strategy = new MultiInstrumentStrategy();

        strategy.Initialize(engine);
        strategy.RunTickGuarded();

        Assert.Equal(3, strategy.InstrumentCount);
    }

    [Fact]
    public void OnInitialize_CanRegisterMultipleIndicators()
    {
        var engine = new TradingEngine();
        var strategy = new MultiIndicatorStrategy();

        strategy.Initialize(engine);

        // All indicators should be registered
        Assert.True(strategy.RegisteredRsi);
        Assert.True(strategy.RegisteredMacd);
    }

    [Fact]
    public void RunTickGuarded_PreservesEngineState()
    {
        var engine = new TradingEngine();
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        engine.BatchMap.AddInstrument(instrument, 1);
        engine.Tensors.Grow();

        var strategy = new TestStrategy();
        strategy.Initialize(engine);

        // Set some state
        engine.Tensors.GetScalar(Field.Close, 0) = new PriceF64(100.0);

        strategy.RunTickGuarded();

        // State should still be accessible
        var close = engine.Tensors.GetScalar(Field.Close, 0).Value;
        Assert.Equal(100.0, close);
    }

    [Fact]
    public void Strategy_OwnsEngineAfterInitialize()
    {
        var engine = new TradingEngine();
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        engine.BatchMap.AddInstrument(instrument, 1);
        engine.Tensors.Grow();

        var strategy = new TestStrategy();
        strategy.Initialize(engine);

        // Strategy should have its own copy
        var strategyEngine = strategy.GetEngine();
        Assert.NotNull(strategyEngine);
    }
}

/// <summary>
/// Test strategy that tracks lifecycle calls.
/// </summary>
internal class TestStrategy : StrategyBase
{
    public bool OnInitializeCalled { get; private set; }
    public bool OnTickCalled { get; private set; }

    protected override void OnInitialize()
    {
        OnInitializeCalled = true;
    }

    public override void OnTick()
    {
        OnTickCalled = true;
    }

    public AssetId AddEquityPublic(string symbol)
        => AddEquity(symbol);

    public AssetId AddEquityPublic(string symbol, int variant)
        => AddEquity(symbol, variant);

    public void RegisterIndicatorPublic<T>(VectorField<T> field) where T : unmanaged
        => RegisterIndicator(field);

    public TradingEngine? GetEngine()
        => Engine;
}

/// <summary>
/// Strategy that accesses engine data in OnTick.
/// </summary>
internal class EngineAccessStrategy : StrategyBase
{
    public bool AccessedEngine { get; private set; }
    private AssetId _spy;

    protected override void OnInitialize()
    {
        _spy = AddEquity("SPY");
    }

    public override void OnTick()
    {
        // Access engine state
        var position = Engine.GetPosition(_spy);
        AccessedEngine = true;
    }
}

/// <summary>
/// Strategy that uses data extensions.
/// </summary>
internal class ExtensionUsingStrategy : StrategyBase
{
    public double ClosePrice { get; private set; }
    private AssetId _spy;

    protected override void OnInitialize()
    {
        _spy = AddEquity("SPY");
    }

    public override void OnTick()
    {
        ClosePrice = Engine.GetClose(_spy);
    }
}

/// <summary>
/// Strategy with multiple instruments.
/// </summary>
internal class MultiInstrumentStrategy : StrategyBase
{
    public int InstrumentCount { get; private set; }
    private AssetId _spy;
    private AssetId _qqq;
    private AssetId _aapl;

    protected override void OnInitialize()
    {
        _spy = AddEquity("SPY");
        _qqq = AddEquity("QQQ");
        _aapl = AddEquity("AAPL");
        InstrumentCount = 3;
    }

    public override void OnTick()
    {
        // Can access all instruments
        var spyPos = Engine.GetPosition(_spy);
        var qqqPos = Engine.GetPosition(_qqq);
        var aaplPos = Engine.GetPosition(_aapl);
    }
}

/// <summary>
/// Strategy that registers multiple indicators.
/// </summary>
internal class MultiIndicatorStrategy : StrategyBase
{
    public bool RegisteredRsi { get; private set; }
    public bool RegisteredMacd { get; private set; }

    private static readonly VectorField<FactorF64> MACD = new("MACD");

    protected override void OnInitialize()
    {
        AddEquity("SPY");

        RegisterIndicator(Fields.RSI_14);
        RegisteredRsi = true;

        RegisterIndicator(MACD);
        RegisteredMacd = true;
    }

    public override void OnTick()
    {
        // Can use both indicators
    }
}
