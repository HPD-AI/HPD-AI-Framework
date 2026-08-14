using System.Text;
using HPD.Payments.Serialization.Wire;

var result = AuthorityWireCodec.Read("{\"kind\":\"agreement\",\"semanticVersion\":1,\"representationVersion\":1,\"semanticFields\":{\"id\":\"aot\"}}"u8, 1, 1, 1);
if (result.Disposition != CompatibilityDisposition.Supported) return 1;
Console.WriteLine(AuthorityWireCodec.ComputeSemanticDigest(result.Document!));
return 0;
