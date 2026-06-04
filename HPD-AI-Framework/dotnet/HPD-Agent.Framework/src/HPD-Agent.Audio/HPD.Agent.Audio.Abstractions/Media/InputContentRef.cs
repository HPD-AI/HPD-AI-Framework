using HPD.Agent.Audio.Policies;

namespace HPD.Agent.Audio.Media;

public sealed record InputContentRef
{
    public required InputContentId Id { get; init; }

    public required InputContentKind Kind { get; init; }

    public required InputContentSourceKind SourceKind { get; init; }

    public string? MediaType { get; init; }

    public string? Name { get; init; }

    public long? SizeBytes { get; init; }

    public string? Sha256 { get; init; }

    public InputContentSourceRef? Source { get; init; }

    public InputWorkspaceContentRef? WorkspaceContent { get; init; }

    public AudioArtifactRef? Artifact { get; init; }

    public ProviderMediaRef? ProviderRef { get; init; }
}

public enum InputContentKind
{
    Audio = 0,
    Image = 1,
    Video = 2,
    Document = 3,
    Text = 4,
    Unknown = 5
}

public enum InputContentSourceKind
{
    TypedContent = 0,
    DataContent = 1,
    UriContent = 2,
    HostedFileContent = 3,
    WorkspaceContent = 4,
    Artifact = 5,
    MetadataOnly = 6
}

public enum InputContentResolutionKind
{
    InlineBytes = 0,
    WorkspaceContentRef = 1,
    HostedFileRef = 2,
    ProviderReadableUri = 3,
    ArtifactRef = 4,
    MetadataOnly = 5,
    Rejected = 6,
    Failed = 7
}

public sealed record InputContentResolution
{
    public required InputContentId InputContentId { get; init; }

    public required InputContentResolutionKind Kind { get; init; }

    public InputMediaDisposition Disposition { get; init; }

    public string? Reason { get; init; }
}
