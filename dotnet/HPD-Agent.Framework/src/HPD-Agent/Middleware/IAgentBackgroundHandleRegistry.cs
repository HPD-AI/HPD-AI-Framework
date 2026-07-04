namespace HPD.Agent.Middleware;

/// <summary>
/// Runtime-owned registry for controllable background resources.
/// </summary>
public interface IAgentBackgroundHandleRegistry
{
    /// <summary>
    /// Registers a controllable background resource with the runtime.
    /// </summary>
    /// <param name="descriptor">The handle descriptor.</param>
    /// <param name="handle">The handle implementation.</param>
    /// <returns>The accepted handle registration.</returns>
    BackgroundHandleRegistration RegisterHandle(
        BackgroundHandleDescriptor descriptor,
        IBackgroundHandle handle);

    /// <summary>
    /// Attempts to find a handle by id within the supplied scope.
    /// </summary>
    /// <param name="handleId">The handle id.</param>
    /// <param name="scope">The required access scope.</param>
    /// <param name="handle">The registered handle, when found and authorized.</param>
    /// <returns><see langword="true"/> when a matching handle was found.</returns>
    bool TryGetHandle(
        string handleId,
        BackgroundHandleScope scope,
        out RegisteredBackgroundHandle handle);

    /// <summary>
    /// Lists handles matching a query.
    /// </summary>
    /// <param name="query">The handle query.</param>
    /// <returns>Registered handles that match the query.</returns>
    IReadOnlyList<RegisteredBackgroundHandle> ListHandles(BackgroundHandleQuery query);
}
