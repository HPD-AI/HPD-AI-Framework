using Rhodium.Primitives;

namespace Rhodium.Simulation.Exchange;

internal sealed class FlatMarketByOrderBook
{
    private const int DefaultCapacity = 1024;
    private const int DefaultMapCapacity = 2048;
    private const byte Empty = 0;
    private const byte Occupied = 1;
    private const byte Deleted = 2;

    private BookOrderId[] _orderIds = new BookOrderId[DefaultCapacity];
    private Side[] _sides = new Side[DefaultCapacity];
    private Price[] _prices = new Price[DefaultCapacity];
    private Qty[] _quantities = new Qty[DefaultCapacity];
    private long[] _sequences = new long[DefaultCapacity];
    private int[] _nextFree = new int[DefaultCapacity];
    private bool[] _active = new bool[DefaultCapacity];
    private long[] _mapKeys = new long[DefaultMapCapacity];
    private int[] _mapValues = new int[DefaultMapCapacity];
    private byte[] _mapStates = new byte[DefaultMapCapacity];
    private int _freeHead = -1;
    private int _nextUnused;
    private int _mapCount;
    private int _mapDeleted;
    private int _bestBidIndex = -1;
    private int _bestAskIndex = -1;

    public void Clear()
    {
        Array.Clear(_active);
        Array.Clear(_mapStates);
        _nextUnused = 0;
        _freeHead = -1;
        _mapCount = 0;
        _mapDeleted = 0;
        _bestBidIndex = -1;
        _bestAskIndex = -1;
    }

    public void AddOrUpdate(BookOrderId orderId, Side side, Price price, Qty quantity, long sequence)
    {
        if (quantity.Value <= 0m)
        {
            Delete(orderId);
            return;
        }

        var isNew = !TryGetIndex(orderId, out var index);
        if (isNew)
        {
            index = AllocateSlot();
            AddIndex(orderId, index);
            _orderIds[index] = orderId;
        }
        else if (_active[index])
        {
            InvalidateBestIf(index, _sides[index]);
        }

        _sides[index] = side;
        _prices[index] = price;
        _quantities[index] = quantity;
        _sequences[index] = sequence;
        _active[index] = true;
        ConsiderBest(index, side);
    }

    public void Delete(BookOrderId orderId)
    {
        if (!RemoveIndex(orderId, out var index))
            return;

        _active[index] = false;
        _quantities[index] = Qty.Zero;
        InvalidateBestIf(index, _sides[index]);
        _nextFree[index] = _freeHead;
        _freeHead = index;
    }

    public void Execute(BookOrderId orderId, Qty executedQuantity)
    {
        if (!TryGetIndex(orderId, out var index))
            return;

        var nextQuantity = new Qty(_quantities[index].Value - executedQuantity.Value);
        if (nextQuantity.Value <= 0m)
        {
            Delete(orderId);
            return;
        }

        _quantities[index] = nextQuantity;
    }

    public bool TryConsume(
        Side aggressorSide,
        Qty requestedQuantity,
        int priceProtectionTicks,
        decimal tickSize,
        out Qty filledQuantity,
        out Price averagePrice)
    {
        var passiveSide = aggressorSide == Side.Buy ? Side.Sell : Side.Buy;
        var remaining = requestedQuantity.Value;
        var filled = 0m;
        var notional = 0m;
        Price? firstPrice = null;
        var maxMove = priceProtectionTicks > 0 ? priceProtectionTicks * tickSize : 0m;

        while (remaining > 0m && TryGetBest(passiveSide, out var index))
        {
            firstPrice ??= _prices[index];
            if (priceProtectionTicks > 0
                && !IsWithinPriceProtection(aggressorSide, _prices[index], firstPrice.Value, maxMove))
            {
                break;
            }

            var quantity = _quantities[index].Value;
            var take = Math.Min(remaining, quantity);
            filled += take;
            notional += take * _prices[index].Value;
            remaining -= take;

            var nextQuantity = new Qty(quantity - take);
            if (nextQuantity.Value <= 0m)
                Delete(_orderIds[index]);
            else
                _quantities[index] = nextQuantity;
        }

        if (filled <= 0m || firstPrice is null)
        {
            filledQuantity = default;
            averagePrice = default;
            return false;
        }

        filledQuantity = new Qty(filled);
        averagePrice = new Price(notional / filled, firstPrice.Value.Currency);
        return true;
    }

