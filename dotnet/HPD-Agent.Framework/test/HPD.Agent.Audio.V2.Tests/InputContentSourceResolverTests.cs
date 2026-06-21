using HPD.Agent;
using HPD.Agent.Audio.AgentIntegration.Detection;
using HPD.Agent.Audio.AgentIntegration.SourceResolution;
using HPD.Agent.Audio.Media;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.V2.Tests;

public sealed class InputContentSourceResolverTests
{
    [Fact]
    public async Task OpenAsync_TypedContent_ReturnsReadableProviderNeutralSource()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var audio = AudioContent.Wav(bytes);
        audio.Name = "voice.wav";
        var detection = DetectSingle(audio);
        var resolver = new AgentInputContentSourceResolver([detection]);

        var result = await resolver.OpenAsync(detection.InputContent);

        Assert.Equal(InputContentSourceOpenStatus.Opened, result.Status);
        Assert.NotNull(result.Source);
        var source = result.Source;
        Assert.Equal(detection.InputContent.Id, source.InputContentId);
        Assert.Equal("audio/wav", source.MediaType);
        Assert.Equal("voice.wav", source.Name);
        Assert.Equal(bytes.Length, source.SizeBytes);
        Assert.Equal(detection.InputContent.Sha256, source.Sha256);
        Assert.Equal(bytes, await ReadAllBytesAsync(source));
    }

    [Fact]
    public async Task OpenAsync_DataContent_ReturnsReadableProviderNeutralSource()
    {
        var bytes = new byte[] { 9, 8, 7 };
        var data = new DataContent(bytes, "audio/ogg")
        {
            Name = "clip.ogg"
        };
        var detection = DetectSingle(data);
        var resolver = new AgentInputContentSourceResolver([detection]);

        var result = await resolver.OpenAsync(detection.InputContent);

        Assert.Equal(InputContentSourceOpenStatus.Opened, result.Status);
        Assert.NotNull(result.Source);
        var source = result.Source;
        Assert.Equal("audio/ogg", source.MediaType);
        Assert.Equal("clip.ogg", source.Name);
        Assert.Equal(bytes.Length, source.SizeBytes);
        Assert.Equal(detection.InputContent.Sha256, source.Sha256);
        Assert.Equal(bytes, await ReadAllBytesAsync(source));
    }

    [Fact]
    public async Task OpenAsync_ContentStoreAudioRef_ReturnsReadableProviderNeutralSource()
    {
        var store = new InMemoryContentStore();
        var bytes = new byte[] { 4, 5, 6 };
        await using var writeStream = new MemoryStream(bytes);
        var info = await store.WriteAsync(
            "session-audio",
            writeStream,
            new ContentMetadata
            {
                ContentType = "audio/webm",
                Name = "stored.webm",
                Origin = ContentSource.User
            },
            new ContentWriteOptions());

        var inputContent = new InputContentRef
        {
            Id = new InputContentId("inputContent-audio-store"),
            Kind = InputContentKind.Audio,
            SourceKind = InputContentSourceKind.ContentStore,
            ContentStore = new InputContentStoreRef(
                StoreKind: "hpd-content",
                Scope: "session-audio",
                ContentId: info.Id,
                Version: info.Version,
                ReadUri: null)
        };
        var resolver = new AgentInputContentSourceResolver([], store);

        var result = await resolver.OpenAsync(inputContent);

        Assert.Equal(InputContentSourceOpenStatus.Opened, result.Status);
        Assert.NotNull(result.Source);
        var source = result.Source;
        Assert.Equal("audio/webm", source.MediaType);
        Assert.Equal("stored.webm", source.Name);
        Assert.Equal(bytes.Length, source.SizeBytes);
        Assert.Equal(bytes, await ReadAllBytesAsync(source));
    }

    [Fact]
    public async Task OpenAsync_TextData_IsDetectedButNotAudioResolved()
    {
        var data = new DataContent(new byte[] { 1, 2, 3 }, "text/plain")
        {
            Name = "notes.txt"
        };
        var detector = new InputContentDetector();
        var detection = Assert.Single(detector.Detect(new ChatMessage(ChatRole.User, [data])));

        var resolver = new AgentInputContentSourceResolver(
            [detection]);

        var result = await resolver.OpenAsync(detection.InputContent);

        Assert.Equal(InputContentSourceOpenStatus.UnsupportedMedia, result.Status);
        Assert.Null(result.Source);
    }

    [Fact]
    public void ProviderFacingResolverApi_DoesNotLeakTypedContentAndAgentDoesNotReferenceAudioAbstractions()
    {
        var agentReferences = typeof(AgentBuilder)
            .Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();

        Assert.DoesNotContain("HPD.Agent.Audio", agentReferences);

        var method = Assert.Single(typeof(IInputContentSourceResolver).GetMethods());
        Assert.DoesNotContain(
            "TypedContent",
            string.Join('|', method.GetParameters().Select(p => p.ParameterType.FullName)));
    }

    private static InputContentDetection DetectSingle(AIContent content)
    {
        var detector = new InputContentDetector();
        return Assert.Single(detector.Detect(new ChatMessage(ChatRole.User, [content])));
    }

    private static async Task<byte[]> ReadAllBytesAsync(InputContentSource source)
    {
        await using var stream = await source.OpenStreamAsync(CancellationToken.None);
        await using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }
}
