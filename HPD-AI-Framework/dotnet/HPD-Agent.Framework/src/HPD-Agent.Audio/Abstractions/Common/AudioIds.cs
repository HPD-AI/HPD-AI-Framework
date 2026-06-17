namespace HPD.Agent.Audio;

public readonly record struct AudioSessionId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct AudioTurnId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct InputContentId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ProviderRouteId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ProviderRouteEpochId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct InteractionSessionId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct LedgerRecordId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct TraceRecordId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ThreadProjectionId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct TurnEvidenceId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct OutputFlowId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct OutputSegmentId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ResponseId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct TransportAdapterId(string Value)
{
    public override string ToString() => Value;
}
