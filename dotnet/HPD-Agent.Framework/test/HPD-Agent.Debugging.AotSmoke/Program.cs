using System.Text;
using System.Text.Json;
using System.Reflection;
using HPD.Agent;
using HPD.Agent.Serialization;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;
using HPDOS.ToolHarnesses.Middleware;

var applicationIdentity = Assembly.GetExecutingAssembly().GetName().Name!;
if (!AgentEventCompositionHost.TryGetApplication(applicationIdentity, out var eventComposition))
    return 8;
var eventCodec = eventComposition.Codec;

var functions = CodingToolHarnessRegistration.CreateToolHarness(
    new CodingToolHarness(),
    serialization: new HPDToolSerializationOptions(CodingToolHarnessJsonContext.Default.Options));
var debugFunctions = functions.Where(function =>
    string.Equals(function.Name, "Debug", StringComparison.Ordinal)).ToArray();
if (debugFunctions.Length != 1)
    return 10;
var debugSchema = debugFunctions[0].JsonSchema;
var debugBranches = debugSchema.GetProperty("properties")
    .GetProperty("request")
    .GetProperty("oneOf");
if (debugBranches.GetArrayLength() != 49)
    return 11;
var debugActions = debugBranches.EnumerateArray()
    .Select(branch => branch.GetProperty("properties").GetProperty("action").GetProperty("const").GetString())
    .ToHashSet(StringComparer.Ordinal);
if (!debugActions.Contains("launch") ||
    !debugActions.Contains("inspectStop") ||
    !debugActions.Contains("writeMemory") ||
    !debugActions.Contains("persistOutput"))
    return 12;

var boundLaunch = JsonSerializer.Deserialize(
    """
    {"action":"launch","target":{"targetKind":"sourceFile","path":"/workspace/app.py"},"stopOnEntry":true}
    """,
    CodingToolHarnessJsonContext.Default.DebugOperation);
var boundInspect = JsonSerializer.Deserialize(
    """{"action":"inspectStop","debugTreeId":"tree_1","maxFrames":5}""",
    CodingToolHarnessJsonContext.Default.DebugOperation);
var boundWrite = JsonSerializer.Deserialize(
    """{"action":"writeMemory","debugTreeId":"tree_1","memoryToken":"memory_1","base64Data":"AA=="}""",
    CodingToolHarnessJsonContext.Default.DebugOperation);
var boundApplication = JsonSerializer.Deserialize(
    """
    {"action":"launch","target":{"targetKind":"applicationProject","path":"/workspace/App","targetFramework":"net10.0","arguments":["one"]}}
    """,
    CodingToolHarnessJsonContext.Default.DebugOperation);
var boundExecutable = JsonSerializer.Deserialize(
    """
    {"action":"launch","target":{"targetKind":"executable","path":"/workspace/App.dll","arguments":["two"]}}
    """,
    CodingToolHarnessJsonContext.Default.DebugOperation);
var boundTest = JsonSerializer.Deserialize(
    """
    {"action":"launch","target":{"targetKind":"test","path":"/workspace/App.Tests","framework":1,"filter":"Name=One"}}
    """,
    CodingToolHarnessJsonContext.Default.DebugOperation);
var boundBasicStep = JsonSerializer.Deserialize(
    """{"action":"stepOver","debugTreeId":"tree_1"}""",
    CodingToolHarnessJsonContext.Default.DebugOperation);
if (boundLaunch is not LaunchDebugOperation ||
    boundApplication is not LaunchDebugOperation
        { Target: ApplicationProjectDebugTarget } ||
    boundExecutable is not LaunchDebugOperation
        { Target: ExecutableDebugTarget } ||
    boundTest is not LaunchDebugOperation
        { Target: TestDebugTarget } ||
    boundBasicStep is not StepOverDebugOperation { ThreadId: null, Granularity: null } ||
    boundInspect is not InspectDebugStopOperation { ThreadId: null } ||
    boundWrite is not WriteDebugMemoryOperation)
    return 13;

