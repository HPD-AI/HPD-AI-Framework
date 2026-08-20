using HPD.Agent.Audio.Ledger;
using HPD.Agent.Audio.Media;
using HPD.Agent.Audio.Runtime.Thread;
using HPD.Agent.Audio.Runtime.Ledger;
using HPD.Agent.Audio.Runtime.Trace;
using HPD.Agent.Audio.Trace;

namespace HPD.Agent.Audio.V2.Tests;

internal static class TestInputContent
{
    public static InputContentRef Audio(
        string name,
        string mediaType,
        long? sizeBytes = 1024,
        string? sha256 = null)
        => new()
        {
            Id = new InputContentId(Path.GetFileNameWithoutExtension(name)),
            Kind = InputContentKind.Audio,
            SourceKind = InputContentSourceKind.TypedContent,
            MediaType = mediaType,
            Name = name,
            SizeBytes = sizeBytes,
            Sha256 = sha256,
            Source = new InputContentSourceRef(
                SourceKind: "TypedContent",
                Name: name,
                MediaType: mediaType,
                SizeBytes: sizeBytes,
                Sha256: sha256)
        };
}

internal static class CanonicalMediaEnvelopeTestExtensions
{
    public static InputContentRef PayloadInputContent(this CanonicalMediaEnvelope envelope)
        => envelope.Payload is MediaPayloadRef.InputContent inputContent
            ? inputContent.Content
            : throw new InvalidOperationException($"Expected inputContent-content payload, found {envelope.Payload.GetType().Name}.");
}

internal static class RuntimeResultTestExtensions
{
    public static InMemoryThreadProjectionSink AsInMemoryThread(this IThreadProjectionSink thread)
        => Assert.IsType<InMemoryThreadProjectionSink>(thread);
}
