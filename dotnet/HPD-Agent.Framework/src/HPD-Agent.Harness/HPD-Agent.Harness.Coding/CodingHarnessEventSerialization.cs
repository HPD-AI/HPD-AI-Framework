using System.Runtime.CompilerServices;
using HPD.Agent.Serialization;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPDOS.ToolHarnesses.Middleware;

public static class CodingHarnessEventSerialization
{
    [ModuleInitializer]
    public static void RegisterEvents()
    {
        AgentEventSerializer.RegisterEventType(
            typeof(ExecuteCommandProcessStartedEvent),
            "EXECUTE_COMMAND_PROCESS_STARTED",
            CodingToolHarnessJsonContext.Default.ExecuteCommandProcessStartedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(ExecuteCommandOutputChunkEvent),
            "EXECUTE_COMMAND_OUTPUT_CHUNK",
            CodingToolHarnessJsonContext.Default.ExecuteCommandOutputChunkEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(ExecuteCommandProgressEvent),
            "EXECUTE_COMMAND_PROGRESS",
            CodingToolHarnessJsonContext.Default.ExecuteCommandProgressEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(ExecuteCommandProcessExitedEvent),
            "EXECUTE_COMMAND_PROCESS_EXITED",
            CodingToolHarnessJsonContext.Default.ExecuteCommandProcessExitedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(ExecuteCommandAutoBackgroundedEvent),
            "EXECUTE_COMMAND_AUTO_BACKGROUNDED",
            CodingToolHarnessJsonContext.Default.ExecuteCommandAutoBackgroundedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(ExecuteCommandPermissionRequestEvent),
            "EXECUTE_COMMAND_PERMISSION_REQUEST",
            CodingToolHarnessJsonContext.Default.ExecuteCommandPermissionRequestEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(ExecuteCommandPermissionResponseEvent),
            "EXECUTE_COMMAND_PERMISSION_RESPONSE",
            CodingToolHarnessJsonContext.Default.ExecuteCommandPermissionResponseEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(ExecuteCommandSandboxCapabilityRequestEvent),
            "EXECUTE_COMMAND_SANDBOX_CAPABILITY_REQUEST",
            CodingToolHarnessJsonContext.Default.ExecuteCommandSandboxCapabilityRequestEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(ExecuteCommandSandboxCapabilityResponseEvent),
            "EXECUTE_COMMAND_SANDBOX_CAPABILITY_RESPONSE",
            CodingToolHarnessJsonContext.Default.ExecuteCommandSandboxCapabilityResponseEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(ExecuteCommandPermissionRulePersistedEvent),
            "EXECUTE_COMMAND_PERMISSION_RULE_PERSISTED",
            CodingToolHarnessJsonContext.Default.ExecuteCommandPermissionRulePersistedEvent);

        AgentEventSerializer.RegisterEventType(
            typeof(FileEditAppliedEvent),
            "FILE_EDIT_APPLIED",
            CodingToolHarnessJsonContext.Default.FileEditAppliedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(FileWriteAppliedEvent),
            "FILE_WRITE_APPLIED",
            CodingToolHarnessJsonContext.Default.FileWriteAppliedEvent);

        AgentEventSerializer.RegisterEventType(
            typeof(LanguageServerDocumentOpenedEvent),
            "LANGUAGE_SERVER_DOCUMENT_OPENED",
            CodingToolHarnessJsonContext.Default.LanguageServerDocumentOpenedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(LanguageServerDocumentChangedEvent),
            "LANGUAGE_SERVER_DOCUMENT_CHANGED",
            CodingToolHarnessJsonContext.Default.LanguageServerDocumentChangedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(LanguageServerDocumentClosedEvent),
            "LANGUAGE_SERVER_DOCUMENT_CLOSED",
            CodingToolHarnessJsonContext.Default.LanguageServerDocumentClosedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(LanguageServerDocumentSavedEvent),
            "LANGUAGE_SERVER_DOCUMENT_SAVED",
            CodingToolHarnessJsonContext.Default.LanguageServerDocumentSavedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(LanguageServerWatchedFileChangedEvent),
            "LANGUAGE_SERVER_WATCHED_FILE_CHANGED",
            CodingToolHarnessJsonContext.Default.LanguageServerWatchedFileChangedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(LanguageServerDiagnosticsReceivedEvent),
            "LANGUAGE_SERVER_DIAGNOSTICS_RECEIVED",
            CodingToolHarnessJsonContext.Default.LanguageServerDiagnosticsReceivedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(LanguageServerStatusSnapshotEvent),
            "LANGUAGE_SERVER_STATUS_SNAPSHOT",
            CodingToolHarnessJsonContext.Default.LanguageServerStatusSnapshotEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugTreeStartedEvent), "DEBUG_TREE_STARTED", CodingToolHarnessJsonContext.Default.DebugTreeStartedEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugSessionStateChangedEvent), "DEBUG_SESSION_STATE_CHANGED", CodingToolHarnessJsonContext.Default.DebugSessionStateChangedEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugSessionExitedEvent), "DEBUG_SESSION_EXITED", CodingToolHarnessJsonContext.Default.DebugSessionExitedEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugSessionTerminatedEvent), "DEBUG_SESSION_TERMINATED", CodingToolHarnessJsonContext.Default.DebugSessionTerminatedEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugTreeFaultedEvent), "DEBUG_TREE_FAULTED", CodingToolHarnessJsonContext.Default.DebugTreeFaultedEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugSessionFailedEvent), "DEBUG_SESSION_FAILED", CodingToolHarnessJsonContext.Default.DebugSessionFailedEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugTreeTerminatedEvent), "DEBUG_TREE_TERMINATED", CodingToolHarnessJsonContext.Default.DebugTreeTerminatedEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugSessionSummaryEvent), "DEBUG_SESSION_SUMMARY", CodingToolHarnessJsonContext.Default.DebugSessionSummaryEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugRestartTransitionEvent), "DEBUG_RESTART_TRANSITION", CodingToolHarnessJsonContext.Default.DebugRestartTransitionEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugChildSessionStartedEvent), "DEBUG_CHILD_SESSION_STARTED", CodingToolHarnessJsonContext.Default.DebugChildSessionStartedEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugBreakpointChangedEvent), "DEBUG_BREAKPOINT_CHANGED", CodingToolHarnessJsonContext.Default.DebugBreakpointChangedEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugRunInTerminalRequestEvent), "DEBUG_RUN_IN_TERMINAL_REQUEST", CodingToolHarnessJsonContext.Default.DebugRunInTerminalRequestEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugRunInTerminalResponseEvent), "DEBUG_RUN_IN_TERMINAL_RESPONSE", CodingToolHarnessJsonContext.Default.DebugRunInTerminalResponseEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugSessionStoppedEvent), "DEBUG_SESSION_STOPPED", CodingToolHarnessJsonContext.Default.DebugSessionStoppedEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugSessionContinuedEvent), "DEBUG_SESSION_CONTINUED", CodingToolHarnessJsonContext.Default.DebugSessionContinuedEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugProcessChangedEvent), "DEBUG_PROCESS_CHANGED", CodingToolHarnessJsonContext.Default.DebugProcessChangedEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugThreadChangedEvent), "DEBUG_THREAD_CHANGED", CodingToolHarnessJsonContext.Default.DebugThreadChangedEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugModuleChangedEvent), "DEBUG_MODULE_CHANGED", CodingToolHarnessJsonContext.Default.DebugModuleChangedEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugLoadedSourceChangedEvent), "DEBUG_LOADED_SOURCE_CHANGED", CodingToolHarnessJsonContext.Default.DebugLoadedSourceChangedEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugCapabilitiesChangedEvent), "DEBUG_CAPABILITIES_CHANGED", CodingToolHarnessJsonContext.Default.DebugCapabilitiesChangedEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugStateInvalidatedEvent), "DEBUG_STATE_INVALIDATED", CodingToolHarnessJsonContext.Default.DebugStateInvalidatedEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugMemoryChangedEvent), "DEBUG_MEMORY_CHANGED", CodingToolHarnessJsonContext.Default.DebugMemoryChangedEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugOutputAvailableEvent), "DEBUG_OUTPUT_AVAILABLE", CodingToolHarnessJsonContext.Default.DebugOutputAvailableEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugProgressStartedEvent), "DEBUG_PROGRESS_STARTED", CodingToolHarnessJsonContext.Default.DebugProgressStartedEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugProgressUpdatedEvent), "DEBUG_PROGRESS_UPDATED", CodingToolHarnessJsonContext.Default.DebugProgressUpdatedEvent);
        AgentEventSerializer.RegisterEventType(typeof(DebugProgressCompletedEvent), "DEBUG_PROGRESS_COMPLETED", CodingToolHarnessJsonContext.Default.DebugProgressCompletedEvent);
    }
}
