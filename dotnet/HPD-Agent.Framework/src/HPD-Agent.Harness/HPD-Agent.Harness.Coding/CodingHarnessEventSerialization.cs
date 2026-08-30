using System.Text.Json.Serialization.Metadata;
using HPD.Agent.Serialization;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPDOS.ToolHarnesses.Middleware;

[assembly: HpdAgentEventModuleManifest(
    "hpd.agent.harness.coding",
    typeof(CodingAgentEventModule),
    typeof(CoreAgentEventModule))]

/// <summary>Immutable durable event declarations owned by the Coding ToolHarness.</summary>
public static class CodingAgentEventModule
{
    /// <summary>Gets the immutable Coding event fragment.</summary>
    public static AgentEventModuleFragment Fragment { get; } = new()
    {
        ModuleId = "hpd.agent.harness.coding",
        Events = Array.AsReadOnly<AgentEventDescriptor>(
        [
            Create(typeof(ExecuteCommandProcessStartedEvent), "EXECUTE_COMMAND_PROCESS_STARTED", CodingToolHarnessJsonContext.Default.ExecuteCommandProcessStartedEvent),
            Create(typeof(ExecuteCommandOutputChunkEvent), "EXECUTE_COMMAND_OUTPUT_CHUNK", CodingToolHarnessJsonContext.Default.ExecuteCommandOutputChunkEvent, AgentEventDurability.LiveOnly),
            Create(typeof(ExecuteCommandProgressEvent), "EXECUTE_COMMAND_PROGRESS", CodingToolHarnessJsonContext.Default.ExecuteCommandProgressEvent),
            Create(typeof(ExecuteCommandProcessExitedEvent), "EXECUTE_COMMAND_PROCESS_EXITED", CodingToolHarnessJsonContext.Default.ExecuteCommandProcessExitedEvent),
            Create(typeof(ExecuteCommandContentWriteFailedEvent), "EXECUTE_COMMAND_CONTENT_WRITE_FAILED", CodingToolHarnessJsonContext.Default.ExecuteCommandContentWriteFailedEvent),
            Create(typeof(ExecuteCommandAutoBackgroundedEvent), "EXECUTE_COMMAND_AUTO_BACKGROUNDED", CodingToolHarnessJsonContext.Default.ExecuteCommandAutoBackgroundedEvent),
            Create(typeof(ExecuteCommandPermissionRequestEvent), "EXECUTE_COMMAND_PERMISSION_REQUEST", CodingToolHarnessJsonContext.Default.ExecuteCommandPermissionRequestEvent),
            Create(typeof(ExecuteCommandPermissionResponseEvent), "EXECUTE_COMMAND_PERMISSION_RESPONSE", CodingToolHarnessJsonContext.Default.ExecuteCommandPermissionResponseEvent),
            Create(typeof(ExecuteCommandPermissionRulePersistedEvent), "EXECUTE_COMMAND_PERMISSION_RULE_PERSISTED", CodingToolHarnessJsonContext.Default.ExecuteCommandPermissionRulePersistedEvent),
            Create(typeof(FileEditAppliedEvent), "FILE_EDIT_APPLIED", CodingToolHarnessJsonContext.Default.FileEditAppliedEvent),
            Create(typeof(FileWriteAppliedEvent), "FILE_WRITE_APPLIED", CodingToolHarnessJsonContext.Default.FileWriteAppliedEvent),
            Create(typeof(LanguageServerDocumentOpenedEvent), "LANGUAGE_SERVER_DOCUMENT_OPENED", CodingToolHarnessJsonContext.Default.LanguageServerDocumentOpenedEvent),
            Create(typeof(LanguageServerDocumentChangedEvent), "LANGUAGE_SERVER_DOCUMENT_CHANGED", CodingToolHarnessJsonContext.Default.LanguageServerDocumentChangedEvent),
            Create(typeof(LanguageServerDocumentClosedEvent), "LANGUAGE_SERVER_DOCUMENT_CLOSED", CodingToolHarnessJsonContext.Default.LanguageServerDocumentClosedEvent),
            Create(typeof(LanguageServerDocumentSavedEvent), "LANGUAGE_SERVER_DOCUMENT_SAVED", CodingToolHarnessJsonContext.Default.LanguageServerDocumentSavedEvent),
            Create(typeof(LanguageServerWatchedFileChangedEvent), "LANGUAGE_SERVER_WATCHED_FILE_CHANGED", CodingToolHarnessJsonContext.Default.LanguageServerWatchedFileChangedEvent),
            Create(typeof(LanguageServerDiagnosticsReceivedEvent), "LANGUAGE_SERVER_DIAGNOSTICS_RECEIVED", CodingToolHarnessJsonContext.Default.LanguageServerDiagnosticsReceivedEvent),
            Create(typeof(LanguageServerStatusSnapshotEvent), "LANGUAGE_SERVER_STATUS_SNAPSHOT", CodingToolHarnessJsonContext.Default.LanguageServerStatusSnapshotEvent),
            Create(typeof(DebugTreeStartedEvent), "DEBUG_TREE_STARTED", CodingToolHarnessJsonContext.Default.DebugTreeStartedEvent),
            Create(typeof(DebugExecutionPlannedEvent), "DEBUG_EXECUTION_PLANNED", CodingToolHarnessJsonContext.Default.DebugExecutionPlannedEvent),
            Create(typeof(DebugExecutionActivatingEvent), "DEBUG_EXECUTION_ACTIVATING", CodingToolHarnessJsonContext.Default.DebugExecutionActivatingEvent),
            Create(typeof(DebugHostProcessStartedEvent), "DEBUG_HOST_PROCESS_STARTED", CodingToolHarnessJsonContext.Default.DebugHostProcessStartedEvent),
            Create(typeof(DebugHostReadyEvent), "DEBUG_HOST_READY", CodingToolHarnessJsonContext.Default.DebugHostReadyEvent),
            Create(typeof(DebugHostProcessExitedEvent), "DEBUG_HOST_PROCESS_EXITED", CodingToolHarnessJsonContext.Default.DebugHostProcessExitedEvent),
            Create(typeof(DebugExecutionActivationFailedEvent), "DEBUG_EXECUTION_ACTIVATION_FAILED", CodingToolHarnessJsonContext.Default.DebugExecutionActivationFailedEvent),
            Create(typeof(DebugOwnedResourceCleanupFailedEvent), "DEBUG_OWNED_RESOURCE_CLEANUP_FAILED", CodingToolHarnessJsonContext.Default.DebugOwnedResourceCleanupFailedEvent),
            Create(typeof(DebugTerminalRecordRetainedEvent), "DEBUG_TERMINAL_RECORD_RETAINED", CodingToolHarnessJsonContext.Default.DebugTerminalRecordRetainedEvent),
            Create(typeof(DebugTerminalRecordEvictedEvent), "DEBUG_TERMINAL_RECORD_EVICTED", CodingToolHarnessJsonContext.Default.DebugTerminalRecordEvictedEvent),
            Create(typeof(DebugSessionStateChangedEvent), "DEBUG_SESSION_STATE_CHANGED", CodingToolHarnessJsonContext.Default.DebugSessionStateChangedEvent),
            Create(typeof(DebugSessionExitedEvent), "DEBUG_SESSION_EXITED", CodingToolHarnessJsonContext.Default.DebugSessionExitedEvent),
            Create(typeof(DebugSessionTerminatedEvent), "DEBUG_SESSION_TERMINATED", CodingToolHarnessJsonContext.Default.DebugSessionTerminatedEvent),
            Create(typeof(DebugTreeFaultedEvent), "DEBUG_TREE_FAULTED", CodingToolHarnessJsonContext.Default.DebugTreeFaultedEvent),
            Create(typeof(DebugSessionFailedEvent), "DEBUG_SESSION_FAILED", CodingToolHarnessJsonContext.Default.DebugSessionFailedEvent),
            Create(typeof(DebugTreeTerminatedEvent), "DEBUG_TREE_TERMINATED", CodingToolHarnessJsonContext.Default.DebugTreeTerminatedEvent),
            Create(typeof(DebugTreeCompletedEvent), "DEBUG_TREE_COMPLETED", CodingToolHarnessJsonContext.Default.DebugTreeCompletedEvent),
            Create(typeof(DebugRestartTransitionEvent), "DEBUG_RESTART_TRANSITION", CodingToolHarnessJsonContext.Default.DebugRestartTransitionEvent),
            Create(typeof(DebugChildSessionStartedEvent), "DEBUG_CHILD_SESSION_STARTED", CodingToolHarnessJsonContext.Default.DebugChildSessionStartedEvent),
            Create(typeof(DebugBreakpointChangedEvent), "DEBUG_BREAKPOINT_CHANGED", CodingToolHarnessJsonContext.Default.DebugBreakpointChangedEvent),
            Create(typeof(DebugBreakpointSelectionAppliedEvent), "DEBUG_BREAKPOINT_SELECTION_APPLIED", CodingToolHarnessJsonContext.Default.DebugBreakpointSelectionAppliedEvent),
            Create(typeof(DebugRunInTerminalRequestEvent), "DEBUG_RUN_IN_TERMINAL_REQUEST", CodingToolHarnessJsonContext.Default.DebugRunInTerminalRequestEvent),
            Create(typeof(DebugRunInTerminalResponseEvent), "DEBUG_RUN_IN_TERMINAL_RESPONSE", CodingToolHarnessJsonContext.Default.DebugRunInTerminalResponseEvent),
            Create(typeof(DebugSessionStoppedEvent), "DEBUG_SESSION_STOPPED", CodingToolHarnessJsonContext.Default.DebugSessionStoppedEvent),
            Create(typeof(DebugPrimaryStopAvailableEvent), "DEBUG_PRIMARY_STOP_AVAILABLE", CodingToolHarnessJsonContext.Default.DebugPrimaryStopAvailableEvent),
            Create(typeof(DebugSessionContinuedEvent), "DEBUG_SESSION_CONTINUED", CodingToolHarnessJsonContext.Default.DebugSessionContinuedEvent),
            Create(typeof(DebugExecutionCommandAppliedEvent), "DEBUG_EXECUTION_COMMAND_APPLIED", CodingToolHarnessJsonContext.Default.DebugExecutionCommandAppliedEvent),
            Create(typeof(DebugStateMutationAppliedEvent), "DEBUG_STATE_MUTATION_APPLIED", CodingToolHarnessJsonContext.Default.DebugStateMutationAppliedEvent),
            Create(typeof(DebugProcessChangedEvent), "DEBUG_PROCESS_CHANGED", CodingToolHarnessJsonContext.Default.DebugProcessChangedEvent),
            Create(typeof(DebugThreadChangedEvent), "DEBUG_THREAD_CHANGED", CodingToolHarnessJsonContext.Default.DebugThreadChangedEvent),
            Create(typeof(DebugModuleChangedEvent), "DEBUG_MODULE_CHANGED", CodingToolHarnessJsonContext.Default.DebugModuleChangedEvent),
            Create(typeof(DebugLoadedSourceChangedEvent), "DEBUG_LOADED_SOURCE_CHANGED", CodingToolHarnessJsonContext.Default.DebugLoadedSourceChangedEvent),
            Create(typeof(DebugCapabilitiesChangedEvent), "DEBUG_CAPABILITIES_CHANGED", CodingToolHarnessJsonContext.Default.DebugCapabilitiesChangedEvent),
            Create(typeof(DebugStateInvalidatedEvent), "DEBUG_STATE_INVALIDATED", CodingToolHarnessJsonContext.Default.DebugStateInvalidatedEvent),
            Create(typeof(DebugMemoryChangedEvent), "DEBUG_MEMORY_CHANGED", CodingToolHarnessJsonContext.Default.DebugMemoryChangedEvent),
            Create(typeof(DebugOutputAvailableEvent), "DEBUG_OUTPUT_AVAILABLE", CodingToolHarnessJsonContext.Default.DebugOutputAvailableEvent),
            Create(typeof(DebugProgressStartedEvent), "DEBUG_PROGRESS_STARTED", CodingToolHarnessJsonContext.Default.DebugProgressStartedEvent),
            Create(typeof(DebugProgressUpdatedEvent), "DEBUG_PROGRESS_UPDATED", CodingToolHarnessJsonContext.Default.DebugProgressUpdatedEvent),
            Create(typeof(DebugProgressCompletedEvent), "DEBUG_PROGRESS_COMPLETED", CodingToolHarnessJsonContext.Default.DebugProgressCompletedEvent)
        ])
    };

    private static AgentEventDescriptor Create(
        Type type,
        string discriminator,
        JsonTypeInfo typeInfo,
        AgentEventDurability durability = AgentEventDurability.Durable) => new()
    {
        EventType = type,
        Discriminator = discriminator,
        JsonTypeInfo = typeInfo,
        Durability = durability,
        ModuleId = "hpd.agent.harness.coding"
    };
}