    public bool TryConsumeLevels(
        Side aggressorSide,
        Qty requestedQuantity,
        int priceProtectionTicks,
        decimal tickSize,
        Span<Level> fills,
        out int fillCount,
        out Qty filledQuantity)
    {
        var passiveSide = aggressorSide == Side.Buy ? Side.Sell : Side.Buy;
        var remaining = requestedQuantity.Value;
        var filled = 0m;
        Price? firstPrice = null;
        fillCount = 0;

        if (fills.Length == 0)
        {
            filledQuantity = default;
            return false;
        }

        var maxMove = priceProtectionTicks > 0 ? priceProtectionTicks * tickSize : 0m;
        while (remaining > 0m && TryGetBest(passiveSide, out var index))
        {
            firstPrice ??= _prices[index];
            if (priceProtectionTicks > 0
                && !IsWithinPriceProtection(aggressorSide, _prices[index], firstPrice.Value, maxMove))
            {
                break;
            }

            if (fillCount == fills.Length)
                break;

            var quantity = _quantities[index].Value;
            var take = Math.Min(remaining, quantity);
            var fillQuantity = new Qty(take);
            fills[fillCount++] = new Level(_prices[index], fillQuantity);
            filled += take;
            remaining -= take;

            var nextQuantity = new Qty(quantity - take);
            if (nextQuantity.Value <= 0m)
                Delete(_orderIds[index]);
            else
                _quantities[index] = nextQuantity;
        }

        filledQuantity = new Qty(filled);
        return filled > 0m;
    }

    public bool CanFullyConsume(
        Side aggressorSide,
        Qty requestedQuantity,
        int priceProtectionTicks,
        decimal tickSize)
    {
        var passiveSide = aggressorSide == Side.Buy ? Side.Sell : Side.Buy;
        if (!TryGetBest(passiveSide, out var bestIndex))
            return false;

        var referencePrice = _prices[bestIndex];
        var maxMove = priceProtectionTicks > 0 ? priceProtectionTicks * tickSize : 0m;
        var available = 0m;
        for (var i = 0; i < _nextUnused; i++)
        {
            if (!_active[i]
                || _sides[i] != passiveSide
                || _quantities[i].Value <= 0m)
            {
                continue;
            }

            if (priceProtectionTicks > 0
                && !IsWithinPriceProtection(aggressorSide, _prices[i], referencePrice, maxMove))
            {
                continue;
            }

            available += _quantities[i].Value;
            if (available >= requestedQuantity.Value)
                return true;
        }

        return false;
    }

    private static bool IsWithinPriceProtection(
        Side aggressorSide,
        Price price,
        Price referencePrice,
        decimal maxMove)
        => aggressorSide == Side.Buy
            ? price.Value <= referencePrice.Value + maxMove
            : price.Value >= referencePrice.Value - maxMove;

    private bool TryGetBest(Side passiveSide, out int bestIndex)
    {
        var cached = passiveSide == Side.Buy ? _bestBidIndex : _bestAskIndex;
        if (IsValidBest(cached, passiveSide))
        {
            bestIndex = cached;
            return true;
        }

        return RebuildBest(passiveSide, out bestIndex);
    }

    private bool RebuildBest(Side passiveSide, out int bestIndex)
    {
        bestIndex = -1;
        for (var i = 0; i < _nextUnused; i++)
        {
            if (!_active[i]
                || _sides[i] != passiveSide
                || _quantities[i].Value <= 0m)
            {
                continue;
            }

            if (bestIndex < 0 || IsBetter(i, bestIndex, passiveSide))
                bestIndex = i;
        }

        SetBest(passiveSide, bestIndex);
        return bestIndex >= 0;
    }

    private bool IsValidBest(int index, Side side)
        => index >= 0
            && index < _nextUnused
            && _active[index]
            && _sides[index] == side
            && _quantities[index].Value > 0m;

    private void ConsiderBest(int index, Side side)
    {
        var current = side == Side.Buy ? _bestBidIndex : _bestAskIndex;
        if (!IsValidBest(current, side) || IsBetter(index, current, side))
            SetBest(side, index);
    }

    private void InvalidateBestIf(int index, Side side)
    {
        if (side == Side.Buy)
        {
            if (_bestBidIndex == index)
                _bestBidIndex = -1;
            return;
        }

        if (_bestAskIndex == index)
            _bestAskIndex = -1;
    }

