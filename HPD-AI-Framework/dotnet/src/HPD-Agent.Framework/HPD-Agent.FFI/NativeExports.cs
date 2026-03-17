using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using HPD.Agent;

namespace HPD.Agent.FFI;

/// <summary>
/// Wrapper holding V3 Session + Branch pair for FFI thread handles.
/// FFI consumers see a single "thread" handle; internally we maintain the V3 split.
/// Note: FFI has InternalsVisibleTo access and can use internal Session/Branch constructors.
/// This is framework code, not user-facing, so internal API usage is appropriate.
/// </summary>
internal sealed class FFIConversationThread
{
    public Session Session { get; }
    public Branch Branch { get; }

    public FFIConversationThread()
    {
        Session = new Session();
        Branch = Session.CreateBranch();
    }
}


/// <summary>
/// Delegate for streaming callback from C# to Rust
/// </summary>
/// <param name="context">Context pointer passed back to Rust</param>
/// <param name="eventJsonPtr">Pointer to UTF-8 JSON string of the event, or null to signal end of stream</param>
public delegate void StreamCallback(IntPtr context, IntPtr eventJsonPtr);

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
    
    [JsonPropertyName("Toolkit_name")]
    public string ToolkitName { get; set; } = string.Empty;
}

/// <summary>
/// Static class containing all C# functions exported to Rust via FFI.
/// This serves as the main entry point for the Rust wrapper library.
/// </summary>
public static partial class NativeExports
{
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
    /// Creates an agent with the given configuration and Toolkits.
    /// </summary>
    /// <param name="configJsonPtr">Pointer to JSON string containing AgentConfig</param>
    /// <param name="ToolkitsJsonPtr">Pointer to JSON string containing Toolkit definitions</param>
    /// <returns>Handle to the created Agent, or IntPtr.Zero on failure</returns>
    [UnmanagedCallersOnly(EntryPoint = "create_agent_with_Toolkits")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "FFI boundary - AgentBuilder uses reflection for C# Toolkit discovery, but FFI only adds native functions manually")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "FFI boundary - AgentBuilder uses reflection for C# Toolkit discovery, but FFI only adds native functions manually")]
    public static IntPtr CreateAgentWithToolkits(IntPtr configJsonPtr, IntPtr ToolkitsJsonPtr)
    {
        try
        {
            string? configJson = Marshal.PtrToStringUTF8(configJsonPtr);
            if (string.IsNullOrEmpty(configJson)) return IntPtr.Zero;

            var agentConfig = JsonSerializer.Deserialize<AgentConfig>(configJson, HPDFFIJsonContext.Default.AgentConfig);
            if (agentConfig == null) return IntPtr.Zero;

            var builder = new AgentBuilder(agentConfig);

            // Parse and add native Toolkits (Rust, C++, Zig, Go, etc.)
            string? ToolkitsJson = Marshal.PtrToStringUTF8(ToolkitsJsonPtr);
            Console.WriteLine($"[FFI] Received Toolkits JSON: {ToolkitsJson}");

            if (!string.IsNullOrEmpty(ToolkitsJson))
            {
                try
                {
                    var nativeFunctions = JsonSerializer.Deserialize(ToolkitsJson, HPDFFIJsonContext.Default.ListNativeFunctionInfo);
                    Console.WriteLine($"[FFI] Deserialized {nativeFunctions?.Count ?? 0} native functions");

                    if (nativeFunctions != null && nativeFunctions.Count > 0)
                    {
                        // Track unique Toolkit names
                        var ToolkitNames = new HashSet<string>();

                        foreach (var nativeFunc in nativeFunctions)
                        {
                            Console.WriteLine($"[FFI] Adding native function: {nativeFunc.Name} - {nativeFunc.Description}");
                            var aiFunction = CreateNativeFunctionWrapper(nativeFunc);
                            builder.WithNativeFunction(aiFunction);

                            // Track Toolkit name for registration
                            if (!string.IsNullOrEmpty(nativeFunc.ToolkitName))
                            {
                                ToolkitNames.Add(nativeFunc.ToolkitName);
                            }
                        }

                        // Register Toolkit executors in native runtime
                        foreach (var ToolkitName in ToolkitNames)
                        {
                            Console.WriteLine($"[FFI] Registering executors for Toolkit: {ToolkitName}");
                            bool success = NativeToolkitFFI.RegisterToolkitExecutors(ToolkitName);
                            Console.WriteLine($"[FFI] Registration result for {ToolkitName}: {success}");
                        }

                        Console.WriteLine($"[FFI] Successfully added {nativeFunctions.Count} native functions to agent");
                    }
                }
                catch (Exception ex)
                {
                    // Log but don't fail - agent can still work without native Toolkits
                    Console.WriteLine($"Failed to parse native Toolkits: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }

            var agent = builder.BuildAsync().GetAwaiter().GetResult();
            return ObjectManager.Add(agent);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create agent: {ex.Message}");
            return IntPtr.Zero;
        }
    }


    /// <summary>
    /// Creates an AIFunction wrapper that calls back to native code via FFI.
    /// Supports Toolkits written in Rust, C++, Zig, Go, Swift, or any C-compatible language.
    /// </summary>
    private static AIFunction CreateNativeFunctionWrapper(NativeFunctionInfo nativeFunc)
    {
        return HPDAIFunctionFactory.Create(
            (arguments, cancellationToken) =>
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
                var result = NativeToolkitFFI.ExecuteFunction(nativeFunc.Name, argsDict);
                
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
                RequiresPermission = nativeFunc.RequiresPermission,
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
        ObjectManager.Remove(agentHandle);
    }

    //    
    // CONVERSATION THREAD MANAGEMENT
    //    

    /// <summary>
    /// Creates a new conversation thread for managing conversation state.
    /// </summary>
    /// <returns>Handle to the created conversation thread, or IntPtr.Zero on failure</returns>
    [UnmanagedCallersOnly(EntryPoint = "create_conversation_thread")]
    public static IntPtr CreateAgentSession()
    {
        try
        {
            var thread = new FFIConversationThread();
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
        try
        {
            var thread = ObjectManager.Get<FFIConversationThread>(threadHandle);
            if (thread == null) return -1;

            return thread.Branch.MessageCount;
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

            var json = JsonSerializer.Serialize(thread.Branch.Messages, HPDFFIJsonContext.Default.IEnumerableChatMessage);
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

            thread.Branch.AddMessage(message);
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

            thread.Branch.Clear();
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
        try
        {
            var agent = ObjectManager.Get<HPD.Agent.Agent>(agentHandle);
            if (agent == null) return IntPtr.Zero;

            string? input = Marshal.PtrToStringUTF8(inputPtr);
            if (string.IsNullOrEmpty(input)) return IntPtr.Zero;

            // Create user message
            var userMessage = new ChatMessage(ChatRole.User, input);
            var messages = new[] { userMessage };

            // Get thread if provided
            FFIConversationThread? thread = null;
            if (threadHandle != IntPtr.Zero)
            {
                thread = ObjectManager.Get<FFIConversationThread>(threadHandle);
            }

            // Run agent and collect all events
            var responseText = new StringBuilder();
            IAsyncEnumerable<AgentEvent> eventStream;

            if (thread != null)
            {
                eventStream = agent.RunAsync(messages, session: thread.Session, branch: thread.Branch, options: null);
            }
            else
            {
                eventStream = agent.RunAsync(messages, session: null, options: null);
            }

            // Block and collect response
            var task = Task.Run(async () =>
            {
                await foreach (var evt in eventStream)
                {
                    // Collect text deltas
                    if (evt is TextDeltaEvent textDelta)
                    {
                        responseText.Append(textDelta.Text);
                    }
                }
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
        try
        {
            var agent = ObjectManager.Get<HPD.Agent.Agent>(agentHandle);
            if (agent == null) return 0;

            string? input = Marshal.PtrToStringUTF8(inputPtr);
            if (string.IsNullOrEmpty(input)) return 0;

            if (callbackPtr == IntPtr.Zero) return 0;

            // Marshal the callback
            var callback = Marshal.GetDelegateForFunctionPointer<StreamCallback>(callbackPtr);

            // Create user message
            var userMessage = new ChatMessage(ChatRole.User, input);
            var messages = new[] { userMessage };

            // Get thread if provided
            FFIConversationThread? thread = null;
            if (threadHandle != IntPtr.Zero)
            {
                thread = ObjectManager.Get<FFIConversationThread>(threadHandle);
            }

            // Run agent and stream events
            IAsyncEnumerable<AgentEvent> eventStream;

            if (thread != null)
            {
                eventStream = agent.RunAsync(messages, session: thread.Session, branch: thread.Branch, options: null);
            }
            else
            {
                eventStream = agent.RunAsync(messages, session: null, options: null);
            }

            // Stream events to callback
            var task = Task.Run(async () =>
            {
                await foreach (var evt in eventStream)
                {
                    // Serialize event to JSON
                    var eventJson = JsonSerializer.Serialize(evt, HPDFFIJsonContext.Default.AgentEvent);
                    var eventPtr = MarshalString(eventJson);

                    try
                    {
                        // Invoke callback
                        callback(context, eventPtr);
                    }
                    finally
                    {
                        // Free the event string
                        Marshal.FreeHGlobal(eventPtr);
                    }
                }

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

    // V2 serialize_thread / deserialize_thread APIs removed — crash recovery is now automatic via UncommittedTurn.

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
        try
        {
            var agent = ObjectManager.Get<HPD.Agent.Agent>(agentHandle);
            if (agent == null) return 0;

            string? permissionId = Marshal.PtrToStringUTF8(permissionIdPtr);
            if (string.IsNullOrEmpty(permissionId)) return 0;

            // Map integer to PermissionChoice enum
            PermissionChoice choice = permissionChoice switch
            {
                1 => PermissionChoice.AlwaysAllow,
                2 => PermissionChoice.AlwaysDeny,
                _ => PermissionChoice.Ask
            };

            // Send response back to the agent
            agent.SendMiddlewareResponse(
                permissionId,
                new PermissionResponseEvent(
                    permissionId,
                    "FFI",  // Source name
                    approved == 1,
                    approved == 1 ? null : "User denied permission via FFI",
                    choice
                )
            );

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
