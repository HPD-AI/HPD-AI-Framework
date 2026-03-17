using Rhodium.Primitives;

namespace Rhodium.Primitives.Tests;

public class QtyTests
{
    [Fact]
    public void Qty_ShouldStoreValue()
    {
        // Arrange & Act
        var qty = new Qty(100m);

        // Assert
        Assert.Equal(100m, qty.Value);
    }

    [Fact]
    public void Qty_ShouldSupportArithmetic()
    {
        // Arrange
        var a = new Qty(100m);
        var b = new Qty(50m);

        // Act & Assert
        Assert.Equal(new Qty(150m), a + b);
        Assert.Equal(new Qty(50m), a - b);
        Assert.Equal(new Qty(200m), a * 2m);
        Assert.Equal(new Qty(50m), a / 2m);
        Assert.Equal(new Qty(-100m), -a);
    }

    [Fact]
    public void Qty_ShouldSupportComparison()
    {
        // Arrange
        var a = new Qty(100m);
        var b = new Qty(50m);

        // Act & Assert
        Assert.True(a > b);
        Assert.True(b < a);
        Assert.True(a >= b);
        Assert.True(b <= a);
    }

    [Fact]
    public void Qty_ShouldProvideHelperProperties()
    {
        // Arrange & Act & Assert
        Assert.True(new Qty(0m).IsZero);
        Assert.True(new Qty(10m).IsPositive);
        Assert.True(new Qty(-10m).IsNegative);
        Assert.Equal(new Qty(10m), new Qty(-10m).Abs);
        Assert.Equal(new Qty(-10m), new Qty(10m).Negate);
    }

    [Fact]
    public void Qty_ShouldSupportImplicitConversions()
    {
        // Act
        Qty fromDecimal = 100m;
        Qty fromInt = 100;
        decimal toDecimal = fromDecimal;

        // Assert
        Assert.Equal(100m, fromDecimal.Value);
        Assert.Equal(100m, fromInt.Value);
        Assert.Equal(100m, toDecimal);
    }
}

public class PriceTests
{
    [Fact]
    public void Price_ShouldStoreValueAndCurrency()
    {
        // Arrange & Act
        var price = new Price(100.50m, Currency.USD);

        // Assert
        Assert.Equal(100.50m, price.Value);
        Assert.Equal(Currency.USD, price.Currency);
    }

    [Fact]
    public void Price_ShouldSupportArithmetic()
    {
        // Arrange
        var a = new Price(100m, Currency.USD);
        var b = new Price(50m, Currency.USD);

        // Act & Assert
        Assert.Equal(new Price(150m, Currency.USD), a + b);
        Assert.Equal(new Price(50m, Currency.USD), a - b);
        Assert.Equal(new Price(200m, Currency.USD), a * 2m);
        Assert.Equal(new Price(50m, Currency.USD), a / 2m);
    }

    [Fact]
    public void Price_ShouldSupportMaxMin()
    {
        // Arrange
        var a = new Price(100m);
        var b = new Price(50m);

        // Act & Assert
        Assert.Equal(a, Price.Max(a, b));
        Assert.Equal(b, Price.Min(a, b));
    }

    [Fact]
    public void Price_ToString_ShouldFormatWithCurrency()
    {
        // Arrange
        var price = new Price(123.45m, Currency.USD);

        // Act
        var str = price.ToString();

        // Assert
        Assert.Contains("123.45", str);
        Assert.Contains("USD", str);
    }
}

public class TickPriceTests
{
    [Fact]
    public void TickPrice_ShouldStoreTicksAndTickSize()
    {
        // Arrange & Act
        var tickPrice = new TickPrice(10050, 0.01m);

        // Assert
        Assert.Equal(10050, tickPrice.Ticks);
        Assert.Equal(0.01m, tickPrice.TickSize);
    }

    [Fact]
    public void TickPrice_ShouldConvertToDecimal()
    {
        // Arrange
        var tickPrice = new TickPrice(10050, 0.01m);

        // Act
        var decimal_value = tickPrice.ToDecimal();

        // Assert
        Assert.Equal(100.50m, decimal_value);
    }

