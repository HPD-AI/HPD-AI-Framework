using System.Runtime.CompilerServices;
using HPD.Agent.Serialization;
using HPDOS.Harneses.Middleware;

internal static class CodingHarnessEventSerialization
{
    [ModuleInitializer]
    internal static void RegisterEvents()
    {
        AgentEventSerializer.RegisterEventType(
            typeof(ExecuteCommandProcessStartedEvent),
            "EXECUTE_COMMAND_PROCESS_STARTED",
            CodingHarnessJsonContext.Default.ExecuteCommandProcessStartedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(ExecuteCommandOutputChunkEvent),
            "EXECUTE_COMMAND_OUTPUT_CHUNK",
            CodingHarnessJsonContext.Default.ExecuteCommandOutputChunkEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(ExecuteCommandProgressEvent),
            "EXECUTE_COMMAND_PROGRESS",
            CodingHarnessJsonContext.Default.ExecuteCommandProgressEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(ExecuteCommandProcessExitedEvent),
            "EXECUTE_COMMAND_PROCESS_EXITED",
            CodingHarnessJsonContext.Default.ExecuteCommandProcessExitedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(ExecuteCommandAutoBackgroundedEvent),
            "EXECUTE_COMMAND_AUTO_BACKGROUNDED",
            CodingHarnessJsonContext.Default.ExecuteCommandAutoBackgroundedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(ExecuteCommandBackgroundListEvent),
            "EXECUTE_COMMAND_BACKGROUND_LIST",
            CodingHarnessJsonContext.Default.ExecuteCommandBackgroundListEvent);

        AgentEventSerializer.RegisterEventType(
            typeof(FileEditAppliedEvent),
            "FILE_EDIT_APPLIED",
            CodingHarnessJsonContext.Default.FileEditAppliedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(FileWriteAppliedEvent),
            "FILE_WRITE_APPLIED",
            CodingHarnessJsonContext.Default.FileWriteAppliedEvent);

        AgentEventSerializer.RegisterEventType(
            typeof(LanguageServerDocumentOpenedEvent),
            "LANGUAGE_SERVER_DOCUMENT_OPENED",
            CodingHarnessJsonContext.Default.LanguageServerDocumentOpenedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(LanguageServerDocumentChangedEvent),
            "LANGUAGE_SERVER_DOCUMENT_CHANGED",
            CodingHarnessJsonContext.Default.LanguageServerDocumentChangedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(LanguageServerDocumentClosedEvent),
            "LANGUAGE_SERVER_DOCUMENT_CLOSED",
            CodingHarnessJsonContext.Default.LanguageServerDocumentClosedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(LanguageServerDocumentSavedEvent),
            "LANGUAGE_SERVER_DOCUMENT_SAVED",
            CodingHarnessJsonContext.Default.LanguageServerDocumentSavedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(LanguageServerWatchedFileChangedEvent),
            "LANGUAGE_SERVER_WATCHED_FILE_CHANGED",
            CodingHarnessJsonContext.Default.LanguageServerWatchedFileChangedEvent);
        AgentEventSerializer.RegisterEventType(
            typeof(LanguageServerDiagnosticsReceivedEvent),
            "LANGUAGE_SERVER_DIAGNOSTICS_RECEIVED",
            CodingHarnessJsonContext.Default.LanguageServerDiagnosticsReceivedEvent);
    }
}
