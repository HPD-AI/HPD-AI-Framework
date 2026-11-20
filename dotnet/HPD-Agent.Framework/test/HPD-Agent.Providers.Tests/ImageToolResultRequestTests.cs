#pragma warning disable OPENAI001
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using Anthropic;
using Anthropic.Core;
using GenerativeAI.Core;
using GenerativeAI.Microsoft.Extensions;
using HPD.Agent.Providers.GoogleAI;
using HPD.Agent.Providers.Bedrock;
using HPD.Agent.Providers.OpenRouter;
using Microsoft.Extensions.AI;
using OpenAI;

namespace HPD.Agent.Providers.Tests;

public sealed class ImageToolResultRequestTests
{
    private const string ImageBytes = "aW1hZ2U=";
    private const string Screenshot = """
        {"content":[{"type":"text","text":"Saved screenshot.png"},{"type":"image","mimeType":"image/png","data":"aW1hZ2U="}],"structuredContent":{"path":"screenshot.png"},"isError":false}
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenAIResponses_SendsScreenshotAsTypedOutput(bool streaming)
    {
        using var capture = new CaptureRequest();
        using var http = new HttpClient(capture);
        var sdk = new OpenAIClient(new ApiKeyCredential("test"), new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(http)
        });
        using var client = new ImageToolResultChatClient(sdk.GetResponsesClient().AsIChatClient("fixture"));
        using var request = await CaptureAsync(client, capture, streaming);
        var output = request.RootElement.GetProperty("input").EnumerateArray()
            .Single(item => item.GetProperty("type").GetString() == "function_call_output");
        Assert.Equal("screenshot-call", output.GetProperty("call_id").GetString());
        var parts = output.GetProperty("output");
        Assert.Equal("Saved screenshot.png", parts[0].GetProperty("text").GetString());
        Assert.Equal("input_image", parts[1].GetProperty("type").GetString());
        Assert.Equal("data:image/png;base64," + ImageBytes, parts[1].GetProperty("image_url").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnthropicMessages_SendsScreenshotInsideToolResult(bool streaming)
    {
        using var capture = new CaptureRequest();
        using var http = new HttpClient(capture);
        using var sdk = new AnthropicClient(new ClientOptions { ApiKey = "test", HttpClient = http });
        using var client = new ImageToolResultChatClient(sdk.AsIChatClient("fixture", 4096));
        using var request = await CaptureAsync(client, capture, streaming);
        var output = request.RootElement.GetProperty("messages").EnumerateArray()
            .SelectMany(message => message.GetProperty("content").EnumerateArray())
            .Single(item => item.GetProperty("type").GetString() == "tool_result");
        Assert.Equal("screenshot-call", output.GetProperty("tool_use_id").GetString());
        var parts = output.GetProperty("content");
        Assert.Equal("Saved screenshot.png", parts[0].GetProperty("text").GetString());
        Assert.Equal("image", parts[1].GetProperty("type").GetString());
        Assert.Equal("image/png", parts[1].GetProperty("source").GetProperty("media_type").GetString());
        Assert.Equal(ImageBytes, parts[1].GetProperty("source").GetProperty("data").GetString());
    }

    [Theory]
    [InlineData("amazon.nova-pro-v1:0", true)]
    [InlineData("us.anthropic.claude-sonnet-4-5-20250929-v1:0", true)]
    [InlineData("amazon.nova-micro-v1:0", false)]
    [InlineData("meta.llama3-3-70b-instruct-v1:0", false)]
    public async Task BedrockConverse_UsesImageBlocksOnlyForSupportedModels(string model, bool supportsImages)
    {
        using var sdk = new CaptureBedrockRequest();
        Assert.Equal(supportsImages, BedrockProvider.SupportsImageToolResults(model));
        using var client = new ImageToolResultChatClient(sdk.AsIChatClient(model),
            BedrockProvider.SupportsImageToolResults(model));
        await Assert.ThrowsAnyAsync<Exception>(() => client.GetResponseAsync(Messages()));
        var tool = Assert.Single(sdk.Request!.Messages.SelectMany(message => message.Content)
            .Where(block => block.ToolResult is not null)).ToolResult;
        Assert.Equal("screenshot-call", tool.ToolUseId);
        Assert.Contains("Saved screenshot.png", tool.Content[0].Text);
        if (supportsImages)
            Assert.Equal(Convert.FromBase64String(ImageBytes), tool.Content[1].Image.Source.Bytes.ToArray());
        else
            Assert.Contains("text-only", Assert.Single(tool.Content).Text);
    }

    [Theory]
    [InlineData("gemini-3-flash-preview", "generateContent", true)]
    [InlineData("gemini-3-flash-preview", "streamGenerateContent", true)]
    [InlineData("gemini-2.5-flash", "generateContent", false)]
    public async Task Gemini_WritesInlineFunctionResponsePartsForGemini3(
        string model, string endpoint, bool supportsImages)
    {
        var adapter = new GoogleImageToolResultPlatformAdapter("test", "v1beta", false, model);
        var messages = Messages();
        var tool = Assert.IsType<FunctionResultContent>(messages[2].Contents[0]);
        tool.Result = adapter.FormatImageResult(ImageToolResultChatClient.ReadImageParts(tool.Result)!);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{model}:{endpoint}")
        {
            Content = new StringContent(JsonSerializer.Serialize(messages.ToGenerateContentRequest(), new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                }),
                Encoding.UTF8, "application/json")
        };
        await ((IPlatformAdapter)adapter).AddAuthorizationAsync(request, false, CancellationToken.None);
        using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync());
        var result = body.RootElement.GetProperty("contents").EnumerateArray()
            .SelectMany(message => message.GetProperty("parts").EnumerateArray())
            .Select(part => part.TryGetProperty("functionResponse", out var value) ? value : default)
            .Single(part => part.ValueKind == JsonValueKind.Object);
        Assert.Equal("screenshot-call", result.GetProperty("name").GetString());
        Assert.Contains("Saved screenshot.png", result.GetProperty("response").GetProperty("content").GetString());
        Assert.Equal(supportsImages, result.TryGetProperty("parts", out var parts));
        if (supportsImages)
            Assert.Equal(ImageBytes, parts[0].GetProperty("inlineData").GetProperty("data").GetString());
        else
            Assert.DoesNotContain(ImageBytes, result.GetRawText());
    }

    [Fact]
    public async Task OpenAIChatCompletions_UsesTextForNativeImageResults()
    {
        using var capture = new CaptureRequest();
        using var http = new HttpClient(capture);
        var sdk = new OpenAIClient(new ApiKeyCredential("test"), new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(http)
        });
        using var client = new ImageToolResultChatClient(sdk.GetChatClient("fixture").AsIChatClient(),
            supportsImages: false);
        var messages = Messages();
        messages[2].Contents[0] = new FunctionResultContent("screenshot-call",
            new AIContent[] { new TextContent("Native image"), new DataContent(Convert.FromBase64String(ImageBytes), "image/png") });
        using var request = await CaptureAsync(client, capture, streaming: false, messages);
        var tool = request.RootElement.GetProperty("messages").EnumerateArray()
            .Single(message => message.GetProperty("role").GetString() == "tool");
        Assert.Contains("Native image", tool.GetProperty("content").ToString());
        Assert.DoesNotContain(ImageBytes, tool.GetRawText());
    }

    [Fact]
    public async Task OpenRouter_ReportsTheScreenshotPathWithoutSendingBase64()
    {
        using var capture = new CaptureRequest();
        using var http = new HttpClient(capture) { BaseAddress = new Uri("https://fixture.invalid/") };
        using var client = new OpenRouterChatClient(http, "fixture");
        using var request = await CaptureAsync(client, capture, streaming: false);
        var tool = request.RootElement.GetProperty("messages").EnumerateArray()
            .Single(message => message.GetProperty("role").GetString() == "tool");
        Assert.Equal("screenshot-call", tool.GetProperty("tool_call_id").GetString());
        var text = tool.GetProperty("content").GetString();
        Assert.Contains("Saved screenshot.png", text);
        Assert.Contains("text-only", text);
        Assert.DoesNotContain(ImageBytes, text);
    }

    [Theory]
    [InlineData("""{"content":[{"type":"text","text":"ordinary output"}]}""")]
    [InlineData("""{"content":[{"type":"image","mimeType":"image/png","data":"bad base64"}]}""")]
    [InlineData("""{"isError":true,"content":[{"type":"image","mimeType":"image/png","data":"aW1hZ2U="}]}""")]
    public void NonImageAndErrorEnvelopesKeepTheirOriginalRepresentation(string json)
    {
        using var result = JsonDocument.Parse(json);
        Assert.Null(ImageToolResultChatClient.ReadImageParts(result.RootElement));
    }

    private static ChatMessage[] Messages()
    {
        using var result = JsonDocument.Parse(Screenshot);
        return
        [
            new(ChatRole.User, "Read the screenshot"),
            new(ChatRole.Assistant, [new FunctionCallContent("screenshot-call", "screenshot", new Dictionary<string, object?>())]),
            new(ChatRole.Tool, [new FunctionResultContent("screenshot-call", result.RootElement.Clone())])
        ];
    }

    private static async Task<JsonDocument> CaptureAsync(IChatClient client, CaptureRequest capture, bool streaming, ChatMessage[]? messages = null)
    {
        messages ??= Messages();
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            if (streaming)
            {
                await foreach (var _ in client.GetStreamingResponseAsync(messages)) { }
            }
            else
            {
                await client.GetResponseAsync(messages);
            }
        });
        return JsonDocument.Parse(Assert.IsType<string>(capture.Body));
    }

    private sealed class CaptureRequest : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error":{"message":"Capture complete"}}""", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class CaptureBedrockRequest() : AmazonBedrockRuntimeClient(
        new BasicAWSCredentials("test", "test"), Amazon.RegionEndpoint.USEast1)
    {
        public ConverseRequest? Request { get; private set; }

        public override Task<ConverseResponse> ConverseAsync(ConverseRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            throw new InvalidOperationException("Capture complete");
        }
    }
}
