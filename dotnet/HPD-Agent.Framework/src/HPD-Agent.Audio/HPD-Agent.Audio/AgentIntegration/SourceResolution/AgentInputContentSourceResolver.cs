using System.Security.Cryptography;
using HPD.Agent.Audio.AgentIntegration.Detection;
using HPD.Agent.Audio.Media;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.AgentIntegration.SourceResolution;

public sealed class AgentInputContentSourceResolver : IInputContentSourceResolver
{
    private const string DefaultMediaType = "application/octet-stream";

    private readonly Dictionary<InputContentId, InputContentDetection> _detections;
    private readonly IContentStore? _contentStore;
    private readonly string? _contentStoreScope;

    public AgentInputContentSourceResolver(
        IEnumerable<InputContentDetection> detections,
        IContentStore? contentStore = null,
        string? contentStoreScope = null)
    {
        ArgumentNullException.ThrowIfNull(detections);

        _detections = detections.ToDictionary(
            d => d.InputContent.Id,
            d => d);
        _contentStore = contentStore;
        _contentStoreScope = contentStoreScope;
    }

    public async ValueTask<InputContentSourceOpenResult> OpenAsync(
        InputContentRef inputContent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputContent);

        if (inputContent.Kind is not InputContentKind.Audio)
        {
            return InputContentSourceOpenResult.NotResolved(
                inputContent.Id,
                InputContentSourceOpenStatus.UnsupportedMedia,
                "Input content is not audio-readable by this resolver.");
        }

        if (_detections.TryGetValue(inputContent.Id, out var detection) &&
            detection.OriginalContent is DataContent dataContent)
        {
            return OpenDataContent(inputContent, dataContent);
        }

        if (inputContent.ContentStore is not null)
        {
            return await OpenContentStoreRefAsync(
                inputContent,
                inputContent.ContentStore,
                cancellationToken).ConfigureAwait(false);
        }

            return InputContentSourceOpenResult.NotResolved(
                inputContent.Id,
                InputContentSourceOpenStatus.UnsupportedSource,
            $"Input content source '{inputContent.SourceKind}' is not provider-readable by this resolver.");
    }

    private static InputContentSourceOpenResult OpenDataContent(
        InputContentRef inputContent,
        DataContent dataContent)
    {
        if (!dataContent.HasTopLevelMediaType("audio"))
        {
            return InputContentSourceOpenResult.NotResolved(
                inputContent.Id,
                InputContentSourceOpenStatus.UnsupportedMedia,
                "Input content bytes are not tagged with an audio media type.");
        }

        var bytes = dataContent.Data.ToArray();
        var mediaType = inputContent.MediaType ?? dataContent.MediaType ?? DefaultMediaType;
        var sha256 = inputContent.Sha256 ?? ComputeSha256(bytes);

        return InputContentSourceOpenResult.Opened(new InputContentSource
        {
            InputContentId = inputContent.Id,
            MediaType = mediaType,
            Name = inputContent.Name ?? dataContent.Name,
            SizeBytes = inputContent.SizeBytes ?? bytes.LongLength,
            Sha256 = sha256,
            OpenStreamAsync = cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false));
            }
        });
    }

    private async ValueTask<InputContentSourceOpenResult> OpenContentStoreRefAsync(
        InputContentRef inputContent,
        InputContentStoreRef storeRef,
        CancellationToken cancellationToken)
    {
        if (_contentStore is null)
        {
            return InputContentSourceOpenResult.NotResolved(
                inputContent.Id,
                InputContentSourceOpenStatus.UnsupportedSource,
                "No content store was provided for this input content resolver.");
        }

        var scope = ContentScope.Create(storeRef.Scope ?? _contentStoreScope ?? ContentScope.Global.Value);
        var address = new ContentAddress(scope, storeRef.ContentId);
        var stat = await _contentStore.StatAsync(
            address,
            cancellationToken).ConfigureAwait(false);

        if (stat is null)
        {
            return InputContentSourceOpenResult.NotResolved(
                inputContent.Id,
                InputContentSourceOpenStatus.NotFound,
                $"Content store item '{storeRef.ContentId}' was not found.");
        }

        var mediaType = inputContent.MediaType ?? stat.ContentType;
        if (!AudioContent.IsAudioMediaType(mediaType))
        {
            return InputContentSourceOpenResult.NotResolved(
                inputContent.Id,
                InputContentSourceOpenStatus.UnsupportedMedia,
                $"Content store item '{storeRef.ContentId}' is not audio.");
        }

        return InputContentSourceOpenResult.Opened(new InputContentSource
        {
            InputContentId = inputContent.Id,
            MediaType = mediaType,
            Name = inputContent.Name ?? stat.Name,
            SizeBytes = inputContent.SizeBytes ?? stat.SizeBytes,
            Sha256 = inputContent.Sha256,
            OpenStreamAsync = async openCancellationToken =>
            {
                var result = await _contentStore.OpenReadAsync(
                    stat.Address,
                    openCancellationToken).ConfigureAwait(false);

                return result?.Content
                    ?? throw new FileNotFoundException(
                        $"Content store item '{storeRef.ContentId}' was not found.",
                        storeRef.ContentId);
            }
        });
    }

    private static string? ComputeSha256(byte[] bytes) =>
        bytes.Length == 0
            ? null
            : Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
