using System.Collections.Immutable;

namespace HPD.Agent.Replay;

public enum ReplayModeV1 : ushort { FactProjection=1,ExactReexecution=2,SemanticReexecution=3,RecoverySimulation=4,ScheduleExploration=5,PrivilegedConformance=6 }
public enum ReplayOutcomeKindV1 : ushort { ExactMatch=1,SemanticMatch=2,Divergent=3,ArtifactIncomplete=4,ImplementationIncompatible=5,PrivacyDenied=6,UnsupportedBoundary=7,CapacityRefused=8,Deadlocked=9,Livelocked=10,ExplorationIncomplete=11,Corrupt=12 }
public enum EffectDispositionKindV1 : ushort { RecordedReceipt=1,SemanticFake=2,QualifiedIdempotentSandbox=3,ReadOnlyShadowQuery=4,ForbiddenLiveEffect=5,Unsupported=6,OutcomeUnknown=7 }
public enum WorkKindV1 : ushort { JournalAttempt=1,QueueAdmit=2,QueueDequeue=3,TimerCallback=4,ProviderCallback=5,ControllerEvaluation=6,SinkReceipt=7,CancellationDelivery=8,RecoveryClaim=9,PrivacyFence=10,SemanticAdapterStep=11 }
public enum RunStateV1 : ushort { Created=1,Validating=2,Ready=3,Running=4,Quiescent=5,Completed=6,Failed=7,Cancelled=8,Disposed=9 }

public readonly record struct ReplayArtifactId { private readonly string? _value;private ReplayArtifactId(string value)=>_value=value;public string Value=>_value??string.Empty;public static bool TryCreate(string? value,out ReplayArtifactId id)=>ReplayIdParserV1.Try(value,"rpa",out id,static x=>new(x));public override string ToString()=>Value; }
public readonly record struct ReplayRunId { private readonly string? _value;private ReplayRunId(string value)=>_value=value;public string Value=>_value??string.Empty;public static bool TryCreate(string? value,out ReplayRunId id)=>ReplayIdParserV1.Try(value,"run",out id,static x=>new(x));public override string ToString()=>Value; }
public readonly record struct ClockDomainId { private readonly string? _value;private ClockDomainId(string value)=>_value=value;public string Value=>_value??string.Empty;public static bool TryCreate(string? value,out ClockDomainId id)=>ReplayIdParserV1.Try(value,"clk",out id,static x=>new(x));public override string ToString()=>Value; }
public readonly record struct TimerId { private readonly string? _value;private TimerId(string value)=>_value=value;public string Value=>_value??string.Empty;public static bool TryCreate(string? value,out TimerId id)=>ReplayIdParserV1.Try(value,"tmr",out id,static x=>new(x));public override string ToString()=>Value; }
public readonly record struct WorkItemId { private readonly string? _value;private WorkItemId(string value)=>_value=value;public string Value=>_value??string.Empty;public static bool TryCreate(string? value,out WorkItemId id)=>ReplayIdParserV1.Try(value,"wrk",out id,static x=>new(x));public override string ToString()=>Value; }
public readonly record struct ScheduleId { private readonly string? _value;private ScheduleId(string value)=>_value=value;public string Value=>_value??string.Empty;public static bool TryCreate(string? value,out ScheduleId id)=>ReplayIdParserV1.Try(value,"sch",out id,static x=>new(x));public override string ToString()=>Value; }
public readonly record struct FaultId { private readonly string? _value;private FaultId(string value)=>_value=value;public string Value=>_value??string.Empty;public static bool TryCreate(string? value,out FaultId id)=>ReplayIdParserV1.Try(value,"flt",out id,static x=>new(x));public override string ToString()=>Value; }
public readonly record struct DrawId { private readonly string? _value;private DrawId(string value)=>_value=value;public string Value=>_value??string.Empty;public static bool TryCreate(string? value,out DrawId id)=>ReplayIdParserV1.Try(value,"drw",out id,static x=>new(x));public override string ToString()=>Value; }
public readonly record struct EnvironmentReadId { private readonly string? _value;private EnvironmentReadId(string value)=>_value=value;public string Value=>_value??string.Empty;public static bool TryCreate(string? value,out EnvironmentReadId id)=>ReplayIdParserV1.Try(value,"env",out id,static x=>new(x));public override string ToString()=>Value; }
public readonly record struct AdapterId { private readonly string? _value;private AdapterId(string value)=>_value=value;public string Value=>_value??string.Empty;public static bool TryCreate(string? value,out AdapterId id)=>ReplayIdParserV1.Try(value,"adp",out id,static x=>new(x));public override string ToString()=>Value; }
public readonly record struct OracleId { private readonly string? _value;private OracleId(string value)=>_value=value;public string Value=>_value??string.Empty;public static bool TryCreate(string? value,out OracleId id)=>ReplayIdParserV1.Try(value,"orc",out id,static x=>new(x));public override string ToString()=>Value; }
public readonly record struct DivergenceId { private readonly string? _value;private DivergenceId(string value)=>_value=value;public string Value=>_value??string.Empty;public static bool TryCreate(string? value,out DivergenceId id)=>ReplayIdParserV1.Try(value,"div",out id,static x=>new(x));public override string ToString()=>Value; }
public readonly record struct PayloadRefId { private readonly string? _value;private PayloadRefId(string value)=>_value=value;public string Value=>_value??string.Empty;public static bool TryCreate(string? value,out PayloadRefId id)=>ReplayIdParserV1.Try(value,"pay",out id,static x=>new(x));public override string ToString()=>Value; }

