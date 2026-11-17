using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.AI.Platform.Studio;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Serialization;
using HPD.Agent.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Studio;

/// <summary>Reviews and applies one complete stored AgentConfig draft through the typed PATCH service.</summary>
internal sealed class ConfigurationUpdateProducer(IServiceProvider services) : IStudioCommandProducer
{
    private const string CommandId = "agent.configuration.update";
    private const string PageId = "agent.configuration";
    private const string ConfirmationId = "confirm.agent.configuration.update";
    private const string ImpactId = "impact.agent.configuration.update";
    private const int MaximumDiffItems = 256;
    private const int MaximumValueLength = 1024;
    private readonly ConcurrentDictionary<string, Preview> _previews = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _execution = new(1, 1);

    public async ValueTask<StudioCanonicalJson?> PreviewAsync(
        StudioProducerInvocation invocation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await AuthorizedAsync(invocation, "base.studio.action.preview", cancellationToken).ConfigureAwait(false) ||
            !TryReadPreview(invocation.Request, out PreviewRequest? request) || request is null)
            return null;

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<IAgentStudioConfigurationAuthority>() is not { } authority) return null;
        AgentStudioConfigurationSnapshot? stored =
            await authority.GetAsync(request.Target.AgentId, cancellationToken).ConfigureAwait(false);
        if (stored is null || !TargetMatches(request.Target, invocation, stored.AgentId)) return null;

        AgentConfig? draft;
        try
        {
            draft = HpdAgentConfigSerializer.Deserialize(request.ConfigJson);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
        if (draft is null) return null;
        HPDAgentHostOptions? host = scope.ServiceProvider.GetService<IOptions<HPDAgentHostOptions>>()?.Value;
        IReadOnlyList<string> errors = AgentConfigValidator.Validate(draft, host?.ValidationLimits);
        if (errors.Count > 0) return null;

        string currentJson = HpdAgentConfigSerializer.Serialize(stored.Config);
        string draftJson = HpdAgentConfigSerializer.Serialize(draft);
        IReadOnlyList<ConfigurationDiff> diff;
        try
        {
            diff = Diff(currentJson, draftJson);
        }
        catch (JsonException)
        {
            return null;
        }
        if (diff.Count == 0 || diff.Count >= MaximumDiffItems) return null;

        string responseAuthority = Hex(invocation.Authority.Checksum);
        if (!StringComparer.Ordinal.Equals(request.ResponseAuthorityChecksum, responseAuthority)) return null;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset expiresAt = now.AddMinutes(5);
        string previewChecksum = Hash(string.Join('\n', CommandId, request.Target.AuthorityChecksum,
            responseAuthority, currentJson, draftJson));
        foreach ((string key, Preview stale) in _previews)
            if (stale.ExpiresAtUtc <= now) _previews.TryRemove(key, out _);
        if (_previews.Count >= 1024) return null;
        var preview = new Preview(request.Target.ApplicationId, stored.AgentId, request.Target.AuthorityChecksum,
            currentJson, draftJson, responseAuthority, expiresAt);
        if (!_previews.TryAdd(previewChecksum, preview)) return null;
        return Result("preview", previewChecksum, expiresAt, diff, null, stored.UpdatedAt);
    }

    public async ValueTask<StudioCanonicalJson?> ExecuteAsync(
        StudioProducerInvocation invocation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await AuthorizedAsync(invocation, "base.studio.action.execute", cancellationToken).ConfigureAwait(false) ||
            !TryReadExecute(invocation.Request, out ExecuteRequest? request) || request is null)
            throw new StudioCommandFailedBeforeInfluenceException();
        await _execution.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_previews.TryRemove(request.PreviewChecksum, out Preview? preview) || preview.ExpiresAtUtc <= DateTimeOffset.UtcNow ||
                !StringComparer.Ordinal.Equals(preview.ApplicationId, request.Target.ApplicationId) ||
                !StringComparer.Ordinal.Equals(preview.AgentId, request.Target.AgentId) ||
                !StringComparer.Ordinal.Equals(preview.TargetAuthorityChecksum, request.Target.AuthorityChecksum) ||
                !StringComparer.Ordinal.Equals(preview.ResponseAuthorityChecksum, Hex(invocation.Authority.Checksum)) ||
                !StringComparer.Ordinal.Equals(request.ResponseAuthorityChecksum, preview.ResponseAuthorityChecksum) ||
                !AcknowledgementMatches(request.Acknowledgement, request.PreviewChecksum))
                throw new StudioCommandFailedBeforeInfluenceException();

            await using AsyncServiceScope scope = services.CreateAsyncScope();
            if (scope.ServiceProvider.GetService<IAgentStudioConfigurationAuthority>() is not { } authority)
                throw new StudioCommandFailedBeforeInfluenceException();
            AgentStudioConfigurationSnapshot? current =
                await authority.GetAsync(preview.AgentId, cancellationToken).ConfigureAwait(false);
            if (current is null || !StringComparer.Ordinal.Equals(
                    HpdAgentConfigSerializer.Serialize(current.Config), preview.CurrentConfigJson))
                throw new StudioCommandFailedBeforeInfluenceException();
            AgentConfig? draft;
            try
            {
                draft = HpdAgentConfigSerializer.Deserialize(preview.DraftConfigJson);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
            {
                throw new StudioCommandFailedBeforeInfluenceException();
            }
            if (draft is null) throw new StudioCommandFailedBeforeInfluenceException();

            AgentStudioConfigurationWriteResult updated;
            try
            {
                updated = await authority.WriteAsync(current.AgentId, draft, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                throw new StudioCommandIndeterminateException();
            }
            if (updated.Status != AgentStudioConfigurationWriteStatus.Success || updated.UpdatedAt is null)
                throw new StudioCommandFailedBeforeInfluenceException();

            IReadOnlyList<ConfigurationDiff> diff = Diff(preview.CurrentConfigJson, preview.DraftConfigJson);
            string receiptChecksum = Hash(string.Join('\n', CommandId, request.RequestIdentity,
                request.PreviewChecksum, preview.DraftConfigJson,
                updated.UpdatedAt.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
            return Result("execute", request.PreviewChecksum, null, diff, receiptChecksum, updated.UpdatedAt.Value);
        }
        finally
        {
            _execution.Release();
        }
    }

    private async ValueTask<bool> AuthorizedAsync(
        StudioProducerInvocation invocation, string capability, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        AuthorizationResult result = await scope.ServiceProvider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(invocation.Bootstrap.HttpContext.User, null, capability).ConfigureAwait(false);
        return result.Succeeded;
    }

    private static bool TargetMatches(CommandTarget target, StudioProducerInvocation invocation, string agentId)
    {
        return StringComparer.Ordinal.Equals(target.ApplicationId,
                invocation.Bootstrap.ApplicationGraph.ApplicationId) &&
            StringComparer.Ordinal.Equals(target.AgentId, agentId);
    }

    private static bool TryReadPreview(StudioCanonicalJson request, out PreviewRequest? value)
    {
        value = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(request.ToArray());
            JsonElement root = document.RootElement;
            if (!StringComparer.Ordinal.Equals(root.GetProperty("commandId").GetString(), CommandId) ||
                !StringComparer.Ordinal.Equals(root.GetProperty("pageId").GetString(), PageId) ||
                !TryTarget(root.GetProperty("target"), out CommandTarget? target) || target is null)
                return false;
            string configJson = root.GetProperty("input").GetProperty("configJson").GetString() ?? "";
            string responseAuthority = root.GetProperty("responseAuthorityChecksum").GetString() ?? "";
            value = new PreviewRequest(target, configJson, responseAuthority);
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
            if (!StringComparer.Ordinal.Equals(root.GetProperty("commandId").GetString(), CommandId) ||
                !StringComparer.Ordinal.Equals(root.GetProperty("pageId").GetString(), PageId) ||
                !TryTarget(root.GetProperty("target"), out CommandTarget? target) || target is null)
                return false;
            JsonElement preview = root.GetProperty("preview");
            JsonElement acknowledgement = root.GetProperty("acknowledgements")[0];
            value = new ExecuteRequest(target,
                preview.GetProperty("previewChecksum").GetString() ?? "",
                root.GetProperty("requestIdentity").GetString() ?? "",
                new CommandAcknowledgement(
                    acknowledgement.GetProperty("purposeId").GetString() ?? "",
                    acknowledgement.GetProperty("impactId").GetString() ?? "",
                    acknowledgement.GetProperty("previewChecksum").GetString() ?? ""),
                root.GetProperty("responseAuthorityChecksum").GetString() ?? "");
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryTarget(JsonElement element, out CommandTarget? target)
    {
        target = null;
        try
        {
            if (!StringComparer.Ordinal.Equals(element.GetProperty("kind").GetString(), "agent")) return false;
            target = new CommandTarget(
                element.GetProperty("applicationId").GetString() ?? "",
                element.GetProperty("agentId").GetString() ?? "",
                element.GetProperty("authorityChecksum").GetString() ?? "");
            return target.ApplicationId.Length > 0 && target.AgentId.Length > 0 &&
                target.AuthorityChecksum.Length == 64;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool AcknowledgementMatches(CommandAcknowledgement value, string previewChecksum) =>
        StringComparer.Ordinal.Equals(value.PurposeId, ConfirmationId) &&
        StringComparer.Ordinal.Equals(value.ImpactId, ImpactId) &&
        StringComparer.Ordinal.Equals(value.PreviewChecksum, previewChecksum);

    private static IReadOnlyList<ConfigurationDiff> Diff(string beforeJson, string afterJson)
    {
        using JsonDocument beforeDocument = JsonDocument.Parse(beforeJson);
        using JsonDocument afterDocument = JsonDocument.Parse(afterJson);
        var result = new List<ConfigurationDiff>();
        Diff(beforeDocument.RootElement, afterDocument.RootElement, "", result);
        return result;
    }

    private static void Diff(JsonElement before, JsonElement after, string path, List<ConfigurationDiff> result)
    {
        if (result.Count > MaximumDiffItems) return;
        if (before.ValueKind == JsonValueKind.Object && after.ValueKind == JsonValueKind.Object)
        {
            string[] names = before.EnumerateObject().Select(static value => value.Name)
                .Union(after.EnumerateObject().Select(static value => value.Name), StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray();
            foreach (string name in names)
            {
                bool hasBefore = before.TryGetProperty(name, out JsonElement beforeValue);
                bool hasAfter = after.TryGetProperty(name, out JsonElement afterValue);
                string childPath = path.Length == 0 ? name : path + "." + name;
                if (hasBefore && hasAfter) Diff(beforeValue, afterValue, childPath, result);
                else if (hasBefore) Add(childPath, "removed", beforeValue, null, result);
                else Add(childPath, "added", null, afterValue, result);
            }
            return;
        }
        if (before.ValueKind == JsonValueKind.Array && after.ValueKind == JsonValueKind.Array)
        {
            JsonElement[] beforeItems = before.EnumerateArray().ToArray();
            JsonElement[] afterItems = after.EnumerateArray().ToArray();
            int count = Math.Max(beforeItems.Length, afterItems.Length);
            for (int index = 0; index < count; index++)
            {
                bool hasBefore = index < beforeItems.Length;
                bool hasAfter = index < afterItems.Length;
                string childPath = $"{path}[{index.ToString(CultureInfo.InvariantCulture)}]";
                if (hasBefore && hasAfter) Diff(beforeItems[index], afterItems[index], childPath, result);
                else if (hasBefore) Add(childPath, "removed", beforeItems[index], null, result);
                else Add(childPath, "added", null, afterItems[index], result);
            }
            return;
        }
        if (!StringComparer.Ordinal.Equals(before.GetRawText(), after.GetRawText()))
            Add(path, "changed", before, after, result);
    }

    private static void Add(string path, string change, JsonElement? before, JsonElement? after,
        List<ConfigurationDiff> result)
    {
        if (result.Count >= MaximumDiffItems) return;
        result.Add(new ConfigurationDiff(path, change,
            before is { } oldValue ? Compact(oldValue) : null,
            after is { } newValue ? Compact(newValue) : null));
    }

    private static string Compact(JsonElement value)
    {
        string raw = value.GetRawText();
        return raw.Length <= MaximumValueLength ? raw : raw[..MaximumValueLength];
    }

    private static StudioCanonicalJson Result(string mode, string previewChecksum, DateTimeOffset? expiresAt,
        IReadOnlyList<ConfigurationDiff> diff, string? receiptChecksum, DateTime updatedAt)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("acknowledgements");
            writer.WriteStartObject();
            writer.WriteString("impactId", ImpactId);
            writer.WriteString("purposeId", ConfirmationId);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteNumber("changeCount", diff.Count);
            writer.WriteStartArray("diff");
            foreach (ConfigurationDiff value in diff)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("after");
                if (value.After is null) writer.WriteNullValue(); else writer.WriteStringValue(value.After);
                writer.WritePropertyName("before");
                if (value.Before is null) writer.WriteNullValue(); else writer.WriteStringValue(value.Before);
                writer.WriteString("change", value.Change);
                writer.WriteString("path", value.Path);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("expiresAtUtc");
            if (expiresAt is { } expiry)
                writer.WriteStringValue(expiry.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
            else writer.WriteNullValue();
            writer.WriteString("mode", mode);
            writer.WriteString("previewChecksum", previewChecksum);
            writer.WritePropertyName("receiptChecksum");
            if (receiptChecksum is null) writer.WriteNullValue(); else writer.WriteStringValue(receiptChecksum);
            writer.WriteString("updatedAtUtc", updatedAt.ToUniversalTime()
                .ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }
        return StudioCanonicalJson.Create(buffer.WrittenSpan, 1_048_576);
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Hex(StudioSha256 value) =>
        Convert.ToHexString(value.ToArray()).ToLowerInvariant();

    private sealed record CommandTarget(string ApplicationId, string AgentId, string AuthorityChecksum);
    private sealed record PreviewRequest(CommandTarget Target, string ConfigJson, string ResponseAuthorityChecksum);
    private sealed record CommandAcknowledgement(string PurposeId, string ImpactId, string PreviewChecksum);
    private sealed record ExecuteRequest(CommandTarget Target, string PreviewChecksum, string RequestIdentity,
        CommandAcknowledgement Acknowledgement, string ResponseAuthorityChecksum);
    private sealed record Preview(string ApplicationId, string AgentId, string TargetAuthorityChecksum,
        string CurrentConfigJson, string DraftConfigJson, string ResponseAuthorityChecksum, DateTimeOffset ExpiresAtUtc);
    private sealed record ConfigurationDiff(string Path, string Change, string? Before, string? After);
}
