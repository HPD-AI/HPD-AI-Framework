using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;

namespace HPD.Agent.FFI;

/// <summary>
/// Wrapper holding V3 Session + Thread pair for FFI thread handles.
/// FFI consumers see a single "thread" handle; internally we maintain the V3 split.
/// Note: FFI has InternalsVisibleTo access and can use internal Session/Thread constructors.
/// This is framework code, not user-facing, so internal API usage is appropriate.
/// </summary>
internal sealed class FFIConversationThread
{
    public Session Session { get; }
    public Thread Thread { get; }

    public FFIConversationThread(string defaultAgentId)
    {
        Session = new Session();
        Thread = Session.CreateThread(defaultAgentId);
    }
}


/// <summary>
/// Delegate for streaming callback from C# to Rust
/// </summary>
/// <param name="context">Context pointer passed back to Rust</param>
/// <param name="eventJsonPtr">Pointer to UTF-8 JSON string of the event, or null to signal end of stream</param>
public delegate void StreamCallback(IntPtr context, IntPtr eventJsonPtr);

/// <summary>Native callback for one thread-routed event delivery.</summary>
/// <param name="json">Callback-scoped UTF-8 JSON bytes.</param>
/// <param name="jsonLength">Number of bytes available at <paramref name="json"/>.</param>
/// <param name="userData">Opaque caller state supplied when the subscription was created.</param>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void EventDeliveryCallback(IntPtr json, nuint jsonLength, IntPtr userData);

/// <summary>Closed result set returned by <c>hpd_agent_subscribe_events</c>.</summary>
public enum HpdSubscribeStatus
{
    /// <summary>The subscription was created.</summary>
    Ok = 0,
    /// <summary>A required pointer or key was missing.</summary>
    InvalidArgument = 1,
    /// <summary>A session or thread key was not valid UTF-8.</summary>
    InvalidUtf8 = 2,
    /// <summary>The hierarchy integer is outside the frozen range.</summary>
    InvalidHierarchy = 3,
    /// <summary>The agent handle is invalid or disposed.</summary>
    DisposedAgent = 4,
    /// <summary>An internal failure prevented subscription creation.</summary>
    InternalError = 5
}

/// <summary>Closed result set returned by <c>hpd_subscription_dispose</c>.</summary>
public enum HpdSubscriptionDisposeStatus
{
    /// <summary>The subscription is quiescent and the caller handle is null.</summary>
    Disposed = 0,
    /// <summary>The pointer to the caller-owned handle was null.</summary>
    InvalidArgument = 1,
    /// <summary>Disposal was attempted from the subscription's callback.</summary>
    FromCallback = 2
}

/// <summary>
/// Represents a native function exported from any C-compatible language (Rust, C++, Zig, Go, Swift, etc.).
/// Language-agnostic structure that describes function metadata for FFI interop.
/// </summary>
public class NativeFunctionInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("wrapperFunctionName")]
    public string WrapperFunctionName { get; set; } = string.Empty;

    [JsonPropertyName("schema")]
    public string Schema { get; set; } = "{}";

    [JsonPropertyName("requiresPermission")]
    public bool RequiresPermission { get; set; }

    [JsonPropertyName("requiredPermissions")]
    public List<string> RequiredPermissions { get; set; } = new();

    [JsonPropertyName("ToolHarness_name")]
    public string ToolHarnessName { get; set; } = string.Empty;
}

/// <summary>Stable language-neutral projection of one unified operation.</summary>
public sealed record FfiAgentOperation
{
    /// <summary>Gets the HPD-authoritative operation identifier.</summary>
    public required string OperationId { get; init; }
    /// <summary>Gets the provider-authoritative identifier when present.</summary>
    public string? ProviderOperationId { get; init; }
    /// <summary>Gets the stable operation name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the lowercase source discriminator.</summary>
    public required string Source { get; init; }
    /// <summary>Gets the lowercase provider-status discriminator.</summary>
    public required string ProviderStatus { get; init; }
    /// <summary>Gets the lowercase observation-status discriminator.</summary>
    public required string ObservationStatus { get; init; }
    /// <summary>Gets the lowercase control-kind discriminator.</summary>
    public required string ControlKind { get; init; }
    /// <summary>Gets the lowercase control capabilities.</summary>
    public required string ControlCapabilities { get; init; }
    /// <summary>Gets the optimistic concurrency version.</summary>
    public required long Version { get; init; }
}

internal sealed class FfiEventSubscription : IDisposable
{
    private static readonly JsonSerializerOptions RouteJson = new(JsonSerializerDefaults.Web);
    private readonly object _gate = new();
    private readonly EventDeliveryCallback _callback;
    private readonly IntPtr _userData;
    private readonly ManualResetEventSlim _quiescent = new(initialState: true);
    private HPD.Events.DeliveryInbox<AgentEventDelivery>? _inbox;
    private Task? _pump;
    private bool _accepting = true;
    private int _callbacks;
    private int _callbackThreadId;

    internal FfiEventSubscription(EventDeliveryCallback callback, IntPtr userData)
    {
        _callback = callback;
        _userData = userData;
    }

    internal void Start(
        HPD.Events.DeliveryInbox<AgentEventDelivery> inbox,
        AgentEventCodec codec)
    {
        _inbox = inbox;
        _pump = Task.Run(async () =>
        {
            await foreach (var delivery in inbox.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                var json = $"{{\"event\":{codec.Serialize(delivery.Event)}," +
                    $"\"route\":{JsonSerializer.Serialize(delivery.Route, RouteJson)}}}";
                Invoke(json);
            }
        });
    }

    internal bool IsCallbackThread =>
        Volatile.Read(ref _callbackThreadId) == System.Environment.CurrentManagedThreadId &&
        Volatile.Read(ref _callbacks) > 0;

    internal unsafe void Invoke(string json)
    {
        lock (_gate)
        {
            if (!_accepting)
                return;
            if (Interlocked.Increment(ref _callbacks) == 1)
                _quiescent.Reset();
        }

        var bytes = Encoding.UTF8.GetBytes(json);
        Volatile.Write(ref _callbackThreadId, System.Environment.CurrentManagedThreadId);
        try
        {
            fixed (byte* pointer = bytes)
                _callback((IntPtr)pointer, (nuint)bytes.Length, _userData);
        }
        finally
        {
            Volatile.Write(ref _callbackThreadId, 0);
            if (Interlocked.Decrement(ref _callbacks) == 0)
                _quiescent.Set();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_accepting)
                return;
            _accepting = false;
        }
        var inbox = Interlocked.Exchange(ref _inbox, null);
        if (inbox is not null)
            inbox.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Interlocked.Exchange(ref _pump, null)?.GetAwaiter().GetResult();
        _quiescent.Wait();
        _quiescent.Dispose();
    }
}

