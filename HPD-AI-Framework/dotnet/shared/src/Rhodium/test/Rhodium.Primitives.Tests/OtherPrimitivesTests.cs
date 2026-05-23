using Rhodium.Primitives;

namespace Rhodium.Primitives.Tests;

public class SideTests
{
    [Fact]
    public void Side_ShouldHaveCorrectValues()
    {
        // Assert
        Assert.Equal((sbyte)-1, (sbyte)Side.Sell);
        Assert.Equal((sbyte)0, (sbyte)Side.None);
        Assert.Equal((sbyte)1, (sbyte)Side.Buy);
    }

    [Fact]
    public void Side_Opposite_ShouldFlipDirection()
    {
        // Act & Assert
        Assert.Equal(Side.Sell, Side.Buy.Opposite());
        Assert.Equal(Side.Buy, Side.Sell.Opposite());
        Assert.Equal(Side.None, Side.None.Opposite());
    }

    [Fact]
    public void Side_Sign_ShouldReturnNumericValue()
    {
        // Act & Assert
        Assert.Equal(1, Side.Buy.Sign());
        Assert.Equal(-1, Side.Sell.Sign());
        Assert.Equal(0, Side.None.Sign());
    }

    [Fact]
    public void Side_FromSign_ShouldConvertFromInt()
    {
        // Act & Assert
        Assert.Equal(Side.Buy, SideExtensions.FromSign(1));
        Assert.Equal(Side.Buy, SideExtensions.FromSign(100));
        Assert.Equal(Side.Sell, SideExtensions.FromSign(-1));
        Assert.Equal(Side.Sell, SideExtensions.FromSign(-100));
        Assert.Equal(Side.None, SideExtensions.FromSign(0));
    }

    [Fact]
    public void Side_FromQty_ShouldConvertFromQuantity()
    {
        // Act & Assert
        Assert.Equal(Side.Buy, SideExtensions.FromQty(new Qty(100m)));
        Assert.Equal(Side.Sell, SideExtensions.FromQty(new Qty(-100m)));
        Assert.Equal(Side.None, SideExtensions.FromQty(Qty.Zero));
    }
}

public class SequenceTests
{
    [Fact]
    public void Sequence_ShouldStoreValue()
    {
        // Arrange & Act
        var seq = new Sequence(42);

        // Assert
        Assert.Equal(42UL, seq.Value);
    }

    [Fact]
    public void Sequence_Next_ShouldIncrement()
    {
        // Arrange
        var seq = new Sequence(10);

        // Act
        var next = seq.Next();

        // Assert
        Assert.Equal(11UL, next.Value);
        Assert.Equal(10UL, seq.Value); // Original unchanged
    }

    [Fact]
    public void Sequence_ShouldSupportComparison()
    {
        // Arrange
        var a = new Sequence(10);
        var b = new Sequence(20);

        // Act & Assert
        Assert.True(b > a);
        Assert.True(a < b);
        Assert.True(b >= a);
        Assert.True(a <= b);
    }

    [Fact]
    public void Sequence_ShouldHaveZeroConstant()
    {
        // Assert
        Assert.Equal(0UL, Sequence.Zero.Value);
    }

    [Fact]
    public void Sequence_ToString_ShouldReturnValue()
    {
        // Arrange
        var seq = new Sequence(123);

        // Act
        var str = seq.ToString();

        // Assert
        Assert.Equal("123", str);
    }
}

public class RiskDecisionTests
{
    [Fact]
    public void RiskDecision_Approved_ShouldWrapValue()
    {
        // Arrange & Act
        var decision = new RiskDecision<int>.Approved(42);

        // Assert
        Assert.True(decision.IsApproved);
        Assert.False(decision.IsRefused);
        Assert.Equal(42, decision.Value);
    }

    [Fact]
    public void RiskDecision_Refused_ShouldWrapValueAndReason()
    {
        // Arrange & Act
        var decision = new RiskDecision<int>.Refused(42, "Risk limit exceeded", "RULE_001");

        // Assert
        Assert.False(decision.IsApproved);
        Assert.True(decision.IsRefused);
        Assert.Equal(42, decision.Value);
        Assert.Equal("Risk limit exceeded", decision.Reason);
        Assert.Equal("RULE_001", decision.RuleId);
    }

