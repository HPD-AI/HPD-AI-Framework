using System.ComponentModel;
using System.Text.Json.Serialization;

/// <summary>Closed model-facing debugger operation.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "action")]
[JsonDerivedType(typeof(LaunchDebugOperation), "launch")]
[JsonDerivedType(typeof(AttachDebugOperation), "attach")]
[JsonDerivedType(typeof(ListDebugSessionsOperation), "listSessions")]
[JsonDerivedType(typeof(GetDebugStatusOperation), "getStatus")]
[JsonDerivedType(typeof(GetDebugHealthOperation), "getHealth")]
[JsonDerivedType(typeof(SnapshotDebugOperation), "snapshot")]
[JsonDerivedType(typeof(InspectDebugStopOperation), "inspectStop")]
[JsonDerivedType(typeof(DisconnectDebugOperation), "disconnect")]
[JsonDerivedType(typeof(TerminateDebugOperation), "terminate")]
[JsonDerivedType(typeof(RestartDebugOperation), "restart")]
[JsonDerivedType(typeof(SetSourceBreakpointsOperation), "setSourceBreakpoints")]
[JsonDerivedType(typeof(SetFunctionBreakpointsOperation), "setFunctionBreakpoints")]
[JsonDerivedType(typeof(SetExceptionBreakpointsOperation), "setExceptionBreakpoints")]
[JsonDerivedType(typeof(SetInstructionBreakpointsOperation), "setInstructionBreakpoints")]
[JsonDerivedType(typeof(DiscoverDataBreakpointOperation), "discoverDataBreakpoint")]
[JsonDerivedType(typeof(SetDataBreakpointsOperation), "setDataBreakpoints")]
[JsonDerivedType(typeof(GetDebugBreakpointsOperation), "getBreakpoints")]
[JsonDerivedType(typeof(GetBreakpointLocationsOperation), "getBreakpointLocations")]
[JsonDerivedType(typeof(ContinueDebugOperation), "continue")]
[JsonDerivedType(typeof(PauseDebugOperation), "pause")]
[JsonDerivedType(typeof(StepOverDebugOperation), "stepOver")]
[JsonDerivedType(typeof(StepInDebugOperation), "stepIn")]
[JsonDerivedType(typeof(StepOutDebugOperation), "stepOut")]
[JsonDerivedType(typeof(StepBackDebugOperation), "stepBack")]
[JsonDerivedType(typeof(ReverseContinueDebugOperation), "reverseContinue")]
[JsonDerivedType(typeof(RestartFrameDebugOperation), "restartFrame")]
[JsonDerivedType(typeof(GotoDebugOperation), "goto")]
[JsonDerivedType(typeof(TerminateThreadsDebugOperation), "terminateThreads")]
[JsonDerivedType(typeof(GetThreadsOperation), "getThreads")]
[JsonDerivedType(typeof(GetStackTraceOperation), "getStackTrace")]
[JsonDerivedType(typeof(GetScopesOperation), "getScopes")]
[JsonDerivedType(typeof(GetVariablesOperation), "getVariables")]
[JsonDerivedType(typeof(EvaluateDebugOperation), "evaluate")]
[JsonDerivedType(typeof(GetExceptionInfoOperation), "getExceptionInfo")]
[JsonDerivedType(typeof(GetModulesOperation), "getModules")]
[JsonDerivedType(typeof(GetLoadedSourcesOperation), "getLoadedSources")]
[JsonDerivedType(typeof(GetSourceOperation), "getSource")]
[JsonDerivedType(typeof(GetStepInTargetsOperation), "getStepInTargets")]
[JsonDerivedType(typeof(GetGotoTargetsOperation), "getGotoTargets")]
[JsonDerivedType(typeof(GetCompletionsOperation), "getCompletions")]
[JsonDerivedType(typeof(ResolveDebugLocationOperation), "resolveLocation")]
[JsonDerivedType(typeof(SetDebugVariableOperation), "setVariable")]
[JsonDerivedType(typeof(SetDebugExpressionOperation), "setExpression")]
[JsonDerivedType(typeof(ReadDebugMemoryOperation), "readMemory")]
[JsonDerivedType(typeof(WriteDebugMemoryOperation), "writeMemory")]
[JsonDerivedType(typeof(DisassembleDebugOperation), "disassemble")]
[JsonDerivedType(typeof(GetDebugOutputOperation), "getOutput")]
[JsonDerivedType(typeof(PersistDebugOutputOperation), "persistOutput")]
[JsonDerivedType(typeof(CancelDebugProgressOperation), "cancelProgress")]
public abstract record DebugOperation : HPD.Agent.Permissions.IActionScopedPermission
{
    string HPD.Agent.Permissions.IActionScopedPermission.PermissionScope =>
        HPD.Agent.ToolHarness.Coding.Debugging.DebugOperationDispatcher.Action(this);
}

