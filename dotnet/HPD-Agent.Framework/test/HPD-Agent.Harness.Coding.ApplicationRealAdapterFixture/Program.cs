namespace HPD.Agent.ToolHarness.Coding.ApplicationRealAdapterFixture;

internal static class Program
{
    private static void Main()
    {
        // Repeat the target location so real-adapter qualification proves
        // late module/symbol binding without depending on machine load.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        var total = 0;
        while (DateTime.UtcNow < deadline)
        {
            var first = 40;
            total = first + 2;
            Thread.Sleep(50);
        }

        Console.WriteLine(total);
    }
}
