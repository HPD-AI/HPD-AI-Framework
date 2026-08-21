using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Runs the provider-neutral text-search certification protocol.</summary>
public static class BaseTextProviderCertification
{
    /// <summary>Gets the exact certification protocol version.</summary>
    public const string ProtocolVersion = BaseTextCertificationReceiptContract.ProtocolVersion;

    /// <summary>Runs bounded protocol and adapter validation against one isolated host.</summary>
    public static async ValueTask<BaseTextCertificationReport> RunAsync(IBaseTextCertificationFixture fixture, BaseTextCertificationHostRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fixture); ArgumentNullException.ThrowIfNull(request);
        ImmutableArray<byte> contract = ContractChecksum(); var cases = ImmutableArray.CreateBuilder<BaseTextCertificationCaseResult>();
        if (fixture.ProtocolVersion != ProtocolVersion || request.ProtocolVersion != ProtocolVersion || fixture.ProviderClass != request.ProviderClass || string.IsNullOrWhiteSpace(fixture.ProviderId) || fixture.ProviderVersion < 1)
            return Report(fixture, contract, [Failure("protocol", OperationStatus.ValidationFailed, "base.testing.text.protocolInvalid")]);
        Validate(request);
        request = Freeze(request);
        IBaseTextCertificationHost? host = null;
        try
        {
            host = await fixture.CreateAsync(request, cancellationToken).ConfigureAwait(false);
            BaseTextCertificationObservationPage initial = await host.ObserveAsync(new() { Take = 1 }, cancellationToken).ConfigureAwait(false);
            bool created = initial.Entries.Length == 1 && initial.Entries[0].Sequence == 1 && initial.Entries[0].Operation == BaseTextCertificationOperationKind.HostCreated && initial.RetainedLowSequence == 1 && initial.CapturedHighSequence >= 1;
            cases.Add(created ? Success("host-created") : Failure("host-created", OperationStatus.StoreError, "base.testing.text.observationInvalid"));
            BaseTextCertificationProviderState state = await host.Provider.InspectAsync(cancellationToken).ConfigureAwait(false);
            cases.Add(state.Generation > 0 && state.AppliedThrough.Value >= 0 && state.VisibleThrough.Value >= 0 ? Success("provider-state") : Failure("provider-state", OperationStatus.StoreError, "base.testing.text.stateInvalid"));
            BaseTextCertificationFaultState faults = await host.Provider.InspectFaultAsync(cancellationToken).ConfigureAwait(false);
            bool exactFaults = faults.Configured.Length == request.Faults.Length && faults.Configured.Select(static value => value.Fault).SequenceEqual(request.Faults.Select(static value => value.Fault));
            cases.Add(exactFaults ? Success("fault-authority") : Failure("fault-authority", OperationStatus.StoreError, "base.testing.text.faultInvalid"));
            await ExecuteSemanticCorpusAsync(host, cases, cancellationToken).ConfigureAwait(false);
            await ValidateObservationsAsync(host, cases, cancellationToken).ConfigureAwait(false);
            BaseTextCertificationShutdownResult shutdown = await host.ShutdownAsync(new() { MaximumWait = TimeSpan.FromSeconds(5) }, cancellationToken).ConfigureAwait(false);
            cases.Add(shutdown.Completed && shutdown.RetainedOperationCount == 0 ? Success("bounded-shutdown") : Failure("bounded-shutdown", OperationStatus.StoreError, "base.testing.text.shutdownIncomplete"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { cases.Add(Failure("adapter", OperationStatus.StoreError, "base.testing.text.adapterFailed")); }
        finally { if (host is not null) try { await host.DisposeAsync().ConfigureAwait(false); } catch { cases.Add(Failure("dispose", OperationStatus.StoreError, "base.testing.text.shutdownIncomplete")); } }
        return Report(fixture, contract, cases.ToImmutable());
    }

    private static async ValueTask ExecuteSemanticCorpusAsync(IBaseTextCertificationHost host, ImmutableArray<BaseTextCertificationCaseResult>.Builder cases, CancellationToken cancellationToken)
    {
        BaseTextCertificationRecord[] corpus =
        [
            Record("a", "tenant-a", true, 1, null, "Distributed systems", "portable lexical search"),
            Record("b", "tenant-a", true, 2, "present", "Distributed storage", "prefix and phrase matching"),
            Record("c", "tenant-a", false, 3, null, "Unrelated title", "distributed systems are hidden by the active filter"),
            Record("d", "tenant-b", true, 4, "present", "Punctuation: distributed-systems", "combining marks and connectors"),
            Record("e", "tenant-a", true, 5, null, "Ordering equality", "same lexical score"),
            Record("f", "tenant-a", true, 6, null, "Ordering equality", "same lexical score"),
        ];
        BaseTextCertificationSeedResult seeded = await host.Authority.SeedAsync(new() { Records = [.. corpus] }, cancellationToken).ConfigureAwait(false);
        cases.Add(seeded.RecordCount == corpus.Length && seeded.Head.Value > 0 && seeded.StateChecksum.Length == 32 ? Success("corpus.seed") : Failure("corpus.seed", OperationStatus.StoreError, "base.testing.text.seedInvalid"));

        BaseTextCertificationOperationResult term = await QueryAsync(host, Node("term", value: "distributed"), 8, cancellationToken).ConfigureAwait(false);
        cases.Add(Matches(term, "a", "b", "c", "d") ? Success("query.term") : Failure("query.term", term.Status, term.Error?.Code ?? "base.testing.text.semanticMismatch"));
        BaseTextCertificationOperationResult phrase = await QueryAsync(host, Node("phrase", terms: ["distributed", "systems"]), 8, cancellationToken).ConfigureAwait(false);
        cases.Add(Matches(phrase, "a", "c", "d") ? Success("query.phrase") : Failure("query.phrase", phrase.Status, phrase.Error?.Code ?? "base.testing.text.semanticMismatch"));
        BaseTextCertificationOperationResult prefix = await QueryAsync(host, Node("prefix", value: "distrib"), 8, cancellationToken).ConfigureAwait(false);
        cases.Add(Matches(prefix, "a", "b", "c", "d") ? Success("query.prefix") : Failure("query.prefix", prefix.Status, prefix.Error?.Code ?? "base.testing.text.semanticMismatch"));
        BaseTextCertificationOperationResult filtered = await QueryAsync(host, Node("term", value: "distributed"), 8, cancellationToken,
            new BaseTextHttpFilter { Kind = "and", Children = [Equal("tenant", "string", text: "tenant-a"), Equal("active", "boolean", boolean: true)] }).ConfigureAwait(false);
        cases.Add(Matches(filtered, "a", "b") ? Success("query.policy-filter-before-ranking") : Failure("query.policy-filter-before-ranking", filtered.Status, filtered.Error?.Code ?? "base.testing.text.policyMismatch"));
        BaseTextCertificationOperationResult ordered = await QueryAsync(host, Node("term", value: "ordering"), 8, cancellationToken,
            order: [new BaseTextHttpOrder { Field = "priority", Direction = "desc", NullOrder = "last" }]).ConfigureAwait(false);
        cases.Add(ordered.Status.IsSuccess() && ordered.Query is { } orderedQuery && orderedQuery.Matches.Select(static value => value.Record.Id).SequenceEqual(["f", "e"])
            ? Success("query.secondary-order") : Failure("query.secondary-order", ordered.Status, ordered.Error?.Code ?? "base.testing.text.orderMismatch"));

        BaseTextCertificationOperationResult first = await QueryAsync(host, Node("term", value: "distributed"), 1, cancellationToken).ConfigureAwait(false);
        bool firstValid = first.Status.IsSuccess() && first.Query is { Matches.Length: 1, Next: not null };
        BaseTextCertificationOperationResult? second = firstValid ? await QueryAsync(host, Node("term", value: "distributed"), 1, cancellationToken, cursor: first.Query!.Next).ConfigureAwait(false) : null;
        bool pageValid = second?.Status.IsSuccess() == true && second.Query is { Matches.Length: 1 } && second.Query.Matches[0].Record.Id != first.Query!.Matches[0].Record.Id;
        cases.Add(firstValid && pageValid ? Success("query.cursor-total-order") : Failure("query.cursor-total-order", second?.Status ?? first.Status, second?.Error?.Code ?? first.Error?.Code ?? "base.testing.text.cursorMismatch"));

        if (term.Query is { Matches.Length: > 0 } values)
        {
            BaseTextHttpMatch<BaseTextCertificationRecord> match = values.Matches[0];
            BaseTextCertificationRevisionResult revision = await host.Authority.InspectRevisionAsync(new() { RecordId = match.Record.Id, Revision = new RevisionToken(match.Revision) }, cancellationToken).ConfigureAwait(false);
            cases.Add(revision.Found && revision.Record == match.Record ? Success("query.exact-revision") : Failure("query.exact-revision", OperationStatus.StoreError, "base.testing.text.revisionMismatch"));
        }
        else cases.Add(Failure("query.exact-revision", term.Status, term.Error?.Code ?? "base.testing.text.revisionMismatch"));
    }

    private static async ValueTask ValidateObservationsAsync(IBaseTextCertificationHost host, ImmutableArray<BaseTextCertificationCaseResult>.Builder cases, CancellationToken cancellationToken)
    {
        BaseTextCertificationObservationPage page = await host.ObserveAsync(new() { AfterSequence = 1, Take = 256 }, cancellationToken).ConfigureAwait(false);
        bool ordered = !page.Overtaken && page.Entries.Length > 0 && page.Entries.Select(static value => value.Sequence).SequenceEqual(page.Entries.Select(static value => value.Sequence).Order()) && page.Entries.All(static value => value.Sequence > 1);
        cases.Add(ordered ? Success("observations.ordered") : Failure("observations.ordered", OperationStatus.StoreError, "base.testing.text.observationInvalid"));
    }

    private static ValueTask<BaseTextCertificationOperationResult> QueryAsync(IBaseTextCertificationHost host, BaseTextHttpQueryNode query, int take, CancellationToken cancellationToken, BaseTextHttpFilter? filter = null, string? cursor = null, BaseTextHttpOrder[]? order = null) =>
        host.ExecuteAsync(new BaseTextCertificationOperation.Query(new BaseTextHttpQueryRequest { IndexId = "base.testing.text.content.v1", Query = query, Filter = filter, Order = order ?? [], Take = take, Cursor = cursor, Consistency = "current" }), cancellationToken);
    private static BaseTextHttpQueryNode Node(string kind, string? value = null, string[]? terms = null) => new() { Kind = kind, Value = value, Terms = terms };
    private static BaseTextHttpFilter Equal(string field, string kind, string? text = null, bool? boolean = null) => new() { Kind = "equal", Field = field, Value = new() { Kind = kind, Text = text, Boolean = boolean } };
    private static BaseTextCertificationRecord Record(string id, string tenant, bool active, long priority, string? optional, string title, string body) => new() { Id = id, Tenant = tenant, Active = active, Priority = priority, Optional = optional, Title = title, Body = body };
    private static bool Matches(BaseTextCertificationOperationResult result, params string[] expected) => result.Status.IsSuccess() && result.Query is not null && result.Query.Matches.Select(static value => value.Record.Id).Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal));

