using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Contains graph-installed verification authority for schedule disaster-recovery manifests.</summary>
public sealed class BaseScheduleRecoveryKeyRegistry
{
    private readonly ImmutableArray<BaseScheduleRecoveryVerificationKey> _keys;

    internal BaseScheduleRecoveryKeyRegistry(IEnumerable<BaseScheduleRecoveryVerificationKey> keys)
    {
        BaseScheduleRecoveryVerificationKey[] materialized = keys
            .OrderBy(static key => key.Id, StringComparer.Ordinal).ThenBy(static key => key.Version).ToArray();
        if (materialized.GroupBy(static key => (key.Id, key.Version)).Any(static group => group.Count() != 1))
            throw new InvalidOperationException("base.activation.recoveryKeyInvalid");
        foreach (BaseScheduleRecoveryVerificationKey key in materialized)
        {
            BaseScheduleRecoveryVerificationKey expected = BaseScheduleRecoveryManifestContract.CreateVerificationKey(
                key.Id, key.Version, key.PublicKey.AsSpan(), key.ActiveFrom, key.RetireAfter);
            if (!expected.Checksum.AsSpan().SequenceEqual(key.Checksum.AsSpan()))
                throw new InvalidOperationException("base.activation.recoveryKeyInvalid");
        }
        _keys = materialized.Select(Clone).ToImmutableArray();
    }

    /// <summary>Gets deeply owned installed keys in canonical identity/version order.</summary>
    public ImmutableArray<BaseScheduleRecoveryVerificationKey> Keys => _keys.Select(Clone).ToImmutableArray();

    private static BaseScheduleRecoveryVerificationKey Clone(BaseScheduleRecoveryVerificationKey value) => value with
    { Id = new string(value.Id.AsSpan()), PublicKey = value.PublicKey.ToArray().ToImmutableArray(), Checksum = value.Checksum.ToArray().ToImmutableArray() };
}
