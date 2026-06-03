using System.Security.Cryptography;
using HPD.Agent.Middleware;
using HPD.Agent.Audio.Media;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.AgentIntegration.Detection;

public sealed class InputContentDetector
{
    public IReadOnlyList<InputContentDetection> Detect(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var detections = new List<InputContentDetection>();
        for (var i = 0; i < message.Contents.Count; i++)
        {
            var content = message.Contents[i];
            if (TryCreateInputContentRef(content, out var inputContent))
            {
                detections.Add(new InputContentDetection(i, content, inputContent));
            }
        }

        return detections;
    }

    public bool TryCreateInputContentRef(AIContent content, out InputContentRef inputContent)
    {
        inputContent = null!;

        switch (content)
        {
            case AudioContent audio:
                inputContent = FromDataContent(
                    audio,
                    InputContentSourceKind.TypedContent,
                    InputContentKind.Audio);
                return true;

            case ImageContent image:
                inputContent = FromDataContent(
                    image,
                    InputContentSourceKind.TypedContent,
                    InputContentKind.Image);
                return true;

            case VideoContent video:
                inputContent = FromDataContent(
                    video,
                    InputContentSourceKind.TypedContent,
                    InputContentKind.Video);
                return true;

            case DocumentContent document:
                inputContent = FromDataContent(
                    document,
                    InputContentSourceKind.TypedContent,
                    InputContentKind.Document);
                return true;

            case DataContent data when TryGetSupportedKind(data.MediaType, out var kind):
                inputContent = FromDataContent(
                    data,
                    InputContentSourceKind.DataContent,
                    kind);
                return true;

            case UriContent uri when TryGetSupportedKind(uri.MediaType, out _):
                inputContent = FromUriContent(uri);
                return true;

            case HostedFileContent hosted when TryGetSupportedKind(hosted.MediaType, out _):
                inputContent = FromHostedFileContent(hosted);
                return true;

            default:
                return false;
        }
    }

    private static InputContentRef FromDataContent(
        DataContent content,
        InputContentSourceKind sourceKind,
        InputContentKind kind)
    {
        var mediaType = content.MediaType;
        var sizeBytes = content.Data.Length;
        var sha256 = content.Data.IsEmpty
            ? null
            : Convert.ToHexString(SHA256.HashData(content.Data.Span)).ToLowerInvariant();

        return new InputContentRef
        {
            Id = NewInputContentId(),
            Kind = kind,
            SourceKind = sourceKind,
            MediaType = mediaType,
            Name = content.Name,
            SizeBytes = sizeBytes,
            Sha256 = sha256,
            Source = new InputContentSourceRef(
                sourceKind.ToString(),
                content.Name,
                mediaType,
                sizeBytes,
                sha256)
        };
    }

    private static InputContentRef FromUriContent(UriContent content)
    {
        var sourceKind = ContentReferenceResolverMiddleware.IsContentReference(content)
            ? InputContentSourceKind.ContentStore
            : InputContentSourceKind.UriContent;
        var kind = ResolveKind(content.MediaType);

        InputContentStoreRef? storeRef = null;
        if (sourceKind is InputContentSourceKind.ContentStore)
        {
            storeRef = new InputContentStoreRef(
                StoreKind: "hpd-content",
                Scope: null,
                ContentId: content.Uri.Host,
                Version: null,
                ReadUri: null);
        }

        return new InputContentRef
        {
            Id = NewInputContentId(),
            Kind = kind,
            SourceKind = sourceKind,
            MediaType = content.MediaType,
            Name = GetFileName(content.Uri),
            ContentStore = storeRef,
            Source = sourceKind is InputContentSourceKind.ContentStore
                ? null
                : new InputContentSourceRef(
                    sourceKind.ToString(),
                    GetFileName(content.Uri),
                    content.MediaType,
                    SizeBytes: null,
                    Sha256: null)
        };
    }

    private static InputContentRef FromHostedFileContent(HostedFileContent content)
    {
        return new InputContentRef
        {
            Id = NewInputContentId(),
            Kind = ResolveKind(content.MediaType),
            SourceKind = InputContentSourceKind.HostedFileContent,
            MediaType = content.MediaType,
            Name = content.Name,
            ProviderRef = new ProviderMediaRef(
                ProviderKey: "hosted-file",
                MediaId: content.FileId,
                MediaType: content.MediaType)
        };
    }

    private static bool TryGetSupportedKind(string? mediaType, out InputContentKind kind)
    {
        kind = ResolveKind(mediaType);
        return kind is not InputContentKind.Unknown;
    }

    private static InputContentKind ResolveKind(string? mediaType)
    {
        var normalized = NormalizeMediaType(mediaType);
        return normalized switch
        {
            null => InputContentKind.Unknown,
            "application/pdf" => InputContentKind.Document,
            _ when normalized.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) => InputContentKind.Audio,
            _ when normalized.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => InputContentKind.Image,
            _ when normalized.StartsWith("video/", StringComparison.OrdinalIgnoreCase) => InputContentKind.Video,
            _ when normalized.StartsWith("text/", StringComparison.OrdinalIgnoreCase) => InputContentKind.Text,
            _ => InputContentKind.Unknown
        };
    }

    private static string? NormalizeMediaType(string? mediaType)
        => string.IsNullOrWhiteSpace(mediaType)
            ? null
            : mediaType.Split(';', 2)[0].Trim().ToLowerInvariant();

    private static InputContentId NewInputContentId() =>
        new($"input-content-{Guid.NewGuid():N}");

    private static string? GetFileName(Uri uri)
    {
        if (!uri.IsAbsoluteUri || string.IsNullOrWhiteSpace(uri.LocalPath))
        {
            return null;
        }

        var fileName = Path.GetFileName(uri.LocalPath);
        return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
    }
}
