namespace HPD.Events.Tests;

public class StaticMigrationGuardTests
{
    [Fact]
    public void No_EventPriority_Type_Remains_In_Core_Source()
    {
        var sourceRoot = GetEventsSourceRoot();

        Assert.False(File.Exists(Path.Combine(sourceRoot, "Abstractions", "EventPriority.cs")));
    }

    [Fact]
    public void Coordinator_Interface_Does_Not_Expose_GlobalQueue_Apis()
    {
        var interfacePath = Path.Combine(GetEventsSourceRoot(), "Abstractions", "IEventCoordinator.cs");
        var source = File.ReadAllText(interfacePath);

        Assert.DoesNotContain("EmitUpstream", source);
        Assert.DoesNotContain("TryRead", source);
        Assert.DoesNotContain("ReadAllAsync", source);
        Assert.DoesNotContain("SubscribeStream", source);
        Assert.DoesNotContain("SubscribeChannel", source);
    }

    [Fact]
    public void Production_Source_Does_Not_Call_Removed_Coordinator_Apis()
    {
        var sourceRoot = GetEventsSourceRoot();
        var matches = FindSourceFiles(sourceRoot, "EmitUpstream")
            .Concat(FindSourceFiles(sourceRoot, "SubscribeStream"))
            .Concat(FindSourceFiles(sourceRoot, "SubscribeChannel"))
            .Concat(FindSourceFiles(sourceRoot, "EventStreamSubscription"))
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void Production_Source_Does_Not_Expose_Old_Struct_Bus_Apis()
    {
        var sourceRoot = GetEventsSourceRoot();
        var matches = FindSourceRegex(sourceRoot, @"\bIStructEventBus\b")
            .Concat(FindSourceRegex(sourceRoot, @"\bStructEventRouter\b"))
            .Concat(FindSourceRegex(sourceRoot, @"\bStructEmitter\b"))
            .Concat(FindSourceRegex(sourceRoot, @"\bStructEmitterOptions\b"))
            .Concat(FindSourceRegex(sourceRoot, @"\bStructInbox\b"))
            .Concat(FindSourceRegex(sourceRoot, @"\bStructInboxOptions\b"))
            .Concat(FindSourceRegex(sourceRoot, @"\bStructSubscription\b"))
            .Concat(FindSourceRegex(sourceRoot, @"\bStructSubscriptionOptions\b"))
            .Concat(FindSourceRegex(sourceRoot, @"\bTryEmitStruct\b"))
            .Concat(FindSourceRegex(sourceRoot, @"\bEmitStructAsync\b"))
            .Concat(FindSourceRegex(sourceRoot, @"\bSubscribeStruct\b"))
            .Concat(FindSourceRegex(sourceRoot, @"\bCreateStructEmitter\b"))
            .ToArray();

        Assert.Empty(matches);
    }

    private static string GetEventsSourceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "HPD-AI-Framework",
                "dotnet",
                "shared",
                "src",
                "HPD-Events");

            if (Directory.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate HPD-Events source root.");
    }

    private static IEnumerable<string> FindSourceFiles(string sourceRoot, string text)
    {
        return Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(text, StringComparison.Ordinal));
    }

    private static IEnumerable<string> FindSourceRegex(string sourceRoot, string pattern)
    {
        return Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(path), pattern));
    }
}
