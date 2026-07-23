using System.Text;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;
using HPD.Environment.Contracts;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugAdapterDiagnosticTests
{
    [Fact]
    public async Task Protocol_client_drains_bounded_adapter_diagnostics_and_exit()
    {
        var transport = new InMemoryDebugProtocolTransport();
        await using var client = new DebugProtocolClient(transport);

        await transport.FeedDiagnosticAsync(Encoding.UTF8.GetBytes("adapter stderr"));
        transport.Complete(new DebugTransportExit(
            ProcessCompletionKind.Exited, 17, "ADAPTER_FAILED"));

        await WaitUntilAsync(() => client.AdapterDiagnostics.Exit is not null);
        var snapshot = client.AdapterDiagnostics;
        snapshot.StandardError.Should().Be("adapter stderr");
        snapshot.Exit!.ExitCode.Should().Be(17);
        snapshot.Exit.SafeReasonCode.Should().Be("ADAPTER_FAILED");
    }

    [Fact]
    public void Diagnostic_store_returns_opaque_reference_and_bounded_record()
    {
        var store = new DebugAdapterDiagnosticStore();
        var reference = store.Retain(
            "netcoredbg",
            "initialize",
            new DebugAdapterDiagnosticSnapshot(
                new string('x', 70 * 1024),
                2,
                4096,
                new DebugTransportExit(ProcessCompletionKind.Exited, 9, "EXITED")));

        reference.Should().StartWith("debug-diagnostic-");
        store.TryGet(reference, out var record).Should().BeTrue();
        record.StandardError.Length.Should().Be(64 * 1024);
        record.AdapterId.Should().Be("netcoredbg");
        record.Phase.Should().Be("initialize");
        record.ExitCode.Should().Be(9);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException();
            await Task.Delay(10);
        }
    }
}