public abstract record DebugTreeOperation(
    [property: Description("Debug tree id returned by launch or attach.")] string DebugTreeId,
    [property: Description("Optional protocol session id inside the tree.")] string? DebugSessionId = null)
    : DebugOperation;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "targetKind")]
[JsonDerivedType(typeof(SourceFileDebugLaunchTarget), "sourceFile")]
[JsonDerivedType(typeof(ProjectDirectoryDebugLaunchTarget), "projectDirectory")]
[JsonDerivedType(typeof(ExecutableDebugLaunchTarget), "executable")]
public abstract record DebugLaunchTarget;

public sealed record SourceFileDebugLaunchTarget(string Path) : DebugLaunchTarget;
public sealed record ProjectDirectoryDebugLaunchTarget(string Path) : DebugLaunchTarget;
public sealed record ExecutableDebugLaunchTarget(string Path) : DebugLaunchTarget;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "targetKind")]
[JsonDerivedType(typeof(ProcessDebugAttachTarget), "process")]
[JsonDerivedType(typeof(EndpointDebugAttachTarget), "endpoint")]
public abstract record DebugAttachTarget;

public sealed record ProcessDebugAttachTarget(int ProcessId) : DebugAttachTarget;
public sealed record EndpointDebugAttachTarget(string EndpointId) : DebugAttachTarget;

public sealed record LaunchDebugOperation(
    DebugLaunchTarget Target,
    string? AdapterId = null,
    string? WorkspacePath = null,
    string? Language = null,
    IReadOnlyList<string>? Arguments = null,
    bool StopOnEntry = false,
    DebugInitialConfigurationInput? InitialConfiguration = null) : DebugOperation;

public sealed record AttachDebugOperation(
    DebugAttachTarget Target,
    string? AdapterId = null,
    string? WorkspacePath = null,
    string? Language = null,
    DebugInitialConfigurationInput? InitialConfiguration = null) : DebugOperation;

