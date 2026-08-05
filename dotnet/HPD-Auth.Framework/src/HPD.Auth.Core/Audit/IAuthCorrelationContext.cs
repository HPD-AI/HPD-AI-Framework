namespace HPD.Auth.Core.Audit;

/// <summary>Exposes the immutable, bounded correlation identifier for the current operation.</summary>
public interface IAuthCorrelationContext
{
    string? CorrelationId { get; }
}
