using System.Security.Cryptography;
using HPD.Agent;
using HPD.Agent.Audio.Media;
using HPD.Agent.Audio.Output;

namespace HPD.Agent.Audio.AgentIntegration.Output;

internal sealed class OutputArtifactWriter
{
    public async ValueTask<StoredAudioArtifact> WriteAssistantAudioArtifactAsync(
        IWorkspaceStore workspace,
        AudioSessionId sessionId,
        BranchRef branch,
        OutputFlowId outputFlowId,
        ResponseId responseId,
        string providerKey,
        string? modelId,
        AssistantTextToSpeechOutputOptions options,
        string mediaType,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(options);

        var bytes = data.ToArray();
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var name = $"assistant-output-{SanitizeNamePart(outputFlowId.Value)}-{SanitizeNamePart(responseId.Value)}{ExtensionFor(mediaType, options.OutputFormat)}";
        var tags = new Dictionary<string, string>
        {
            ["kind"] = "assistant-audio",
            ["outputFlowId"] = outputFlowId.Value,
            ["responseId"] = responseId.Value,
            ["provider"] = providerKey,
            ["origin"] = ContentSource.Agent.ToString()
        };
        AddIfPresent(tags, "model", modelId);
        AddIfPresent(tags, "voice", options.VoiceId);

        var branchSpace = await EnsureBranchSpaceAsync(workspace, sessionId.Value, branch.BranchId, cancellationToken).ConfigureAwait(false);
        await using var stream = new MemoryStream(bytes, writable: false);
        var attachment = await workspace.WriteContentAsync(
            WorkspacePrincipalRef.System,
            branchSpace.Id,
            existingAttachmentId: null,
            stream,
            new WriteWorkspaceSpaceContentRequest
            {
                ContentType = mediaType,
                Role = WorkspaceContentRoles.Artifact,
                Name = name,
                PathHint = WorkspaceContentPaths.BranchArtifacts(sessionId.Value, branch.BranchId),
                Permission = WorkspacePermissions.ReadWrite,
                ContentMetadata = tags
            },
            cancellationToken).ConfigureAwait(false);
        var info = await workspace.StatContentAsync(
            WorkspacePrincipalRef.System,
            attachment.ContentId,
            attachment.ContentVersion,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Workspace content '{attachment.ContentId}' was not found after write.");

        return new StoredAudioArtifact(
            new AudioArtifactRef("hpd-content", info.Id, info.ContentType, info.SizeBytes, sha256),
            info.ContentType,
            info.SizeBytes,
            sha256);
    }

    private static async Task<WorkspaceSpaceInfo> EnsureBranchSpaceAsync(
        IWorkspaceStore workspace,
        string sessionId,
        string branchId,
        CancellationToken cancellationToken)
    {
        var sessionSpace = await EnsureSessionSpaceAsync(workspace, sessionId, cancellationToken).ConfigureAwait(false);
        var existing = await workspace.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceSessionRepository.BranchKind,
                ExternalId = branchId,
                ParentSpaceId = sessionSpace.Id
            },
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return existing;

        return await workspace.CreateChildSpaceAsync(
            WorkspacePrincipalRef.System,
            sessionSpace.Id,
            new CreateWorkspaceSpaceRequest
            {
                Kind = WorkspaceSessionRepository.BranchKind,
                ExternalId = branchId,
                Name = branchId,
                Slug = branchId
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<WorkspaceSpaceInfo> EnsureSessionSpaceAsync(
        IWorkspaceStore workspace,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var existing = await workspace.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceSessionRepository.SessionKind,
                ExternalId = sessionId
            },
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return existing;

        return await workspace.CreateSpaceAsync(
            WorkspacePrincipalRef.System,
            new CreateWorkspaceSpaceRequest
            {
                Kind = WorkspaceSessionRepository.SessionKind,
                ExternalId = sessionId,
                Name = sessionId,
                Slug = sessionId
            },
            cancellationToken).ConfigureAwait(false);
    }

    public static string? ToMediaType(string? outputFormat)
    {
        return outputFormat?.ToLowerInvariant() switch
        {
            "mp3" => "audio/mpeg",
            "wav" => "audio/wav",
            "opus" => "audio/opus",
            "aac" => "audio/aac",
            "flac" => "audio/flac",
            "pcm" => "audio/pcm",
            var value when value?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true => value,
            _ => null
        };
    }

    private static string ExtensionFor(string mediaType, string? requestedFormat)
    {
        var normalized = FirstNonWhiteSpace(requestedFormat, mediaType)?.ToLowerInvariant();
        return normalized switch
        {
            "mp3" or "audio/mpeg" => ".mp3",
            "wav" or "audio/wav" or "audio/x-wav" => ".wav",
            "opus" or "audio/opus" => ".opus",
            "aac" or "audio/aac" => ".aac",
            "flac" or "audio/flac" => ".flac",
            "pcm" or "audio/pcm" => ".pcm",
            _ => ".bin"
        };
    }

    private static string SanitizeNamePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim('-', '.', ' ');
        return string.IsNullOrWhiteSpace(sanitized) ? "value" : sanitized;
    }

    private static void AddIfPresent(IDictionary<string, string> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[key] = value!;
        }
    }

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}

internal sealed record StoredAudioArtifact(
    AudioArtifactRef Artifact,
    string MediaType,
    long SizeBytes,
    string Sha256);
