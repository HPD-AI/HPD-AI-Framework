using System.Buffers;
using System.Text.Json;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

public sealed record DebugSemanticLaunchConfiguration(
    string Target,
    string WorkingDirectory,
    DebugTargetKind SemanticTargetKind,
    DebugAdapterProgramKind ProgramKind,
    IReadOnlyList<string>? Arguments = null,
    bool StopOnEntry = false);

public sealed record DebugSemanticAttachConfiguration(
    string WorkingDirectory,
    string? ProcessId = null);

/// <summary>
/// Trusted adapter boundary that converts closed semantic start inputs into adapter-owned DAP
/// configuration. Model input never crosses this boundary as arbitrary JSON.
/// </summary>
public interface IDebugAdapterConfigurationComposer
{
    JsonElement ComposeLaunch(DebugAdapterDescriptor descriptor, DebugSemanticLaunchConfiguration configuration);
    JsonElement ComposeAttach(DebugAdapterDescriptor descriptor, DebugSemanticAttachConfiguration configuration);
}

public sealed class BuiltInDebugAdapterConfigurationComposer : IDebugAdapterConfigurationComposer
{
    private static readonly HashSet<string> SupportedAdapters = new(StringComparer.Ordinal)
    {
        "debugpy", "netcoredbg", "gdb", "lldb-dap", "codelldb", "delve", "javascript", "rdbg"
    };

    public JsonElement ComposeLaunch(
        DebugAdapterDescriptor descriptor,
        DebugSemanticLaunchConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(configuration);
        ValidateAdapter(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.Target);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.WorkingDirectory);
        if ((descriptor.TargetKinds & configuration.SemanticTargetKind) == 0)
            throw new InvalidOperationException(
                $"Debug adapter '{descriptor.Id}' does not support target kind '{configuration.SemanticTargetKind}'.");
        if ((descriptor.ProgramKinds & configuration.ProgramKind) == 0)
            throw new DebugStartPlanningException(
                "adapter_program_kind_unsupported",
                "The selected debug adapter cannot consume the resolved launch target.");

        return Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("request", "launch");
            if (descriptor.Id == "javascript") writer.WriteString("type", "pwa-node");
            if (descriptor.Id == "rdbg") writer.WriteString("type", "rdbg");
            if (descriptor.Id == "debugpy")
            {
                writer.WriteBoolean("justMyCode", false);
                writer.WriteString("console", "internalConsole");
            }
            if (descriptor.Id == "delve")
                writer.WriteString("mode",
                    configuration.SemanticTargetKind is DebugTargetKind.ProjectDirectory or DebugTargetKind.SourceFile
                        ? "debug"
                        : "exec");
            writer.WriteString("program", configuration.Target);
            writer.WriteString("cwd", configuration.WorkingDirectory);
            if (configuration.Arguments is { } arguments)
            {
                writer.WriteStartArray("args");
                foreach (var argument in arguments)
                {
                    ArgumentNullException.ThrowIfNull(argument);
                    writer.WriteStringValue(argument);
                }
                writer.WriteEndArray();
            }
            switch (descriptor.Id)
            {
                case "netcoredbg":
                    writer.WriteBoolean("stopAtEntry", configuration.StopOnEntry);
                    break;
                case "gdb":
                    writer.WriteBoolean("stopOnEntry", configuration.StopOnEntry);
                    writer.WriteBoolean("stopAtBeginningOfMainSubprogram", configuration.StopOnEntry);
                    break;
                default:
                    writer.WriteBoolean("stopOnEntry", configuration.StopOnEntry);
                    break;
            }
            writer.WriteEndObject();
        });
    }

    public JsonElement ComposeAttach(
        DebugAdapterDescriptor descriptor,
        DebugSemanticAttachConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(configuration);
        ValidateAdapter(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.WorkingDirectory);
        if (configuration.ProcessId is not null &&
            (descriptor.TargetKinds & DebugTargetKind.Process) == 0)
            throw new InvalidOperationException(
                $"Debug adapter '{descriptor.Id}' does not support process attachment.");

        return Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("request", "attach");
            if (descriptor.Id == "javascript") writer.WriteString("type", "pwa-node");
            if (descriptor.Id == "rdbg") writer.WriteString("type", "rdbg");
            if (descriptor.Id == "debugpy") writer.WriteBoolean("justMyCode", false);
            if (descriptor.Id == "delve") writer.WriteString("mode", "local");
            writer.WriteString("cwd", configuration.WorkingDirectory);
            if (configuration.ProcessId is { } processId)
            {
                WriteProcessId(writer, "pid", processId);
                WriteProcessId(writer, "processId", processId);
            }
            writer.WriteEndObject();
        });
    }

    private static void ValidateAdapter(DebugAdapterDescriptor descriptor)
    {
        if (!SupportedAdapters.Contains(descriptor.Id))
            throw new NotSupportedException(
                $"Debug adapter '{descriptor.Id}' has no registered semantic configuration composer.");
    }

    private static void WriteProcessId(Utf8JsonWriter writer, string propertyName, string processId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processId);
        if (long.TryParse(processId, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var numeric))
            writer.WriteNumber(propertyName, numeric);
        else
            writer.WriteString(propertyName, processId);
    }

    private static JsonElement Write(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
            writer.Flush();
        }
        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }
}

public static class DebugAdapterSemanticPlanExtensions
{
    public static ValueTask<DebugAdapterLaunchPlan> CreateSemanticLaunchPlanAsync(
        this IDebugAdapterFactory factory,
        IDebugAdapterConfigurationComposer composer,
        DebugAdapterDescriptor descriptor,
        DebugAdapterResolutionContext resolution,
        DebugSemanticLaunchConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(composer);
        var adapterConfiguration = composer.ComposeLaunch(descriptor, configuration);
        return factory.CreateLaunchPlanAsync(descriptor, new DebugLaunchContext
        {
            Resolution = resolution,
            Target = configuration.Target,
            Configuration = adapterConfiguration
        }, cancellationToken);
    }

    public static ValueTask<DebugAdapterLaunchPlan> CreateSemanticAttachPlanAsync(
        this IDebugAdapterFactory factory,
        IDebugAdapterConfigurationComposer composer,
        DebugAdapterDescriptor descriptor,
        DebugAdapterResolutionContext resolution,
        DebugSemanticAttachConfiguration configuration,
        string? endpointId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(composer);
        var adapterConfiguration = composer.ComposeAttach(descriptor, configuration);
        return factory.CreateAttachPlanAsync(descriptor, new DebugAttachContext
        {
            Resolution = resolution,
            ProcessId = configuration.ProcessId,
            EndpointId = endpointId,
            Configuration = adapterConfiguration
        }, cancellationToken);
    }
}
