using System.Runtime.CompilerServices;
using HPD.Agent.Providers;
using HPD.Audio.Primitives;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.LiveKit;

#pragma warning disable MEAI001

/// <summary>Acquires and owns one configured STT client for each retained Audio session.</summary>
internal sealed class ConfiguredManagedStreamingSpeechToTextSource(
    Func<CancellationToken, ValueTask<ProviderClientConstruction<ISpeechToTextClient>>> acquire,
    ManagedStreamingSpeechToTextOptions options) : IManagedAudioTranscriptSource
{
    public async IAsyncEnumerable<ManagedAudioInputObservation> RunAsync(
        IAudioSource source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var construction = await acquire(cancellationToken).ConfigureAwait(false);
        try
        {
            var inner = new ManagedStreamingSpeechToTextSource(construction.Client, options);
            await foreach (var candidate in inner.RunAsync(source, cancellationToken).ConfigureAwait(false))
                yield return candidate;
        }
        finally
        {
            await construction.Owner.DisposeAsync().ConfigureAwait(false);
        }
    }
}