var generatedDebug = (HPDAIFunctionFactory.HPDAIFunction)debugFunctions[0];
var binder = generatedDebug.HPDOptions.ArgumentBinder;
if (binder is null)
    return 14;
string[] representativeRequests =
[
    """{"request":{"action":"launch","target":{"targetKind":"sourceFile","path":"/workspace/app.py"},"stopOnEntry":true}}""",
    """{"request":{"action":"launch","target":{"targetKind":"applicationProject","path":"/workspace/App","arguments":["one"]}}}""",
    """{"request":{"action":"launch","target":{"targetKind":"executable","path":"/workspace/App.dll","arguments":["two"]}}}""",
    """{"request":{"action":"launch","target":{"targetKind":"test","path":"/workspace/App.Tests","framework":"DotNet","filter":"Name=One"}}}""",
    """{"request":{"action":"attach","target":{"targetKind":"process","processId":42}}}""",
    """{"request":{"action":"terminate","debugTreeId":"tree_1","target":"Tree"}}""",
    """{"request":{"action":"setSourceBreakpoints","debugTreeId":"tree_1","breakpoints":[{"path":"/workspace/app.py","line":2}]}}""",
    """{"request":{"action":"stepOver","debugTreeId":"tree_1"}}""",
    """{"request":{"action":"inspectStop","debugTreeId":"tree_1"}}""",
    """{"request":{"action":"getStackTrace","debugTreeId":"tree_1"}}""",
    """{"request":{"action":"getModules","debugTreeId":"tree_1","count":30,"continuationToken":"module_page_1"}}""",
    """{"request":{"action":"setVariable","debugTreeId":"tree_1","variablesToken":"variables_1","name":"value","value":"42"}}""",
    """{"request":{"action":"writeMemory","debugTreeId":"tree_1","memoryToken":"memory_1","base64Data":"AA=="}}""",
    """{"request":{"action":"getOutput","debugTreeId":"tree_1","maximumRecords":10}}"""
];
foreach (var requestJson in representativeRequests)
{
    using var requestDocument = JsonDocument.Parse(requestJson);
    var binding = binder(requestDocument.RootElement);
    if (binding.Errors.Count != 0 || binding.Value is null)
    {
        Console.Error.WriteLine($"Debug binder smoke failed for: {requestJson}");
        foreach (var error in binding.Errors)
            Console.Error.WriteLine(error);
        return 15;
    }
}
using (var removedTargetDocument = JsonDocument.Parse(
    """{"request":{"action":"launch","target":{"targetKind":"project""" +
    """Directory","path":"/workspace/App"}}}"""))
{
    var invalidBinding = binder(removedTargetDocument.RootElement);
    if (invalidBinding.Errors.Count == 0)
        return 17;
}
using (var removedArgumentsDocument = JsonDocument.Parse(
    """{"request":{"action":"launch","target":{"targetKind":"executable","path":"/workspace/App.dll"},"arguments":["stale"]}}"""))
{
    var invalidBinding = binder(removedArgumentsDocument.RootElement);
    if (invalidBinding.Errors.Count == 0)
        return 18;
}
using (var invalidDocument = JsonDocument.Parse(
    """{"request":{"action":"continue","debugTreeId":"tree_1","threadId":1,"host":"localhost"}}"""))
{
    var invalidBinding = binder(invalidDocument.RootElement);
    if (invalidBinding.Errors.Count == 0)
        return 16;
}

var planMetadata = new DebugExecutionPlanMetadata(
    "dotnet-vstest",
    DebugSemanticStartKind.HostedLaunchAttach,
    nameof(TestDebugTarget),
    "App.Tests");
var activationMetadata = new DebugExecutionActivationMetadata(
    DebugSemanticStartKind.HostedLaunchAttach,
    DebugAdapterStartMethod.Attach,
    "netcoredbg",
    1);
