using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.AI.Platform.Studio;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Serialization;
using HPD.Agent.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Studio;

/// <summary>Reviews and creates one Agent definition over the canonical definition service.</summary>
internal sealed class AgentStudioCreationCommandProducer(IServiceProvider services) : IStudioCommandProducer
{
    private readonly ConcurrentDictionary<string, PreviewEvidence> _previews =
        new(StringComparer.Ordinal);

    public async ValueTask<StudioCanonicalJson?> PreviewAsync(
        StudioProducerInvocation invocation, CancellationToken cancellationToken)
    {
        if (!await AuthorizedAsync(invocation, "base.studio.action.preview", cancellationToken)
                .ConfigureAwait(false) ||
            !TryReadPreview(invocation.Request, out PreviewRequest? request) ||
            request is null ||
            !StringComparer.Ordinal.Equals(request.ResponseAuthorityChecksum,
                Hex(invocation.Authority.Checksum)) ||
            !TargetMatches(request.Target, invocation))
            return null;

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<IAgentDefinitionService>() is not { } definitions ||
            await definitions.GetAgentAsync(request.AgentId, cancellationToken).ConfigureAwait(false) is not null)
            return null;
        AgentConfig? config = TryDeserializeAndValidate(scope.ServiceProvider, request.ConfigJson);
        if (config is null) return null;

