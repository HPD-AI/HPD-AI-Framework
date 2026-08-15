using HPD.Agent.Authority;
namespace HPD.Agent.Tests.Authority;
public sealed class CoreAuthoritySchemaHashV1Tests
{
 [Fact]public void Position_and_vector_hash_domains_are_distinct(){var s=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());var journal=new JournalPositionV1(s,1);var thread=new ThreadPositionV1(ThreadId.Create(),1,1);var axis=new AxisEntryV1(new AuthorityAxisValueV1.Graph(GraphGenerationId.Create()));var vector=ExpectedAuthorityVectorV1.Create(s,[new AuthorityAxisValueV1.Graph(GraphGenerationId.Create())]);Assert.NotEqual(AuthorityPositionCodecsV1.ComputeHash(journal),AuthorityPositionCodecsV1.ComputeHash(thread));Assert.NotEqual(AuthorityVectorCodecsV1.ComputeHash(axis),AuthorityVectorCodecsV1.ComputeHash(vector));}
}