    [Fact]
    public void RiskDecision_Match_ShouldHandleApproved()
    {
        // Arrange
        var decision = new RiskDecision<int>.Approved(42);

        // Act
        var result = decision.Match(
            onApproved: v => $"Approved: {v}",
            onRefused: (v, r) => $"Refused: {r}"
        );

        // Assert
        Assert.Equal("Approved: 42", result);
    }

    [Fact]
    public void RiskDecision_Match_ShouldHandleRefused()
    {
        // Arrange
        var decision = new RiskDecision<int>.Refused(42, "Too risky");

        // Act
        var result = decision.Match(
            onApproved: v => $"Approved: {v}",
            onRefused: (v, r) => $"Refused: {r}"
        );

        // Assert
        Assert.Equal("Refused: Too risky", result);
    }

    [Fact]
    public void RiskDecision_WhereApproved_ShouldFilterApprovals()
    {
        // Arrange
        var decisions = new RiskDecision<int>[]
        {
            new RiskDecision<int>.Approved(1),
            new RiskDecision<int>.Refused(2, "Bad"),
            new RiskDecision<int>.Approved(3)
        };

        // Act
        var approved = decisions.WhereApproved().ToList();

        // Assert
        Assert.Equal(2, approved.Count);
        Assert.Contains(1, approved);
        Assert.Contains(3, approved);
    }

    [Fact]
    public void RiskDecision_WhereRefused_ShouldFilterRefusals()
    {
        // Arrange
        var decisions = new RiskDecision<int>[]
        {
            new RiskDecision<int>.Approved(1),
            new RiskDecision<int>.Refused(2, "Reason1"),
            new RiskDecision<int>.Refused(3, "Reason2")
        };

        // Act
        var refused = decisions.WhereRefused().ToList();

        // Assert
        Assert.Equal(2, refused.Count);
        Assert.Contains((2, "Reason1"), refused);
        Assert.Contains((3, "Reason2"), refused);
    }
}

public class BalanceTests
{
    [Fact]
    public void Balance_ShouldInitializeWithEquity()
    {
        // Arrange & Act
        var balance = new Balance(Currency.USD, Money.USD(1000m));

        // Assert
        Assert.Equal(Currency.USD, balance.Currency);
        Assert.Equal(Money.USD(1000m), balance.Equity);
    }

    [Fact]
    public void Balance_Available_ShouldEqualEquityWhenNoReservations()
    {
        // Arrange
        var balance = new Balance(Currency.USD, Money.USD(1000m));

        // Act & Assert
        Assert.Equal(Money.USD(1000m), balance.Available);
        Assert.Equal(Money.USD(0m), balance.Locked);
    }

    [Fact]
    public void Balance_Reserve_ShouldLockFunds()
    {
        // Arrange
        var balance = new Balance(Currency.USD, Money.USD(1000m));
        var orderId = OrderId.New();

        // Act
        balance.Reserve(orderId, Money.USD(300m));

        // Assert
        Assert.Equal(Money.USD(700m), balance.Available);
        Assert.Equal(Money.USD(300m), balance.Locked);
    }

    [Fact]
    public void Balance_Reserve_ShouldThrowIfInsufficientFunds()
    {
        // Arrange
        var balance = new Balance(Currency.USD, Money.USD(100m));
        var orderId = OrderId.New();

        // Act & Assert
        var ex = Assert.Throws<InsufficientFundsException>(() =>
            balance.Reserve(orderId, Money.USD(200m))
        );
        Assert.Equal(orderId, ex.OrderId);
        Assert.Equal(Money.USD(200m), ex.Requested);
        Assert.Equal(Money.USD(100m), ex.Available);
    }

    [Fact]
    public void Balance_Release_ShouldUnlockFunds()
    {
        // Arrange
        var balance = new Balance(Currency.USD, Money.USD(1000m));
        var orderId = OrderId.New();
        balance.Reserve(orderId, Money.USD(300m));

        // Act
        balance.Release(orderId);

        // Assert
        Assert.Equal(Money.USD(1000m), balance.Available);
        Assert.Equal(Money.USD(0m), balance.Locked);
    }

    [Fact]
    public void Balance_ApplyFill_ShouldReleaseAndDeductFromEquity()
    {
        // Arrange
        var balance = new Balance(Currency.USD, Money.USD(1000m));
        var orderId = OrderId.New();
        balance.Reserve(orderId, Money.USD(300m));

        // Act
        balance.ApplyFill(orderId, Money.USD(250m), Money.USD(5m));

        // Assert
        Assert.Equal(Money.USD(745m), balance.Equity); // 1000 - 250 - 5
        Assert.Equal(Money.USD(745m), balance.Available);
        Assert.Equal(Money.USD(0m), balance.Locked);
    }