        string canonicalConfig = HpdAgentConfigSerializer.Serialize(config);
        string configChecksum = Hash(canonicalConfig);
        DateTimeOffset now = UtcNow;
        DateTimeOffset expiresAt = now.AddMinutes(5);
        string previewChecksum = Hash(string.Join('\n', AgentStudioCreationContracts.CreateAgent,
            request.Target.AuthorityChecksum, request.ResponseAuthorityChecksum,
            request.AgentId, canonicalConfig));
        foreach ((string key, PreviewEvidence stale) in _previews)
            if (stale.ExpiresAtUtc <= now) _previews.TryRemove(key, out _);
        if (_previews.Count >= 1_024 ||
            !_previews.TryAdd(previewChecksum, new PreviewEvidence(
                request.Target, request.AgentId, request.ConfigJson,
                request.ResponseAuthorityChecksum, expiresAt)))
            return null;
        return Result("preview", previewChecksum, expiresAt, null, request.AgentId,
            config.Name, configChecksum, false);
    }

    public async ValueTask<StudioCanonicalJson?> ExecuteAsync(
        StudioProducerInvocation invocation, CancellationToken cancellationToken)
    {
        if (!await AuthorizedAsync(invocation, "base.studio.action.execute", cancellationToken)
                .ConfigureAwait(false) ||
            !TryReadExecute(invocation.Request, out ExecuteRequest? request) ||
            request is null ||
            !AcknowledgementMatches(request.Acknowledgement, request.PreviewChecksum) ||
            !_previews.TryRemove(request.PreviewChecksum, out PreviewEvidence? preview) ||
            preview.ExpiresAtUtc <= UtcNow ||
            !preview.Target.SameIdentity(request.Target) ||
            !StringComparer.Ordinal.Equals(preview.ResponseAuthorityChecksum,
                Hex(invocation.Authority.Checksum)) ||
            !StringComparer.Ordinal.Equals(request.ResponseAuthorityChecksum,
                preview.ResponseAuthorityChecksum))
            throw new StudioCommandFailedBeforeInfluenceException();

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<IAgentDefinitionService>() is not { } definitions ||
            await definitions.GetAgentAsync(preview.AgentId, cancellationToken).ConfigureAwait(false) is not null)
            throw new StudioCommandFailedBeforeInfluenceException();
        AgentConfig? config = TryDeserializeAndValidate(scope.ServiceProvider, preview.ConfigJson);
        if (config is null) throw new StudioCommandFailedBeforeInfluenceException();

        AgentServiceResult<StoredAgentDto> created;
        try
        {
            created = await definitions.CreateAgentAsync(
                new CreateAgentRequest(preview.AgentId, config, null), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new StudioCommandIndeterminateException();
        }
        catch
        {
            throw new StudioCommandIndeterminateException();
        }
        if (created.Status != AgentServiceStatus.Success || created.Value is null)
            throw new StudioCommandFailedBeforeInfluenceException();

        string canonicalConfig = HpdAgentConfigSerializer.Serialize(created.Value.Config);
        string configChecksum = Hash(canonicalConfig);
        string receiptChecksum = Hash(string.Join('\n', AgentStudioCreationContracts.CreateAgent,
            request.RequestIdentity, request.PreviewChecksum, created.Value.Id, configChecksum,
            created.Value.UpdatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
        return Result("execute", request.PreviewChecksum, null, receiptChecksum,
            created.Value.Id, created.Value.Name, configChecksum, true);
    }

    private async ValueTask<bool> AuthorizedAsync(
        StudioProducerInvocation invocation, string capability, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        AuthorizationResult result = await scope.ServiceProvider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(invocation.Bootstrap.HttpContext.User, null, capability)
            .ConfigureAwait(false);
        return result.Succeeded;
    }

    private static AgentConfig? TryDeserializeAndValidate(IServiceProvider services, string configJson)
    {
        AgentConfig? config;
        try
        {
            config = HpdAgentConfigSerializer.Deserialize(configJson);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
        if (config is null) return null;
        HPDAgentHostOptions? host = services.GetService<IOptions<HPDAgentHostOptions>>()?.Value;
        return AgentConfigValidator.Validate(config, host?.ValidationLimits).Count == 0 ? config : null;
    }

    private static bool TargetMatches(ApplicationTarget target, StudioProducerInvocation invocation) =>
        StringComparer.Ordinal.Equals(target.ApplicationId,
            invocation.Bootstrap.ApplicationGraph.ApplicationId) &&
        StringComparer.Ordinal.Equals(target.AuthorityChecksum,
            Hex(new StudioApplicationResource(target.ApplicationId).AuthorityChecksum));

    private static bool TryReadPreview(StudioCanonicalJson request, out PreviewRequest? value)
    {
        value = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(request.ToArray());
            JsonElement root = document.RootElement;
            if (!Exact(root, "commandId", "input", "pageId", "responseAuthorityChecksum", "target") ||
                !StringComparer.Ordinal.Equals(root.GetProperty("commandId").GetString(),
                    AgentStudioCreationContracts.CreateAgent) ||
                !StringComparer.Ordinal.Equals(root.GetProperty("pageId").GetString(), "agent.creation") ||
                !TryTarget(root.GetProperty("target"), out ApplicationTarget? target) ||
                target is null ||
                !TryInput(root.GetProperty("input"), out string? agentId, out string? configJson))
                return false;
            string responseAuthority = root.GetProperty("responseAuthorityChecksum").GetString() ?? "";
            if (!ValidSha256(responseAuthority)) return false;
            value = new PreviewRequest(target, agentId!, configJson!, responseAuthority);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryReadExecute(StudioCanonicalJson request, out ExecuteRequest? value)
    {
        value = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(request.ToArray());
            JsonElement root = document.RootElement;
            if (!Exact(root, "acknowledgements", "commandId", "freshAuthentication", "pageId",
                    "preview", "requestIdentity", "responseAuthorityChecksum", "target") ||
                !StringComparer.Ordinal.Equals(root.GetProperty("commandId").GetString(),
                    AgentStudioCreationContracts.CreateAgent) ||
                !StringComparer.Ordinal.Equals(root.GetProperty("pageId").GetString(), "agent.creation") ||
                root.GetProperty("freshAuthentication").ValueKind != JsonValueKind.Null ||
                root.GetProperty("acknowledgements").GetArrayLength() != 1 ||
                !TryTarget(root.GetProperty("target"), out ApplicationTarget? target) ||
                target is null)
                return false;
            JsonElement preview = root.GetProperty("preview");
            JsonElement acknowledgement = root.GetProperty("acknowledgements")[0];
            if (!Exact(preview, "acknowledgements", "agentId", "configChecksum", "created",
                    "expiresAtUtc", "mode", "name", "previewChecksum", "receiptChecksum") ||
                !StringComparer.Ordinal.Equals(preview.GetProperty("mode").GetString(), "preview") ||
                preview.GetProperty("created").ValueKind != JsonValueKind.False ||
                preview.GetProperty("receiptChecksum").ValueKind != JsonValueKind.Null ||
                !Exact(acknowledgement, "impactId", "previewChecksum", "purposeId"))
                return false;
            string? requestIdentity = root.GetProperty("requestIdentity").GetString();
            string? previewChecksum = preview.GetProperty("previewChecksum").GetString();
            string? responseAuthority = root.GetProperty("responseAuthorityChecksum").GetString();
            if (requestIdentity is not { Length: >= 1 and <= 128 } ||
                requestIdentity.Any(static character =>
                    !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-') ||
                !ValidSha256(previewChecksum) || !ValidSha256(responseAuthority))
                return false;
            value = new ExecuteRequest(target,
                new Acknowledgement(
                    acknowledgement.GetProperty("purposeId").GetString() ?? "",
                    acknowledgement.GetProperty("impactId").GetString() ?? "",
                    acknowledgement.GetProperty("previewChecksum").GetString() ?? ""),
                requestIdentity, previewChecksum!, responseAuthority!);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryInput(JsonElement input, out string? agentId, out string? configJson)
    {
        agentId = null;
        configJson = null;
        if (!Exact(input, "agentId", "configJson")) return false;
        agentId = input.GetProperty("agentId").GetString();
        configJson = input.GetProperty("configJson").GetString();
        return agentId is { Length: >= 1 and <= 128 } &&
            agentId.All(static character =>
                char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_') &&
            configJson is { Length: >= 2 and <= 1_048_576 };
    }

    private static bool TryTarget(JsonElement element, out ApplicationTarget? target)
    {
        target = null;
        if (!Exact(element, "applicationId", "authorityChecksum", "kind") ||
            !StringComparer.Ordinal.Equals(element.GetProperty("kind").GetString(), "application"))
            return false;
        string applicationId = element.GetProperty("applicationId").GetString() ?? "";
        string authorityChecksum = element.GetProperty("authorityChecksum").GetString() ?? "";
        if (applicationId.Length == 0 || !ValidSha256(authorityChecksum)) return false;
        target = new ApplicationTarget(applicationId, authorityChecksum);
        return true;
    }

    private static bool AcknowledgementMatches(Acknowledgement value, string previewChecksum) =>
        StringComparer.Ordinal.Equals(value.PurposeId, AgentStudioCreationContracts.ConfirmationId) &&
        StringComparer.Ordinal.Equals(value.ImpactId, AgentStudioCreationContracts.ImpactId) &&
        StringComparer.Ordinal.Equals(value.PreviewChecksum, previewChecksum);

    private static StudioCanonicalJson Result(string mode, string previewChecksum,
        DateTimeOffset? expiresAt, string? receiptChecksum, string agentId,
        string name, string configChecksum, bool created)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("acknowledgements");
            writer.WriteStartObject();
            writer.WriteString("impactId", AgentStudioCreationContracts.ImpactId);
            writer.WriteString("purposeId", AgentStudioCreationContracts.ConfirmationId);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteString("agentId", agentId);
            writer.WriteString("configChecksum", configChecksum);
            writer.WriteBoolean("created", created);
            WriteNullable(writer, "expiresAtUtc", expiresAt is { } expiry ? Time(expiry) : null);
            writer.WriteString("mode", mode);
            writer.WriteString("name", name);
            writer.WriteString("previewChecksum", previewChecksum);
            WriteNullable(writer, "receiptChecksum", receiptChecksum);
            writer.WriteEndObject();
        }
        return StudioCanonicalJson.Create(buffer.WrittenSpan, 1_048_576);
    }

    private static bool Exact(JsonElement value, params string[] names) =>
        value.ValueKind == JsonValueKind.Object &&
        value.EnumerateObject().Select(static item => item.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(names.Order(StringComparer.Ordinal), StringComparer.Ordinal);

    private static bool ValidSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null) writer.WriteNull(name);
        else writer.WriteString(name, value);
    }

    private static string Time(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static string Hex(StudioSha256 value) =>
        Convert.ToHexString(value.ToArray()).ToLowerInvariant();

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private DateTimeOffset UtcNow =>
        services.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;

    private sealed record ApplicationTarget(string ApplicationId, string AuthorityChecksum)
    {
        public bool SameIdentity(ApplicationTarget other) =>
            StringComparer.Ordinal.Equals(ApplicationId, other.ApplicationId) &&
            StringComparer.Ordinal.Equals(AuthorityChecksum, other.AuthorityChecksum);
    }

    private sealed record PreviewRequest(
        ApplicationTarget Target,
        string AgentId,
        string ConfigJson,
        string ResponseAuthorityChecksum);

    private sealed record ExecuteRequest(
        ApplicationTarget Target,
        Acknowledgement Acknowledgement,
        string RequestIdentity,
        string PreviewChecksum,
        string ResponseAuthorityChecksum);

    private sealed record Acknowledgement(string PurposeId, string ImpactId, string PreviewChecksum);

    private sealed record PreviewEvidence(
        ApplicationTarget Target,
        string AgentId,
        string ConfigJson,
        string ResponseAuthorityChecksum,
        DateTimeOffset ExpiresAtUtc);
}
