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
            .Concat(FindSourceFiles(sourceRoot, ".TryRead("))
            .Concat(FindSourceFiles(sourceRoot, "SubscribeStream"))
            .Concat(FindSourceFiles(sourceRoot, "SubscribeChannel"))
            .Concat(FindSourceFiles(sourceRoot, "EventStreamSubscription"))
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
}
