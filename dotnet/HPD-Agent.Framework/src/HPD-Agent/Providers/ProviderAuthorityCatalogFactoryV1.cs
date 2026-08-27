using System.Security.Cryptography;
using System.Text;
using HPD.Agent.Authority;

namespace HPD.Agent.Providers;

internal static class ProviderAuthorityCatalogFactoryV1
{
    internal static ProviderCatalogV1? TryCreate(IReadOnlyList<ProviderManifestFragment> fragments)
    {
        if (fragments.Count == 0 || fragments.Any(static fragment => string.IsNullOrWhiteSpace(fragment.OwnerAssembly)))
            return null;

        var contributions = new List<ProviderContributionV1>();
        foreach (var fragment in fragments.OrderBy(static value => value.OwnerAssembly, StringComparer.Ordinal))
        {
            foreach (var registration in fragment.RuntimeFactories.OrderBy(static value => value.ProviderKey, StringComparer.Ordinal))
            {
                var descriptor = fragment.Descriptors.Single(value =>
                    StringComparer.OrdinalIgnoreCase.Equals(value.ProviderKey, registration.ProviderKey));
                foreach (var backendKey in registration.BackendKeys.OrderBy(static value => value, StringComparer.Ordinal))
                foreach (var family in registration.Families.OrderBy(static value => value))
                {
                    var providerKey = descriptor.ProviderKey;
                    var familyDescriptor = descriptor.Families[family];
                    var credentialAliases = fragment.SecretAliases
                        .Select(static value => new BoundedAscii(value.SecretKey));
                    var codecIds = fragments.SelectMany(static value => value.SerializationContracts)
                        .Where(value => StringComparer.OrdinalIgnoreCase.Equals(value.ProviderKey, providerKey) && value.Family == family)
                        .Select(value => SchemaId.FromValue(DeriveStable("hpd.provider-codec-schema.v1", providerKey, FamilyToken(family), value.Kind.ToString())));

                    contributions.Add(new ProviderContributionV1(
                        ProviderId.FromValue(DeriveStable("hpd.provider-id.v1", providerKey)),
                        ProviderFamilyId.FromValue(DeriveStable("hpd.provider-family-id.v1", FamilyToken(family))),
                        new BoundedAscii(fragment.OwnerAssembly!),
                        [Role(family)],
                        new ProviderCapabilitySetV1(1, 0, EmptyHash("hpd.provider-capabilities.v1")),
                        codecIds,
                        ProviderFactoryId.FromValue(DeriveStable(
                            "hpd.provider-factory-id.v1", providerKey, backendKey, FamilyToken(family))),
                        Lifetime(familyDescriptor.Lifetime),
                        credentialAliases,
                        EmptyHash("hpd.provider-support-manifest.v1")));
                }
            }
        }
        return new ProviderCatalogV1(contributions);
    }

    private static ProviderRoleV1 Role(ProviderClientFamily family) => family switch
    {
        ProviderClientFamily.Chat => ProviderRoleV1.Chat,
        ProviderClientFamily.Embeddings => ProviderRoleV1.Embeddings,
        ProviderClientFamily.HostedFiles => ProviderRoleV1.HostedFiles,
        ProviderClientFamily.ImageGeneration => ProviderRoleV1.ImageGeneration,
        ProviderClientFamily.Realtime => ProviderRoleV1.Realtime,
        ProviderClientFamily.SpeechToText => ProviderRoleV1.SpeechToText,
        ProviderClientFamily.TextToSpeech => ProviderRoleV1.TextToSpeech,
        ProviderClientFamily.VoiceActivityDetection => ProviderRoleV1.Vad,
        ProviderClientFamily.EndOfTurnDetection => ProviderRoleV1.EndOfTurn,
        _ => throw new ProviderCompositionException("HPDP015", $"Provider family '{family}' has no authority-catalog role."),
    };

    private static ProviderLifetimeV1 Lifetime(ProviderFamilyLifetime lifetime) => lifetime switch
    {
        ProviderFamilyLifetime.ReusableClient => ProviderLifetimeV1.AgentScoped,
        ProviderFamilyLifetime.StatefulPerAudioSession => ProviderLifetimeV1.SessionScoped,
        ProviderFamilyLifetime.StatefulPerRun => ProviderLifetimeV1.Transient,
        ProviderFamilyLifetime.StatefulPerTurn => ProviderLifetimeV1.Transient,
        _ => throw new ProviderCompositionException("HPDP016", $"Provider lifetime '{lifetime}' is outside the authority catalog."),
    };

    private static string FamilyToken(ProviderClientFamily family) => family.ToString();

    private static StableId128 DeriveStable(string domain, params string[] parts)
    {
        using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(incremental, domain);
        foreach (var part in parts) Append(incremental, part);
        var digest = incremental.GetHashAndReset();
        if (digest.AsSpan(0, 16).IndexOfAnyExcept((byte)0) < 0) digest[15] = 1;
        return StableId128.FromBytes(digest.AsSpan(0, 16));
    }

    private static Hash256 EmptyHash(string domain)
    {
        using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(incremental, domain);
        return Hash256.FromBytes(incremental.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
