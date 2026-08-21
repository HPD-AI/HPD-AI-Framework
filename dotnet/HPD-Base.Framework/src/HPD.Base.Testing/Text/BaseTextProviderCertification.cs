using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Runs the provider-neutral text-search certification protocol.</summary>
public static class BaseTextProviderCertification
{
    /// <summary>Gets the exact certification protocol version.</summary>
    public const string ProtocolVersion = "hpd.base.text.certification.v1";

    /// <summary>Runs bounded protocol and adapter validation against one isolated host.</summary>
    public static async ValueTask<BaseTextCertificationReport> RunAsync(IBaseTextCertificationFixture fixture, BaseTextCertificationHostRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fixture); ArgumentNullException.ThrowIfNull(request);
        ImmutableArray<byte> contract = ContractChecksum(); var cases = ImmutableArray.CreateBuilder<BaseTextCertificationCaseResult>();
        if (fixture.ProtocolVersion != ProtocolVersion || request.ProtocolVersion != ProtocolVersion || fixture.ProviderClass != request.ProviderClass || string.IsNullOrWhiteSpace(fixture.ProviderId) || fixture.ProviderVersion < 1)
            return Report(fixture, contract, [Failure("protocol", OperationStatus.ValidationFailed, "base.testing.text.protocolInvalid")]);
        Validate(request);
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
            BaseTextCertificationShutdownResult shutdown = await host.ShutdownAsync(new() { MaximumWait = TimeSpan.FromSeconds(5) }, cancellationToken).ConfigureAwait(false);
            cases.Add(shutdown.Completed && shutdown.RetainedOperationCount == 0 ? Success("bounded-shutdown") : Failure("bounded-shutdown", OperationStatus.StoreError, "base.testing.text.shutdownIncomplete"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { cases.Add(Failure("adapter", OperationStatus.StoreError, "base.testing.text.adapterFailed")); }
        finally { if (host is not null) try { await host.DisposeAsync().ConfigureAwait(false); } catch { cases.Add(Failure("dispose", OperationStatus.StoreError, "base.testing.text.shutdownIncomplete")); } }
        return Report(fixture, contract, cases.ToImmutable());
    }

    private static void Validate(BaseTextCertificationHostRequest request)
    {
        if (!Enum.IsDefined(request.ProviderClass) || !Enum.IsDefined(request.Plan) || request.TimeProvider is null || request.TokenKeys.IsDefaultOrEmpty || request.Faults.IsDefault || request.Faults.Length > 64) throw new ArgumentException("The certification host request is invalid.", nameof(request));
        foreach (BaseTextCertificationFaultSchedule fault in request.Faults)
            if (!Enum.IsDefined(fault.Fault) || fault.Occurrence is < 1 or > 16 || fault.Delay < TimeSpan.Zero || fault.PartialSuccessCount < 0 || IsNonCooperative(fault.Fault) && fault.Delay != TimeSpan.Zero) throw new ArgumentException("The certification fault schedule is invalid.", nameof(request));
    }
    private static bool IsNonCooperative(BaseTextCertificationFault value) => value is BaseTextCertificationFault.QueryNonCooperative or BaseTextCertificationFault.ProjectionWriteNonCooperative or BaseTextCertificationFault.InspectionNonCooperative or BaseTextCertificationFault.RebuildNonCooperative;
    private static BaseTextCertificationCaseResult Success(string id) => new() { Id = id, Passed = true, Status = OperationStatus.Ok };
    private static BaseTextCertificationCaseResult Failure(string id, OperationStatus status, string code) => new() { Id = id, Passed = false, Status = status, ErrorCode = code };
    private static ImmutableArray<byte> ContractChecksum() => ImmutableArray.Create(SHA256.HashData(Encoding.ASCII.GetBytes("HPDB-TEXT-CERTIFICATION-CONTRACT-1\0" + ProtocolVersion)));
    private static BaseTextCertificationReport Report(IBaseTextCertificationFixture fixture, ImmutableArray<byte> contract, ImmutableArray<BaseTextCertificationCaseResult> cases)
    {
        using var stream = new MemoryStream(); stream.Write(Encoding.ASCII.GetBytes("HPDB-TEXT-CERTIFICATION-REPORT-1\0")); stream.Write(contract.AsSpan());
        foreach (BaseTextCertificationCaseResult item in cases) { stream.Write(Encoding.UTF8.GetBytes(item.Id)); stream.WriteByte(item.Passed ? (byte)1 : (byte)0); stream.Write(Encoding.UTF8.GetBytes(item.ErrorCode ?? string.Empty)); }
        return new() { ProtocolVersion = ProtocolVersion, ProviderId = fixture.ProviderId, ProviderVersion = fixture.ProviderVersion, ProviderClass = fixture.ProviderClass, Passed = cases.Length != 0 && cases.All(static value => value.Passed), Cases = cases, ContractChecksum = contract, ReportChecksum = ImmutableArray.Create(SHA256.HashData(stream.ToArray())) };
    }
}
