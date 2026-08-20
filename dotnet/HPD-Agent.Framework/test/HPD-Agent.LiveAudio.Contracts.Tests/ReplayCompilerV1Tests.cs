using HPD.Agent.Audio.Runtime.Replay;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class ReplayCompilerV1Tests
{
    [Fact]
    public async Task Compiler_reads_one_immutable_snapshot_and_has_no_append_surface()
    {var f=new Fixture();await f.SeedAsync(2);var before=await f.HeadAsync();var result=Assert.IsType<ReplayCompileResultV1.Compiled>(await ReplayCompilerV1.CompileAsync(new AuthorityJournalReplaySourceV1(f.Journal),f.Session));Assert.Equal(2,result.Compilation.Facts.Count);Assert.Equal(before,result.Compilation.Through);Assert.Equal(before,await f.HeadAsync());Assert.DoesNotContain(typeof(IAuthorityReplaySourceV1).GetMethods(),m=>m.Name.Contains("Append",StringComparison.Ordinal));}
    [Fact]
    public async Task Same_history_compiles_to_same_fingerprint()
    {var f=new Fixture();await f.SeedAsync(2);var source=new AuthorityJournalReplaySourceV1(f.Journal);var a=Assert.IsType<ReplayCompileResultV1.Compiled>(await ReplayCompilerV1.CompileAsync(source,f.Session)).Compilation;var b=Assert.IsType<ReplayCompileResultV1.Compiled>(await ReplayCompilerV1.CompileAsync(source,f.Session)).Compilation;Assert.Equal(a.Fingerprint,b.Fingerprint);Assert.Equal(a.Through,b.Through);}
    [Fact]
    public async Task Non_batch_page_fails_closed()
    {var session=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());Assert.Equal("replay-page-invalid",Assert.IsType<ReplayCompileResultV1.InvalidHistory>(await ReplayCompilerV1.CompileAsync(new BadSource(session),session)).SafeCode.ToString());}
    [Fact]
    public async Task Cancellation_is_propagated_without_oracle_result()
    {var f=new Fixture();using var source=new CancellationTokenSource();source.Cancel();await Assert.ThrowsAsync<OperationCanceledException>(async()=>await ReplayCompilerV1.CompileAsync(new AuthorityJournalReplaySourceV1(f.Journal),f.Session,source.Token));}
    private sealed class Fixture
    {private readonly TenantId _tenant=TenantId.Create();internal Fixture(){Session=new(RuntimeGenerationId.Create(),LiveSessionId.Create());Journal=new(new AuthorityPayloadAdmissionRegistryV1([new SessionAuthorityStampPayloadRegistrationV1()]),()=>new UtcInstant(1),new(16,128,1_000_000));}internal SessionAuthorityStampV1 Session{get;}internal InMemoryAuthorityJournalV1 Journal{get;}internal async Task SeedAsync(int count){for(var i=0;i<count;i++){var payload=SessionAuthorityStampV1Codec.Encode(new(RuntimeGenerationId.Create(),LiveSessionId.Create()));var registration=new SessionAuthorityStampPayloadRegistrationV1();var proposal=new ProposedAuthorityFactV1(JournalFactId.Create(),null,registration.Owner,registration.Schema,payload,AuthorityPayloadHashV1.Compute(registration.SchemaToken,registration.Schema,payload),new CorrelationEnvelopeV1(_tenant,operationId:OperationId.Create()),new UtcInstant(1));Assert.IsType<AppendAuthorityResultV1.Committed>(await Journal.AppendAsync(new(Session,i,[],[proposal],4096)));}}internal async Task<long> HeadAsync()=>Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await Journal.ReadAsync(new(Session,0,long.MaxValue,16,65536))).SnapshotThrough;}
    private sealed class BadSource(SessionAuthorityStampV1 session):IAuthorityReplaySourceV1
    {public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,CancellationToken cancellationToken=default){cancellationToken.ThrowIfCancellationRequested();return ValueTask.FromResult<ReadAuthorityRangeResultV1>(new ReadAuthorityRangeResultV1.ItemTooLarge(new JournalPositionV1(session,1),2,1));}}
}
