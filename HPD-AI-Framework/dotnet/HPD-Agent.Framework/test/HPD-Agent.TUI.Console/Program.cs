using HPD.Agent.TUI.Console.Modes;

var mode = args.Length == 0 ? "demo" : args[0].Trim().ToLowerInvariant();
var modeArgs = args.Length <= 1 ? [] : args[1..];

switch (mode)
{
    case "demo":
        await DemoMode.RunAsync(modeArgs);
        break;
    case "direct":
        await DirectMode.RunAsync(modeArgs);
        break;
    case "server":
        await ServerMode.RunAsync(modeArgs);
        break;
    case "coding":
        await CodingMode.RunAsync(modeArgs);
        break;
    case "-h":
    case "--help":
    case "help":
        PrintUsage();
        break;
    default:
        global::System.Console.Error.WriteLine($"Unknown mode: {mode}");
        PrintUsage();
        System.Environment.ExitCode = 2;
        break;
}

static void PrintUsage()
{
    global::System.Console.WriteLine("Usage:");
    global::System.Console.WriteLine("  dotnet run --project test/HPD-Agent.TUI.Console -- demo");
    global::System.Console.WriteLine("  dotnet run --project test/HPD-Agent.TUI.Console -- direct");
    global::System.Console.WriteLine("  dotnet run --project test/HPD-Agent.TUI.Console -- server [--url http://127.0.0.1:5057] [--agent tui-console-agent] [--session local-session] [--branch main]");
    global::System.Console.WriteLine("  dotnet run --project test/HPD-Agent.TUI.Console -- coding [--workspace /path/to/project] [--list-tools]");
}
