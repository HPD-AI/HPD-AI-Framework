using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent;
using HPD.Events;
using HPDOS.Harneses.Middleware;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(AgentEvent))]

// ExecuteCommand events
[JsonSerializable(typeof(ExecuteCommandEvent))]
[JsonSerializable(typeof(ExecuteCommandProcessStartedEvent))]
[JsonSerializable(typeof(ExecuteCommandOutputChunkEvent))]
[JsonSerializable(typeof(ExecuteCommandProgressEvent))]
[JsonSerializable(typeof(ExecuteCommandProcessExitedEvent))]
[JsonSerializable(typeof(ExecuteCommandAutoBackgroundedEvent))]
[JsonSerializable(typeof(ExecuteCommandBackgroundListEvent))]
[JsonSerializable(typeof(ExecuteCommandCategory))]
[JsonSerializable(typeof(ExecuteCommandStreamKind))]
[JsonSerializable(typeof(ExecuteCommandCompletionKind))]

// File mutation events
[JsonSerializable(typeof(FileMutationAppliedEvent))]
[JsonSerializable(typeof(FileEditAppliedEvent))]
[JsonSerializable(typeof(FileWriteAppliedEvent))]
[JsonSerializable(typeof(FileWriteMode))]
[JsonSerializable(typeof(CodingFileMutationKind))]
[JsonSerializable(typeof(FileMutationSnapshot))]
[JsonSerializable(typeof(FileMutationTextEdit))]
[JsonSerializable(typeof(FileMutationRange))]
[JsonSerializable(typeof(FileMutationHunk))]
[JsonSerializable(typeof(FileMutationDiffStat))]
[JsonSerializable(typeof(FileMutationNote))]
[JsonSerializable(typeof(FileEditAppliedReplacement))]
[JsonSerializable(typeof(FileEditNormalizationNote))]
[JsonSerializable(typeof(IReadOnlyList<FileMutationTextEdit>), TypeInfoPropertyName = "FileMutationTextEditReadOnlyList")]
[JsonSerializable(typeof(List<FileMutationTextEdit>), TypeInfoPropertyName = "FileMutationTextEditList")]
[JsonSerializable(typeof(IReadOnlyList<FileMutationHunk>), TypeInfoPropertyName = "FileMutationHunkReadOnlyList")]
[JsonSerializable(typeof(List<FileMutationHunk>), TypeInfoPropertyName = "FileMutationHunkList")]
[JsonSerializable(typeof(IReadOnlyList<FileMutationNote>), TypeInfoPropertyName = "FileMutationNoteReadOnlyList")]
[JsonSerializable(typeof(List<FileMutationNote>), TypeInfoPropertyName = "FileMutationNoteList")]
[JsonSerializable(typeof(IReadOnlyList<FileEditAppliedReplacement>), TypeInfoPropertyName = "FileEditAppliedReplacementReadOnlyList")]
[JsonSerializable(typeof(List<FileEditAppliedReplacement>), TypeInfoPropertyName = "FileEditAppliedReplacementList")]
[JsonSerializable(typeof(IReadOnlyList<FileEditNormalizationNote>), TypeInfoPropertyName = "FileEditNormalizationNoteReadOnlyList")]
[JsonSerializable(typeof(List<FileEditNormalizationNote>), TypeInfoPropertyName = "FileEditNormalizationNoteList")]
[JsonSerializable(typeof(IReadOnlyList<FileMutationRange>), TypeInfoPropertyName = "FileMutationRangeReadOnlyList")]
[JsonSerializable(typeof(List<FileMutationRange>), TypeInfoPropertyName = "FileMutationRangeList")]
[JsonSerializable(typeof(IReadOnlyList<string>), TypeInfoPropertyName = "StringReadOnlyList")]
[JsonSerializable(typeof(List<string>), TypeInfoPropertyName = "StringList")]

// Language server events
[JsonSerializable(typeof(LanguageServerEvent))]
[JsonSerializable(typeof(LanguageServerDocumentOpenedEvent))]
[JsonSerializable(typeof(LanguageServerDocumentChangedEvent))]
[JsonSerializable(typeof(LanguageServerDocumentClosedEvent))]
[JsonSerializable(typeof(LanguageServerDocumentSavedEvent))]
[JsonSerializable(typeof(LanguageServerWatchedFileChangedEvent))]
[JsonSerializable(typeof(LanguageServerDiagnosticsReceivedEvent))]
[JsonSerializable(typeof(LanguageServerWatchedFileChangeKind))]

// Common event fields
[JsonSerializable(typeof(EventChannel))]
[JsonSerializable(typeof(EventKind))]
[JsonSerializable(typeof(EventDirection))]
[JsonSerializable(typeof(DateTimeOffset))]
public partial class CodingHarnessJsonContext : JsonSerializerContext
{
}