    [Fact]
    public void Balance_Credit_ShouldIncreaseEquity()
    {
        // Arrange
        var balance = new Balance(Currency.USD, Money.USD(1000m));

        // Act
        balance.Credit(Money.USD(500m));

        // Assert
        Assert.Equal(Money.USD(1500m), balance.Equity);
        Assert.Equal(Money.USD(1500m), balance.Available);
    }

    [Fact]
    public void Balance_ShouldSupportMultipleReservations()
    {
        // Arrange
        var balance = new Balance(Currency.USD, Money.USD(1000m));
        var order1 = OrderId.New();
        var order2 = OrderId.New();

        // Act
        balance.Reserve(order1, Money.USD(300m));
        balance.Reserve(order2, Money.USD(200m));

        // Assert
        Assert.Equal(Money.USD(500m), balance.Available);
        Assert.Equal(Money.USD(500m), balance.Locked);
    }
}

public class OrderIdTests
{
    [Fact]
    public void OrderId_New_ShouldGenerateUniqueIds()
    {
        // Act
        var id1 = OrderId.New();
        var id2 = OrderId.New();
        var id3 = OrderId.New();

        // Assert
        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id2, id3);
        Assert.NotEqual(id1, id3);
    }

    [Fact]
    public void OrderId_ShouldSupportImplicitConversion()
    {
        // Act
        OrderId orderId = 12345L;

        // Assert
        Assert.Equal(12345L, orderId.Value);
    }

    [Fact]
    public void OrderId_ToString_ShouldReturnValue()
    {
        // Arrange
        OrderId orderId = 99999L;

        // Act
        var str = orderId.ToString();

        // Assert
        Assert.Equal("99999", str);
    }
}

public class SettlementIdTests
{
    [Fact]
    public void SettlementId_New_ShouldGenerateUniqueIds()
    {
        var id1 = SettlementId.New();
        var id2 = SettlementId.New();
        var id3 = SettlementId.New();

        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id2, id3);
        Assert.True(id2.Value > id1.Value);
        Assert.True(id3.Value > id2.Value);
    }

    [Fact]
    public void SettlementId_ShouldSupportImplicitConversion()
    {
        SettlementId settlementId = 12345L;

        Assert.Equal(12345L, settlementId.Value);
    }

    [Fact]
    public void SettlementId_ToString_ShouldReturnValue()
    {
        SettlementId settlementId = 99999L;

        Assert.Equal("99999", settlementId.ToString());
    }
}

public class AssetDeliveryIdTests
{
    [Fact]
    public void AssetDeliveryId_New_ShouldGenerateUniqueIds()
    {
        var id1 = AssetDeliveryId.New();
        var id2 = AssetDeliveryId.New();
        var id3 = AssetDeliveryId.New();

        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id2, id3);
        Assert.True(id2.Value > id1.Value);
        Assert.True(id3.Value > id2.Value);
    }

    [Fact]
    public void AssetDeliveryId_ShouldSupportImplicitConversion()
    {
        AssetDeliveryId deliveryId = 12345L;

        Assert.Equal(12345L, deliveryId.Value);
    }

    [Fact]
    public void AssetDeliveryId_ToString_ShouldReturnValue()
    {
        AssetDeliveryId deliveryId = 99999L;

        Assert.Equal("99999", deliveryId.ToString());
    }
}

public class AccountTransferIdTests
{
    [Fact]
    public void AccountTransferId_New_ShouldGenerateUniqueIds()
    {
        var id1 = AccountTransferId.New();
        var id2 = AccountTransferId.New();
        var id3 = AccountTransferId.New();

        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id2, id3);
        Assert.True(id2.Value > id1.Value);
        Assert.True(id3.Value > id2.Value);
    }

    [Fact]
    public void AccountTransferId_ShouldSupportImplicitConversion()
    {
        AccountTransferId transferId = 12345L;

        Assert.Equal(12345L, transferId.Value);
    }

    [Fact]
    public void AccountTransferId_ToString_ShouldReturnValue()
    {
        AccountTransferId transferId = 99999L;

        Assert.Equal("99999", transferId.ToString());
    }
}
