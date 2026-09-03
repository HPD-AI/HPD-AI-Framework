using System.Text;
using System.Text.Json;
using HPD.Agent.FFI;

namespace HPD.Agent.Tests.EventRouting;

public sealed class FfiEventSubscriptionTests
{
    [Fact]
    public async Task CallbackCannotDisposeItself_ThenExternalDisposeIsQuiescent()
    {
        await using var agent = await AgentEventSubscriptionTests.BuildAgentForFfiAsync();
        var agentHandle = NativeExports.RegisterManagedAgentForTesting(agent);
        var key = new ThreadKey("ffi-session", "ffi-thread");
        var callbackCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackDisposeStatus = HpdSubscriptionDisposeStatus.Disposed;
        var subscription = IntPtr.Zero;
        EventDeliveryCallback callback = (json, length, _) =>
        {
            string payload;
            unsafe
            {
                payload = Encoding.UTF8.GetString(new ReadOnlySpan<byte>((void*)json, checked((int)length)));
            }
            using var document = JsonDocument.Parse(payload);
            Assert.Equal("ffi-thread", document.RootElement.GetProperty("route").GetProperty("origin").GetProperty("threadId").GetString());
            callbackDisposeStatus = NativeExports.DisposeSubscriptionForTesting(ref subscription);
            callbackCompleted.TrySetResult();
        };

        try
        {
            var status = NativeExports.SubscribeEventsForTesting(
                agentHandle,
                Encoding.UTF8.GetBytes(key.SessionId),
                Encoding.UTF8.GetBytes(key.ThreadId),
                (int)AgentEventHierarchy.ExactThread,
                callback,
                IntPtr.Zero,
                out subscription);
            Assert.Equal(HpdSubscribeStatus.Ok, status);
            Assert.NotEqual(IntPtr.Zero, subscription);

            AgentEventSubscriptionTests.EmitForFfi(agent, key, "ffi");
            await callbackCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(HpdSubscriptionDisposeStatus.FromCallback, callbackDisposeStatus);
            Assert.NotEqual(IntPtr.Zero, subscription);
            Assert.Equal(HpdSubscriptionDisposeStatus.Disposed, NativeExports.DisposeSubscriptionForTesting(ref subscription));
            Assert.Equal(IntPtr.Zero, subscription);
            Assert.Equal(HpdSubscriptionDisposeStatus.Disposed, NativeExports.DisposeSubscriptionForTesting(ref subscription));
        }
        finally
        {
            if (subscription != IntPtr.Zero)
                NativeExports.DisposeSubscriptionForTesting(ref subscription);
            NativeExports.DestroyHandleForTesting(agentHandle);
            GC.KeepAlive(callback);
        }
    }

    [Fact]
    public async Task SubscribeRejectsInvalidUtf8AndHierarchyWithoutCreatingHandle()
    {
        await using var agent = await AgentEventSubscriptionTests.BuildAgentForFfiAsync();
        var agentHandle = NativeExports.RegisterManagedAgentForTesting(agent);
        EventDeliveryCallback callback = (_, _, _) => { };
        try
        {
            var invalidUtf8 = NativeExports.SubscribeEventsForTesting(
                agentHandle, [0xff], Encoding.UTF8.GetBytes("thread"), 0, callback, IntPtr.Zero, out var first);
            var invalidHierarchy = NativeExports.SubscribeEventsForTesting(
                agentHandle, Encoding.UTF8.GetBytes("session"), Encoding.UTF8.GetBytes("thread"), 99, callback, IntPtr.Zero, out var second);

            Assert.Equal(HpdSubscribeStatus.InvalidUtf8, invalidUtf8);
            Assert.Equal(IntPtr.Zero, first);
            Assert.Equal(HpdSubscribeStatus.InvalidHierarchy, invalidHierarchy);
            Assert.Equal(IntPtr.Zero, second);
        }
        finally
        {
            NativeExports.DestroyHandleForTesting(agentHandle);
            GC.KeepAlive(callback);
        }
    }
}