var breakpointMetadata = new DebugBreakpointCounts(1, 1, 0, 1);
var terminalMetadata = new DebugTerminalRecordMetadata(
    "tree_1",
    "Terminated",
    0,
    DateTimeOffset.UtcNow,
    breakpointMetadata,
    128,
    0,
    0,
    null);
var projectMetadata = new DebugProjectEvaluationMetadata(
    "Test",
    "VSTest",
    "net10.0",
    "App.Tests.dll",
    "fingerprint",
    true);
var exceptionFilterMetadata = new DebugExceptionFilterMetadata(
    "all",
    "All exceptions",
    true,
    false);
var capabilitySummaryMetadata = new DebugCapabilitySummaryMetadata(
    ["setVariable"],
    ["setExpression"],
    ["steppingGranularity"],
    [exceptionFilterMetadata]);
var launchNoticeMetadata = new DebugLaunchNoticeMetadata(
    "no_initial_stop_strategy",
    "Use stopOnEntry or initial breakpoints.");
var modulePageMetadata = new DebugModulePageMetadata(
    [new DebugModuleMetadata(
        "App.dll",
        "/workspace/App.dll",
        IsOptimized: false,
        IsUserCode: true,
        Version: "1.0.0",
        SymbolStatus: "Symbols loaded")],
    1,
    null,
    DebugModuleInventorySource.AdapterRequest,
    DebugModuleInventoryCompleteness.Authoritative);
if (JsonSerializer.SerializeToUtf8Bytes(
        planMetadata,
        CodingToolHarnessJsonContext.Default.DebugExecutionPlanMetadata).Length == 0 ||
    JsonSerializer.SerializeToUtf8Bytes(
        activationMetadata,
        CodingToolHarnessJsonContext.Default.DebugExecutionActivationMetadata).Length == 0 ||
    JsonSerializer.SerializeToUtf8Bytes(
        breakpointMetadata,
        CodingToolHarnessJsonContext.Default.DebugBreakpointCounts).Length == 0 ||
    JsonSerializer.SerializeToUtf8Bytes(
        terminalMetadata,
        CodingToolHarnessJsonContext.Default.DebugTerminalRecordMetadata).Length == 0 ||
    JsonSerializer.SerializeToUtf8Bytes(
        projectMetadata,
        CodingToolHarnessJsonContext.Default.DebugProjectEvaluationMetadata).Length == 0 ||
    JsonSerializer.SerializeToUtf8Bytes(
        exceptionFilterMetadata,
        CodingToolHarnessJsonContext.Default.DebugExceptionFilterMetadata).Length == 0 ||
    JsonSerializer.SerializeToUtf8Bytes(
        capabilitySummaryMetadata,
        CodingToolHarnessJsonContext.Default.DebugCapabilitySummaryMetadata).Length == 0 ||
    JsonSerializer.SerializeToUtf8Bytes(
        launchNoticeMetadata,
        CodingToolHarnessJsonContext.Default.DebugLaunchNoticeMetadata).Length == 0 ||
    JsonSerializer.SerializeToUtf8Bytes(
        modulePageMetadata,
        CodingToolHarnessJsonContext.Default.DebugModulePageMetadata).Length == 0)
    return 19;

var plannedEvent = new DebugExecutionPlannedEvent
{
    DebugTreeId = "tree_1",
    DebugSessionId = "session_1",
    AdapterId = "netcoredbg",
    SemanticStartKind = DebugSemanticStartKind.HostedLaunchAttach,
    AdapterStartMethod = DebugAdapterStartMethod.Attach,
    ExecutionPlannerId = "dotnet-vstest"
};
var plannedPayload = eventCodec.Serialize(plannedEvent);
if (eventCodec.DeserializeEvent(plannedPayload)
    is not { AdapterStartMethod: DebugAdapterStartMethod.Attach })
    return 20;
