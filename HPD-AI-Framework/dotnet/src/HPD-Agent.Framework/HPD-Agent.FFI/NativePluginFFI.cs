using System;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Collections.Generic;

namespace HPD.Agent.FFI
{
    /// <summary>
    /// Language-agnostic FFI bindings for external Toolkit systems.
    /// Supports any language that exports C-compatible functions (Rust, C++, Zig, Go, Swift, etc.)
    ///
    /// Protocol: JSON over C ABI
    /// - All data exchanged as JSON strings via pointers
    /// - Standard C calling convention (cdecl)
    /// - Memory management: caller allocates, callee frees via free_string()
    ///
    /// Compatible Languages:
    /// - Rust (with #[no_mangle] extern "C")
    /// - C/C++ (with extern "C")
    /// - Zig (with export fn)
    /// - Go (with //export via CGO)
    /// - Swift (with @_cdecl)
    /// - Python (with ctypes/cffi)
    /// - Node.js (with Node-API/NAPI)
    /// </summary>
    public static class NativeToolkitFFI
    {
        //    
        // CONFIGURATION: Native library name
        //
        // Customize per platform/language:
        // - Rust:   "hpd_rust_agent" or "libhpd_rust_agent.so"
        // - C++:    "hpd_cpp_Toolkits" or "hpd_cpp_Toolkits.dll"
        // - Zig:    "hpd_zig_Toolkits"
        // - Go:     "hpd_go_Toolkits"
        // - Swift:  "hpd_swift_Toolkits"
        // - Multi:  "hpd_native_Toolkits" (any language)
        //    
        private const string LibraryName = "hpd_native_Toolkits";

        //    
        // FFI IMPORTS: C ABI functions (language-agnostic)
        //
        // Any language can implement these by exporting C-compatible symbols.
        // All functions use JSON for data exchange to ensure language neutrality.
        //    

