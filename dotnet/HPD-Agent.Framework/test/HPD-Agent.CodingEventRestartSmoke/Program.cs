using System.Reflection;
using HPD.Agent;
using HPD.Agent.Serialization;
using HPDOS.ToolHarnesses.Middleware;

if (args.Length != 2 || args[0] is not ("write" or "read"))
    return 64;

var identity = Assembly.GetExecutingAssembly().GetName().Name!;
if (!AgentEventCompositionHost.TryGetApplication(identity, out var composition))
    return 1;
if (!composition.Codec.TryGetByDiscriminator("EXECUTE_COMMAND_PROCESS_STARTED", out _))
    return 2;

var store = new FileSessionStore(Path.GetFullPath(args[1]), composition.Codec);
if (args[0] == "write")
{
    await store.AppendThreadEventAsync("restart-session", "main", new ExecuteCommandProcessStartedEvent
    {
        ToolCallId = "call-1",
        FunctionName = "ExecuteCommand",
        CommandId = "cmd-1",
        Command = "dotnet test",
        BaseCommand = "dotnet",
        Category = ExecuteCommandCategory.Test,
        WorkingDirectory = "/workspace",
        Shell = "/bin/sh",
        StartedAt = DateTimeOffset.UnixEpoch,
        Background = true,
        AutoBackgroundEligible = false,
        ProcessId = 42,
        TimeoutMilliseconds = 120_000,
        EventFlowId = "cmd-1"
    });
    return 0;
}

var hydrated = await store.CollectThreadEventsAsync("restart-session", "main");
return hydrated?.OfType<ExecuteCommandProcessStartedEvent>().SingleOrDefault()?.CommandId == "cmd-1"
    ? 0
    : 3;