    private void SetBest(Side side, int index)
    {
        if (side == Side.Buy)
        {
            _bestBidIndex = index;
            return;
        }

        _bestAskIndex = index;
    }

    private bool IsBetter(int candidate, int current, Side passiveSide)
    {
        var candidatePrice = _prices[candidate].Value;
        var currentPrice = _prices[current].Value;
        if (candidatePrice != currentPrice)
            return passiveSide == Side.Sell
                ? candidatePrice < currentPrice
                : candidatePrice > currentPrice;

        return _sequences[candidate] < _sequences[current];
    }

    private int AllocateSlot()
    {
        if (_freeHead >= 0)
        {
            var index = _freeHead;
            _freeHead = _nextFree[index];
            return index;
        }

        if (_nextUnused == _orderIds.Length)
            Grow();

        return _nextUnused++;
    }

    private void Grow()
    {
        var oldLength = _orderIds.Length;
        var nextLength = oldLength * 2;
        Array.Resize(ref _orderIds, nextLength);
        Array.Resize(ref _sides, nextLength);
        Array.Resize(ref _prices, nextLength);
        Array.Resize(ref _quantities, nextLength);
        Array.Resize(ref _sequences, nextLength);
        Array.Resize(ref _nextFree, nextLength);
        Array.Resize(ref _active, nextLength);
    }

    private bool TryGetIndex(BookOrderId orderId, out int index)
    {
        var key = orderId.Value;
        var mask = _mapKeys.Length - 1;
        var slot = Hash(key) & mask;
        while (true)
        {
            var state = _mapStates[slot];
            if (state == Empty)
            {
                index = default;
                return false;
            }

            if (state == Occupied && _mapKeys[slot] == key)
            {
                index = _mapValues[slot];
                return true;
            }

            slot = (slot + 1) & mask;
        }
    }

    private void AddIndex(BookOrderId orderId, int index)
    {
        if ((_mapCount + _mapDeleted + 1) * 2 >= _mapKeys.Length)
            GrowMap();

        var key = orderId.Value;
        var mask = _mapKeys.Length - 1;
        var slot = Hash(key) & mask;
        var firstDeleted = -1;
        while (true)
        {
            var state = _mapStates[slot];
            if (state == Empty)
            {
                var target = firstDeleted >= 0 ? firstDeleted : slot;
                if (firstDeleted >= 0)
                    _mapDeleted--;

                _mapKeys[target] = key;
                _mapValues[target] = index;
                _mapStates[target] = Occupied;
                _mapCount++;
                return;
            }

            if (state == Deleted)
            {
                firstDeleted = firstDeleted < 0 ? slot : firstDeleted;
            }
            else if (_mapKeys[slot] == key)
            {
                _mapValues[slot] = index;
                return;
            }

            slot = (slot + 1) & mask;
        }
    }

    private bool RemoveIndex(BookOrderId orderId, out int index)
    {
        var key = orderId.Value;
        var mask = _mapKeys.Length - 1;
        var slot = Hash(key) & mask;
        while (true)
        {
            var state = _mapStates[slot];
            if (state == Empty)
            {
                index = default;
                return false;
            }

            if (state == Occupied && _mapKeys[slot] == key)
            {
                index = _mapValues[slot];
                _mapStates[slot] = Deleted;
                _mapCount--;
                _mapDeleted++;
                return true;
            }

            slot = (slot + 1) & mask;
        }
    }

    private void GrowMap()
    {
        var oldKeys = _mapKeys;
        var oldValues = _mapValues;
        var oldStates = _mapStates;
        _mapKeys = new long[oldKeys.Length * 2];
        _mapValues = new int[_mapKeys.Length];
        _mapStates = new byte[_mapKeys.Length];
        _mapCount = 0;
        _mapDeleted = 0;

        for (var i = 0; i < oldKeys.Length; i++)
        {
            if (oldStates[i] == Occupied)
                AddIndex(new BookOrderId(oldKeys[i]), oldValues[i]);
        }
    }

    private static int Hash(long value)
    {
        unchecked
        {
            var x = (ulong)value;
            x ^= x >> 33;
            x *= 0xff51afd7ed558ccdUL;
            x ^= x >> 33;
            x *= 0xc4ceb9fe1a85ec53UL;
            x ^= x >> 33;
            return (int)x;
        }
    }
}