    [Fact]
    public void TickPrice_ShouldConvertFromDecimal()
    {
        // Arrange & Act
        var tickPrice = TickPrice.FromDecimal(100.50m, 0.01m);

        // Assert
        Assert.Equal(10050, tickPrice.Ticks);
    }

    [Fact]
    public void TickPrice_ShouldConvertFromPrice()
    {
        // Arrange
        var price = new Price(100.50m);

        // Act
        var tickPrice = TickPrice.FromPrice(price, 0.01m);

        // Assert
        Assert.Equal(10050, tickPrice.Ticks);
    }

    [Fact]
    public void TickPrice_ShouldSupportTickArithmetic()
    {
        // Arrange
        var tickPrice = new TickPrice(100, 0.01m);

        // Act & Assert
        Assert.Equal(new TickPrice(110, 0.01m), tickPrice + 10);
        Assert.Equal(new TickPrice(90, 0.01m), tickPrice - 10);
    }

    [Fact]
    public void TickPrice_ShouldSupportTickDifference()
    {
        // Arrange
        var a = new TickPrice(150, 0.01m);
        var b = new TickPrice(100, 0.01m);

        // Act
        var diff = a - b;

        // Assert
        Assert.Equal(50, diff);
    }

    [Fact]
    public void TickPrice_ShouldSupportComparison()
    {
        // Arrange
        var a = new TickPrice(150, 0.01m);
        var b = new TickPrice(100, 0.01m);

        // Act & Assert
        Assert.True(a > b);
        Assert.True(b < a);
    }
}

public class CurrencyTests
{
    [Fact]
    public void Currency_ShouldHavePredefinedCurrencies()
    {
        // Assert
        Assert.Equal("USD", Currency.USD.Code);
        Assert.Equal("EUR", Currency.EUR.Code);
        Assert.Equal("GBP", Currency.GBP.Code);
        Assert.Equal("JPY", Currency.JPY.Code);
        Assert.Equal("BTC", Currency.BTC.Code);
        Assert.Equal("ETH", Currency.ETH.Code);
    }

    [Fact]
    public void Currency_ShouldSupportImplicitConversion()
    {
        // Act
        Currency currency = "CAD";

        // Assert
        Assert.Equal("CAD", currency.Code);
    }

    [Fact]
    public void Currency_ToString_ShouldReturnCode()
    {
        // Arrange
        Currency currency = "AUD";

        // Act
        var str = currency.ToString();

        // Assert
        Assert.Equal("AUD", str);
    }
}

public class MoneyTests
{
    [Fact]
    public void Money_ShouldStoreAmountAndCurrency()
    {
        // Arrange & Act
        var money = new Money(1000.50m, Currency.USD);

        // Assert
        Assert.Equal(1000.50m, money.Amount);
        Assert.Equal(Currency.USD, money.Currency);
    }

    [Fact]
    public void Money_ShouldSupportArithmetic()
    {
        // Arrange
        var a = new Money(1000m, Currency.USD);
        var b = new Money(500m, Currency.USD);

        // Act & Assert
        Assert.Equal(new Money(1500m, Currency.USD), a + b);
        Assert.Equal(new Money(500m, Currency.USD), a - b);
        Assert.Equal(new Money(2000m, Currency.USD), a * 2m);
        Assert.Equal(new Money(-1000m, Currency.USD), -a);
    }

    [Fact]
    public void Money_ShouldProvideHelperMethods()
    {
        // Act & Assert
        Assert.Equal(new Money(0m, Currency.EUR), Money.Zero(Currency.EUR));
        Assert.Equal(new Money(100m, Currency.USD), Money.USD(100m));
    }

    [Fact]
    public void Money_ShouldProvideHelperProperties()
    {
        // Arrange & Act & Assert
        Assert.True(new Money(0m, Currency.USD).IsZero);
        Assert.True(new Money(100m, Currency.USD).IsPositive);
        Assert.True(new Money(-100m, Currency.USD).IsNegative);
    }

    [Fact]
    public void Money_ToString_ShouldFormatWithCurrency()
    {
        // Arrange
        var money = new Money(1234.56m, Currency.USD);

        // Act
        var str = money.ToString();

        // Assert
        Assert.Contains("1,234.56", str);
        Assert.Contains("USD", str);
    }
}
