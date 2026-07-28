using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using HPD.Base.Dependencies.Configuration;
using HPD.Base.Events;

namespace HPD.Base.Dependencies.Internal;

internal sealed class DefaultBaseDependencyServices :
    IBaseDependencyReferenceFactory,
    IBaseDependencyInvalidationMapper,
    IBaseDependencyTemplateProvider
{
    private const string VersionPrefix = "d1.";
    private readonly byte[] _key;
    private readonly int _maxReferences;
    private readonly IReadOnlyList<IBaseMutationDependencyRule> _rules;
    private readonly Dictionary<string, BaseDependencyTemplate> _templates;

    public DefaultBaseDependencyServices(
        BaseDependencyOptions options,
        IEnumerable<BaseDependencyTemplate> templates,
        IEnumerable<IBaseMutationDependencyRule> rules)
    {
        ArgumentNullException.ThrowIfNull(options);
        _key = options.ProtectionKey.ToArray();
        _maxReferences = options.MaxReferencesPerInvalidation;
        _rules = rules.ToArray();
        _templates = templates.ToDictionary(static template => template.Id, StringComparer.Ordinal);
        Templates = _templates.Values.OrderBy(static template => template.Id, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<BaseDependencyTemplate> Templates { get; }

    public BaseDependencyReference Create(string templateId, params BaseDependencyParameter[] parameters)
    {
        if (!_templates.TryGetValue(templateId, out var template))
            throw new ArgumentException("Dependency template is not registered.", nameof(templateId));
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.Length != template.ParameterNames.Length)
            throw new ArgumentException("Dependency parameter count does not match the registered template.", nameof(parameters));

        var ordered = new BaseDependencyParameter[parameters.Length];
        for (var index = 0; index < template.ParameterNames.Length; index++)
        {
            var expected = template.ParameterNames[index];
            var matches = parameters.Where(parameter => string.Equals(parameter.Name, expected, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1)
                throw new ArgumentException("Dependency parameters must match registered names exactly.", nameof(parameters));
            ordered[index] = matches[0];
        }

        var writer = new ArrayBufferWriter<byte>();
        Write(writer, templateId);
        foreach (var parameter in ordered)
        {
            if (parameter.Value is { Length: > 4096 })
                throw new ArgumentException("Dependency parameter values cannot exceed 4096 characters.", nameof(parameters));
            Write(writer, parameter.Name);
            Write(writer, parameter.Value);
        }

        var digest = HMACSHA256.HashData(_key, writer.WrittenSpan);
        return new BaseDependencyReference
        {
            TemplateId = templateId,
            Value = VersionPrefix + Base64Url(digest)
        };
    }

    public BaseDependencySet CreateSet(params BaseDependencyReference[] references)
    {
        ArgumentNullException.ThrowIfNull(references);
        return new BaseDependencySet { References = Deduplicate(references, int.MaxValue) };
    }

    public async ValueTask<BaseDependencyInvalidation> MapAsync(
        BaseRecordMutationEvent mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        cancellationToken.ThrowIfCancellationRequested();
        var collectionId = mutation.Resource.CollectionId
            ?? throw new ArgumentException("A record mutation must identify its collection.", nameof(mutation));
        var recordId = mutation.Resource.RecordId
            ?? throw new ArgumentException("A record mutation must identify its record.", nameof(mutation));

        var references = new List<BaseDependencyReference>
        {
            Create(BaseDependencyIds.Collection,
                new BaseDependencyParameter("tenant", mutation.TenantId),
                new BaseDependencyParameter("collection", collectionId)),
            Create(BaseDependencyIds.Record,
                new BaseDependencyParameter("tenant", mutation.TenantId),
                new BaseDependencyParameter("collection", collectionId),
                new BaseDependencyParameter("record", recordId.Value))
        };

        foreach (var rule in _rules)
        {
            var inputs = await rule.ResolveAsync(mutation, cancellationToken).ConfigureAwait(false);
            foreach (var input in inputs)
                references.Add(Create(input.TemplateId, input.Parameters));
        }

        var resolved = Deduplicate(references, int.MaxValue);
        if (resolved.Length > _maxReferences)
            throw new BaseDependencyInvalidationException(
                "Dependency invalidation exceeds the configured reference limit.");

        return new BaseDependencyInvalidation
        {
            EventId = mutation.EventId,
            OccurredAt = mutation.Timestamp,
            Reason = BaseDependencyInvalidationReasons.RecordMutation,
            References = resolved
        };
    }

    private static BaseDependencyReference[] Deduplicate(
        IEnumerable<BaseDependencyReference> references,
        int maximum) =>
        references
            .DistinctBy(static reference => (reference.TemplateId, reference.Value))
            .OrderBy(static reference => reference.TemplateId, StringComparer.Ordinal)
            .ThenBy(static reference => reference.Value, StringComparer.Ordinal)
            .Take(maximum)
            .ToArray();

    private static void Write(IBufferWriter<byte> writer, string? value)
    {
        if (value is null)
        {
            var nullLength = writer.GetSpan(sizeof(int));
            BinaryPrimitives.WriteInt32BigEndian(nullLength, -1);
            writer.Advance(sizeof(int));
            return;
        }

        var byteCount = Encoding.UTF8.GetByteCount(value);
        var span = writer.GetSpan(sizeof(int) + byteCount);
        BinaryPrimitives.WriteInt32BigEndian(span, byteCount);
        Encoding.UTF8.GetBytes(value, span[sizeof(int)..]);
        writer.Advance(sizeof(int) + byteCount);
    }

    private static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
