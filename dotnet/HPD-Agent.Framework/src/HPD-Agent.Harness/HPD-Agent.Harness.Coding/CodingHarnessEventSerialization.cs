using System.Runtime.CompilerServices;
using HPD.Agent.Serialization;
using HPDOS.ToolHarnesses.Middleware;

internal static class CodingToolHarnessEventSerialization
{
    [ModuleInitializer]
    internal static void RegisterEvents()
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
            typeof(ExecuteCommandBackgroundListEvent),
            "EXECUTE_COMMAND_BACKGROUND_LIST",
            CodingToolHarnessJsonContext.Default.ExecuteCommandBackgroundListEvent);

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
    }
}
