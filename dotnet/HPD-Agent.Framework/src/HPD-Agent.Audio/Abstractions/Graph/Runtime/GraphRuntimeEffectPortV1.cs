using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal abstract record GraphRuntimeEffectRequestV1
{
    private GraphRuntimeEffectRequestV1() { }

    internal abstract OperationId OperationId { get; }
    internal abstract GraphRuntimeCommandKindV1 Kind { get; }
    internal abstract Hash256 RequestHash { get; }

    internal static GraphRuntimeEffectRequestV1 From(GraphRuntimeReducerV1.EffectRequired capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (!capability.IsAuthentic || capability.Command.OperationId.IsValid is false ||
            capability.Command.EffectRequestHash == default ||
            capability.AdmittedCommand.IsValid is false ||
            capability.AdmittedCommand.Session != capability.Command.ExpectedPredecessor.Session ||
            capability.AdmittedCommand.Sequence <= capability.Command.ExpectedPredecessor.Sequence)
            throw new ArgumentException("An authenticated admitted graph-runtime command is required.", nameof(capability));

        return capability.Command switch
        {
            GraphRuntimeCommandV1.Activate => Activate.Create(capability),
            GraphRuntimeCommandV1.Retire => Retire.Create(capability),
            _ => throw new ArgumentException("A closed graph-runtime command arm is required.", nameof(capability)),
        };
    }

    internal sealed record Activate : GraphRuntimeEffectRequestV1
    {
        private readonly OperationId _operationId;
        private readonly Hash256 _requestHash;

        private Activate(OperationId operationId, Hash256 requestHash, JournalPositionV1 graphAuthorityFact,
            Hash256 topologyFingerprint, GraphGenerationId graphGeneration, JournalPositionV1 capacityGrantFact)
        {
            _operationId = operationId;
            _requestHash = requestHash;
            GraphAuthorityFact = graphAuthorityFact;
            TopologyFingerprint = topologyFingerprint;
            GraphGeneration = graphGeneration;
            CapacityGrantFact = capacityGrantFact;
        }

        internal static Activate Create(GraphRuntimeReducerV1.EffectRequired capability)
        {
            if (!capability.IsAuthentic || capability.Command is not GraphRuntimeCommandV1.Activate command)
                throw new ArgumentException("An authenticated Activate capability is required.", nameof(capability));
            return new Activate(command.OperationId, command.EffectRequestHash, command.GraphAuthorityFact,
                command.TopologyFingerprint, command.GraphGeneration, command.CapacityGrantFact);
        }

        internal override OperationId OperationId => _operationId;
        internal override GraphRuntimeCommandKindV1 Kind => GraphRuntimeCommandKindV1.Activate;
        internal override Hash256 RequestHash => _requestHash;
        internal JournalPositionV1 GraphAuthorityFact { get; }
        internal Hash256 TopologyFingerprint { get; }
        internal GraphGenerationId GraphGeneration { get; }
        internal JournalPositionV1 CapacityGrantFact { get; }
    }

    internal sealed record Retire : GraphRuntimeEffectRequestV1
    {
        private readonly OperationId _operationId;
        private readonly Hash256 _requestHash;

        private Retire(OperationId operationId, Hash256 requestHash, JournalPositionV1 activeRuntimeFact)
        {
            _operationId = operationId;
            _requestHash = requestHash;
            ActiveRuntimeFact = activeRuntimeFact;
        }

        internal static Retire Create(GraphRuntimeReducerV1.EffectRequired capability)
        {
            if (!capability.IsAuthentic || capability.Command is not GraphRuntimeCommandV1.Retire command)
                throw new ArgumentException("An authenticated Retire capability is required.", nameof(capability));
            return new Retire(command.OperationId, command.EffectRequestHash, command.ActiveRuntimeFact);
        }

        internal override OperationId OperationId => _operationId;
        internal override GraphRuntimeCommandKindV1 Kind => GraphRuntimeCommandKindV1.Retire;
        internal override Hash256 RequestHash => _requestHash;
        internal JournalPositionV1 ActiveRuntimeFact { get; }
    }
}

