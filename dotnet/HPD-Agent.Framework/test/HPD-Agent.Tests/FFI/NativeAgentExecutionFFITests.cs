using System.Runtime.InteropServices;
using System.Text.Json;
using HPD.Agent.FFI;
using HPD.Agent.Providers;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.FFI;

public sealed class NativeAgentExecutionFFITests
{
    [Fact]
    public async Task RunAgentStreaming_RoutesCallbackPermissionResponseAndThreadState()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueToolCall(
            "SensitiveTool",
            "call_1",
            new Dictionary<string, object?> { ["target"] = "fixture" });
        fakeClient.EnqueueTextResponse("permission accepted");

        var tool = HPDAIFunctionFactory.Create(
            static (arguments, _, cancellationToken) =>
                Task.FromResult<object?>($"tool executed for {arguments["target"]}"),
            new HPDAIFunctionFactoryOptions
            {
                Name = "SensitiveTool",
                Description = "A sensitive tool requiring FFI permission approval",
                RequiresPermission = true,
            });

        var config = new AgentConfig
        {
            Name = "FFIAgent",
            MaxAgenticIterations = 5,
            Clients = new AgentClientsConfig
            {
                Chat = new ChatClientConfig
                {
                    Provider = new HPD.Agent.Providers.ProviderReference { Key = "test" },
                    ModelName = "test-model",
                },
            },
            ServerConfiguredTools = [tool],
            AgenticLoop = new AgenticLoopConfig
            {
                MaxTurnDuration = TimeSpan.FromSeconds(10),
            },
        };
        config.Clients.Chat!.Override = ClientOverride<IChatClient>.Borrow(fakeClient, "test", "local");

        var agent = await new AgentBuilder(config, new TestProviderRegistry(fakeClient))
            .WithPermissions()
            .BuildAsync(CancellationToken.None);

        var agentHandle = NativeExports.RegisterManagedAgentForTesting(agent);
        var threadHandle = NativeExports.CreateConversationThreadForTesting(agentHandle);
        var streamEvents = new List<string>();
        var sawEndOfStream = false;
        var approvedPermissionId = string.Empty;

        try
        {
            StreamCallback callback = (_, eventJsonPtr) =>
            {
                if (eventJsonPtr == IntPtr.Zero)
                {
                    sawEndOfStream = true;
                    return;
                }

                var eventJson = Marshal.PtrToStringUTF8(eventJsonPtr)
                    ?? throw new InvalidOperationException("FFI callback event JSON was null.");
                streamEvents.Add(eventJson);

                using var document = JsonDocument.Parse(eventJson);
                var root = document.RootElement;
                var type = root.GetProperty("type").GetString();
                if (type == "PERMISSION_REQUEST")
                {
                    approvedPermissionId = root.GetProperty("permissionId").GetString()
                        ?? throw new InvalidOperationException("Permission request omitted permissionId.");

                    var responded = NativeExports.RespondToPermissionForTesting(
                        agentHandle,
                        approvedPermissionId,
                        approved: 1,
                        permissionChoice: 1);

                    Assert.Equal(1, responded);
                }
            };

            var result = NativeExports.RunAgentStreamingForTesting(
                agentHandle,
                "Use the sensitive tool",
                threadHandle,
                callback,
                IntPtr.Zero);

            Assert.Equal(1, result);
            Assert.True(sawEndOfStream);
            Assert.NotEmpty(approvedPermissionId);
            Assert.Contains(streamEvents, json => json.Contains("\"type\":\"PERMISSION_REQUEST\"", StringComparison.Ordinal));
            Assert.Contains(streamEvents, json => json.Contains("\"type\":\"TOOL_CALL_RESULT\"", StringComparison.Ordinal));
            Assert.Contains(streamEvents, json => json.Contains("\"type\":\"TEXT_DELTA\"", StringComparison.Ordinal) &&
                                                  json.Contains("permission accepted", StringComparison.Ordinal));
            Assert.True(NativeExports.GetMessageCountForTesting(threadHandle) >= 2);
        }
        finally
        {
            NativeExports.DestroyHandleForTesting(threadHandle);
            NativeExports.DestroyHandleForTesting(agentHandle);
        }
    }
}
