using Rhodium.HFT;

namespace Rhodium.HFT.Tests;

/// <summary>
/// Tests for MarketTensorBasis virtual index mapping.
/// </summary>
public class MarketTensorBasisTests
{
    [Fact]
    public void RegisterInstrument_AssignsSequentialIndices()
    {
        // Arrange
        var config = new MarketTensorSpaceConfig
        {
            InstrumentCount = 10,
            PriceLevelsPerInstrument = 100,
            OrderSlotsPerLevel = 50
        };
        var basis = new MarketTensorBasis(config);

        // Act
        basis.RegisterInstrument("BTC-USD");
        basis.RegisterInstrument("ETH-USD");
        basis.RegisterInstrument("SOL-USD");

        // Assert
        var vi1 = basis.GetVI("BTC-USD", 0, 0);
        var vi2 = basis.GetVI("ETH-USD", 0, 0);
        var vi3 = basis.GetVI("SOL-USD", 0, 0);

        Assert.Equal(0, vi1); // First instrument
        Assert.Equal(5000, vi2); // Second instrument (100 levels * 50 slots)
        Assert.Equal(10000, vi3); // Third instrument
    }

    [Fact]
    public void RegisterInstrument_SameInstrumentTwice_DoesNotDuplicate()
    {
        // Arrange
        var config = new MarketTensorSpaceConfig();
        var basis = new MarketTensorBasis(config);

        // Act
        basis.RegisterInstrument("BTC-USD");
        basis.RegisterInstrument("BTC-USD");

        // Assert
        Assert.Equal(1, basis.RegisteredInstrumentCount);
    }

    [Fact]
    public void GetVI_CalculatesCorrectVirtualIndex()
    {
        // Arrange
        var config = new MarketTensorSpaceConfig
        {
            InstrumentCount = 10,
            PriceLevelsPerInstrument = 100,
            OrderSlotsPerLevel = 50
        };
        var basis = new MarketTensorBasis(config);
        basis.RegisterInstrument("BTC-USD");

        // Act
        var vi = basis.GetVI("BTC-USD", priceLevel: 5, orderSlot: 10);

        // Assert
        // VI = instrIdx * (levels * slots) + levelIdx * slots + slotIdx
        // VI = 0 * (100 * 50) + 5 * 50 + 10
        // VI = 0 + 250 + 10 = 260
        Assert.Equal(260, vi);
    }

    [Fact]
    public void GetVI_ThrowsOnUnregisteredInstrument()
    {
        // Arrange
        var config = new MarketTensorSpaceConfig();
        var basis = new MarketTensorBasis(config);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            basis.GetVI("UNKNOWN", 0, 0));

