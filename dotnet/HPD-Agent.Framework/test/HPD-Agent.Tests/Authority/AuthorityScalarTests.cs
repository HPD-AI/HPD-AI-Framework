using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class AuthorityScalarTests
{
    private static readonly string[] ExpectedFamilyRows =
    [
        "ten|TenantId|S1|S1|correlation", "prn|PrincipalId|S9|S9|privacy", "sub|SubjectId|S9|S9|privacy",
        "ses|SessionId|S1|S1|correlation", "thr|ThreadId|S1|S1|correlation", "liv|LiveSessionId|S1|S1|authority",
        "run|RuntimeGenerationId|S1|S1|generation", "par|ParticipantId|S1|S1|authority",
        "fct|JournalFactId|S1|S1|authority", "sch|SchemaId|S1|S1|registry", "aut|AuthorizationId|S9|S9|privacy",
        "prj|ProjectionId|S9|S9|projection", "op|OperationId|S1|S1|operation", "cpy|CopyId|S9|S9|privacy",
        "cpr|CopyRangeId|S9|S9|privacy", "cgr|CaptureGrantId|S9|S9|privacy", "cap|CaptureId|S9|S9|privacy",
        "dsc|DisclosureId|S9|S9|privacy", "hld|HoldId|S9|S9|privacy", "del|DeletionId|S9|S9|privacy",
        "exp|ExportId|S9|S9|privacy", "cnt|ContentId|S9|S9|custody", "sbr|SubscriberId|S9|S9|delivery",
        "grf|GraphGenerationId|S2|S2|generation", "act|ActivityGenerationId|S3|S3|generation",
        "trn|TurnGenerationId|S4|S4|generation", "pvg|ProviderGenerationId|S5|S5|generation",
        "out|OutputGenerationId|S6|S6|generation", "snk|SinkGenerationId|S6|S6|generation",
        "tol|ToolGenerationId|S7|S7|generation", "rte|RouteGenerationId|S8|S8|generation",
        "prv|PrivacyGenerationId|S9|S9|generation", "trp|TransportGenerationId|S11|S11|generation",
        "cpp|CapacityPurposeId|S2|S2|capacity", "pur|PurposeId|S9|S9|privacy",
        "aud|AudienceId|S9|S9|privacy", "lim|LimitationId|S1|S1|qualification",
        "cus|CustodianDescriptorId|S9|S9|privacy", "pvd|ProviderId|AgentCore|AgentCore|provider",
        "pvf|ProviderFamilyId|AgentCore|AgentCore|provider", "fac|ProviderFactoryId|AgentCore|AgentCore|provider",
        "pln|LiveAudioPlanId|S1|S1|composition", "env|EnvironmentProfileId|S1|S1|qualification",
        "srf|SurfaceId|S1|S1|qualification", "clk|ClockDomainId|S1|S1|clock", "boo|BootId|S1|S1|clock",
    ];

    [Fact]
    public void StableIds_RoundTripCanonicalText()
    {
        var id = LiveSessionId.Create();
        var text = id.ToString();

        Assert.StartsWith("liv:", text);
        Assert.Equal(30, text.Length);
        Assert.True(LiveSessionId.TryParse(text, out var parsed));
        Assert.Equal(id, parsed);
    }

    [Fact]
    public void StableId_UsesTheCheckedInNetworkOrderGoldenVector()
    {
        var bytes = Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
        Assert.Equal("fct:00041061050R3GG28A1C60T3GF", StableId128.FromBytes(bytes).Format("fct"));
    }

    [Fact]
    public void GeneratedFamilyLedger_IsExactAndEveryWrapperParsesItsFamily()
    {
        var actual = AuthorityIdFamilyRegistryV1.All.Select(
            row => $"{row.Token}|{row.Type}|{row.Owner}|{row.AllocatorOwner}|{row.Kind}").ToArray();
        Assert.Equal(ExpectedFamilyRows, actual);
        Assert.Equal(46, actual.Distinct(StringComparer.Ordinal).Count());

        var raw = StableId128.FromBytes(Convert.FromHexString("000102030405060708090a0b0c0d0e0f"));
        foreach (var row in AuthorityIdFamilyRegistryV1.All)
        {
            var wrapper = typeof(LiveSessionId).Assembly.GetType($"HPD.Agent.Authority.{row.Type}", throwOnError: true)!;
            var method = wrapper.GetMethod("TryParse", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
            var arguments = new object?[] { raw.Format(row.Token), null };
            Assert.True((bool)method.Invoke(null, arguments)!);
            Assert.NotNull(arguments[1]);
            Assert.Equal(raw.Format(row.Token), arguments[1]!.ToString());
            Assert.False((bool)method.Invoke(null, new object?[] { $"bad:{raw.Format(row.Token).Split(':')[1]}", null })!);
        }
    }

    [Theory]
    [InlineData("liv:00000000000000000000000000")]
    [InlineData("liv:80000000000000000000000000")]
    [InlineData("liv:01ARZ3NDEKTSV4RRFFQ69G5FAI")]
    [InlineData("run:01ARZ3NDEKTSV4RRFFQ69G5FAV")]
    public void LiveSessionId_RejectsInvalidOrWrongFamily(string text) =>
        Assert.False(LiveSessionId.TryParse(text, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("liv:0000000000000000000000000")]
    [InlineData("liv:000000000000000000000000000")]
    [InlineData(" liv:01ARZ3NDEKTSV4RRFFQ69G5FAV")]
    [InlineData("liv:01arz3ndektsv4rrffq69g5fav")]
    [InlineData("liv:01ARZ3NDEKTSV4RRFFQ69G5FAL")]
    [InlineData("liv:01ARZ3NDEKTSV4RRFFQ69G5FAO")]
    [InlineData("liv:01ARZ3NDEKTSV4RRFFQ69G5FAU")]
    [InlineData("liv:01ARZ3NDEKTSV4RRFFQ69G5FAV=")]
    [InlineData("liv:01ARZ3NDEKTSV4RRFFQ69G5FAé")]
    public void StableId_RejectsNoncanonicalText(string? text) =>
        Assert.False(LiveSessionId.TryParse(text, out _));

    [Fact]
    public void StableId_RejectsEveryNoncanonicalLeadingDigit()
    {
        const string tail = "0000000000000000000000000";
        foreach (var first in "89ABCDEFGHJKMNPQRSTVWXYZ")
            Assert.False(LiveSessionId.TryParse($"liv:{first}{tail}", out _));
    }

    [Fact]
    public void SemanticWrappers_AreNotInterchangeable()
    {
        var liveText = LiveSessionId.Create().ToString();
        Assert.False(RuntimeGenerationId.TryParse(liveText, out _));
    }

    [Fact]
    public void Hash256_UsesLowercaseCanonicalText()
    {
        var hash = Hash256.Compute("abc"u8);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash.ToString());
        Assert.True(Hash256.TryParse(hash.ToString(), out var parsed));
        Assert.Equal(hash, parsed);
        Assert.False(Hash256.TryParse(hash.ToString().ToUpperInvariant(), out _));
    }

    [Fact]
    public void DefaultHash_RemainsValueEqualButHasNoBoundaryText()
    {
        Assert.Equal(default(Hash256), default(Hash256));
        Assert.Equal(string.Empty, default(Hash256).ToString());
    }

    [Fact]
    public void DefaultIds_HaveNoCanonicalBoundaryText()
    {
        Assert.Equal(string.Empty, default(TenantId).ToString());
        Assert.Equal(string.Empty, default(SessionId).ToString());
        Assert.Equal(string.Empty, default(ThreadId).ToString());
        Assert.Equal(string.Empty, default(LiveSessionId).ToString());
        Assert.Equal(string.Empty, default(RuntimeGenerationId).ToString());
    }
}
