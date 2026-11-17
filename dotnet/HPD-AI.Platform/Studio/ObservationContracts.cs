using System.Collections.Immutable;
using System.Text.Json;

namespace HPD.AI.Platform.Studio;

/// <summary>Base type of the closed resource-specific Studio observation authority union.</summary>
public abstract record StudioObservationAuthority
{
    private protected StudioObservationAuthority(string kind, StudioSha256 checksum)
    { Kind = kind; Checksum = checksum; }
    /// <summary>Gets the closed wire discriminator.</summary>
    public string Kind { get; }
    /// <summary>Gets the purpose-bound authority checksum.</summary>
    public StudioSha256 Checksum { get; }
    internal abstract void WriteJson(Utf8JsonWriter writer);
}

/// <summary>Observes graph-owned resources under the exact installed graph, Studio, and policy owners.</summary>
public sealed record StudioGraphObservationAuthority : StudioObservationAuthority
{
    private StudioGraphObservationAuthority(long graphGeneration, StudioSha256 graphChecksum, long studioGeneration,
        StudioSha256 studioChecksum, long policyGeneration, StudioSha256 policyChecksum, StudioSha256 checksum)
        : base("graph", checksum)
    { ApplicationGraphGeneration = graphGeneration; ApplicationGraphChecksum = graphChecksum; StudioOwnerGeneration = studioGeneration;
      StudioOwnerChecksum = studioChecksum; PolicyOwnerGeneration = policyGeneration; PolicyOwnerChecksum = policyChecksum; }
    /// <summary>Gets the application-graph generation.</summary>
    public long ApplicationGraphGeneration { get; }
    /// <summary>Gets the application-graph checksum.</summary>
    public StudioSha256 ApplicationGraphChecksum { get; }
    /// <summary>Gets the Studio-owner generation.</summary>
    public long StudioOwnerGeneration { get; }
    /// <summary>Gets the Studio-owner checksum.</summary>
    public StudioSha256 StudioOwnerChecksum { get; }
    /// <summary>Gets the policy-owner generation.</summary>
    public long PolicyOwnerGeneration { get; }
    /// <summary>Gets the policy-owner checksum.</summary>
    public StudioSha256 PolicyOwnerChecksum { get; }

    /// <summary>Creates one exact graph observation authority from the common response lease.</summary>
    public static StudioGraphObservationAuthority Create(StudioResponseAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        StudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.observation-authority.graph.v1", writer =>
        { writer.Int64(authority.ApplicationGraphGeneration); writer.Checksum(authority.ApplicationGraphChecksum);
          writer.Int64(authority.StudioOwnerGeneration); writer.Checksum(authority.StudioOwnerChecksum);
          writer.Int64(authority.PolicyOwnerGeneration); writer.Checksum(authority.PolicyOwnerChecksum); });
        return new(authority.ApplicationGraphGeneration, StudioSha256.FromDigest(authority.ApplicationGraphChecksum.ToArray()),
            authority.StudioOwnerGeneration, StudioSha256.FromDigest(authority.StudioOwnerChecksum.ToArray()),
            authority.PolicyOwnerGeneration, StudioSha256.FromDigest(authority.PolicyOwnerChecksum.ToArray()), checksum);
    }

    internal override void WriteJson(Utf8JsonWriter writer)
    {
        writer.WriteStartObject(); Hex(writer, "applicationGraphChecksum", ApplicationGraphChecksum); Long(writer, "applicationGraphGeneration", ApplicationGraphGeneration);
        Hex(writer, "authorityChecksum", Checksum); writer.WriteString("kind", Kind); Hex(writer, "policyOwnerChecksum", PolicyOwnerChecksum);
        Long(writer, "policyOwnerGeneration", PolicyOwnerGeneration); Hex(writer, "studioOwnerChecksum", StudioOwnerChecksum);
        Long(writer, "studioOwnerGeneration", StudioOwnerGeneration); writer.WriteEndObject();
    }
    private static void Long(Utf8JsonWriter writer, string name, long value) => writer.WriteString(name, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private static void Hex(Utf8JsonWriter writer, string name, StudioSha256 value) => writer.WriteString(name, Convert.ToHexString(value.ToArray()).ToLowerInvariant());
}

/// <summary>Encodes the common closed Studio observation result union.</summary>
public static class StudioObservationJson
{
    /// <summary>Encodes one current, already disclosure-projected typed observation.</summary>
    public static StudioCanonicalJson Current(StudioResourceIdentity resource,
        StudioObservationAuthority observationAuthority, StudioCanonicalJson value,
        IEnumerable<StudioResolvedLink> links, IEnumerable<StudioCanonicalJson> evidence,
        StudioCanonicalJson accounting, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(resource); ArgumentNullException.ThrowIfNull(observationAuthority);
        ArgumentNullException.ThrowIfNull(value); ArgumentNullException.ThrowIfNull(accounting);
        ImmutableArray<StudioResolvedLink> ownedLinks = StudioContractValidation.Materialize(links, 128, true, nameof(links));
        ImmutableArray<StudioCanonicalJson> ownedEvidence = StudioContractValidation.Materialize(evidence, 256, true, nameof(evidence));
        var buffer = new System.Buffers.ArrayBufferWriter<byte>(); using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject(); writer.WritePropertyName("accounting"); writer.WriteRawValue(accounting.ToArray(), true);
        writer.WritePropertyName("evidence"); writer.WriteStartArray();
        foreach (StudioCanonicalJson item in ownedEvidence) writer.WriteRawValue(item.ToArray(), true); writer.WriteEndArray();
        writer.WriteString("kind", "current"); writer.WritePropertyName("links"); writer.WriteStartArray();
        foreach (StudioResolvedLink link in ownedLinks)
        { writer.WriteStartObject(); writer.WriteString("label", link.Label); writer.WriteString("relation", Relation(link.Relation)); writer.WritePropertyName("target"); link.Target.WriteJson(writer); writer.WriteEndObject(); }
        writer.WriteEndArray(); writer.WritePropertyName("observationAuthority"); observationAuthority.WriteJson(writer);
        writer.WritePropertyName("resource"); resource.WriteJson(writer); writer.WritePropertyName("value"); writer.WriteRawValue(value.ToArray(), true);
        writer.WriteEndObject(); writer.Flush();
        return StudioCanonicalJson.Create(buffer.WrittenSpan, maximumBytes);
    }
    private static string Relation(StudioLinkRelation value) => char.ToLowerInvariant(value.ToString()[0]) + value.ToString()[1..];
}