internal sealed record GraphRuntimeEffectQueryV1
{
    internal GraphRuntimeEffectQueryV1(OperationId operationId, GraphRuntimeCommandKindV1 kind, Hash256 requestHash)
    {
        if (!operationId.IsValid || !Enum.IsDefined(kind) || requestHash == default)
            throw new ArgumentException("A complete graph-runtime effect identity is required.");
        OperationId = operationId;
        Kind = kind;
        RequestHash = requestHash;
    }

    internal OperationId OperationId { get; }
    internal GraphRuntimeCommandKindV1 Kind { get; }
    internal Hash256 RequestHash { get; }
}

internal abstract record GraphRuntimeEffectExecutionResultV1
{
    private GraphRuntimeEffectExecutionResultV1() { }

    internal sealed record Completed : GraphRuntimeEffectExecutionResultV1
    {
        private readonly byte[] _receiptBytes;

        internal Completed(ReadOnlySpan<byte> receiptBytes)
        {
            if (receiptBytes.Length is < 1 or > 4096)
                throw new ArgumentOutOfRangeException(nameof(receiptBytes));
            _receiptBytes = receiptBytes.ToArray();
        }

        internal ReadOnlyMemory<byte> ReceiptBytes => _receiptBytes;
    }

    internal sealed record Refused : GraphRuntimeEffectExecutionResultV1
    {
        internal Refused(BoundedAscii safeCode)
        {
            if (!safeCode.IsValid)
                throw new ArgumentException("A valid refusal code is required.", nameof(safeCode));
            SafeCode = safeCode;
        }

        internal BoundedAscii SafeCode { get; }
    }

    internal sealed record OutcomeUnknown : GraphRuntimeEffectExecutionResultV1
    {
        internal OutcomeUnknown(BoundedAscii safeCode)
        {
            if (!safeCode.IsValid)
                throw new ArgumentException("A valid unknown-outcome code is required.", nameof(safeCode));
            SafeCode = safeCode;
        }

        internal BoundedAscii SafeCode { get; }
    }
}

internal abstract record GraphRuntimeEffectQueryResultV1
{
    private GraphRuntimeEffectQueryResultV1() { }

    internal sealed record Completed : GraphRuntimeEffectQueryResultV1
    {
        private readonly byte[] _receiptBytes;

        internal Completed(ReadOnlySpan<byte> receiptBytes)
        {
            if (receiptBytes.Length is < 1 or > 4096)
                throw new ArgumentOutOfRangeException(nameof(receiptBytes));
            _receiptBytes = receiptBytes.ToArray();
        }

        internal ReadOnlyMemory<byte> ReceiptBytes => _receiptBytes;
    }

    internal sealed record NotObserved : GraphRuntimeEffectQueryResultV1;
    internal sealed record Contradictory : GraphRuntimeEffectQueryResultV1;

    internal sealed record OutcomeUnknown : GraphRuntimeEffectQueryResultV1
    {
        internal OutcomeUnknown(BoundedAscii safeCode)
        {
            if (!safeCode.IsValid)
                throw new ArgumentException("A valid unknown-outcome code is required.", nameof(safeCode));
            SafeCode = safeCode;
        }

        internal BoundedAscii SafeCode { get; }
    }
}

internal interface IGraphRuntimeEffectPortV1
{
    ValueTask<GraphRuntimeEffectExecutionResultV1> ExecuteAsync(
        GraphRuntimeEffectRequestV1 request,
        CancellationToken cancellationToken = default);

    ValueTask<GraphRuntimeEffectQueryResultV1> QueryAsync(
        GraphRuntimeEffectQueryV1 query,
        CancellationToken cancellationToken = default);
}
