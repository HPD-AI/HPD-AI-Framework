using System.IO;

namespace HPD.Agent.Audio.Media;

public interface IInputContentSourceResolver
{
    ValueTask<InputContentSourceOpenResult> OpenAsync(
        InputContentRef inputContent,
        CancellationToken cancellationToken = default);
}

public sealed record InputContentSourceOpenResult
{
    public required InputContentId InputContentId { get; init; }

    public required InputContentSourceOpenStatus Status { get; init; }

    public InputContentSource? Source { get; init; }

    public string? Reason { get; init; }

    public static InputContentSourceOpenResult Opened(InputContentSource source) =>
        new()
        {
            InputContentId = source.InputContentId,
            Status = InputContentSourceOpenStatus.Opened,
            Source = source
        };

    public static InputContentSourceOpenResult NotResolved(
        InputContentId inputContentId,
        InputContentSourceOpenStatus status,
        string reason) =>
        new()
        {
            InputContentId = inputContentId,
            Status = status,
            Reason = reason
        };
}

public enum InputContentSourceOpenStatus
{
    Opened = 0,
    NotFound = 1,
    UnsupportedSource = 2,
    UnsupportedMedia = 3,
    Failed = 4
}

public sealed record InputContentSource
{
    public required InputContentId InputContentId { get; init; }

    public required string MediaType { get; init; }

    public string? Name { get; init; }

    public long? SizeBytes { get; init; }

    public string? Sha256 { get; init; }

    public required Func<CancellationToken, ValueTask<Stream>> OpenStreamAsync { get; init; }
}