var breakpointSelectionEvent = new DebugBreakpointSelectionAppliedEvent
{
    DebugTreeId = "tree_1",
    DebugSessionId = "session_1",
    AdapterId = "netcoredbg",
    ToolCallId = "call_1",
    Action = "setSourceBreakpoints",
    BreakpointKind = DebugBreakpointKind.Source,
    Before = [],
    After =
    [
        new DebugBreakpointSelectionEventItem
        {
            ClientBreakpointId = "client_1",
            Kind = DebugBreakpointKind.Source,
            DisplayPath = "Program.cs",
            RequestedLine = 10,
            ResolvedLine = 11,
            Acknowledged = true,
            Verified = true
        }
    ],
    Changes = [new("client_1", DebugBreakpointSelectionDeltaKind.Added)],
    Counts = new(1, 1, 1, 0),
    SourcePreviews =
    [
        new DebugSourcePreview
        {
            DisplayPath = "Program.cs",
            Language = "csharp",
            Hunks = [new(8, ["line 8", "line 9", "line 10"])],
            Truncated = false
        }
    ],
    DetailsTruncated = false
};
var stopSummaryEvent = new DebugPrimaryStopAvailableEvent
{
    DebugTreeId = "tree_1",
    DebugSessionId = "session_1",
    AdapterId = "netcoredbg",
    AdapterThreadId = 1,
    SuspensionEpoch = 2,
    Reason = "breakpoint",
    FrameName = "Main",
    DisplayPath = "Program.cs",
    Line = 10,
    InspectionSucceeded = true,
    HitBreakpointIdentityUnknown = false
};
var treeCompletedEvent = new DebugTreeCompletedEvent
{
    DebugTreeId = "tree_1",
    DebugSessionId = "session_1",
    AdapterId = "netcoredbg",
    FinalStatus = "Terminated",
    ExitCode = 0,
    DurationMilliseconds = 100,
    SessionCount = 1,
    ChildSessionCount = 0,
    Breakpoints = new(1, 1, 1, 0, 1, 0),
    BreakpointStopCount = 1,
    RetainedOutputBytes = 0,
    DroppedOutputRecords = 0,
    DroppedOutputBytes = 0,
    ProjectionFailures = 0
};
if (eventCodec.DeserializeEvent(eventCodec.Serialize(breakpointSelectionEvent))
        is not { After.Count: 1 } ||
    eventCodec.DeserializeEvent(eventCodec.Serialize(stopSummaryEvent))
        is not { SuspensionEpoch: 2 } ||
    eventCodec.DeserializeEvent(eventCodec.Serialize(treeCompletedEvent))
        is not { BreakpointStopCount: 1 })
    return 22;
