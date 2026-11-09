using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal abstract record GraphRuntimeEffectRequest
{
    private GraphRuntimeEffectRequest() { }

    internal abstract OperationId OperationId { get; }
    internal abstract GraphRuntimeCommandKind Kind { get; }
    internal abstract Hash256 RequestHash { get; }

    internal static GraphRuntimeEffectRequest From(GraphRuntimeReducer.EffectRequired capability)
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
            GraphRuntimeCommand.Activate => Activate.Create(capability),
            GraphRuntimeCommand.Retire => Retire.Create(capability),
            _ => throw new ArgumentException("A closed graph-runtime command arm is required.", nameof(capability)),
        };
    }

    internal sealed record Activate : GraphRuntimeEffectRequest
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

        internal static Activate Create(GraphRuntimeReducer.EffectRequired capability)
        {
            if (!capability.IsAuthentic || capability.Command is not GraphRuntimeCommand.Activate command)
                throw new ArgumentException("An authenticated Activate capability is required.", nameof(capability));
            return new Activate(command.OperationId, command.EffectRequestHash, command.GraphAuthorityFact,
                command.TopologyFingerprint, command.GraphGeneration, command.CapacityGrantFact);
        }

        internal override OperationId OperationId => _operationId;
        internal override GraphRuntimeCommandKind Kind => GraphRuntimeCommandKind.Activate;
        internal override Hash256 RequestHash => _requestHash;
        internal JournalPositionV1 GraphAuthorityFact { get; }
        internal Hash256 TopologyFingerprint { get; }
        internal GraphGenerationId GraphGeneration { get; }
        internal JournalPositionV1 CapacityGrantFact { get; }
    }

    internal sealed record Retire : GraphRuntimeEffectRequest
    {
        private readonly OperationId _operationId;
        private readonly Hash256 _requestHash;

        private Retire(OperationId operationId, Hash256 requestHash, JournalPositionV1 activeRuntimeFact)
        {
            _operationId = operationId;
            _requestHash = requestHash;
            ActiveRuntimeFact = activeRuntimeFact;
        }

        internal static Retire Create(GraphRuntimeReducer.EffectRequired capability)
        {
            if (!capability.IsAuthentic || capability.Command is not GraphRuntimeCommand.Retire command)
                throw new ArgumentException("An authenticated Retire capability is required.", nameof(capability));
            return new Retire(command.OperationId, command.EffectRequestHash, command.ActiveRuntimeFact);
        }

        internal override OperationId OperationId => _operationId;
        internal override GraphRuntimeCommandKind Kind => GraphRuntimeCommandKind.Retire;
        internal override Hash256 RequestHash => _requestHash;
        internal JournalPositionV1 ActiveRuntimeFact { get; }
    }
}

internal sealed record GraphRuntimeEffectQuery
{
    internal GraphRuntimeEffectQuery(OperationId operationId, GraphRuntimeCommandKind kind, Hash256 requestHash)
    {
        if (!operationId.IsValid || !Enum.IsDefined(kind) || requestHash == default)
            throw new ArgumentException("A complete graph-runtime effect identity is required.");
        OperationId = operationId;
        Kind = kind;
        RequestHash = requestHash;
    }

    internal OperationId OperationId { get; }
    internal GraphRuntimeCommandKind Kind { get; }
    internal Hash256 RequestHash { get; }
}

internal abstract record GraphRuntimeEffectExecutionResult
{
    private GraphRuntimeEffectExecutionResult() { }

    internal sealed record Completed : GraphRuntimeEffectExecutionResult
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

    internal sealed record Refused : GraphRuntimeEffectExecutionResult
    {
        internal Refused(BoundedAscii safeCode)
        {
            if (!safeCode.IsValid)
                throw new ArgumentException("A valid refusal code is required.", nameof(safeCode));
            SafeCode = safeCode;
        }

        internal BoundedAscii SafeCode { get; }
    }

    internal sealed record OutcomeUnknown : GraphRuntimeEffectExecutionResult
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

internal abstract record GraphRuntimeEffectQueryResult
{
    private GraphRuntimeEffectQueryResult() { }

    internal sealed record Completed : GraphRuntimeEffectQueryResult
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

    internal sealed record NotObserved : GraphRuntimeEffectQueryResult;
    internal sealed record Contradictory : GraphRuntimeEffectQueryResult;

    internal sealed record OutcomeUnknown : GraphRuntimeEffectQueryResult
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

internal interface IGraphRuntimeEffectPort
{
    ValueTask<GraphRuntimeEffectExecutionResult> ExecuteAsync(
        GraphRuntimeEffectRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<GraphRuntimeEffectQueryResult> QueryAsync(
        GraphRuntimeEffectQuery query,
        CancellationToken cancellationToken = default);
}
