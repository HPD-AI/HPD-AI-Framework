using System;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Collections.Generic;

namespace HPD.Agent.FFI
{
    /// <summary>
    /// Language-agnostic FFI bindings for external Harness systems.
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
    public static class NativeHarnessFFI
    {
        //    
        // CONFIGURATION: Native library name
        //
        // Customize per platform/language:
        // - Rust:   "hpd_rust_agent" or "libhpd_rust_agent.so"
        // - C++:    "hpd_cpp_Harneses" or "hpd_cpp_Harneses.dll"
        // - Zig:    "hpd_zig_Harneses"
        // - Go:     "hpd_go_Harneses"
        // - Swift:  "hpd_swift_Harneses"
        // - Multi:  "hpd_native_Harneses" (any language)
        //    
        private const string LibraryName = "hpd_native_Harneses";

        //    
        // FFI IMPORTS: C ABI functions (language-agnostic)
        //
        // Any language can implement these by exporting C-compatible symbols.
        // All functions use JSON for data exchange to ensure language neutrality.
        //    

        /// <summary>
        /// Get Harness registry as JSON string.
        /// Native signature: const char* get_Harness_registry()
        /// </summary>
        [DllImport(LibraryName, EntryPoint = "get_Harness_registry", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetHarnessRegistryNative();

        /// <summary>
        /// Get Harness schemas as JSON string.
        /// Native signature: const char* get_Harness_schemas()
        /// </summary>
        [DllImport(LibraryName, EntryPoint = "get_Harness_schemas", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetHARNESSchemasNative();

        /// <summary>
        /// Get Harness statistics as JSON string.
        /// Native signature: const char* get_Harness_stats()
        /// </summary>
        [DllImport(LibraryName, EntryPoint = "get_Harness_stats", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetHARNESStatsNative();

        /// <summary>
        /// Get list of function names as JSON array.
        /// Native signature: const char* get_function_list()
        /// </summary>
        [DllImport(LibraryName, EntryPoint = "get_function_list", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetFunctionListNative();

        /// <summary>
        /// Execute a Harness function with JSON arguments, returns JSON result.
        /// Native signature: const char* execute_Harness_function(const char* function_name, const char* args_json)
        /// </summary>
        [DllImport(LibraryName, EntryPoint = "execute_Harness_function", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ExecuteHarnessFunctionNative(
            [MarshalAs(UnmanagedType.LPStr)] string functionName,
            [MarshalAs(UnmanagedType.LPStr)] string argsJson);

        /// <summary>
        /// Free a string allocated by the native Harness runtime.
        /// Native signature: void free_string(char* ptr)
        /// </summary>
        [DllImport(LibraryName, EntryPoint = "free_string", CallingConvention = CallingConvention.Cdecl)]
        private static extern void FreeStringNative(IntPtr ptr);

        /// <summary>
        /// Register Harness executors in the native runtime.
        /// Native signature: bool register_Harness_executors(const char* Harness_name)
        /// </summary>
        [DllImport(LibraryName, EntryPoint = "register_Harness_executors", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool RegisterHarnessExecutorsNative(
            [MarshalAs(UnmanagedType.LPStr)] string HarnessName);

        //    
        // PUBLIC API: Language-agnostic wrapper methods
        //    

        /// <summary>
        /// Register Harness executors in the native Harness runtime.
        /// This MUST be called after loading Harness info to populate the function registry.
        /// Works with any language that implements the C ABI.
        /// </summary>
        /// <param name="HarnessName">Name of the Harness to register</param>
        /// <returns>True if registration succeeded</returns>
        public static bool RegisterHarnessExecutors(string HarnessName)
        {
            return RegisterHarnessExecutorsNative(HarnessName);
        }

        /// <summary>
        /// Get all registered Harneses from the native runtime.
        /// Returns JSON data from Rust, C++, Zig, Go, Swift, or any C-compatible Harness system.
        /// </summary>
        /// <returns>Harness registry containing all registered Harneses</returns>
        public static HarnessRegistry GetHarnessRegistry()
        {
            var ptr = GetHarnessRegistryNative();
            if (ptr == IntPtr.Zero)
                return new HarnessRegistry { Harneses = new List<HarnessInfo>() };

            try
            {
                var json = Marshal.PtrToStringAnsi(ptr);
                if (string.IsNullOrEmpty(json))
                    return new HarnessRegistry { Harneses = new List<HarnessInfo>() };

                return JsonSerializer.Deserialize(json, HPDFFIJsonContext.Default.HarnessRegistry) ??
                       new HarnessRegistry { Harneses = new List<HarnessInfo>() };
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
        /// Get Harness statistics (counts, performance metrics, etc.).
        /// </summary>
        /// <returns>Harness statistics from the native runtime</returns>
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
        /// Execute a Harness function in the native runtime.
        /// Communicates via JSON - works with any language.
        /// </summary>
        /// <param name="functionName">Name of the function to execute</param>
        /// <param name="arguments">Function arguments as a dictionary (will be serialized to JSON)</param>
        /// <returns>Execution result containing success status, result data, or error message</returns>
        public static HarnessExecutionResult ExecuteFunction(string functionName, Dictionary<string, object> arguments)
        {
            var argsJson = JsonSerializer.Serialize(arguments, HPDFFIJsonContext.Default.DictionaryStringObject);
            var ptr = ExecuteHarnessFunctionNative(functionName, argsJson);

            if (ptr == IntPtr.Zero)
            {
                return new HarnessExecutionResult
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
                    return new HarnessExecutionResult
                    {
                        Success = false,
                        Error = "Empty response from function"
                    };
                }

                var response = JsonDocument.Parse(json);
                return new HarnessExecutionResult
                {
                    Success = true,
                    Result = response
                };
            }
            catch (Exception ex)
            {
                return new HarnessExecutionResult
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
    /// Harness registry information from native runtime.
    /// Language-agnostic: works with JSON from Rust, C++, Zig, Go, Swift, etc.
    /// </summary>
    public class HarnessRegistry
    {
        public List<HarnessInfo> Harneses { get; set; } = new();
    }

    /// <summary>
    /// Information about a single Harness.
    /// </summary>
    public class HarnessInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<FunctionInfo> Functions { get; set; } = new();
    }

    /// <summary>
    /// Information about a Harness function.
    /// </summary>
    public class FunctionInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Wrapper { get; set; } = string.Empty;
    }

    /// <summary>
    /// Harness statistics from native runtime.
    /// </summary>
    public class HARNESStats
    {
        public int TotalHarneses { get; set; }
        public int TotalFunctions { get; set; }
        public List<HARNESSummary> Harneses { get; set; } = new();
    }

    /// <summary>
    /// Summary information about a Harness.
    /// </summary>
    public class HARNESSummary
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int FunctionCount { get; set; }
    }

    /// <summary>
    /// Result of executing a Harness function.
    /// Language-agnostic: success/error pattern works across all languages.
    /// </summary>
    public class HarnessExecutionResult
    {
        public bool Success { get; set; }
        public JsonDocument? Result { get; set; }
        public string? Error { get; set; }
    }
}