var activatingEvent = new DebugExecutionActivatingEvent
{
    DebugTreeId = "tree_1",
    DebugSessionId = "session_1",
    AdapterId = "netcoredbg",
    SemanticStartKind = DebugSemanticStartKind.HostedLaunchAttach,
    AdapterStartMethod = DebugAdapterStartMethod.Attach,
    ExecutionPlannerId = "dotnet-vstest"
};
var hostStartedEvent = new DebugHostProcessStartedEvent
{
    DebugTreeId = "tree_1",
    DebugSessionId = "session_1",
    AdapterId = "netcoredbg",
    SafeProcessRole = "testhost-runner"
};
var hostReadyEvent = new DebugHostReadyEvent
{
    DebugTreeId = "tree_1",
    DebugSessionId = "session_1",
    AdapterId = "netcoredbg",
    SafeProcessRole = "testhost"
};
var hostExitedEvent = new DebugHostProcessExitedEvent
{
    DebugTreeId = "tree_1",
    DebugSessionId = "session_1",
    AdapterId = "netcoredbg",
    SafeProcessRole = "testhost-runner",
    ExitCode = 0
};
var activationFailedEvent = new DebugExecutionActivationFailedEvent
{
    DebugTreeId = "tree_1",
    DebugSessionId = "session_1",
    AdapterId = "netcoredbg",
    ExecutionPlannerId = "dotnet-vstest",
    SafeReasonCode = "debug_host_start_failed"
};
var cleanupFailedEvent = new DebugOwnedResourceCleanupFailedEvent
{
    DebugTreeId = "tree_1",
    DebugSessionId = "session_1",
    AdapterId = "netcoredbg",
    SafeResourceKind = "process",
    SafeResourceIdentity = "testhost-runner"
};
var retainedEvent = new DebugTerminalRecordRetainedEvent
{
    DebugTreeId = "tree_1",
    DebugSessionId = "session_1",
    AdapterId = "netcoredbg",
    FinalStatus = "Terminated"
};
var evictedEvent = new DebugTerminalRecordEvictedEvent
{
    DebugTreeId = "tree_1",
    DebugSessionId = "session_1",
    AdapterId = "netcoredbg",
    SafeReasonCode = "COUNT_BOUND"
};
if (eventCodec.DeserializeEvent(eventCodec.Serialize(activatingEvent)) is null ||
    eventCodec.DeserializeEvent(eventCodec.Serialize(hostStartedEvent)) is null ||
    eventCodec.DeserializeEvent(eventCodec.Serialize(hostReadyEvent)) is null ||
    eventCodec.DeserializeEvent(eventCodec.Serialize(hostExitedEvent)) is null ||
    eventCodec.DeserializeEvent(eventCodec.Serialize(activationFailedEvent)) is null ||
    eventCodec.DeserializeEvent(eventCodec.Serialize(cleanupFailedEvent)) is null ||
    eventCodec.DeserializeEvent(eventCodec.Serialize(retainedEvent)) is null ||
    eventCodec.DeserializeEvent(eventCodec.Serialize(evictedEvent)) is null)
    return 21;

var descriptor = new DebugAdapterDescriptor
{
    Id = "debugpy",
    Languages = ["python"],
    FileExtensions = [".py"],
    RootMarkers = ["pyproject.toml"],
    TargetKinds = DebugTargetKind.SourceFile | DebugTargetKind.Process,
    ProgramKinds = DebugAdapterProgramKind.SourceFile,
    Provenance = new()
    {
        PackageId = "hpd.debugpy",
        PackageVersion = "1",
        AssemblyName = "HPD-Agent.Debugging.AotSmoke"
    }
};

var configuration = new BuiltInDebugAdapterConfigurationComposer().ComposeLaunch(
    descriptor,
    new("/workspace/app.py", "/workspace", DebugTargetKind.SourceFile,
        DebugAdapterProgramKind.SourceFile, ["--smoke"], StopOnEntry: true));
var adapterConfiguration = configuration.EnumerateObject()
    .ToDictionary(x => x.Name, x => x.Value.Clone(), StringComparer.Ordinal);
var arguments = new LaunchRequestArguments
{
    NoDebug = false,
    AdapterConfiguration = adapterConfiguration
};
var payload = JsonSerializer.SerializeToUtf8Bytes(arguments, DapJsonContext.Default.LaunchRequestArguments);
var framed = DebugProtocolFramer.Encode(payload);

var separator = Encoding.ASCII.GetBytes("\r\n\r\n");
var bodyOffset = framed.AsSpan().IndexOf(separator);
if (bodyOffset < 0)
    return 1;
bodyOffset += separator.Length;
using var roundTrip = JsonDocument.Parse(framed.AsMemory(bodyOffset));
var root = roundTrip.RootElement;
return root.GetProperty("program").GetString() == "/workspace/app.py" &&
       root.GetProperty("args")[0].GetString() == "--smoke" &&
       root.GetProperty("stopOnEntry").GetBoolean()
    ? 0
    : 2;