        /// <summary>
        /// Get Toolkit registry as JSON string.
        /// Native signature: const char* get_Toolkit_registry()
        /// </summary>
        [DllImport(LibraryName, EntryPoint = "get_Toolkit_registry", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetToolkitRegistryNative();

        /// <summary>
        /// Get Toolkit schemas as JSON string.
        /// Native signature: const char* get_Toolkit_schemas()
        /// </summary>
        [DllImport(LibraryName, EntryPoint = "get_Toolkit_schemas", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetToolkitSchemasNative();

        /// <summary>
        /// Get Toolkit statistics as JSON string.
        /// Native signature: const char* get_Toolkit_stats()
        /// </summary>
        [DllImport(LibraryName, EntryPoint = "get_Toolkit_stats", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetToolkitStatsNative();

        /// <summary>
        /// Get list of function names as JSON array.
        /// Native signature: const char* get_function_list()
        /// </summary>
        [DllImport(LibraryName, EntryPoint = "get_function_list", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetFunctionListNative();

        /// <summary>
        /// Execute a Toolkit function with JSON arguments, returns JSON result.
        /// Native signature: const char* execute_Toolkit_function(const char* function_name, const char* args_json)
        /// </summary>
        [DllImport(LibraryName, EntryPoint = "execute_Toolkit_function", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ExecuteToolkitFunctionNative(
            [MarshalAs(UnmanagedType.LPStr)] string functionName,
            [MarshalAs(UnmanagedType.LPStr)] string argsJson);

        /// <summary>
        /// Free a string allocated by the native Toolkit runtime.
        /// Native signature: void free_string(char* ptr)
        /// </summary>
        [DllImport(LibraryName, EntryPoint = "free_string", CallingConvention = CallingConvention.Cdecl)]
        private static extern void FreeStringNative(IntPtr ptr);

        /// <summary>
        /// Register Toolkit executors in the native runtime.
        /// Native signature: bool register_Toolkit_executors(const char* Toolkit_name)
        /// </summary>
        [DllImport(LibraryName, EntryPoint = "register_Toolkit_executors", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool RegisterToolkitExecutorsNative(
            [MarshalAs(UnmanagedType.LPStr)] string ToolkitName);

        //    
        // PUBLIC API: Language-agnostic wrapper methods
        //    

        /// <summary>
        /// Register Toolkit executors in the native Toolkit runtime.
        /// This MUST be called after loading Toolkit info to populate the function registry.
        /// Works with any language that implements the C ABI.
        /// </summary>
        /// <param name="ToolkitName">Name of the Toolkit to register</param>
        /// <returns>True if registration succeeded</returns>
        public static bool RegisterToolkitExecutors(string ToolkitName)
        {
            return RegisterToolkitExecutorsNative(ToolkitName);
        }

        /// <summary>
        /// Get all registered Toolkits from the native runtime.
        /// Returns JSON data from Rust, C++, Zig, Go, Swift, or any C-compatible Toolkit system.
        /// </summary>
        /// <returns>Toolkit registry containing all registered Toolkits</returns>
        public static ToolkitRegistry GetToolkitRegistry()
        {
            var ptr = GetToolkitRegistryNative();
            if (ptr == IntPtr.Zero)
                return new ToolkitRegistry { Toolkits = new List<ToolkitInfo>() };

            try
            {
                var json = Marshal.PtrToStringAnsi(ptr);
                if (string.IsNullOrEmpty(json))
                    return new ToolkitRegistry { Toolkits = new List<ToolkitInfo>() };

                return JsonSerializer.Deserialize(json, HPDFFIJsonContext.Default.ToolkitRegistry) ??
                       new ToolkitRegistry { Toolkits = new List<ToolkitInfo>() };
            }
            finally
            {
                FreeStringNative(ptr);
            }
        }

        /// <summary>
        /// Get all function schemas as a JSON object.
        /// Schemas describe function parameters, return types, and documentation.
        /// </summary>
        /// <returns>JSON document containing all function schemas</returns>
        public static JsonDocument GetToolkitSchemas()
        {
            var ptr = GetToolkitSchemasNative();
            if (ptr == IntPtr.Zero)
                return JsonDocument.Parse("{}");

            try
            {
                var json = Marshal.PtrToStringAnsi(ptr);
                return JsonDocument.Parse(json ?? "{}");
            }
            finally
            {
                FreeStringNative(ptr);
            }
        }

        /// <summary>
        /// Get Toolkit statistics (counts, performance metrics, etc.).
        /// </summary>
        /// <returns>Toolkit statistics from the native runtime</returns>
        public static ToolkitStats GetToolkitStats()
        {
            var ptr = GetToolkitStatsNative();
            if (ptr == IntPtr.Zero)
                return new ToolkitStats();

            try
            {
                var json = Marshal.PtrToStringAnsi(ptr);
                if (string.IsNullOrEmpty(json))
                    return new ToolkitStats();

                return JsonSerializer.Deserialize(json, HPDFFIJsonContext.Default.ToolkitStats) ?? new ToolkitStats();
            }
            finally
            {
                FreeStringNative(ptr);
            }
        }

        /// <summary>
        /// Get list of all available function names from the native runtime.
        /// </summary>
        /// <returns>List of function names</returns>
        public static List<string> GetFunctionList()
        {
            var ptr = GetFunctionListNative();
            if (ptr == IntPtr.Zero)
                return new List<string>();

            try
            {
                var json = Marshal.PtrToStringAnsi(ptr);
                if (string.IsNullOrEmpty(json))
                    return new List<string>();

                return JsonSerializer.Deserialize(json, HPDFFIJsonContext.Default.ListString) ?? new List<string>();
            }
            finally
            {
                FreeStringNative(ptr);
            }
        }

        /// <summary>
        /// Execute a Toolkit function in the native runtime.
        /// Communicates via JSON - works with any language.
        /// </summary>
        /// <param name="functionName">Name of the function to execute</param>
        /// <param name="arguments">Function arguments as a dictionary (will be serialized to JSON)</param>
        /// <returns>Execution result containing success status, result data, or error message</returns>
        public static ToolkitExecutionResult ExecuteFunction(string functionName, Dictionary<string, object> arguments)
        {
            var argsJson = JsonSerializer.Serialize(arguments, HPDFFIJsonContext.Default.DictionaryStringObject);
            var ptr = ExecuteToolkitFunctionNative(functionName, argsJson);

            if (ptr == IntPtr.Zero)
            {
                return new ToolkitExecutionResult
                {
                    Success = false,
                    Error = "Failed to execute function"
                };
            }

            try
            {
                var json = Marshal.PtrToStringAnsi(ptr);
                if (string.IsNullOrEmpty(json))
                {
                    return new ToolkitExecutionResult
                    {
                        Success = false,
                        Error = "Empty response from function"
                    };
                }

                var response = JsonDocument.Parse(json);
                return new ToolkitExecutionResult
                {
                    Success = true,
                    Result = response
                };
            }
            catch (Exception ex)
            {
                return new ToolkitExecutionResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
            finally
            {
                FreeStringNative(ptr);
            }
        }
    }

    //    
    // LANGUAGE-AGNOSTIC DATA STRUCTURES
    //
    // These work with JSON from any language that can serialize to JSON.
    // The structures are designed to be simple and portable across language boundaries.
    //    

    /// <summary>
    /// Toolkit registry information from native runtime.
    /// Language-agnostic: works with JSON from Rust, C++, Zig, Go, Swift, etc.
    /// </summary>
    public class ToolkitRegistry
    {
        public List<ToolkitInfo> Toolkits { get; set; } = new();
    }

    /// <summary>
    /// Information about a single Toolkit.
    /// </summary>
    public class ToolkitInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<FunctionInfo> Functions { get; set; } = new();
    }

    /// <summary>
    /// Information about a Toolkit function.
    /// </summary>
    public class FunctionInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Wrapper { get; set; } = string.Empty;
    }

    /// <summary>
    /// Toolkit statistics from native runtime.
    /// </summary>
    public class ToolkitStats
    {
        public int TotalToolkits { get; set; }
        public int TotalFunctions { get; set; }
        public List<ToolkitSummary> Toolkits { get; set; } = new();
    }

    /// <summary>
    /// Summary information about a Toolkit.
    /// </summary>
    public class ToolkitSummary
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int FunctionCount { get; set; }
    }

    /// <summary>
    /// Result of executing a Toolkit function.
    /// Language-agnostic: success/error pattern works across all languages.
    /// </summary>
    public class ToolkitExecutionResult
    {
        public bool Success { get; set; }
        public JsonDocument? Result { get; set; }
        public string? Error { get; set; }
    }
}
