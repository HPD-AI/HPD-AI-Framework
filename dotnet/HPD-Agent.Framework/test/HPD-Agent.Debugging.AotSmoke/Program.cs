using System.Text;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;

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
    """{"action":"inspectStop","debugTreeId":"tree_1","threadId":1,"maxFrames":5}""",
    CodingToolHarnessJsonContext.Default.DebugOperation);
var boundWrite = JsonSerializer.Deserialize(
    """{"action":"writeMemory","debugTreeId":"tree_1","memoryToken":"memory_1","base64Data":"AA=="}""",
    CodingToolHarnessJsonContext.Default.DebugOperation);
if (boundLaunch is not LaunchDebugOperation ||
    boundInspect is not InspectDebugStopOperation ||
    boundWrite is not WriteDebugMemoryOperation)
    return 13;

var generatedDebug = (HPDAIFunctionFactory.HPDAIFunction)debugFunctions[0];
var binder = generatedDebug.HPDOptions.ArgumentBinder;
if (binder is null)
    return 14;
string[] representativeRequests =
[
    """{"request":{"action":"launch","target":{"targetKind":"sourceFile","path":"/workspace/app.py"},"stopOnEntry":true}}""",
    """{"request":{"action":"attach","target":{"targetKind":"process","processId":42}}}""",
    """{"request":{"action":"terminate","debugTreeId":"tree_1","target":"Tree"}}""",
    """{"request":{"action":"setSourceBreakpoints","debugTreeId":"tree_1","breakpoints":[{"path":"/workspace/app.py","line":2}]}}""",
    """{"request":{"action":"stepOver","debugTreeId":"tree_1","threadId":1}}""",
    """{"request":{"action":"inspectStop","debugTreeId":"tree_1","threadId":1}}""",
    """{"request":{"action":"setVariable","debugTreeId":"tree_1","variablesToken":"variables_1","name":"value","value":"42"}}""",
    """{"request":{"action":"writeMemory","debugTreeId":"tree_1","memoryToken":"memory_1","base64Data":"AA=="}}""",
    """{"request":{"action":"getOutput","debugTreeId":"tree_1","maximumRecords":10}}"""
];
foreach (var requestJson in representativeRequests)
{
    using var requestDocument = JsonDocument.Parse(requestJson);
    var binding = binder(requestDocument.RootElement);
    if (binding.Errors.Count != 0 || binding.Value is null)
        return 15;
}
using (var invalidDocument = JsonDocument.Parse(
    """{"request":{"action":"continue","debugTreeId":"tree_1","threadId":1,"host":"localhost"}}"""))
{
    var invalidBinding = binder(invalidDocument.RootElement);
    if (invalidBinding.Errors.Count == 0)
        return 16;
}

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
