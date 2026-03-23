using HPD.RAG.Core.Context;

namespace HPD.RAG.Core.Pipeline;

/// <summary>
/// Tier 1.5 custom node — runtime routing decisions (quality branching, classification, guardrails).
/// MRAG bridges this to HPD.Graph's PortOutputs internally.
/// Builder exposes .Port(n).To() for port-based edge wiring.
/// </summary>
public interface IMragRouter<TIn>
{
    Task<MragRouteResult> RouteAsync(TIn input, MragProcessingContext context, CancellationToken ct);
}

/// <summary>
/// Result from an IMragRouter. Carries the output port index and the data to pass downstream.
/// Data is constrained to MRAG DTO types registered in MragJsonSerializerContext — anonymous types
/// and arbitrary objects are rejected at runtime to keep all socket values AOT-safe.
/// </summary>
public sealed class MragRouteResult
{
    public int Port { get; }
    public object Data { get; }

    private MragRouteResult(int port, object data)
    {
        Port = port;
        Data = data;
    }

    public static MragRouteResult To(int port, object data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new MragRouteResult(port, data);
    }
}