        Assert.Contains("not registered", ex.Message);
    }

    [Fact]
    public void GetVI_ThrowsOnInvalidPriceLevel_Negative()
    {
        // Arrange
        var config = new MarketTensorSpaceConfig
        {
            PriceLevelsPerInstrument = 100
        };
        var basis = new MarketTensorBasis(config);
        basis.RegisterInstrument("BTC-USD");

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            basis.GetVI("BTC-USD", priceLevel: -1, orderSlot: 0));

        Assert.Contains("Price level", ex.Message);
    }

    [Fact]
    public void GetVI_ThrowsOnInvalidPriceLevel_TooHigh()
    {
        // Arrange
        var config = new MarketTensorSpaceConfig
        {
            PriceLevelsPerInstrument = 100
        };
        var basis = new MarketTensorBasis(config);
        basis.RegisterInstrument("BTC-USD");

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            basis.GetVI("BTC-USD", priceLevel: 100, orderSlot: 0));

        Assert.Contains("Price level", ex.Message);
    }

    [Fact]
    public void GetVI_ThrowsOnInvalidOrderSlot_Negative()
    {
        // Arrange
        var config = new MarketTensorSpaceConfig
        {
            OrderSlotsPerLevel = 50
        };
        var basis = new MarketTensorBasis(config);
        basis.RegisterInstrument("BTC-USD");

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            basis.GetVI("BTC-USD", priceLevel: 0, orderSlot: -1));

        Assert.Contains("Order slot", ex.Message);
    }

    [Fact]
    public void GetVI_ThrowsOnInvalidOrderSlot_TooHigh()
    {
        // Arrange
        var config = new MarketTensorSpaceConfig
        {
            OrderSlotsPerLevel = 50
        };
        var basis = new MarketTensorBasis(config);
        basis.RegisterInstrument("BTC-USD");

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            basis.GetVI("BTC-USD", priceLevel: 0, orderSlot: 50));

        Assert.Contains("Order slot", ex.Message);
    }

    [Fact]
    public void FromVI_ReverseMappingCorrect()
    {
        // Arrange
        var config = new MarketTensorSpaceConfig
        {
            InstrumentCount = 10,
            PriceLevelsPerInstrument = 100,
            OrderSlotsPerLevel = 50
        };
        var basis = new MarketTensorBasis(config);
        basis.RegisterInstrument("BTC-USD");
        basis.RegisterInstrument("ETH-USD");

        // Act
        var (instrument, level, slot) = basis.FromVI(5260);

        // Assert
        // VI = 5260
        // levelsSlots = 100 * 50 = 5000
        // instrIdx = 5260 / 5000 = 1 (ETH-USD)
        // remainder = 5260 % 5000 = 260
        // level = 260 / 50 = 5
        // slot = 260 % 50 = 10
        Assert.Equal("ETH-USD", instrument);
        Assert.Equal(5, level);
        Assert.Equal(10, slot);
    }

    [Fact]
    public void FromVI_RoundTrip_PreservesOriginal()
    {
        // Arrange
        var config = new MarketTensorSpaceConfig
        {
            InstrumentCount = 10,
            PriceLevelsPerInstrument = 100,
            OrderSlotsPerLevel = 50
        };
        var basis = new MarketTensorBasis(config);
        basis.RegisterInstrument("BTC-USD");

        // Act
        var originalVI = basis.GetVI("BTC-USD", 42, 17);
        var (instrument, level, slot) = basis.FromVI(originalVI);
        var reconstructedVI = basis.GetVI(instrument, level, slot);

        // Assert
        Assert.Equal("BTC-USD", instrument);
        Assert.Equal(42, level);
        Assert.Equal(17, slot);
        Assert.Equal(originalVI, reconstructedVI);
    }

    [Fact]
    public void FromVI_ThrowsOnInvalidVI()
    {
        // Arrange
        var config = new MarketTensorSpaceConfig();
        var basis = new MarketTensorBasis(config);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => basis.FromVI(999999));

        Assert.Contains("No instrument found", ex.Message);
    }

    [Fact]
    public void RegisteredInstrumentCount_ReturnsCorrectCount()
    {
        // Arrange
        var config = new MarketTensorSpaceConfig();
        var basis = new MarketTensorBasis(config);

        // Act & Assert
        Assert.Equal(0, basis.RegisteredInstrumentCount);

        basis.RegisterInstrument("BTC-USD");
        Assert.Equal(1, basis.RegisteredInstrumentCount);

        basis.RegisterInstrument("ETH-USD");
        Assert.Equal(2, basis.RegisteredInstrumentCount);

        basis.RegisterInstrument("BTC-USD"); // Duplicate
        Assert.Equal(2, basis.RegisteredInstrumentCount);
    }

    [Fact]
    public void IsRegistered_ReturnsTrueForRegisteredInstrument()
    {
        // Arrange
        var config = new MarketTensorSpaceConfig();
        var basis = new MarketTensorBasis(config);
        basis.RegisterInstrument("BTC-USD");

        // Act & Assert
        Assert.True(basis.IsRegistered("BTC-USD"));
    }

    [Fact]
    public void IsRegistered_ReturnsFalseForUnregisteredInstrument()
    {
        // Arrange
        var config = new MarketTensorSpaceConfig();
        var basis = new MarketTensorBasis(config);

        // Act & Assert
        Assert.False(basis.IsRegistered("BTC-USD"));
    }

    [Fact]
    public void RegisterInstrument_ThrowsWhenMaxInstrumentsExceeded()
    {
        // Arrange
        var config = new MarketTensorSpaceConfig
        {
            InstrumentCount = 2 // Small limit for testing
        };
        var basis = new MarketTensorBasis(config);

        // Act
        basis.RegisterInstrument("BTC-USD");
        basis.RegisterInstrument("ETH-USD");

        // Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            basis.RegisterInstrument("SOL-USD"));

        Assert.Contains("Cannot register more than 2 instruments", ex.Message);
        Assert.Contains("Increase MarketTensorSpaceConfig.InstrumentCount", ex.Message);
    }

    [Fact]
    public void GetVI_MultipleInstruments_CorrectOffsets()
    {
        // Arrange
        var config = new MarketTensorSpaceConfig
        {
            InstrumentCount = 10,
            PriceLevelsPerInstrument = 10,
            OrderSlotsPerLevel = 5
        };
        var basis = new MarketTensorBasis(config);
        basis.RegisterInstrument("A");
        basis.RegisterInstrument("B");
        basis.RegisterInstrument("C");

        // Act
        var viA = basis.GetVI("A", 0, 0);
        var viB = basis.GetVI("B", 0, 0);
        var viC = basis.GetVI("C", 0, 0);

        // Assert
        Assert.Equal(0, viA);
        Assert.Equal(50, viB); // 10 levels * 5 slots
        Assert.Equal(100, viC); // 2 * (10 levels * 5 slots)
    }

    [Fact]
    public void GetVI_MaxPriceLevelAndSlot_CalculatesCorrectly()
    {
        // Arrange
        var config = new MarketTensorSpaceConfig
        {
            InstrumentCount = 10,
            PriceLevelsPerInstrument = 100,
            OrderSlotsPerLevel = 50
        };
        var basis = new MarketTensorBasis(config);
        basis.RegisterInstrument("BTC-USD");

        // Act
        var vi = basis.GetVI("BTC-USD", priceLevel: 99, orderSlot: 49);

        // Assert
        // VI = 0 * 5000 + 99 * 50 + 49
        // VI = 0 + 4950 + 49 = 4999
        Assert.Equal(4999, vi);
    }

    [Fact]
    public void FromVI_FirstInstrument_FirstSlot()
    {
        // Arrange
        var config = new MarketTensorSpaceConfig
        {
            InstrumentCount = 10,
            PriceLevelsPerInstrument = 100,
            OrderSlotsPerLevel = 50
        };
        var basis = new MarketTensorBasis(config);
        basis.RegisterInstrument("BTC-USD");

        // Act
        var (instrument, level, slot) = basis.FromVI(0);

        // Assert
        Assert.Equal("BTC-USD", instrument);
        Assert.Equal(0, level);
        Assert.Equal(0, slot);
    }

    [Fact]
    public void TotalMarketVIs_CalculatesCorrectly()
    {
        // Arrange
        var config = new MarketTensorSpaceConfig
        {
            InstrumentCount = 500,
            PriceLevelsPerInstrument = 200,
            OrderSlotsPerLevel = 100
        };

        // Act
        var totalVIs = config.TotalMarketVIs;

        // Assert
        Assert.Equal(10_000_000, totalVIs); // 500 * 200 * 100
    }
}
