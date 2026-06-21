using System.Diagnostics;
using System.Runtime.InteropServices;
using HPD.Agent.FFI;

namespace HPD.Agent.Tests.FFI;

public sealed class NativeToolHarnessFFITests : IDisposable
{
    private static readonly object s_resolverGate = new();
    private static string? s_nativeLibraryPath;
    private static bool s_resolverRegistered;

    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "hpd-native-toolharness-ffi",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void NativeToolHarnessFFI_LoadsNativeLibraryAndExecutesFunction()
    {
        var compiler = FindCompiler();
        if (compiler is null)
        {
            return;
        }

        Directory.CreateDirectory(_tempDirectory);
        var libraryPath = BuildNativeToolHarnessLibrary(compiler);
        RegisterResolver(libraryPath);

        var registry = NativeToolHarnessFFI.GetToolHarnessRegistry();
        Assert.Single(registry.ToolHarnesses);
        Assert.Equal("NativeMath", registry.ToolHarnesses[0].Name);
        Assert.Single(registry.ToolHarnesses[0].Functions);
        Assert.Equal("native_add", registry.ToolHarnesses[0].Functions[0].Name);

        using var schemas = NativeToolHarnessFFI.GetHARNESSchemas();
        Assert.True(schemas.RootElement.TryGetProperty("native_add", out _));

        var stats = NativeToolHarnessFFI.GetHARNESStats();
        Assert.Equal(1, stats.TotalToolHarnesses);
        Assert.Equal(1, stats.TotalFunctions);

        Assert.Equal(["native_add"], NativeToolHarnessFFI.GetFunctionList());
        Assert.True(NativeToolHarnessFFI.RegisterToolHarnessExecutors("NativeMath"));

        var result = NativeToolHarnessFFI.ExecuteFunction(
            "native_add",
            new Dictionary<string, object>
            {
                ["left"] = 19,
                ["right"] = 23
            });

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Result);
        using (result.Result)
        {
            Assert.True(result.Result.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(42, result.Result.RootElement.GetProperty("result").GetInt32());
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private static string? FindCompiler()
    {
        var candidate = OperatingSystem.IsWindows() ? "cl.exe" : "cc";
        var pathVariable = global::System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (var directory in pathVariable.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;

            var path = Path.Combine(directory, candidate);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private string BuildNativeToolHarnessLibrary(string compiler)
    {
        var sourcePath = Path.Combine(_tempDirectory, "hpd_native_ToolHarnesses.c");
        var libraryPath = Path.Combine(
            _tempDirectory,
            OperatingSystem.IsMacOS()
                ? "libhpd_native_ToolHarnesses.dylib"
                : OperatingSystem.IsWindows()
                    ? "hpd_native_ToolHarnesses.dll"
                    : "libhpd_native_ToolHarnesses.so");

        File.WriteAllText(sourcePath, NativeToolHarnessSource);

        var arguments = OperatingSystem.IsMacOS()
            ? $"-dynamiclib -fPIC \"{sourcePath}\" -o \"{libraryPath}\""
            : OperatingSystem.IsWindows()
                ? $"/LD \"{sourcePath}\" /Fe:\"{libraryPath}\""
                : $"-shared -fPIC \"{sourcePath}\" -o \"{libraryPath}\"";

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = compiler,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Failed to start native compiler.");

        process.WaitForExit();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        var newLine = global::System.Environment.NewLine;

        Assert.True(
            process.ExitCode == 0,
            $"Native compiler failed with exit code {process.ExitCode}.{newLine}{output}{newLine}{error}");

        return libraryPath;
    }

    private static void RegisterResolver(string libraryPath)
    {
        lock (s_resolverGate)
        {
            s_nativeLibraryPath ??= libraryPath;
            if (s_resolverRegistered)
                return;

            NativeLibrary.SetDllImportResolver(
                typeof(NativeToolHarnessFFI).Assembly,
                (libraryName, assembly, searchPath) =>
                {
                    if (libraryName == "hpd_native_ToolHarnesses" &&
                        s_nativeLibraryPath is { } path)
                    {
                        return NativeLibrary.Load(path, assembly, searchPath);
                    }

                    return IntPtr.Zero;
                });

            s_resolverRegistered = true;
        }
    }

    private const string NativeToolHarnessSource = """
        #include <stdbool.h>
        #include <stdlib.h>
        #include <string.h>

        static char* copy_string(const char* value) {
            size_t length = strlen(value) + 1;
            char* copy = (char*)malloc(length);
            if (copy != 0) {
                memcpy(copy, value, length);
            }
            return copy;
        }

        const char* get_ToolHarness_registry(void) {
            return copy_string("{\"toolHarnesses\":[{\"name\":\"NativeMath\",\"description\":\"Native math harness\",\"functions\":[{\"name\":\"native_add\",\"wrapper\":\"native_add\"}]}]}");
        }

        const char* get_ToolHarness_schemas(void) {
            return copy_string("{\"native_add\":{\"type\":\"object\"}}");
        }

        const char* get_ToolHarness_stats(void) {
            return copy_string("{\"totalToolHarnesses\":1,\"totalFunctions\":1,\"toolHarnesses\":[{\"name\":\"NativeMath\",\"description\":\"Native math harness\",\"functionCount\":1}]}");
        }

        const char* get_function_list(void) {
            return copy_string("[\"native_add\"]");
        }

        const char* execute_ToolHarness_function(const char* function_name, const char* args_json) {
            (void)function_name;
            (void)args_json;
            return copy_string("{\"success\":true,\"result\":42}");
        }

        void free_string(char* ptr) {
            free(ptr);
        }

        bool register_ToolHarness_executors(const char* ToolHarness_name) {
            return ToolHarness_name != 0 && strcmp(ToolHarness_name, "NativeMath") == 0;
        }
        """;
}
