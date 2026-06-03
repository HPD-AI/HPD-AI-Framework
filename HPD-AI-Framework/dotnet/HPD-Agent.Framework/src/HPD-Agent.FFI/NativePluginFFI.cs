using System;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Collections.Generic;

namespace HPD.Agent.FFI
{
    /// <summary>
    /// Language-agnostic FFI bindings for external ToolHarness systems.
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
    public static class NativeToolHarnessFFI
    {
        //    
        // CONFIGURATION: Native library name
        //
        // Customize per platform/language:
        // - Rust:   "hpd_rust_agent" or "libhpd_rust_agent.so"
        // - C++:    "hpd_cpp_ToolHarnesses" or "hpd_cpp_ToolHarnesses.dll"
        // - Zig:    "hpd_zig_ToolHarnesses"
        // - Go:     "hpd_go_ToolHarnesses"
        // - Swift:  "hpd_swift_ToolHarnesses"
        // - Multi:  "hpd_native_ToolHarnesses" (any language)
        //    
        private const string LibraryName = "hpd_native_ToolHarnesses";

        //    
        // FFI IMPORTS: C ABI functions (language-agnostic)
        //
        // Any language can implement these by exporting C-compatible symbols.
        // All functions use JSON for data exchange to ensure language neutrality.
        //    

