using HPD.Payments.Contracts.ExternalEffect;
using HPD.Payments.Host.Api;

var configuration = new PaymentsApiConfiguration(PaymentsApiProfile.EmbeddedSqlite);
var response = PaymentsApiTransport.Project("native-operation", ExternalEffectState.PossibleDispatch, "secret", false);
if (configuration.WireVersion != response.WireVersion || response.ExternalReference is not null || PaymentsApiTransport.Routes.Count != 2) return 1;
Console.WriteLine("PASS Host.Api Native AOT transport graph");
return 0;
