namespace HPD.Agent.Middleware;

/// <summary>Middleware that owns resources released when its built Agent is disposed.</summary>
public interface IAgentOwnedMiddleware : IAgentMiddleware, IAsyncDisposable;