/// <summary>
/// Static class containing all C# functions exported to Rust via FFI.
/// This serves as the main entry point for the Rust wrapper library.
/// </summary>
public static partial class NativeExports
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions DeliveryJson = new(JsonSerializerDefaults.Web);

    internal static IntPtr RegisterManagedAgentForTesting(HPD.Agent.Agent agent) =>
        ObjectManager.Add(agent);

    internal static IntPtr RegisterProviderAccountServiceForTesting(
        ProviderAuthenticationCoordinator coordinator,
        IProviderAuthenticationSelectionAuthorizer authorizer)
    {
        var handle = ObjectManager.Add(new object());
        ObjectManager.Attach(handle, new ProviderAccountFfiService(coordinator, authorizer));
        return handle;
    }

    internal static void DestroyHandleForTesting(IntPtr handle) =>
        ObjectManager.Remove(handle);

    internal static IntPtr CreateConversationThreadForTesting(IntPtr agentHandle) =>
        CreateConversationThreadCore(agentHandle);

    internal static int GetMessageCountForTesting(IntPtr threadHandle) =>
        GetMessageCountCore(threadHandle);

    internal static int RunAgentStreamingForTesting(
        IntPtr agentHandle,
        string input,
        IntPtr threadHandle,
        StreamCallback callback,
        IntPtr context) =>
        RunAgentStreamingCore(agentHandle, input, threadHandle, callback, context);

    internal static unsafe HpdSubscribeStatus SubscribeEventsForTesting(
        IntPtr agentHandle,
        ReadOnlySpan<byte> sessionId,
        ReadOnlySpan<byte> threadId,
        int hierarchy,
        EventDeliveryCallback callback,
        IntPtr userData,
        out IntPtr subscription)
    {
        fixed (byte* session = sessionId)
        fixed (byte* thread = threadId)
        {
            var result = SubscribeEventsCore(
                agentHandle,
                (IntPtr)session,
                (nuint)sessionId.Length,
                (IntPtr)thread,
                (nuint)threadId.Length,
                hierarchy,
                callback,
                userData,
                out subscription);
            return result;
        }
    }

    internal static HpdSubscriptionDisposeStatus DisposeSubscriptionForTesting(ref IntPtr subscription) =>
        DisposeSubscriptionCore(ref subscription);

    internal static int RespondToPermissionForTesting(
        IntPtr agentHandle,
        string permissionId,
        int approved,
        int permissionChoice) =>
        RespondToPermissionCore(agentHandle, permissionId, approved, permissionChoice);

    /// <summary>
    /// Test function to verify FFI communication between C# and Rust.
    /// Accepts a UTF-8 string from Rust and returns a response.
    /// </summary>
    /// <param name="messagePtr">Pointer to a UTF-8 encoded string from Rust</param>
    /// <returns>Pointer to a UTF-8 encoded response string allocated by C#</returns>
    [UnmanagedCallersOnly(EntryPoint = "ping")]
    public static IntPtr Ping(IntPtr messagePtr)
    {
        try
        {
            // Marshal the string from Rust
            string? message = Marshal.PtrToStringUTF8(messagePtr);
            string response = $"Pong: You sent '{message}'";

            // Convert to UTF-8 bytes and allocate unmanaged memory
            byte[] responseBytes = Encoding.UTF8.GetBytes(response + '\0'); // null-terminated
            IntPtr responsePtr = Marshal.AllocHGlobal(responseBytes.Length);
            Marshal.Copy(responseBytes, 0, responsePtr, responseBytes.Length);

            return responsePtr;
        }
        catch (Exception ex)
        {
            // In case of error, return a pointer to an error message
            string errorResponse = $"Error in Ping: {ex.Message}";
            byte[] errorBytes = Encoding.UTF8.GetBytes(errorResponse + '\0'); // null-terminated
            IntPtr errorPtr = Marshal.AllocHGlobal(errorBytes.Length);
            Marshal.Copy(errorBytes, 0, errorPtr, errorBytes.Length);
            return errorPtr;
        }
    }

    /// <summary>
    /// Frees memory allocated by C# for strings returned to Rust.
    /// This must be called by Rust for every string pointer received from C#.
    /// </summary>
    /// <param name="stringPtr">Pointer to the string memory to free</param>
    [UnmanagedCallersOnly(EntryPoint = "free_string")]
    public static void FreeString(IntPtr stringPtr)
    {
        if (stringPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(stringPtr);
        }
    }

    /// <summary>
    /// Creates an agent with the given configuration and ToolHarnesses.
    /// </summary>
    /// <param name="configJsonPtr">Pointer to JSON string containing AgentConfig</param>
    /// <param name="ToolHarnessesJsonPtr">Pointer to JSON string containing ToolHarness definitions</param>
    /// <returns>Handle to the created Agent, or IntPtr.Zero on failure</returns>
    [UnmanagedCallersOnly(EntryPoint = "create_agent_with_ToolHarnesses")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "FFI boundary - AgentBuilder uses reflection for C# ToolHarness discovery, but FFI only adds native functions manually")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "FFI boundary - AgentBuilder uses reflection for C# ToolHarness discovery, but FFI only adds native functions manually")]
    public static IntPtr CreateAgentWithToolHarnesses(IntPtr configJsonPtr, IntPtr ToolHarnessesJsonPtr)
    {
        try
        {
            string? configJson = Marshal.PtrToStringUTF8(configJsonPtr);
            if (string.IsNullOrEmpty(configJson)) return IntPtr.Zero;

            var providerComposition = HPD.Agent.Providers.Generated.GeneratedProviderComposition.Composition;
            var agentConfig = DeserializeAgentConfig(configJson);

            var builder = new AgentBuilder(agentConfig, providerComposition)
                .WithEventComposition(GeneratedAgentEventComposition.Composition);

            // Parse and add native ToolHarnesses (Rust, C++, Zig, Go, etc.)
            string? ToolHarnessesJson = Marshal.PtrToStringUTF8(ToolHarnessesJsonPtr);
            Console.WriteLine($"[FFI] Received ToolHarnesses JSON: {ToolHarnessesJson}");

            if (!string.IsNullOrEmpty(ToolHarnessesJson))
            {
                try
                {
                    var nativeFunctions = JsonSerializer.Deserialize(ToolHarnessesJson, HPDFFIJsonContext.Default.ListNativeFunctionInfo);
                    Console.WriteLine($"[FFI] Deserialized {nativeFunctions?.Count ?? 0} native functions");

                    if (nativeFunctions != null && nativeFunctions.Count > 0)
                    {
                        // Track unique ToolHarness names
                        var ToolHarnessNames = new HashSet<string>();

                        foreach (var nativeFunc in nativeFunctions)
                        {
                            Console.WriteLine($"[FFI] Adding native function: {nativeFunc.Name} - {nativeFunc.Description}");
                            var aiFunction = CreateNativeFunctionWrapper(nativeFunc);
                            builder.WithNativeFunction(aiFunction);

                            // Track ToolHarness name for registration
                            if (!string.IsNullOrEmpty(nativeFunc.ToolHarnessName))
                            {
                                ToolHarnessNames.Add(nativeFunc.ToolHarnessName);
                            }
                        }

                        // Register ToolHarness executors in native runtime
                        foreach (var ToolHarnessName in ToolHarnessNames)
                        {
                            Console.WriteLine($"[FFI] Registering executors for ToolHarness: {ToolHarnessName}");
                            bool success = NativeToolHarnessFFI.RegisterToolHarnessExecutors(ToolHarnessName);
                            Console.WriteLine($"[FFI] Registration result for {ToolHarnessName}: {success}");
                        }

                        Console.WriteLine($"[FFI] Successfully added {nativeFunctions.Count} native functions to agent");
                    }
                }
                catch (Exception ex)
                {
                    // Log but don't fail - agent can still work without native ToolHarnesses
                    Console.WriteLine($"Failed to parse native ToolHarnesses: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }

            var agent = builder.BuildAsync().GetAwaiter().GetResult();
            var handle = ObjectManager.Add(agent);
            var coordinator = builder.ServiceProvider?.GetService<ProviderAuthenticationCoordinator>();
            var authorizer = builder.ServiceProvider?.GetService<IProviderAuthenticationSelectionAuthorizer>();
            if (coordinator is not null && authorizer is not null)
                ObjectManager.Attach(handle, new ProviderAccountFfiService(coordinator, authorizer));
            return handle;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create agent: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    internal static AgentConfig DeserializeAgentConfig(string configJson)
        => HPD.Agent.Serialization.HpdAgentConfigSerializer.Deserialize(
            configJson,
            HPD.Agent.Providers.Generated.GeneratedProviderComposition.Composition)
            ?? throw new JsonException("Agent configuration was null.");


    /// <summary>
    /// Creates an AIFunction wrapper that calls back to native code via FFI.
    /// Supports ToolHarnesses written in Rust, C++, Zig, Go, Swift, or any C-compatible language.
    /// </summary>
    private static AIFunction CreateNativeFunctionWrapper(NativeFunctionInfo nativeFunc)
    {
        return HPDAIFunctionFactory.Create(
            (arguments, _, cancellationToken) =>
            {
                // Convert AIFunctionArguments to a simple dictionary
                var argsDict = new Dictionary<string, object>();
                foreach (var kvp in arguments)
                {
                    if (kvp.Key != "__raw_json__" && kvp.Value != null) // Skip internal keys and null values
                    {
                        argsDict[kvp.Key] = kvp.Value;
                    }
                }

                // Execute the native function via FFI
                var result = NativeToolHarnessFFI.ExecuteFunction(nativeFunc.Name, argsDict);

                if (!result.Success)
                {
                    // Return error as structured response for better AI understanding
                    return Task.FromResult<object?>(new { error = result.Error ?? "Unknown error", success = false });
                }

                // Parse the result
                if (result.Result != null)
                {
                    try
                    {
                        using (result.Result)
                        {
                            var root = result.Result.RootElement;

                            // Check if it's a success/result envelope
                            if (root.TryGetProperty("success", out var successProp) &&
                                root.TryGetProperty("result", out var resultProp))
                            {
                                if (successProp.GetBoolean())
                                {
                                    // Return just the result value
                                    return Task.FromResult<object?>(resultProp.ValueKind == JsonValueKind.String
                                        ? resultProp.GetString()
                                        : resultProp.GetRawText());
                                }
                                else if (root.TryGetProperty("error", out var errorProp))
                                {
                                    return Task.FromResult<object?>(new { error = errorProp.GetString(), success = false });
                                }
                            }

                            // Return raw response if not in envelope format
                            return Task.FromResult<object?>(root.GetRawText());
                        }
                    }
                    catch (Exception ex)
                    {
                        return Task.FromResult<object?>(new { error = $"Failed to parse result: {ex.Message}", success = false });
                    }
                }

                return Task.FromResult<object?>(null);
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = nativeFunc.Name,
                Description = nativeFunc.Description,
                FunctionPermission = nativeFunc.RequiresPermission
                    ? new AIFunctionPermissionDeclaration
                    {
                        RequiresPermission = true,
                        Authority = $"function/{Uri.EscapeDataString(nativeFunc.Name)}",
                        Source = PermissionDeclarationSource.FrameworkDefault
                    }
                    : null,
                SchemaProvider = () =>
                {
                    try
                    {
                        // Parse the schema JSON from native code
                        var schemaDoc = JsonDocument.Parse(nativeFunc.Schema);
                        var rootSchema = schemaDoc.RootElement;

                        // Check if this is an OpenAPI function calling format
                        if (rootSchema.TryGetProperty("function", out var functionElement) &&
                            functionElement.TryGetProperty("parameters", out var parametersElement))
                        {
                            // Extract just the parameters schema for Microsoft.Extensions.AI
                            return parametersElement.Clone();
                        }
                        else
                        {
                            // Use the schema as-is if it's already in the right format
                            return rootSchema.Clone();
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log error and fallback to empty object schema
                        Console.WriteLine($"Warning: Failed to parse schema for {nativeFunc.Name}: {ex.Message}");
                        return JsonDocument.Parse("{}").RootElement;
                    }
                }
            }
        );
    }

    /// <summary>
    /// Destroys an agent and releases its resources.
    /// </summary>
    /// <param name="agentHandle">Handle to the agent to destroy</param>
    [UnmanagedCallersOnly(EntryPoint = "destroy_agent")]
    public static void DestroyAgent(IntPtr agentHandle)
    {
        try
        {
            if (ObjectManager.Get<HPD.Agent.Agent>(agentHandle) is { } agent)
                agent.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Agent shutdown failed: {exception.Message}");
        }
        finally { ObjectManager.Remove(agentHandle); }
    }

    /// <summary>Returns the agent's single unified operation collection as UTF-8 JSON.</summary>
    /// <param name="agentHandle">Handle of the owning agent.</param>
    /// <returns>A caller-owned JSON string pointer, or zero when the handle is invalid.</returns>
    [UnmanagedCallersOnly(EntryPoint = "list_agent_operations")]
    public static IntPtr ListAgentOperations(IntPtr agentHandle)
    {
        var agent = ObjectManager.Get<HPD.Agent.Agent>(agentHandle);
        if (agent is null) return IntPtr.Zero;
        var operations = agent.ListOperations().Select(static operation => new FfiAgentOperation
        {
            OperationId = operation.OperationId,
            ProviderOperationId = operation.ProviderOperationId,
            Name = operation.Name,
            Source = operation.SourceKind.ToString().ToLowerInvariant(),
            ProviderStatus = operation.ProviderStatus.ToString().ToLowerInvariant(),
            ObservationStatus = operation.ObservationStatus.ToString().ToLowerInvariant(),
            ControlKind = operation.Control.Kind.ToString().ToLowerInvariant(),
            ControlCapabilities = operation.Control.Capabilities.ToString().Replace(" ", string.Empty).ToLowerInvariant(),
            Version = operation.Version
        }).ToList();
        return MarshalString(JsonSerializer.Serialize(operations, HPDFFIJsonContext.Default.ListFfiAgentOperation));
    }

    /// <summary>Requests cancellation of one unified operation.</summary>
    /// <param name="agentHandle">Handle of the owning agent.</param>
    /// <param name="operationIdPtr">Pointer to the UTF-8 operation identifier.</param>
    /// <returns>Zero on success and minus one on invalid input or failure.</returns>
    [UnmanagedCallersOnly(EntryPoint = "cancel_agent_operation")]
    public static int CancelAgentOperation(IntPtr agentHandle, IntPtr operationIdPtr)
    {
        try
        {
            var agent = ObjectManager.Get<HPD.Agent.Agent>(agentHandle);
            var operationId = Marshal.PtrToStringUTF8(operationIdPtr);
            if (agent is null || string.IsNullOrWhiteSpace(operationId)) return -1;
            agent.CancelOperationAsync(operationId).AsTask().GetAwaiter().GetResult();
            return 0;
        }
        catch { return -1; }
    }

    /// <summary>Begins an authorization transaction for a portable provider-account selection.</summary>
    /// <param name="agentHandle">Handle of the agent whose authentication runtime is used.</param>
    /// <param name="requestJsonPtr">Pointer to a UTF-8 <see cref="BeginProviderAuthorizationFfiRequest"/> document.</param>
    /// <returns>A caller-owned redacted challenge or error JSON string.</returns>
    [UnmanagedCallersOnly(EntryPoint = "begin_provider_authorization")]
    public static IntPtr BeginProviderAuthorization(IntPtr agentHandle, IntPtr requestJsonPtr) =>
        BeginProviderAuthorizationCore(agentHandle, Marshal.PtrToStringUTF8(requestJsonPtr));

    /// <summary>Completes a correlated provider authorization transaction.</summary>
    /// <param name="agentHandle">Handle of the owning agent.</param>
    /// <param name="requestJsonPtr">Pointer to a UTF-8 completion request.</param>
    /// <returns>A caller-owned JSON string containing <see langword="true"/> or a redacted error.</returns>
    [UnmanagedCallersOnly(EntryPoint = "complete_provider_authorization")]
    public static IntPtr CompleteProviderAuthorization(IntPtr agentHandle, IntPtr requestJsonPtr) =>
        CompleteProviderAuthorizationCore(agentHandle, Marshal.PtrToStringUTF8(requestJsonPtr));

    /// <summary>Advances one device authorization transaction by at most one provider step.</summary>
    [UnmanagedCallersOnly(EntryPoint = "advance_provider_device_authorization")]
    public static IntPtr AdvanceProviderDeviceAuthorization(IntPtr agentHandle, IntPtr requestJsonPtr) =>
        AdvanceProviderDeviceAuthorizationCore(agentHandle, Marshal.PtrToStringUTF8(requestJsonPtr));

    /// <summary>Reads one device authorization transaction without contacting the provider.</summary>
    [UnmanagedCallersOnly(EntryPoint = "get_provider_device_authorization_status")]
    public static IntPtr GetProviderDeviceAuthorizationStatus(IntPtr agentHandle, IntPtr requestJsonPtr) =>
        GetProviderDeviceAuthorizationStatusCore(agentHandle, Marshal.PtrToStringUTF8(requestJsonPtr));

    /// <summary>Cancels one device authorization transaction.</summary>
    [UnmanagedCallersOnly(EntryPoint = "cancel_provider_device_authorization")]
    public static IntPtr CancelProviderDeviceAuthorization(IntPtr agentHandle, IntPtr requestJsonPtr) =>
        CancelProviderDeviceAuthorizationCore(agentHandle, Marshal.PtrToStringUTF8(requestJsonPtr));

    /// <summary>Reads redacted authorization status for a provider account.</summary>
    [UnmanagedCallersOnly(EntryPoint = "get_provider_authorization_status")]
    public static IntPtr GetProviderAuthorizationStatus(IntPtr agentHandle, IntPtr requestJsonPtr) =>
        ProviderAccountOperationCore(agentHandle, Marshal.PtrToStringUTF8(requestJsonPtr),
            static (service, request) => service.StatusAsync(request),
            HPDFFIJsonContext.Default.ProviderAuthorizationStatus);

    /// <summary>Conditionally removes local authorization state.</summary>
    [UnmanagedCallersOnly(EntryPoint = "disconnect_provider_account")]
    public static IntPtr DisconnectProviderAccount(IntPtr agentHandle, IntPtr requestJsonPtr) =>
        ProviderAccountOperationCore(agentHandle, Marshal.PtrToStringUTF8(requestJsonPtr),
            static (service, request) => service.DisconnectAsync(request),
            HPDFFIJsonContext.Default.ProviderDisconnectResult);

    /// <summary>Attempts provider-side credential revocation without deleting local state.</summary>
    [UnmanagedCallersOnly(EntryPoint = "revoke_provider_account")]
    public static IntPtr RevokeProviderAccount(IntPtr agentHandle, IntPtr requestJsonPtr) =>
        ProviderAccountOperationCore(agentHandle, Marshal.PtrToStringUTF8(requestJsonPtr),
            static (service, request) => service.RevokeAsync(request),
            HPDFFIJsonContext.Default.ProviderRevocationResult);

    /// <summary>Attempts remote revocation and conditionally removes local state.</summary>
    [UnmanagedCallersOnly(EntryPoint = "revoke_and_disconnect_provider_account")]
    public static IntPtr RevokeAndDisconnectProviderAccount(IntPtr agentHandle, IntPtr requestJsonPtr) =>
        ProviderAccountOperationCore(agentHandle, Marshal.PtrToStringUTF8(requestJsonPtr),
            static (service, request) => service.RevokeAndDisconnectAsync(request),
            HPDFFIJsonContext.Default.ProviderDisconnectResult);

    internal static IntPtr BeginProviderAuthorizationCore(IntPtr agentHandle, string? requestJson)
    {
        try
        {
            var service = RequireProviderAccountService(agentHandle);
            var request = JsonSerializer.Deserialize(requestJson ?? string.Empty,
                HPDFFIJsonContext.Default.BeginProviderAuthorizationFfiRequest)
                ?? throw new JsonException("The provider authorization begin request was null.");
            var result = service.BeginAsync(request).AsTask().GetAwaiter().GetResult();
            return MarshalString(JsonSerializer.Serialize(
                result, HPDFFIJsonContext.Default.ProviderAuthorizationChallenge));
        }
        catch (Exception exception) { return ProviderAccountError(exception); }
    }

    internal static IntPtr CompleteProviderAuthorizationCore(IntPtr agentHandle, string? requestJson)
    {
        try
        {
            var service = RequireProviderAccountService(agentHandle);
            var request = JsonSerializer.Deserialize(requestJson ?? string.Empty,
                HPDFFIJsonContext.Default.CompleteProviderAuthorizationFfiRequest)
                ?? throw new JsonException("The provider authorization completion request was null.");
            service.CompleteAsync(request).AsTask().GetAwaiter().GetResult();
            return MarshalString("true");
        }
        catch (Exception exception) { return ProviderAccountError(exception); }
    }

    internal static IntPtr AdvanceProviderDeviceAuthorizationCore(IntPtr agentHandle, string? requestJson)
    {
        try
        {
            var service = RequireProviderAccountService(agentHandle);
            var request = JsonSerializer.Deserialize(requestJson ?? string.Empty,
                HPDFFIJsonContext.Default.ProviderDeviceAuthorizationFfiRequest)
                ?? throw new JsonException("The provider device authorization advance request was null.");
            var result = service.AdvanceDeviceAsync(request).AsTask().GetAwaiter().GetResult();
            return MarshalString(JsonSerializer.Serialize(
                result, HPDFFIJsonContext.Default.ProviderDeviceAuthorizationStatus));
        }
        catch (Exception exception) { return ProviderAccountError(exception); }
    }

    internal static IntPtr GetProviderDeviceAuthorizationStatusCore(IntPtr agentHandle, string? requestJson)
    {
        try
        {
            var service = RequireProviderAccountService(agentHandle);
            var request = JsonSerializer.Deserialize(requestJson ?? string.Empty,
                HPDFFIJsonContext.Default.ProviderDeviceAuthorizationFfiRequest)
                ?? throw new JsonException("The provider device authorization status request was null.");
            var result = service.GetDeviceStatusAsync(request).AsTask().GetAwaiter().GetResult();
            return MarshalString(JsonSerializer.Serialize(
                result, HPDFFIJsonContext.Default.ProviderDeviceAuthorizationStatus));
        }
        catch (Exception exception) { return ProviderAccountError(exception); }
    }

    internal static IntPtr CancelProviderDeviceAuthorizationCore(IntPtr agentHandle, string? requestJson)
    {
        try
        {
            var service = RequireProviderAccountService(agentHandle);
            var request = JsonSerializer.Deserialize(requestJson ?? string.Empty,
                HPDFFIJsonContext.Default.ProviderDeviceAuthorizationFfiRequest)
                ?? throw new JsonException("The provider device authorization cancellation request was null.");
            service.CancelDeviceAsync(request).AsTask().GetAwaiter().GetResult();
            return MarshalString("true");
        }
        catch (Exception exception) { return ProviderAccountError(exception); }
    }

    private static IntPtr ProviderAccountOperationCore<TResult>(
        IntPtr agentHandle,
        string? requestJson,
        Func<ProviderAccountFfiService, ProviderAccountFfiRequest, ValueTask<TResult>> operation,
        JsonTypeInfo<TResult> resultType)
    {
        try
        {
            var service = RequireProviderAccountService(agentHandle);
            var request = JsonSerializer.Deserialize(requestJson ?? string.Empty,
                HPDFFIJsonContext.Default.ProviderAccountFfiRequest)
                ?? throw new JsonException("The provider account request was null.");
            var result = operation(service, request).AsTask().GetAwaiter().GetResult();
            return MarshalString(JsonSerializer.Serialize(result, resultType));
        }
        catch (Exception exception) { return ProviderAccountError(exception); }
    }

    private static ProviderAccountFfiService RequireProviderAccountService(IntPtr agentHandle) =>
        ObjectManager.GetAttachment<ProviderAccountFfiService>(agentHandle)
        ?? throw new InvalidOperationException("The FFI host did not install provider account authorization services.");

    private static IntPtr ProviderAccountError(Exception exception)
    {
        var code = exception switch
        {
            ProviderAuthenticationException authentication => authentication.DiagnosticCode,
            AgentRunConfigurationException configuration => configuration.Code,
            JsonException => "InvalidProviderAccountRequest",
            _ => "ProviderAccountServicesUnavailable"
        };
        return MarshalString(JsonSerializer.Serialize(
            new ProviderAccountFfiError { DiagnosticCode = code },
            HPDFFIJsonContext.Default.ProviderAccountFfiError));
    }

    //
    // CONVERSATION THREAD MANAGEMENT
    //

    /// <summary>
    /// Creates a new conversation thread for managing conversation state.
    /// </summary>
    /// <param name="agentHandle">Handle of the agent that owns the thread.</param>
    /// <returns>Handle to the created conversation thread, or IntPtr.Zero on failure</returns>
    [UnmanagedCallersOnly(EntryPoint = "create_conversation_thread")]
    public static IntPtr CreateAgentSession(IntPtr agentHandle)
    {
        return CreateConversationThreadCore(agentHandle);
    }

    private static IntPtr CreateConversationThreadCore(IntPtr agentHandle)
    {
        try
        {
            var agent = ObjectManager.Get<HPD.Agent.Agent>(agentHandle)
                ?? throw new InvalidOperationException("Agent handle is invalid.");
            var thread = new FFIConversationThread(agent.AgentId);
            return ObjectManager.Add(thread);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create conversation session: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Destroys a conversation thread and releases its resources.
    /// </summary>
    /// <param name="threadHandle">Handle to the thread to destroy</param>
    [UnmanagedCallersOnly(EntryPoint = "destroy_conversation_thread")]
    public static void DestroyAgentSession(IntPtr threadHandle)
    {
        ObjectManager.Remove(threadHandle);
    }

    /// <summary>
    /// Gets the conversation thread ID.
    /// </summary>
    /// <param name="threadHandle">Handle to the conversation thread</param>
    /// <returns>Pointer to UTF-8 encoded thread ID string, or IntPtr.Zero on failure</returns>
    [UnmanagedCallersOnly(EntryPoint = "get_thread_id")]
    public static IntPtr GetThreadId(IntPtr threadHandle)
    {
        try
        {
            var thread = ObjectManager.Get<FFIConversationThread>(threadHandle);
            if (thread == null) return IntPtr.Zero;

            return MarshalString(thread.Session.Id);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to get thread ID: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Gets the number of messages in the conversation thread.
    /// </summary>
    /// <param name="threadHandle">Handle to the conversation thread</param>
    /// <returns>Number of messages, or -1 on failure</returns>
    [UnmanagedCallersOnly(EntryPoint = "get_message_count")]
    public static int GetMessageCount(IntPtr threadHandle)
    {
        return GetMessageCountCore(threadHandle);
    }

    private static int GetMessageCountCore(IntPtr threadHandle)
    {
        try
        {
            var thread = ObjectManager.Get<FFIConversationThread>(threadHandle);
            if (thread == null) return -1;

            return thread.Thread.MessageCount;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to get message count: {ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// Gets all messages from the conversation thread as JSON.
    /// </summary>
    /// <param name="threadHandle">Handle to the conversation thread</param>
    /// <returns>Pointer to UTF-8 encoded JSON array of messages, or IntPtr.Zero on failure</returns>
    [UnmanagedCallersOnly(EntryPoint = "get_thread_messages")]
    public static IntPtr GetThreadMessages(IntPtr threadHandle)
    {
        try
        {
            var thread = ObjectManager.Get<FFIConversationThread>(threadHandle);
            if (thread == null) return IntPtr.Zero;

            var json = JsonSerializer.Serialize(thread.Thread.Messages, HPDFFIJsonContext.Default.IEnumerableChatMessage);
            return MarshalString(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to get thread messages: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Adds a message to the conversation thread.
    /// </summary>
    /// <param name="threadHandle">Handle to the conversation thread</param>
    /// <param name="messageJsonPtr">Pointer to UTF-8 encoded JSON of the ChatMessage</param>
    /// <returns>1 on success, 0 on failure</returns>
    [UnmanagedCallersOnly(EntryPoint = "add_thread_message")]
    public static int AddThreadMessage(IntPtr threadHandle, IntPtr messageJsonPtr)
    {
        try
        {
            var thread = ObjectManager.Get<FFIConversationThread>(threadHandle);
            if (thread == null) return 0;

            string? messageJson = Marshal.PtrToStringUTF8(messageJsonPtr);
            if (string.IsNullOrEmpty(messageJson)) return 0;

            var message = JsonSerializer.Deserialize(messageJson, HPDFFIJsonContext.Default.ChatMessage);
            if (message == null) return 0;

            thread.Thread.AddMessage(message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to add message to session: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Clears all messages from the conversation thread.
    /// </summary>
    /// <param name="threadHandle">Handle to the conversation thread</param>
    /// <returns>1 on success, 0 on failure</returns>
    [UnmanagedCallersOnly(EntryPoint = "clear_thread")]
    public static int ClearThread(IntPtr threadHandle)
    {
        try
        {
            var thread = ObjectManager.Get<FFIConversationThread>(threadHandle);
            if (thread == null) return 0;

            thread.Thread.Clear();
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to clear session: {ex.Message}");
            return 0;
        }
    }

    //
    // AGENT EXECUTION APIs
    //

    /// <summary>
    /// Runs the agent synchronously with the given input and returns the final response.
    /// This is a simple, blocking API for non-streaming use cases.
    /// </summary>
    /// <param name="agentHandle">Handle to the agent</param>
    /// <param name="inputPtr">Pointer to UTF-8 encoded user input string</param>
    /// <param name="threadHandle">Handle to the conversation thread (optional, can be IntPtr.Zero for stateless)</param>
    /// <returns>Pointer to UTF-8 encoded response string, or IntPtr.Zero on failure</returns>
    [UnmanagedCallersOnly(EntryPoint = "run_agent")]
    public static IntPtr RunAgent(IntPtr agentHandle, IntPtr inputPtr, IntPtr threadHandle)
    {
        string? input = Marshal.PtrToStringUTF8(inputPtr);
        if (string.IsNullOrEmpty(input)) return IntPtr.Zero;
        return RunAgentCore(agentHandle, input, threadHandle);
    }

    private static IntPtr RunAgentCore(IntPtr agentHandle, string input, IntPtr threadHandle)
    {
        try
        {
            var agent = ObjectManager.Get<HPD.Agent.Agent>(agentHandle);
            if (agent == null) return IntPtr.Zero;

            // Create user message
            var userMessage = new ChatMessage(ChatRole.User, input);
            var messages = new[] { userMessage };

            // Get thread if provided
            FFIConversationThread? thread = null;
            if (threadHandle != IntPtr.Zero)
            {
                thread = ObjectManager.Get<FFIConversationThread>(threadHandle);
            }

            if (thread is null)
                return 0;

            // Run agent and collect all events
            var responseText = new StringBuilder();

            // Block and collect response
            var task = Task.Run(async () =>
            {
                using var subscription = agent.Subscribe<TextDeltaEvent>(evt =>
                {
                    responseText.Append(evt.Text);
                    return ValueTask.CompletedTask;
                });

                await agent.RunAsync(new UserMessagesInputEvent { Messages = messages,
                    Session = thread?.Session,
                    Thread = thread?.Thread
                });
            });

            task.Wait();

            return MarshalString(responseText.ToString());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to run agent: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return MarshalString($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs the agent with streaming callbacks.
    /// Calls the provided callback function for each event emitted by the agent.
    /// </summary>
    /// <param name="agentHandle">Handle to the agent</param>
    /// <param name="inputPtr">Pointer to UTF-8 encoded user input string</param>
    /// <param name="threadHandle">Handle to the conversation thread (optional, can be IntPtr.Zero for stateless)</param>
    /// <param name="callback">Callback function to invoke for each event</param>
    /// <param name="context">User context pointer passed back to callback</param>
    /// <returns>1 on success, 0 on failure</returns>
    [UnmanagedCallersOnly(EntryPoint = "run_agent_streaming")]
    public static int RunAgentStreaming(IntPtr agentHandle, IntPtr inputPtr, IntPtr threadHandle,
        IntPtr callbackPtr, IntPtr context)
    {
        string? input = Marshal.PtrToStringUTF8(inputPtr);
        if (string.IsNullOrEmpty(input)) return 0;

        if (callbackPtr == IntPtr.Zero) return 0;

        var callback = Marshal.GetDelegateForFunctionPointer<StreamCallback>(callbackPtr);
        return RunAgentStreamingCore(agentHandle, input, threadHandle, callback, context);
    }

    /// <summary>Creates a persistent, thread-routed native event subscription.</summary>
    [UnmanagedCallersOnly(EntryPoint = "hpd_agent_subscribe_events")]
    public static unsafe int SubscribeEvents(
        IntPtr agentHandle,
        IntPtr sessionId,
        nuint sessionIdLength,
        IntPtr threadId,
        nuint threadIdLength,
        int hierarchy,
        IntPtr callback,
        IntPtr userData,
        IntPtr subscriptionAddress)
    {
        if (subscriptionAddress == IntPtr.Zero)
            return (int)HpdSubscribeStatus.InvalidArgument;

        *(IntPtr*)subscriptionAddress = IntPtr.Zero;
        if (callback == IntPtr.Zero)
            return (int)HpdSubscribeStatus.InvalidArgument;

        try
        {
            var managedCallback = Marshal.GetDelegateForFunctionPointer<EventDeliveryCallback>(callback);
            var status = SubscribeEventsCore(
                agentHandle,
                sessionId,
                sessionIdLength,
                threadId,
                threadIdLength,
                hierarchy,
                managedCallback,
                userData,
                out var subscription);
            if (status == HpdSubscribeStatus.Ok)
                *(IntPtr*)subscriptionAddress = subscription;
            return (int)status;
        }
        catch
        {
            return (int)HpdSubscribeStatus.InternalError;
        }
    }

    /// <summary>Disposes a native event subscription and waits for admitted callbacks to finish.</summary>
    [UnmanagedCallersOnly(EntryPoint = "hpd_subscription_dispose")]
    public static unsafe int DisposeSubscription(IntPtr subscriptionAddress)
    {
        if (subscriptionAddress == IntPtr.Zero)
            return (int)HpdSubscriptionDisposeStatus.InvalidArgument;

        try
        {
            ref var subscription = ref *(IntPtr*)subscriptionAddress;
            return (int)DisposeSubscriptionCore(ref subscription);
        }
        catch
        {
            return (int)HpdSubscriptionDisposeStatus.InvalidArgument;
        }
    }

    private static HpdSubscribeStatus SubscribeEventsCore(
        IntPtr agentHandle,
        IntPtr sessionId,
        nuint sessionIdLength,
        IntPtr threadId,
        nuint threadIdLength,
        int hierarchyValue,
        EventDeliveryCallback callback,
        IntPtr userData,
        out IntPtr subscription)
    {
        subscription = IntPtr.Zero;
        if (sessionId == IntPtr.Zero || sessionIdLength == 0 || threadId == IntPtr.Zero || threadIdLength == 0)
            return HpdSubscribeStatus.InvalidArgument;
        if (sessionIdLength > int.MaxValue || threadIdLength > int.MaxValue)
            return HpdSubscribeStatus.InvalidArgument;
        if (!Enum.IsDefined(typeof(AgentEventHierarchy), hierarchyValue))
            return HpdSubscribeStatus.InvalidHierarchy;
        if (ObjectManager.Get<HPD.Agent.Agent>(agentHandle) is not { } agent)
            return HpdSubscribeStatus.DisposedAgent;

        string session;
        string thread;
        try
        {
            unsafe
            {
                session = StrictUtf8.GetString(new ReadOnlySpan<byte>((void*)sessionId, (int)sessionIdLength));
                thread = StrictUtf8.GetString(new ReadOnlySpan<byte>((void*)threadId, (int)threadIdLength));
            }
        }
        catch (DecoderFallbackException)
        {
            return HpdSubscribeStatus.InvalidUtf8;
        }
        if (string.IsNullOrWhiteSpace(session) || string.IsNullOrWhiteSpace(thread))
            return HpdSubscribeStatus.InvalidArgument;

        try
        {
            var nativeSubscription = new FfiEventSubscription(callback, userData);
            var inbox = agent.CreateEventDeliveryInbox(
                new ThreadKey(session, thread),
                (AgentEventHierarchy)hierarchyValue,
                HPD.Events.EventInboxOptions.Deterministic());
            nativeSubscription.Start(inbox, agent.Config.EventComposition!.Codec);
            subscription = ObjectManager.Add(nativeSubscription);
            return HpdSubscribeStatus.Ok;
        }
        catch
        {
            return HpdSubscribeStatus.InternalError;
        }
    }

    private static HpdSubscriptionDisposeStatus DisposeSubscriptionCore(ref IntPtr subscription)
    {
        var handle = subscription;
        if (handle == IntPtr.Zero)
            return HpdSubscriptionDisposeStatus.Disposed;
        var managed = ObjectManager.Get<FfiEventSubscription>(handle);
        if (managed is null)
        {
            subscription = IntPtr.Zero;
            return HpdSubscriptionDisposeStatus.Disposed;
        }
        if (managed.IsCallbackThread)
            return HpdSubscriptionDisposeStatus.FromCallback;

        subscription = IntPtr.Zero;
        ObjectManager.Remove(handle);
        managed.Dispose();
        return HpdSubscriptionDisposeStatus.Disposed;
    }

    private static int RunAgentStreamingCore(
        IntPtr agentHandle,
        string input,
        IntPtr threadHandle,
        StreamCallback callback,
        IntPtr context)
    {
        try
        {
            var agent = ObjectManager.Get<HPD.Agent.Agent>(agentHandle);
            if (agent == null) return 0;

            if (string.IsNullOrEmpty(input)) return 0;

            // Create user message
            var userMessage = new ChatMessage(ChatRole.User, input);
            var messages = new[] { userMessage };

            // Get thread if provided
            var thread = ObjectManager.Get<FFIConversationThread>(threadHandle);
            if (thread is null) return 0;

            // Stream events to callback
            var task = Task.Run(async () =>
            {
                var threadKey = new ThreadKey(thread.Session.Id, thread.Thread.Id);
                await using var inbox = agent.CreateEventDeliveryInbox(
                    threadKey,
                    AgentEventHierarchy.ExactThread,
                    HPD.Events.EventInboxOptions.Deterministic());
                var consume = Task.Run(async () =>
                {
                    await foreach (var delivery in inbox.Reader.ReadAllAsync().ConfigureAwait(false))
                    {
                        var eventJson = $"{{\"event\":{agent.Config.EventComposition!.Codec.Serialize(delivery.Event)}," +
                            $"\"route\":{JsonSerializer.Serialize(delivery.Route, DeliveryJson)}}}";
                        var eventPtr = MarshalString(eventJson);
                        try { callback(context, eventPtr); }
                        finally { Marshal.FreeHGlobal(eventPtr); }
                    }
                });

                await agent.RunAsync(new UserMessagesInputEvent { Messages = messages,
                    Session = thread?.Session,
                    Thread = thread?.Thread
                });
                await inbox.DisposeAsync().ConfigureAwait(false);
                await consume.ConfigureAwait(false);

                // Signal end of stream with null pointer
                callback(context, IntPtr.Zero);
            });

            task.Wait();
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to run agent streaming: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return 0;
        }
    }

    // V2 serialize_thread / deserialize_thread APIs removed — recovery is projected from thread events.

    //
    // PERMISSION SYSTEM APIs (Human-in-the-Loop)
    //

    /// <summary>
    /// Responds to a permission request from the agent.
    /// Call this after receiving PermissionRequestEvent via streaming callback.
    /// </summary>
    /// <param name="agentHandle">Handle to the agent</param>
    /// <param name="permissionIdPtr">Pointer to UTF-8 encoded permission ID</param>
    /// <param name="approved">1 if approved, 0 if denied</param>
    /// <param name="permissionChoice">0 = Ask, 1 = AlwaysAllow, 2 = AlwaysDeny</param>
    /// <returns>1 on success, 0 on failure</returns>
    [UnmanagedCallersOnly(EntryPoint = "respond_to_permission")]
    public static int RespondToPermission(
        IntPtr agentHandle,
        IntPtr permissionIdPtr,
        int approved,
        int permissionChoice)
    {
        string? permissionId = Marshal.PtrToStringUTF8(permissionIdPtr);
        if (string.IsNullOrEmpty(permissionId)) return 0;
        return RespondToPermissionCore(agentHandle, permissionId, approved, permissionChoice);
    }

    private static int RespondToPermissionCore(
        IntPtr agentHandle,
        string permissionId,
        int approved,
        int permissionChoice)
    {
        try
        {
            var agent = ObjectManager.Get<HPD.Agent.Agent>(agentHandle);
            if (agent == null) return 0;

            if (string.IsNullOrEmpty(permissionId)) return 0;

            // Map integer to PermissionChoice enum
            var choiceId = permissionChoice switch
            {
                1 => "always_allow",
                2 => "always_deny",
                _ => approved == 1 ? "allow_once" : "deny_once"
            };

            // Send response back to the agent
            agent.AnswerRequestAsync(
                new PermissionResponseEvent(
                    permissionId,
                    "FFI",  // Source name
                    choiceId,
                    approved == 1 ? null : "User denied permission via FFI"
                )
            ).GetAwaiter().GetResult();

            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to respond to permission: {ex.Message}");
            return 0;
        }
    }

    //
    // HELPER METHODS
    //

    /// <summary>
    /// Helper method to marshal a C# string to unmanaged UTF-8 memory.
    /// </summary>
    private static IntPtr MarshalString(string str)
    {
        if (string.IsNullOrEmpty(str)) return IntPtr.Zero;

        byte[] bytes = Encoding.UTF8.GetBytes(str + '\0'); // null-terminated
        IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        return ptr;
    }

    //
    // Future APIs:
    // - Advanced memory management APIs (optional user-facing CRUD)
    // - Provider discovery and management
    //
}

/// <summary>
/// Native P/Invoke declarations for Apple Intelligence advanced features.
/// These map to Swift functions in HPDIntelligence framework.
/// </summary>
public static partial class AppleIntelligenceFFI
{
    private const string NativeDll = "HPDIntelligence";

    [DllImport(NativeDll, CharSet = CharSet.Ansi)]
    public static extern int AppleIntelligenceSupportsUseCase(string useCase);

    [DllImport(NativeDll, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.LPStr)]
    public static extern string AppleIntelligenceAvailableUseCases();

    [DllImport(NativeDll, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.LPStr)]
    public static extern string AppleIntelligenceAvailableGuardrailCategories();

    [DllImport(NativeDll, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.LPStr)]
    public static extern string AppleIntelligenceCreateGuardrailsConfig(string disallowedCategory);

    [DllImport(NativeDll, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.LPStr)]
    public static extern string AppleIntelligenceAvailableAdapters();

    [DllImport(NativeDll, CharSet = CharSet.Ansi)]
    public static extern int AppleIntelligenceAdapterIsAvailable(string adapterId);
}
