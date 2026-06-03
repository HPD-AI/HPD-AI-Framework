using HPD.Agent;
using HPD.Agent.Audio.AgentIntegration.Detection;
using HPD.Agent.Audio.Media;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.V2.Tests;

public sealed class InputContentDetectionTests
{
    [Fact]
    public void Detect_PreservesTypedContentIdentity_WithoutReplacingContent()
    {
        var detector = new InputContentDetector();
        var audio = AudioContent.Wav(new byte[] { 1, 2, 3, 4 });
        audio.Name = "voice.wav";
        var message = new ChatMessage(ChatRole.User, [audio]);

        var detection = Assert.Single(detector.Detect(message));

        Assert.Same(audio, detection.OriginalContent);
        Assert.Same(audio, message.Contents[detection.ContentIndex]);
        Assert.Equal(InputContentSourceKind.TypedContent, detection.InputContent.SourceKind);
        Assert.Equal(InputContentKind.Audio, detection.InputContent.Kind);
        Assert.Equal("voice.wav", detection.InputContent.Name);
        Assert.Equal(audio.MediaType, detection.InputContent.MediaType);
        Assert.Equal(4, detection.InputContent.SizeBytes);
        Assert.NotNull(detection.InputContent.Sha256);
        Assert.NotNull(detection.InputContent.Source);
        Assert.Null(detection.InputContent.Artifact);
        Assert.Null(detection.InputContent.ProviderRef);
    }

    [Fact]
    public void Detect_PreservesTextDataContentAsFiniteInput()
    {
        var detector = new InputContentDetector();
        var textBytes = new DataContent(new byte[] { 1, 2, 3 }, "text/plain")
        {
            Name = "notes.txt"
        };
        var message = new ChatMessage(ChatRole.User, [textBytes]);

        var detection = Assert.Single(detector.Detect(message));

        Assert.Same(textBytes, detection.OriginalContent);
        Assert.Same(textBytes, message.Contents[0]);
        Assert.Equal(InputContentSourceKind.DataContent, detection.InputContent.SourceKind);
        Assert.Equal(InputContentKind.Text, detection.InputContent.Kind);
        Assert.Equal("notes.txt", detection.InputContent.Name);
        Assert.Equal("text/plain", detection.InputContent.MediaType);
        Assert.Equal(3, detection.InputContent.SizeBytes);
    }
}
