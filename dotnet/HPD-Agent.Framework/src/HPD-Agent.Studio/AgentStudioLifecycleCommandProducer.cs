// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.AI.Platform.Studio;
using HPD.Agent;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Studio;

/// <summary>
/// Reviews and executes destructive session/thread lifecycle commands. Fresh authentication is
/// enforced by the shared Studio command endpoint before this producer executes.
/// </summary>
internal sealed class AgentStudioLifecycleCommandProducer(IServiceProvider services, string commandId)
    : IStudioCommandProducer
{
    private const string SessionConfirmationId = "confirm.session.delete";
    private const string SessionImpactId = "impact.session.delete";
    private const string ThreadConfirmationId = "confirm.thread.delete";
    private const string ThreadImpactId = "impact.thread.delete";
    private readonly ConcurrentDictionary<string, PreviewEvidence> _previews =
        new(StringComparer.Ordinal);

    public async ValueTask<StudioCanonicalJson?> PreviewAsync(
        StudioProducerInvocation invocation, CancellationToken cancellationToken)
    {
        if (!await AuthorizedAsync(invocation, "base.studio.action.preview", cancellationToken)
                .ConfigureAwait(false) ||
            !TryReadPreview(invocation.Request, out Target? target, out JsonElement input,
                out string? responseAuthorityChecksum) ||
            !StringComparer.Ordinal.Equals(responseAuthorityChecksum,
                Hex(invocation.Authority.Checksum)) ||
            target is null || !TargetMatches(target, invocation))
            return null;

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        if (!await ExistsAsync(scope.ServiceProvider, target, cancellationToken).ConfigureAwait(false))
            return null;
        DateTimeOffset now = UtcNow;
        DateTimeOffset expiresAt = now.AddMinutes(5);
        string previewChecksum = Hash(String.Join('\n', commandId, target.AuthorityChecksum,
            Hex(invocation.Authority.Checksum), input.GetRawText()));
        foreach ((string key, PreviewEvidence stale) in _previews)
            if (stale.ExpiresAtUtc <= now) _previews.TryRemove(key, out _);
        if (_previews.Count >= 1_024 ||
            !_previews.TryAdd(previewChecksum, new PreviewEvidence(
                target, input.Clone(), Hex(invocation.Authority.Checksum), expiresAt)))
            return null;
        return Result("preview", previewChecksum, expiresAt, null, null, true);
    }

    public async ValueTask<StudioCanonicalJson?> ExecuteAsync(
        StudioProducerInvocation invocation, CancellationToken cancellationToken)
    {
        if (!await AuthorizedAsync(invocation, "base.studio.action.execute", cancellationToken)
                .ConfigureAwait(false) ||
            !TryReadExecute(invocation.Request, out Target? target, out string? requestIdentity,
                out string? previewChecksum, out string? responseAuthorityChecksum,
                out Acknowledgement? acknowledgement) ||
            target is null || requestIdentity is null || previewChecksum is null ||
            acknowledgement is null ||
            !AcknowledgementMatches(acknowledgement, previewChecksum) ||
            !_previews.TryRemove(previewChecksum, out PreviewEvidence? preview) ||
            preview.ExpiresAtUtc <= UtcNow ||
            !preview.Target.SameIdentity(target) ||
            !StringComparer.Ordinal.Equals(preview.ResponseAuthorityChecksum,
                Hex(invocation.Authority.Checksum)) ||
            !StringComparer.Ordinal.Equals(responseAuthorityChecksum,
                preview.ResponseAuthorityChecksum))
            throw new StudioCommandFailedBeforeInfluenceException();

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        bool deleted;
        try
        {
            deleted = await DeleteAsync(scope.ServiceProvider, target, preview.Input, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new StudioCommandIndeterminateException();
        }
        catch
        {
            throw new StudioCommandIndeterminateException();
        }

        if (!deleted) throw new StudioCommandFailedBeforeInfluenceException();
        string receipt = Hash(String.Join('\n', commandId, requestIdentity, previewChecksum,
            target.Kind, target.SessionId, target.ThreadId ?? ""));
        return Result("execute", previewChecksum, null, receipt,
            new CommandResult(true, new Dictionary<string, Action<Utf8JsonWriter>>
            {
                ["deleted"] = writer => writer.WriteBoolean("deleted", true)
            }), true);
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

    private static bool TargetMatches(Target target, StudioProducerInvocation invocation) =>
        StringComparer.Ordinal.Equals(target.ApplicationId,
            invocation.Bootstrap.ApplicationGraph.ApplicationId) &&
        StringComparer.Ordinal.Equals(target.AuthorityChecksum,
            Hex(Resource(target).AuthorityChecksum));

    private static StudioResourceIdentity Resource(Target target) => target.Kind switch
    {
        "agentSession" => new StudioAgentSessionResource(target.ApplicationId, target.SessionId),
        "agentThread" => new StudioAgentThreadResource(
            target.ApplicationId, target.SessionId, target.ThreadId!),
        _ => throw new InvalidOperationException("agent.studio.lifecycleTargetInvalid")
    };

    private static async ValueTask<bool> ExistsAsync(
        IServiceProvider scopedServices, Target target, CancellationToken cancellationToken)
    {
        if (target.Kind == "agentSession")
            return scopedServices.GetService<IAgentSessionService>() is { } sessions &&
                await sessions.GetSessionAsync(target.SessionId, cancellationToken).ConfigureAwait(false) is not null;
        return scopedServices.GetService<IAgentThreadService>() is { } threads &&
            (await threads.GetThreadAsync(target.SessionId, target.ThreadId!, cancellationToken)
                .ConfigureAwait(false)).Status == AgentServiceStatus.Success;
    }

    private async ValueTask<bool> DeleteAsync(
        IServiceProvider scopedServices, Target target, JsonElement input,
        CancellationToken cancellationToken)
    {
        if (target.Kind == "agentSession")
            return scopedServices.GetService<IAgentSessionService>() is { } sessions &&
                await sessions.DeleteSessionAsync(target.SessionId, cancellationToken).ConfigureAwait(false);
        bool recursive = commandId == AgentStudioExperimentContracts.DeleteThread &&
            input.TryGetProperty("recursive", out JsonElement value) && value.GetBoolean();
        return scopedServices.GetService<IAgentThreadService>() is { } threads &&
            (await threads.DeleteThreadAsync(target.SessionId, target.ThreadId!, recursive, cancellationToken)
                .ConfigureAwait(false)).Status == AgentServiceStatus.Success;
    }

    private DateTimeOffset UtcNow =>
        services.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;

    private bool TryReadPreview(StudioCanonicalJson request, out Target? target, out JsonElement input,
        out string? responseAuthorityChecksum)
    {
        target = null;
        input = default;
        responseAuthorityChecksum = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(request.ToArray());
            JsonElement root = document.RootElement;
            if (!Exact(root, "commandId", "input", "pageId", "responseAuthorityChecksum", "target") ||
                !StringComparer.Ordinal.Equals(root.GetProperty("commandId").GetString(), commandId) ||
                !StringComparer.Ordinal.Equals(root.GetProperty("pageId").GetString(), ExpectedPage()) ||
                !TryTarget(root.GetProperty("target"), out target) ||
                !ValidInput(root.GetProperty("input")))
                return false;
            input = root.GetProperty("input").Clone();
            responseAuthorityChecksum = root.GetProperty("responseAuthorityChecksum").GetString();
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private bool TryReadExecute(StudioCanonicalJson request, out Target? target,
        out string? requestIdentity, out string? previewChecksum, out string? responseAuthorityChecksum,
        out Acknowledgement? acknowledgement)
    {
        target = null;
        requestIdentity = null;
        previewChecksum = null;
        responseAuthorityChecksum = null;
        acknowledgement = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(request.ToArray());
            JsonElement root = document.RootElement;
            if (!Exact(root, "acknowledgements", "commandId", "freshAuthentication", "pageId",
                    "preview", "requestIdentity", "responseAuthorityChecksum", "target") ||
                !StringComparer.Ordinal.Equals(root.GetProperty("commandId").GetString(), commandId) ||
                !StringComparer.Ordinal.Equals(root.GetProperty("pageId").GetString(), ExpectedPage()) ||
                !ValidFreshAuthentication(root.GetProperty("freshAuthentication")) ||
                root.GetProperty("acknowledgements").GetArrayLength() != 1 ||
                !TryTarget(root.GetProperty("target"), out target))
                return false;
            JsonElement preview = root.GetProperty("preview");
            JsonElement evidence = root.GetProperty("acknowledgements")[0];
            if (!Exact(preview, "acknowledgements", "deleted", "expiresAtUtc", "mode",
                    "previewChecksum", "receiptChecksum") ||
                !StringComparer.Ordinal.Equals(preview.GetProperty("mode").GetString(), "preview") ||
                preview.GetProperty("deleted").ValueKind != JsonValueKind.False ||
                preview.GetProperty("receiptChecksum").ValueKind != JsonValueKind.Null ||
                !TryCanonicalTime(preview.GetProperty("expiresAtUtc"), out _) ||
                !Exact(evidence, "impactId", "previewChecksum", "purposeId"))
                return false;
            requestIdentity = root.GetProperty("requestIdentity").GetString();
            previewChecksum = preview.GetProperty("previewChecksum").GetString();
            responseAuthorityChecksum = root.GetProperty("responseAuthorityChecksum").GetString();
            acknowledgement = new Acknowledgement(
                evidence.GetProperty("purposeId").GetString() ?? "",
                evidence.GetProperty("impactId").GetString() ?? "",
                evidence.GetProperty("previewChecksum").GetString() ?? "");
            return requestIdentity is { Length: >= 1 and <= 128 } &&
                requestIdentity.All(ValidIdentityCharacter) &&
                ValidSha256(previewChecksum) &&
                ValidSha256(responseAuthorityChecksum);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private bool TryTarget(JsonElement element, out Target? target)
    {
        target = null;
        try
        {
            string[] expected = commandId == AgentStudioExperimentContracts.DeleteSession
                ? ["applicationId", "authorityChecksum", "kind", "sessionId"]
                : ["applicationId", "authorityChecksum", "kind", "sessionId", "threadId"];
            if (!Exact(element, expected)) return false;
            string kind = element.GetProperty("kind").GetString() ?? "";
            if (commandId == AgentStudioExperimentContracts.DeleteSession && kind != "agentSession" ||
                commandId == AgentStudioExperimentContracts.DeleteThread && kind != "agentThread")
                return false;
            target = new Target(
                kind,
                element.GetProperty("applicationId").GetString() ?? "",
                element.GetProperty("authorityChecksum").GetString() ?? "",
                element.GetProperty("sessionId").GetString() ?? "",
                kind == "agentThread" ? element.GetProperty("threadId").GetString() : null);
            return target.ApplicationId.Length > 0 && target.SessionId.Length > 0 &&
                ValidSha256(target.AuthorityChecksum) &&
                (kind != "agentThread" || target.ThreadId is { Length: > 0 });
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private string ExpectedPage() => commandId == AgentStudioExperimentContracts.DeleteSession
        ? "session.detail" : "thread.detail";

    private bool ValidInput(JsonElement input) =>
        commandId == AgentStudioExperimentContracts.DeleteSession
            ? Exact(input)
            : Exact(input, "recursive") &&
              input.GetProperty("recursive").ValueKind is JsonValueKind.True or JsonValueKind.False;

    private bool AcknowledgementMatches(Acknowledgement value, string previewChecksum)
    {
        (string purpose, string impact) = commandId == AgentStudioExperimentContracts.DeleteSession
            ? (SessionConfirmationId, SessionImpactId)
            : (ThreadConfirmationId, ThreadImpactId);
        return StringComparer.Ordinal.Equals(value.PurposeId, purpose) &&
            StringComparer.Ordinal.Equals(value.ImpactId, impact) &&
            StringComparer.Ordinal.Equals(value.PreviewChecksum, previewChecksum);
    }

    private StudioCanonicalJson Result(string mode, string previewChecksum,
        DateTimeOffset? expiresAt, string? receiptChecksum, CommandResult? result, bool acknowledged)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("acknowledgements");
            if (acknowledged)
            {
                writer.WriteStartObject();
                (string purpose, string impact) = commandId == AgentStudioExperimentContracts.DeleteSession
                    ? (SessionConfirmationId, SessionImpactId)
                    : (ThreadConfirmationId, ThreadImpactId);
                writer.WriteString("impactId", impact);
                writer.WriteString("purposeId", purpose);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            if (result is null) writer.WriteBoolean("deleted", false);
            else writer.WriteBoolean("deleted", true);
            WriteNullable(writer, "expiresAtUtc",
                expiresAt is { } expiry ? Time(expiry) : null);
            writer.WriteString("mode", mode);
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

    private static bool ValidFreshAuthentication(JsonElement value) =>
        value.ValueKind == JsonValueKind.String &&
        value.GetString() is { Length: >= 32 and <= 4096 } protectedValue &&
        protectedValue.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static bool ValidSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool ValidIdentityCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '.' or '-';

    private static bool TryCanonicalTime(JsonElement value, out DateTimeOffset time) =>
        DateTimeOffset.TryParseExact(value.GetString(), "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out time);

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

    private sealed record Target(
        string Kind,
        string ApplicationId,
        string AuthorityChecksum,
        string SessionId,
        string? ThreadId)
    {
        public bool SameIdentity(Target other) =>
            StringComparer.Ordinal.Equals(Kind, other.Kind) &&
            StringComparer.Ordinal.Equals(ApplicationId, other.ApplicationId) &&
            StringComparer.Ordinal.Equals(AuthorityChecksum, other.AuthorityChecksum) &&
            StringComparer.Ordinal.Equals(SessionId, other.SessionId) &&
            StringComparer.Ordinal.Equals(ThreadId, other.ThreadId);
    }

    private sealed record Acknowledgement(string PurposeId, string ImpactId, string PreviewChecksum);

    private sealed record PreviewEvidence(
        Target Target,
        JsonElement Input,
        string ResponseAuthorityChecksum,
        DateTimeOffset ExpiresAtUtc);

    private sealed record CommandResult(
        bool Accepted,
        IReadOnlyDictionary<string, Action<Utf8JsonWriter>> Fields);
}
