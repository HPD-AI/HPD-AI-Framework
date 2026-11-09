// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Base;

namespace HPD.Agent.Stores.HpdBase;

/// <summary>Persists stored agent definitions through HPD Base collections.</summary>
public sealed class HpdBaseAgentStore : IAgentStore
{
    private const int PageSize = 250;
    private static readonly PrincipalContext StorePrincipal = new()
    {
        AuthenticationState = PrincipalAuthenticationState.System,
        SubjectKind = AccessSubjectKind.System,
        SubjectId = "hpd-agent-stores",
    };

    private readonly BaseSession _session;

    /// <summary>Creates an HPD Base-backed agent definition store.</summary>
    public HpdBaseAgentStore(IBaseSessionFactory sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        _session = sessions.For(StorePrincipal);
    }

    /// <inheritdoc />
    public StorePersistenceCapability PersistenceCapability =>
        StorePersistenceCapability.RestartDurable;

    /// <inheritdoc />
    public async Task<StoredAgent?> LoadAsync(
        string agentId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        BaseResult<BaseRecord<HpdBaseAgentDefinition>> result = await _session
            .Collection(HpdBaseAgentDefinition.Collection)
            .GetAsync(RecordId.Create(RecordKey(agentId)), ct)
            .ConfigureAwait(false);

        if (result is BaseFailure<BaseRecord<HpdBaseAgentDefinition>> failure &&
            failure.Status == OperationStatus.NotFound)
            return null;

        BaseRecord<HpdBaseAgentDefinition> record = result.RequireValue();
        return Deserialize(record!.Value.Payload, agentId);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        StoredAgent agent,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(agent.Id);

        string payload = JsonSerializer.Serialize(
            agent,
            HPDJsonContext.Default.StoredAgent);
        HpdBaseAgentDefinition definition = new()
        {
            AgentId = agent.Id,
            Name = agent.Name,
            Payload = payload,
            CreatedAt = new DateTimeOffset(agent.CreatedAt, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(agent.UpdatedAt, TimeSpan.Zero),
        };

        await _session
            .Collection(HpdBaseAgentDefinition.Collection)
            .UpsertAsync(
                RecordId.Create(RecordKey(agent.Id)),
                definition,
                definition,
                cancellationToken: ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        string agentId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        BaseCollectionSession<HpdBaseAgentDefinition> collection =
            _session.Collection(HpdBaseAgentDefinition.Collection);
        RecordId id = RecordId.Create(RecordKey(agentId));
        BaseResult<BaseRecord<HpdBaseAgentDefinition>> existing =
            await collection.GetAsync(id, ct).ConfigureAwait(false);
        if (!existing.TryGetValue(out _))
        {
            if (existing is BaseFailure<BaseRecord<HpdBaseAgentDefinition>> failure &&
                failure.Status == OperationStatus.NotFound)
            {
                return;
            }
            existing.RequireValue();
        }

        await collection.DeleteAsync(id, cancellationToken: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<List<string>> ListIdsAsync(CancellationToken ct = default)
    {
        var ids = new List<string>();
        BaseQuery<HpdBaseAgentDefinition> query = _session
            .Collection(HpdBaseAgentDefinition.Collection)
            .Query()
            .OrderBy(HpdBaseAgentDefinition.Fields.AgentId)
            .Take(PageSize);

        while (true)
        {
            BasePage<BaseRecord<HpdBaseAgentDefinition>> page =
                (await query.PageAsync(ct).ConfigureAwait(false)).RequireValue();
            ids.AddRange(page.Items.Select(item => item.Value.AgentId));
            if (!page.Page.HasMore || string.IsNullOrWhiteSpace(page.Page.NextCursor))
                return ids;

            query = query.ContinueFrom(page.Page.NextCursor);
        }
    }

    private static StoredAgent Deserialize(string payload, string agentId)
    {
        try
        {
            return JsonSerializer.Deserialize(
                       payload,
                       HPDJsonContext.Default.StoredAgent)
                   ?? throw new JsonException("Stored agent payload was JSON null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Stored agent definition '{agentId}' contains invalid JSON.",
                ex);
        }
    }

    private static string RecordKey(string agentId) =>
        "agent_" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(agentId)))
            .ToLowerInvariant();
}