    private static void Validate(BaseTextCertificationHostRequest request)
    {
        if (!Enum.IsDefined(request.ProviderClass) || !Enum.IsDefined(request.Plan) || request.TimeProvider is null || request.TokenKeys.IsDefaultOrEmpty || request.Faults.IsDefault || request.Faults.Length > 64) throw new ArgumentException("The certification host request is invalid.", nameof(request));
        if (request.TokenKeys.Length > 8 || request.TokenKeys.Any(static key => key.Id == 0 || key.Key is not { Length: 32 } || key.IssueUntil is { } issueUntil && issueUntil <= key.IssueNotBefore || key.DecryptUntil is { } decryptUntil && decryptUntil <= key.IssueNotBefore) || request.TokenKeys.Select(static key => key.Id).Distinct().Count() != request.TokenKeys.Length)
            throw new ArgumentException("The certification token authority is invalid.", nameof(request));
        foreach (BaseTextCertificationFaultSchedule fault in request.Faults)
            if (!Enum.IsDefined(fault.Fault) || fault.Occurrence is < 1 or > 16 || fault.Delay < TimeSpan.Zero || fault.PartialSuccessCount < 0 || IsNonCooperative(fault.Fault) && fault.Delay != TimeSpan.Zero) throw new ArgumentException("The certification fault schedule is invalid.", nameof(request));
    }
    private static BaseTextCertificationHostRequest Freeze(BaseTextCertificationHostRequest value) => new()
    {
        ProtocolVersion = value.ProtocolVersion,
        ProviderClass = value.ProviderClass,
        Plan = value.Plan,
        Limits = value.Limits with { },
        TimeProvider = value.TimeProvider,
        TokenKeys = value.TokenKeys.Select(static key => key with { Key = key.Key.ToArray() }).ToImmutableArray(),
        Faults = value.Faults.Select(static fault => fault with { }).ToImmutableArray(),
    };
    private static bool IsNonCooperative(BaseTextCertificationFault value) => value is BaseTextCertificationFault.QueryNonCooperative or BaseTextCertificationFault.ProjectionWriteNonCooperative or BaseTextCertificationFault.InspectionNonCooperative or BaseTextCertificationFault.RebuildNonCooperative;
    private static BaseTextCertificationCaseResult Success(string id) => new() { Id = id, Passed = true, Status = OperationStatus.Ok };
    private static BaseTextCertificationCaseResult Failure(string id, OperationStatus status, string code) => new() { Id = id, Passed = false, Status = status, ErrorCode = code };
    private static ImmutableArray<byte> ContractChecksum() => BaseTextCertificationReceiptContract.ContractChecksum;
    private static BaseTextCertificationReport Report(IBaseTextCertificationFixture fixture, ImmutableArray<byte> contract, ImmutableArray<BaseTextCertificationCaseResult> cases)
    {
        ImmutableArray<byte> capability = BaseTextCertificationReceiptContract.CapabilityChecksum(fixture.Capability); ImmutableArray<string> dependencies = fixture.NativeDependencyReceipts;
        using var stream = new MemoryStream(); stream.Write(Encoding.ASCII.GetBytes("HPDB-TEXT-CERTIFICATION-REPORT-1\0")); stream.Write(contract.AsSpan()); Write(stream, ProtocolVersion); Write(stream, fixture.ProviderId); WriteInt(stream, fixture.ProviderVersion); stream.WriteByte((byte)fixture.ProviderClass); stream.Write(capability.AsSpan()); WriteInt(stream, dependencies.Length); foreach (string dependency in dependencies) Write(stream, dependency);
        WriteInt(stream, cases.Length); foreach (BaseTextCertificationCaseResult item in cases) { Write(stream, item.Id); stream.WriteByte(item.Passed ? (byte)1 : (byte)0); WriteInt(stream, (int)item.Status); Write(stream, item.ErrorCode ?? string.Empty); }
        ImmutableArray<byte> report = ImmutableArray.Create(SHA256.HashData(stream.ToArray()));
        return new() { ProtocolVersion = ProtocolVersion, ProviderId = fixture.ProviderId, ProviderVersion = fixture.ProviderVersion, ProviderClass = fixture.ProviderClass, Passed = cases.Length != 0 && cases.All(static value => value.Passed), Cases = cases, CapabilityChecksum = capability, NativeDependencyReceipts = dependencies, ContractChecksum = contract, ReportChecksum = report, CertificationReceipt = BaseTextCertificationReceiptContract.Create(fixture.ProviderId, fixture.ProviderVersion, fixture.ProviderClass, fixture.Capability, dependencies, report) };
        static void Write(Stream target, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); WriteInt(target, bytes.Length); target.Write(bytes); }
        static void WriteInt(Stream target, int value) { Span<byte> bytes = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, value); target.Write(bytes); }
    }
}