public readonly struct ReplayHash256V1 : IEquatable<ReplayHash256V1>
{
    private readonly ImmutableArray<byte> _bytes;
    private ReplayHash256V1(ImmutableArray<byte> bytes)=>_bytes=bytes;
    public ImmutableArray<byte> Bytes=>IsValid?_bytes:ImmutableArray<byte>.Empty;
    public bool IsValid=>!_bytes.IsDefault&&_bytes.Length==32;
    public static bool TryCreate(ReadOnlySpan<byte> bytes,out ReplayHash256V1 value){if(bytes.Length!=32){value=default;return false;}value=new(ImmutableArray.Create(bytes.ToArray()));return true;}
    public bool Equals(ReplayHash256V1 other)=>Bytes.AsSpan().SequenceEqual(other.Bytes.AsSpan());
    public override bool Equals(object? obj)=>obj is ReplayHash256V1 other&&Equals(other);
    public override int GetHashCode()=>IsValid?BitConverter.ToInt32(_bytes.AsSpan()):0;
    public static bool operator ==(ReplayHash256V1 left,ReplayHash256V1 right)=>left.Equals(right);
    public static bool operator !=(ReplayHash256V1 left,ReplayHash256V1 right)=>!left.Equals(right);
}
public readonly record struct VirtualInstant(long Nanoseconds);
public readonly record struct VirtualDuration(long Nanoseconds);

public sealed class ReplayBoundsV1
{
    private ReplayBoundsV1(ulong maxArtifactBytes,ulong maxPayloadBytes,ulong maxItems,ulong maxTimers,ulong maxWorkItems,ulong maxReadyItems,ulong maxQueueItems,ulong maxQueueBytes,ulong maxScheduleDepth,ulong maxScheduleBranches,ulong maxFaults,long maxVirtualNanoseconds,ulong maxCpuSteps,ulong maxMemoryBytes,ulong maxResults,ulong maxShrinkCandidates)
    {MaxArtifactBytes=maxArtifactBytes;MaxPayloadBytes=maxPayloadBytes;MaxItems=maxItems;MaxTimers=maxTimers;MaxWorkItems=maxWorkItems;MaxReadyItems=maxReadyItems;MaxQueueItems=maxQueueItems;MaxQueueBytes=maxQueueBytes;MaxScheduleDepth=maxScheduleDepth;MaxScheduleBranches=maxScheduleBranches;MaxFaults=maxFaults;MaxVirtualNanoseconds=maxVirtualNanoseconds;MaxCpuSteps=maxCpuSteps;MaxMemoryBytes=maxMemoryBytes;MaxResults=maxResults;MaxShrinkCandidates=maxShrinkCandidates;}
    public ulong MaxArtifactBytes{get;}public ulong MaxPayloadBytes{get;}public ulong MaxItems{get;}public ulong MaxTimers{get;}public ulong MaxWorkItems{get;}public ulong MaxReadyItems{get;}public ulong MaxQueueItems{get;}public ulong MaxQueueBytes{get;}public ulong MaxScheduleDepth{get;}public ulong MaxScheduleBranches{get;}public ulong MaxFaults{get;}public long MaxVirtualNanoseconds{get;}public ulong MaxCpuSteps{get;}public ulong MaxMemoryBytes{get;}public ulong MaxResults{get;}public ulong MaxShrinkCandidates{get;}
    public static bool TryCreate(ulong maxArtifactBytes,ulong maxPayloadBytes,ulong maxItems,ulong maxTimers,ulong maxWorkItems,ulong maxReadyItems,ulong maxQueueItems,ulong maxQueueBytes,ulong maxScheduleDepth,ulong maxScheduleBranches,ulong maxFaults,long maxVirtualNanoseconds,ulong maxCpuSteps,ulong maxMemoryBytes,ulong maxResults,ulong maxShrinkCandidates,out ReplayBoundsV1? bounds)
    {var values=new[]{maxArtifactBytes,maxPayloadBytes,maxItems,maxTimers,maxWorkItems,maxReadyItems,maxQueueItems,maxQueueBytes,maxScheduleDepth,maxScheduleBranches,maxFaults,maxCpuSteps,maxMemoryBytes,maxResults,maxShrinkCandidates};if(values.Any(x=>x==0)||maxVirtualNanoseconds<=0||maxPayloadBytes>maxArtifactBytes||maxReadyItems>maxWorkItems||maxResults>maxScheduleBranches){bounds=null;return false;}bounds=new(maxArtifactBytes,maxPayloadBytes,maxItems,maxTimers,maxWorkItems,maxReadyItems,maxQueueItems,maxQueueBytes,maxScheduleDepth,maxScheduleBranches,maxFaults,maxVirtualNanoseconds,maxCpuSteps,maxMemoryBytes,maxResults,maxShrinkCandidates);return true;}
}

internal static class ReplayIdParserV1
{
    private const string Alphabet="0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    internal static bool Try<T>(string? value,string family,out T result,Func<string,T> factory)
    {if(value is null||value.Length!=30||!value.StartsWith(family+":",StringComparison.Ordinal)||value[4]>'7'){result=default!;return false;}for(var i=4;i<30;i++)if(!Alphabet.Contains(value[i],StringComparison.Ordinal)){result=default!;return false;}if(value.AsSpan(4).IndexOfAnyExcept('0')<0){result=default!;return false;}result=factory(value);return true;}
}
