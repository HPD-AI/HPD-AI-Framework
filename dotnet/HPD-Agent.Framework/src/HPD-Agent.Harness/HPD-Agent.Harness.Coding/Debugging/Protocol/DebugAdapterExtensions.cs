using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HPD.Agent.Middleware;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

public interface IDebugAdapterExtensionRegistration
{
    string AdapterId { get; }
    string Command { get; }
    DebugTreeGrant RequiredGrant { get; }
    bool RequiresPrivilegedAuthorization { get; }
    int MaximumRequestBytes { get; }
    int MaximumResponseBytes { get; }
    Type RequestType { get; }
    Type ResponseType { get; }
}

public abstract class DebugAdapterExtension<TRequest, TResponse> : IDebugAdapterExtensionRegistration
{
    public abstract string AdapterId { get; }
    public abstract string Command { get; }
    public abstract JsonTypeInfo<TRequest> RequestTypeInfo { get; }
    public abstract JsonTypeInfo<TResponse> ResponseTypeInfo { get; }
    public virtual DebugTreeGrant RequiredGrant => DebugTreeGrant.Inspect;
    public virtual bool RequiresPrivilegedAuthorization => false;
    public virtual int MaximumRequestBytes => 64 * 1024;
    public virtual int MaximumResponseBytes => 256 * 1024;
    public Type RequestType => typeof(TRequest);
    public Type ResponseType => typeof(TResponse);
    public virtual ValueTask ValidateAsync(TRequest request, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

/// <summary>
/// Trusted-host-only typed invocation boundary. Model-facing debugger functions do not expose it.
/// The function context supplies runtime ownership. Mutating extensions remain behind approved debugger
/// functions, which pass the same narrow operation proof used by built-in mutations.
/// </summary>
public interface IDebugAdapterExtensionHost
{
    ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(
        FunctionExecutionContext context,
        string debugTreeId,
        string? debugSessionId,
        string command,
        TRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Typed, host-only adapter extension boundary. It is intentionally absent from DebugSemanticService.</summary>
internal sealed class DebugAdapterExtensionRegistry : IDebugAdapterExtensionHost
{
    private readonly IReadOnlyDictionary<(string Adapter, string Command), IDebugAdapterExtensionRegistration> _extensions;

    public DebugAdapterExtensionRegistry(IEnumerable<IDebugAdapterExtensionRegistration> extensions)
    {
        var canonical = DebugProtocolFeatureInventory.All.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        var registrations = new Dictionary<(string, string), IDebugAdapterExtensionRegistration>();
        foreach (var extension in extensions)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(extension.AdapterId);
            ArgumentException.ThrowIfNullOrWhiteSpace(extension.Command);
            if (canonical.Contains(extension.Command))
                throw new InvalidOperationException($"Debug adapter extension '{extension.Command}' shadows a canonical DAP command.");
            if (extension.MaximumRequestBytes <= 0 || extension.MaximumResponseBytes <= 0)
                throw new InvalidOperationException("Debug adapter extension byte limits must be positive.");
            if (extension.RequestType is { } requestType &&
                (requestType == typeof(object) || requestType == typeof(JsonElement) ||
                 typeof(System.Collections.IDictionary).IsAssignableFrom(requestType)))
                throw new InvalidOperationException("Debug adapter extensions require a concrete typed request contract.");
            if (extension.ResponseType == typeof(object) || extension.ResponseType == typeof(JsonElement))
                throw new InvalidOperationException("Debug adapter extensions require a concrete typed response contract.");
            if (!registrations.TryAdd((extension.AdapterId, extension.Command), extension))
                throw new InvalidOperationException($"Duplicate debug adapter extension '{extension.AdapterId}/{extension.Command}'.");
        }
        _extensions = registrations;
    }

    public ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(
        FunctionExecutionContext context,
        string debugTreeId,
        string? debugSessionId,
        string command,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var runtime = DebugRuntimeBinding.Capture(context, requireProcessExecution: false);
        var owner = new DebugTreeLookupScope(runtime.AgentRuntimeRegistrationId, runtime.SessionId, runtime.ThreadId);
        var manager = runtime.SessionManager as DebugSessionManager
            ?? throw new InvalidOperationException("The runtime debug session manager implementation is unsupported.");
        var tree = manager.ResolveTree(owner, debugTreeId);
        var session = tree.SelectSession(debugSessionId);
        if (_extensions.TryGetValue((session.AdapterPlan.AdapterId, command), out var registration) &&
            registration.RequiresPrivilegedAuthorization)
            throw new DebugSemanticException(DebugSemanticFailureReason.PermissionDenied,
                "A mutating host extension must be invoked by an approved debugger function with a narrow operation proof.");
        return InvokeAsync<TRequest, TResponse>(runtime, owner, debugTreeId, debugSessionId,
            command, request, null, cancellationToken);
    }

    public async ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(
        DebugRuntimeBinding runtime, DebugTreeLookupScope owner, string treeId, string? sessionId,
        string command, TRequest request,
        DebugPrivilegedOperationAuthorization? privilegedAuthorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var manager = runtime.SessionManager as DebugSessionManager
            ?? throw new InvalidOperationException("The runtime debug session manager implementation is unsupported.");
        var tree = manager.ResolveTree(owner, treeId);
        tree.RuntimeBinding.State.ThrowIfUnavailable();
        var session = tree.SelectSession(sessionId);
        var key = (session.AdapterPlan.AdapterId, command);
        if (!_extensions.TryGetValue(key, out var registration) ||
            registration is not DebugAdapterExtension<TRequest, TResponse> extension)
            throw new DebugSemanticException(DebugSemanticFailureReason.HostExtensionUnavailable,
                "The requested typed debugger extension is not registered for this adapter and contract.");
        try { tree.Authorization.Demand(extension.RequiredGrant); }
        catch (UnauthorizedAccessException exception)
        {
            throw new DebugSemanticException(DebugSemanticFailureReason.PermissionDenied,
                "The debug tree does not authorize this host extension.", exception);
        }
        tree.Authorization.ValidateCurrent(tree.RuntimeBinding, session.AdapterPlan);
        if (extension.RequiresPrivilegedAuthorization)
            (privilegedAuthorization ?? throw new DebugSemanticException(DebugSemanticFailureReason.PermissionDenied,
                "The host extension requires privileged authorization."))
                .Validate(tree, session, DebugPrivilegedOperation.HostExtension);
        await extension.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request, extension.RequestTypeInfo);
        if (requestBytes.Length > extension.MaximumRequestBytes)
            throw new DebugSemanticException(DebugSemanticFailureReason.InvalidArguments,
                "The typed host-extension request exceeds its byte limit.");
        var response = await session.Protocol.SendAsync(new DapRequestDescriptor<TRequest, TResponse>(
            command, DapRequestDirection.ClientToAdapter, extension.RequestTypeInfo, extension.ResponseTypeInfo),
            request, cancellationToken).ConfigureAwait(false);
        if (JsonSerializer.SerializeToUtf8Bytes(response, extension.ResponseTypeInfo).Length > extension.MaximumResponseBytes)
            throw new DebugSemanticException(DebugSemanticFailureReason.OutputTooLarge,
                "The typed host-extension response exceeds its byte limit.");
        return response;
    }
}