        /// <summary>
        /// Get ToolHarness registry as JSON string.
        /// Native signature: const char* get_ToolHarness_registry()
        /// </summary>
        [DllImport(LibraryName, EntryPoint = "get_ToolHarness_registry", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetToolHarnessRegistryNative();

        /// <summary>
        /// Get ToolHarness schemas as JSON string.
        /// Native signature: const char* get_ToolHarness_schemas()
        /// </summary>
        [DllImport(LibraryName, EntryPoint = "get_ToolHarness_schemas", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetHARNESSchemasNative();

        /// <summary>
        /// Get ToolHarness statistics as JSON string.
        /// Native signature: const char* get_ToolHarness_stats()
        /// </summary>
        [DllImport(LibraryName, EntryPoint = "get_ToolHarness_stats", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetHARNESStatsNative();

        /// <summary>
        /// Get list of function names as JSON array.
        /// Native signature: const char* get_function_list()
        /// </summary>
        [DllImport(LibraryName, EntryPoint = "get_function_list", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetFunctionListNative();

        /// <summary>
        /// Execute a ToolHarness function with JSON arguments, returns JSON result.
        /// Native signature: const char* execute_ToolHarness_function(const char* function_name, const char* args_json)
        /// </summary>
        [DllImport(LibraryName, EntryPoint = "execute_ToolHarness_function", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ExecuteToolHarnessFunctionNative(
            [MarshalAs(UnmanagedType.LPStr)] string functionName,
            [MarshalAs(UnmanagedType.LPStr)] string argsJson);

        /// <summary>
        /// Free a string allocated by the native ToolHarness runtime.
        /// Native signature: void free_string(char* ptr)
        /// </summary>
        [DllImport(LibraryName, EntryPoint = "free_string", CallingConvention = CallingConvention.Cdecl)]
        private static extern void FreeStringNative(IntPtr ptr);

        /// <summary>
        /// Register ToolHarness executors in the native runtime.
        /// Native signature: bool register_ToolHarness_executors(const char* ToolHarness_name)
        /// </summary>
        [DllImport(LibraryName, EntryPoint = "register_ToolHarness_executors", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool RegisterToolHarnessExecutorsNative(
            [MarshalAs(UnmanagedType.LPStr)] string ToolHarnessName);

        //    
        // PUBLIC API: Language-agnostic wrapper methods
        //    

        /// <summary>
        /// Register ToolHarness executors in the native ToolHarness runtime.
        /// This MUST be called after loading ToolHarness info to populate the function registry.
        /// Works with any language that implements the C ABI.
        /// </summary>
        /// <param name="ToolHarnessName">Name of the ToolHarness to register</param>
        /// <returns>True if registration succeeded</returns>
        public static bool RegisterToolHarnessExecutors(string ToolHarnessName)
        {
            return RegisterToolHarnessExecutorsNative(ToolHarnessName);
        }

        /// <summary>
        /// Get all registered ToolHarnesses from the native runtime.
        /// Returns JSON data from Rust, C++, Zig, Go, Swift, or any C-compatible ToolHarness system.
        /// </summary>
        /// <returns>ToolHarness registry containing all registered ToolHarnesses</returns>
        public static ToolHarnessRegistry GetToolHarnessRegistry()
        {
            var ptr = GetToolHarnessRegistryNative();
            if (ptr == IntPtr.Zero)
                return new ToolHarnessRegistry { ToolHarnesses = new List<ToolHarnessInfo>() };

            try
            {
                var json = Marshal.PtrToStringAnsi(ptr);
                if (string.IsNullOrEmpty(json))
                    return new ToolHarnessRegistry { ToolHarnesses = new List<ToolHarnessInfo>() };

                return JsonSerializer.Deserialize(json, HPDFFIJsonContext.Default.ToolHarnessRegistry) ??
                       new ToolHarnessRegistry { ToolHarnesses = new List<ToolHarnessInfo>() };
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
        public static JsonDocument GetHARNESSchemas()
        {
            var ptr = GetHARNESSchemasNative();
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
        /// Get ToolHarness statistics (counts, performance metrics, etc.).
        /// </summary>
        /// <returns>ToolHarness statistics from the native runtime</returns>
        public static HARNESStats GetHARNESStats()
        {
            var ptr = GetHARNESStatsNative();
            if (ptr == IntPtr.Zero)
                return new HARNESStats();

            try
            {
                var json = Marshal.PtrToStringAnsi(ptr);
                if (string.IsNullOrEmpty(json))
                    return new HARNESStats();

                return JsonSerializer.Deserialize(json, HPDFFIJsonContext.Default.HARNESStats) ?? new HARNESStats();
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
        /// Execute a ToolHarness function in the native runtime.
        /// Communicates via JSON - works with any language.
        /// </summary>
        /// <param name="functionName">Name of the function to execute</param>
        /// <param name="arguments">Function arguments as a dictionary (will be serialized to JSON)</param>
        /// <returns>Execution result containing success status, result data, or error message</returns>
        public static ToolHarnessExecutionResult ExecuteFunction(string functionName, Dictionary<string, object> arguments)
        {
            var argsJson = JsonSerializer.Serialize(arguments, HPDFFIJsonContext.Default.DictionaryStringObject);
            var ptr = ExecuteToolHarnessFunctionNative(functionName, argsJson);

            if (ptr == IntPtr.Zero)
            {
                return new ToolHarnessExecutionResult
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
                    return new ToolHarnessExecutionResult
                    {
                        Success = false,
                        Error = "Empty response from function"
                    };
                }

                var response = JsonDocument.Parse(json);
                return new ToolHarnessExecutionResult
                {
                    Success = true,
                    Result = response
                };
            }
            catch (Exception ex)
            {
                return new ToolHarnessExecutionResult
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
    /// ToolHarness registry information from native runtime.
    /// Language-agnostic: works with JSON from Rust, C++, Zig, Go, Swift, etc.
    /// </summary>
    public class ToolHarnessRegistry
    {
        public List<ToolHarnessInfo> ToolHarnesses { get; set; } = new();
    }

    /// <summary>
    /// Information about a single ToolHarness.
    /// </summary>
    public class ToolHarnessInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<FunctionInfo> Functions { get; set; } = new();
    }

    /// <summary>
    /// Information about a ToolHarness function.
    /// </summary>
    public class FunctionInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Wrapper { get; set; } = string.Empty;
    }

    /// <summary>
    /// ToolHarness statistics from native runtime.
    /// </summary>
    public class HARNESStats
    {
        public int TotalToolHarnesses { get; set; }
        public int TotalFunctions { get; set; }
        public List<HARNESSummary> ToolHarnesses { get; set; } = new();
    }

    /// <summary>
    /// Summary information about a ToolHarness.
    /// </summary>
    public class HARNESSummary
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int FunctionCount { get; set; }
    }

    /// <summary>
    /// Result of executing a ToolHarness function.
    /// Language-agnostic: success/error pattern works across all languages.
    /// </summary>
    public class ToolHarnessExecutionResult
    {
        public bool Success { get; set; }
        public JsonDocument? Result { get; set; }
        public string? Error { get; set; }
    }
}
