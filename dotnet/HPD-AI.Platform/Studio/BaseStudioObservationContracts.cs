using System.Collections.Immutable;
using System.Text.Json;

namespace HPD.AI.Platform.Studio;

/// <summary>Base type of the closed resource-specific Studio observation authority union.</summary>
public abstract record BaseStudioObservationAuthority
{
    private protected BaseStudioObservationAuthority(string kind, BaseStudioSha256 checksum)
    { Kind = kind; Checksum = checksum; }
    /// <summary>Gets the closed wire discriminator.</summary>
    public string Kind { get; }
    /// <summary>Gets the purpose-bound authority checksum.</summary>
    public BaseStudioSha256 Checksum { get; }
    internal abstract void WriteJson(Utf8JsonWriter writer);
}

/// <summary>Observes graph-owned resources under the exact installed graph, Studio, and policy owners.</summary>
public sealed record BaseStudioGraphObservationAuthority : BaseStudioObservationAuthority
{
    private BaseStudioGraphObservationAuthority(long graphGeneration, BaseStudioSha256 graphChecksum, long studioGeneration,
        BaseStudioSha256 studioChecksum, long policyGeneration, BaseStudioSha256 policyChecksum, BaseStudioSha256 checksum)
        : base("graph", checksum)
    { ApplicationGraphGeneration = graphGeneration; ApplicationGraphChecksum = graphChecksum; StudioOwnerGeneration = studioGeneration;
      StudioOwnerChecksum = studioChecksum; PolicyOwnerGeneration = policyGeneration; PolicyOwnerChecksum = policyChecksum; }
    /// <summary>Gets the application-graph generation.</summary>
    public long ApplicationGraphGeneration { get; }
    /// <summary>Gets the application-graph checksum.</summary>
    public BaseStudioSha256 ApplicationGraphChecksum { get; }
    /// <summary>Gets the Studio-owner generation.</summary>
    public long StudioOwnerGeneration { get; }
    /// <summary>Gets the Studio-owner checksum.</summary>
    public BaseStudioSha256 StudioOwnerChecksum { get; }
    /// <summary>Gets the policy-owner generation.</summary>
    public long PolicyOwnerGeneration { get; }
    /// <summary>Gets the policy-owner checksum.</summary>
    public BaseStudioSha256 PolicyOwnerChecksum { get; }

    /// <summary>Creates one exact graph observation authority from the common response lease.</summary>
    public static BaseStudioGraphObservationAuthority Create(BaseStudioResponseAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.observation-authority.graph.v1", writer =>
        { writer.Int64(authority.ApplicationGraphGeneration); writer.Checksum(authority.ApplicationGraphChecksum);
          writer.Int64(authority.StudioOwnerGeneration); writer.Checksum(authority.StudioOwnerChecksum);
          writer.Int64(authority.PolicyOwnerGeneration); writer.Checksum(authority.PolicyOwnerChecksum); });
        return new(authority.ApplicationGraphGeneration, BaseStudioSha256.FromDigest(authority.ApplicationGraphChecksum.ToArray()),
            authority.StudioOwnerGeneration, BaseStudioSha256.FromDigest(authority.StudioOwnerChecksum.ToArray()),
            authority.PolicyOwnerGeneration, BaseStudioSha256.FromDigest(authority.PolicyOwnerChecksum.ToArray()), checksum);
    }

    internal override void WriteJson(Utf8JsonWriter writer)
    {
        writer.WriteStartObject(); Hex(writer, "applicationGraphChecksum", ApplicationGraphChecksum); Long(writer, "applicationGraphGeneration", ApplicationGraphGeneration);
        Hex(writer, "authorityChecksum", Checksum); writer.WriteString("kind", Kind); Hex(writer, "policyOwnerChecksum", PolicyOwnerChecksum);
        Long(writer, "policyOwnerGeneration", PolicyOwnerGeneration); Hex(writer, "studioOwnerChecksum", StudioOwnerChecksum);
        Long(writer, "studioOwnerGeneration", StudioOwnerGeneration); writer.WriteEndObject();
    }
    private static void Long(Utf8JsonWriter writer, string name, long value) => writer.WriteString(name, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private static void Hex(Utf8JsonWriter writer, string name, BaseStudioSha256 value) => writer.WriteString(name, Convert.ToHexString(value.ToArray()).ToLowerInvariant());
}

/// <summary>Encodes the common closed Studio observation result union.</summary>
public static class BaseStudioObservationJson
{
    /// <summary>Encodes one current, already disclosure-projected typed observation.</summary>
    public static BaseStudioCanonicalJson Current(BaseStudioResourceIdentity resource,
        BaseStudioObservationAuthority observationAuthority, BaseStudioCanonicalJson value,
        IEnumerable<BaseStudioResolvedLink> links, IEnumerable<BaseStudioCanonicalJson> evidence,
        BaseStudioCanonicalJson accounting, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(resource); ArgumentNullException.ThrowIfNull(observationAuthority);
        ArgumentNullException.ThrowIfNull(value); ArgumentNullException.ThrowIfNull(accounting);
        ImmutableArray<BaseStudioResolvedLink> ownedLinks = StudioContractValidation.Materialize(links, 128, true, nameof(links));
        ImmutableArray<BaseStudioCanonicalJson> ownedEvidence = StudioContractValidation.Materialize(evidence, 256, true, nameof(evidence));
        var buffer = new System.Buffers.ArrayBufferWriter<byte>(); using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject(); writer.WritePropertyName("accounting"); writer.WriteRawValue(accounting.ToArray(), true);
        writer.WritePropertyName("evidence"); writer.WriteStartArray();
        foreach (BaseStudioCanonicalJson item in ownedEvidence) writer.WriteRawValue(item.ToArray(), true); writer.WriteEndArray();
        writer.WriteString("kind", "current"); writer.WritePropertyName("links"); writer.WriteStartArray();
        foreach (BaseStudioResolvedLink link in ownedLinks)
        { writer.WriteStartObject(); writer.WriteString("label", link.Label); writer.WriteString("relation", Relation(link.Relation)); writer.WritePropertyName("target"); link.Target.WriteJson(writer); writer.WriteEndObject(); }
        writer.WriteEndArray(); writer.WritePropertyName("observationAuthority"); observationAuthority.WriteJson(writer);
        writer.WritePropertyName("resource"); resource.WriteJson(writer); writer.WritePropertyName("value"); writer.WriteRawValue(value.ToArray(), true);
        writer.WriteEndObject(); writer.Flush();
        return BaseStudioCanonicalJson.Create(buffer.WrittenSpan, maximumBytes);
    }
    private static string Relation(BaseStudioLinkRelation value) => char.ToLowerInvariant(value.ToString()[0]) + value.ToString()[1..];
}
