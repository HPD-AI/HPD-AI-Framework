using System.Runtime.CompilerServices;
using HPD.Agent.Providers;
using HPD.Audio.Primitives;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.LiveKit;

#pragma warning disable MEAI001

/// <summary>Acquires and owns one configured STT client for each retained Audio session.</summary>
internal sealed class ConfiguredManagedStreamingSpeechToTextSourceV1(
    Func<CancellationToken, ValueTask<ProviderClientConstruction<ISpeechToTextClient>>> acquire,
    ManagedStreamingSpeechToTextOptionsV1 options) : IManagedAudioTranscriptSourceV1
{
    public async IAsyncEnumerable<ManagedAudioInputObservationV1> RunAsync(
        IAudioSource source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var construction = await acquire(cancellationToken).ConfigureAwait(false);
        try
        {
            var inner = new ManagedStreamingSpeechToTextSourceV1(construction.Client, options);
            await foreach (var candidate in inner.RunAsync(source, cancellationToken).ConfigureAwait(false))
                yield return candidate;
        }
        finally
        {
            await construction.Owner.DisposeAsync().ConfigureAwait(false);
        }
    }
}
