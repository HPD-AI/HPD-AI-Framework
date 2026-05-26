using Rhodium.Primitives;

namespace Rhodium.Simulation.Exchange;

public enum SimulationCommandPriority : byte
{
    Cancel = 0,
    Modify = 1,
    Submit = 2,
    ModuleGenerated = 3
}

public readonly record struct SimulationCommandEnvelope(
    Instant ArrivesAt,
    SimulationCommandPriority Priority,
    long Sequence,
    SimulationOrderCommand? Submit = null,
    SimulationCancelCommand? Cancel = null,
    SimulationModifyCommand? Modify = null);

/// <summary>
/// Deterministic exchange-owned command heap ordered by arrival time, priority, then sequence.
/// </summary>
public sealed class SimulationCommandHeap
{
    private const int DefaultCapacity = 64;

    private SimulationCommandEnvelope[] _commands;
    private int _count;
    private long _nextSequence;

    public SimulationCommandHeap(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Command heap capacity must be positive.");

        _commands = new SimulationCommandEnvelope[capacity];
    }

    public int Count => _count;

    public void EnqueueSubmit(SimulationOrderCommand command, Instant arrivesAt)
        => Enqueue(new SimulationCommandEnvelope(
            arrivesAt,
            SimulationCommandPriority.Submit,
            ++_nextSequence,
            Submit: command));

    public void EnqueueCancel(SimulationCancelCommand command, Instant arrivesAt)
        => Enqueue(new SimulationCommandEnvelope(
            arrivesAt,
            SimulationCommandPriority.Cancel,
            ++_nextSequence,
            Cancel: command));

    public void EnqueueModify(SimulationModifyCommand command, Instant arrivesAt)
        => Enqueue(new SimulationCommandEnvelope(
            arrivesAt,
            SimulationCommandPriority.Modify,
            ++_nextSequence,
            Modify: command));

    public bool HasDue(Instant now)
        => _count > 0 && _commands[0].ArrivesAt <= now;

    public bool TryDequeueDue(Instant now, out SimulationCommandEnvelope command)
    {
        if (!HasDue(now))
        {
            command = default;
            return false;
        }

        command = _commands[0];
        RemoveAt(0);
        return true;
    }

    public bool TryRemoveInflightSubmit(OrderId orderId, out SimulationOrderCommand command)
    {
        for (var i = 0; i < _count; i++)
        {
            var pending = _commands[i];
            if (pending.Submit is not { } submit || submit.ClientOrderId != orderId)
                continue;

            RemoveAt(i);
            command = submit;
            return true;
        }

        command = default;
        return false;
    }

    public bool TryModifyInflightSubmit(SimulationModifyCommand modify, out SimulationOrderCommand command)
    {
        for (var i = 0; i < _count; i++)
        {
            var pending = _commands[i];
            if (pending.Submit is not { } submit || submit.ClientOrderId != modify.OrderId)
                continue;

            var nextExecution = modify.NewLimitPrice.HasValue
                ? submit.Execution.At(modify.NewLimitPrice.Value)
                : submit.Execution;
            var next = submit with
            {
                Quantity = modify.NewQuantity ?? submit.Quantity,
                Execution = nextExecution
            };

            _commands[i] = pending with { Submit = next };
            command = next;
            return true;
        }

        command = default;
        return false;
    }

    public int RemoveSameArrivalModifies(OrderId orderId, Instant arrivesAt)
    {
        var removed = 0;
        for (var i = _count - 1; i >= 0; i--)
        {
            var pending = _commands[i];
            if (pending.ArrivesAt != arrivesAt
                || pending.Modify is not { } modify
                || modify.OrderId != orderId)
            {
                continue;
            }

            RemoveAt(i);
            removed++;
        }

        return removed;
    }

    public void Clear()
    {
        Array.Clear(_commands, 0, _count);
        _count = 0;
        _nextSequence = 0;
    }

    private void Enqueue(SimulationCommandEnvelope command)
    {
        if (_count == _commands.Length)
            Grow();

        _commands[_count] = command;
        SiftUp(_count);
        _count++;
    }

    private void RemoveAt(int index)
    {
        var lastIndex = _count - 1;
        if (index == lastIndex)
        {
            _commands[lastIndex] = default;
            _count--;
            return;
        }

        _commands[index] = _commands[lastIndex];
        _commands[lastIndex] = default;
        _count--;
        if (index > 0 && Compare(_commands[index], _commands[Parent(index)]) < 0)
            SiftUp(index);
        else
            SiftDown(index);
    }

    private void SiftUp(int index)
    {
        while (index > 0)
        {
            var parent = Parent(index);
            if (Compare(_commands[index], _commands[parent]) >= 0)
                return;

            (_commands[index], _commands[parent]) = (_commands[parent], _commands[index]);
            index = parent;
        }
    }

    private void SiftDown(int index)
    {
        while (true)
        {
            var left = index * 2 + 1;
            if (left >= _count)
                return;

            var right = left + 1;
            var smallest = right < _count && Compare(_commands[right], _commands[left]) < 0
                ? right
                : left;

            if (Compare(_commands[index], _commands[smallest]) <= 0)
                return;

            (_commands[index], _commands[smallest]) = (_commands[smallest], _commands[index]);
            index = smallest;
        }
    }

    private static int Parent(int index)
        => (index - 1) / 2;

    private void Grow()
    {
        var next = new SimulationCommandEnvelope[_commands.Length * 2];
        Array.Copy(_commands, next, _commands.Length);
        _commands = next;
    }

    private static int Compare(SimulationCommandEnvelope x, SimulationCommandEnvelope y)
    {
        var byTime = x.ArrivesAt.CompareTo(y.ArrivesAt);
        if (byTime != 0)
            return byTime;

        var byPriority = x.Priority.CompareTo(y.Priority);
        if (byPriority != 0)
            return byPriority;

        return x.Sequence.CompareTo(y.Sequence);
    }
}