public sealed record ListDebugSessionsOperation : DebugOperation;
public sealed record GetDebugStatusOperation(string DebugTreeId, string? DebugSessionId = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record GetDebugHealthOperation(string DebugTreeId, string? DebugSessionId = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record SnapshotDebugOperation(string DebugTreeId, string? DebugSessionId = null, int MaximumOutputBytes = 4096) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record InspectDebugStopOperation(
    string DebugTreeId, string? DebugSessionId = null, int? ThreadId = null,
    int MaximumFrames = 10, bool IncludeScopes = true, bool IncludeVariables = true,
    int MaximumVariablesPerScope = 30, int MaximumOutputBytes = 4096)
    : DebugTreeOperation(DebugTreeId, DebugSessionId);

public enum DebugDisconnectMode { Detach, TerminateDebuggee, SuspendDebuggee }
public enum DebugTerminationTarget { Tree, Session, Debuggee }
public sealed record DisconnectDebugOperation(string DebugTreeId, string? DebugSessionId = null, DebugDisconnectMode Mode = DebugDisconnectMode.Detach) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record TerminateDebugOperation(string DebugTreeId, string? DebugSessionId = null, DebugTerminationTarget Target = DebugTerminationTarget.Tree) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record RestartDebugOperation(string DebugTreeId, string? DebugSessionId = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);

public sealed record DebugInitialConfigurationInput(
    IReadOnlyList<DebugSourceBreakpointInput>? SourceBreakpoints = null,
    IReadOnlyList<DebugFunctionBreakpointInput>? FunctionBreakpoints = null,
    IReadOnlyList<DebugExceptionBreakpointInput>? ExceptionBreakpoints = null);

public sealed record DebugSourceBreakpointInput(string Path, int Line, int? Column = null, string? Condition = null, string? HitCondition = null, string? LogMessage = null);
public sealed record DebugFunctionBreakpointInput(string Name, string? Condition = null, string? HitCondition = null);
public sealed record DebugExceptionBreakpointInput(string FilterId, string? Condition = null);
public sealed record DebugInstructionBreakpointInput(string InstructionReferenceToken, long? Offset = null, string? Condition = null, string? HitCondition = null);
public enum DebugDataBreakpointAccessType { Read, Write, ReadWrite }
public sealed record DebugDataBreakpointInput(string DataBreakpointToken, DebugDataBreakpointAccessType? AccessType = null, string? Condition = null, string? HitCondition = null);

public sealed record SetSourceBreakpointsOperation(string DebugTreeId, IReadOnlyList<DebugSourceBreakpointInput> Breakpoints, string? DebugSessionId = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record SetFunctionBreakpointsOperation(string DebugTreeId, IReadOnlyList<DebugFunctionBreakpointInput> Breakpoints, string? DebugSessionId = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record SetExceptionBreakpointsOperation(string DebugTreeId, IReadOnlyList<DebugExceptionBreakpointInput> Breakpoints, string? DebugSessionId = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record SetInstructionBreakpointsOperation(string DebugTreeId, IReadOnlyList<DebugInstructionBreakpointInput> Breakpoints, string? DebugSessionId = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record DiscoverDataBreakpointOperation(
    string DebugTreeId, string Name, string? DebugSessionId = null,
    string? VariablesToken = null, string? FrameToken = null, long? Bytes = null,
    bool? AsAddress = null, string? Mode = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record SetDataBreakpointsOperation(string DebugTreeId, IReadOnlyList<DebugDataBreakpointInput> Breakpoints, string? DebugSessionId = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record GetDebugBreakpointsOperation(string DebugTreeId, string? DebugSessionId = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record GetBreakpointLocationsOperation(
    string DebugTreeId, string SourceToken, int StartLine, string? DebugSessionId = null,
    int? StartColumn = null, int? EndLine = null, int? EndColumn = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);

public enum DebugStepGranularity { Statement, Line, Instruction }
public sealed record ContinueDebugOperation(string DebugTreeId, int ThreadId, string? DebugSessionId = null, bool SingleThread = false, int WaitTimeoutMilliseconds = 30_000) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record PauseDebugOperation(string DebugTreeId, int ThreadId, string? DebugSessionId = null, int WaitTimeoutMilliseconds = 30_000) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record StepOverDebugOperation(string DebugTreeId, int ThreadId, string? DebugSessionId = null, DebugStepGranularity Granularity = DebugStepGranularity.Statement, bool SingleThread = false, int WaitTimeoutMilliseconds = 30_000) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record StepInDebugOperation(string DebugTreeId, int ThreadId, string? DebugSessionId = null, string? TargetToken = null, DebugStepGranularity Granularity = DebugStepGranularity.Statement, bool SingleThread = false, int WaitTimeoutMilliseconds = 30_000) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record StepOutDebugOperation(string DebugTreeId, int ThreadId, string? DebugSessionId = null, DebugStepGranularity Granularity = DebugStepGranularity.Statement, bool SingleThread = false, int WaitTimeoutMilliseconds = 30_000) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record StepBackDebugOperation(string DebugTreeId, int ThreadId, string? DebugSessionId = null, DebugStepGranularity Granularity = DebugStepGranularity.Statement, bool SingleThread = false, int WaitTimeoutMilliseconds = 30_000) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record ReverseContinueDebugOperation(string DebugTreeId, int ThreadId, string? DebugSessionId = null, bool SingleThread = false, int WaitTimeoutMilliseconds = 30_000) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record RestartFrameDebugOperation(string DebugTreeId, string FrameToken, string? DebugSessionId = null, int WaitTimeoutMilliseconds = 30_000) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record GotoDebugOperation(string DebugTreeId, int ThreadId, string TargetToken, string? DebugSessionId = null, int WaitTimeoutMilliseconds = 30_000) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record TerminateThreadsDebugOperation(string DebugTreeId, IReadOnlyList<int> ThreadIds, string? DebugSessionId = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);

public sealed record GetThreadsOperation(string DebugTreeId, string? DebugSessionId = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record GetStackTraceOperation(string DebugTreeId, int ThreadId, string? DebugSessionId = null, int Levels = 20, string? ContinuationToken = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record GetScopesOperation(string DebugTreeId, string FrameToken, string? DebugSessionId = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public enum DebugVariableFilter { Indexed, Named }
public sealed record GetVariablesOperation(string DebugTreeId, string VariablesToken, string? DebugSessionId = null, DebugVariableFilter? Filter = null, int Count = 100, string? ContinuationToken = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public enum DebugEvaluationContext { Repl, Watch, Hover, Clipboard, Variables }
public sealed record EvaluateDebugOperation(string DebugTreeId, string Expression, string? DebugSessionId = null, string? FrameToken = null, DebugEvaluationContext Context = DebugEvaluationContext.Repl) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record GetExceptionInfoOperation(string DebugTreeId, int ThreadId, string? DebugSessionId = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record GetModulesOperation(string DebugTreeId, string? DebugSessionId = null, int Count = 100, string? ContinuationToken = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record GetLoadedSourcesOperation(string DebugTreeId, string? DebugSessionId = null, int Count = 100, string? ContinuationToken = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record GetSourceOperation(string DebugTreeId, string SourceToken, string? DebugSessionId = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record GetStepInTargetsOperation(string DebugTreeId, string FrameToken, string? DebugSessionId = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record GetGotoTargetsOperation(string DebugTreeId, int ThreadId, string SourceToken, int Line, string? DebugSessionId = null, int? Column = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record GetCompletionsOperation(string DebugTreeId, string Text, int Column, string? DebugSessionId = null, int? Line = null, string? FrameToken = null, int Count = 100, string? ContinuationToken = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record ResolveDebugLocationOperation(string DebugTreeId, string LocationToken, string? DebugSessionId = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);

public sealed record SetDebugVariableOperation(string DebugTreeId, string VariablesToken, string Name, string Value, string? DebugSessionId = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record SetDebugExpressionOperation(string DebugTreeId, string Expression, string Value, string? DebugSessionId = null, string? FrameToken = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record ReadDebugMemoryOperation(string DebugTreeId, string MemoryToken, int Count, string? DebugSessionId = null, long Offset = 0) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record WriteDebugMemoryOperation(string DebugTreeId, string MemoryToken, string Base64Data, string? DebugSessionId = null, long Offset = 0, bool AllowPartial = false) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record DisassembleDebugOperation(string DebugTreeId, string MemoryToken, int InstructionCount = 100, string? DebugSessionId = null, long ByteOffset = 0, long InstructionOffset = 0, bool ResolveSymbols = false, string? ContinuationToken = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);

public enum DebugOutputFilter { Console, Stdout, Stderr, Important }
public sealed record GetDebugOutputOperation(string DebugTreeId, string? DebugSessionId = null, long? AfterSequence = null, int MaximumRecords = 100, int MaximumBytes = 16 * 1024, IReadOnlyList<DebugOutputFilter>? Categories = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record PersistDebugOutputOperation(string DebugTreeId, string? DebugSessionId = null, long? FromSequence = null, long? ToSequence = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
public sealed record CancelDebugProgressOperation(string DebugTreeId, string ProgressId, string? DebugSessionId = null) : DebugTreeOperation(DebugTreeId, DebugSessionId);
