using HPD.Payments.Contracts.ExternalEffect;
using HPD.Payments.Host.Api;

var configuration = new PaymentsApiConfiguration(PaymentsApiProfile.EmbeddedInMemory);
PaymentsApiTransport.RequireVersion(configuration.WireVersion);
var redacted = PaymentsApiTransport.Project("operation-one", ExternalEffectState.PossibleDispatch, "pi_secret", false);
if (redacted.ExternalReference is not null || redacted.State != nameof(ExternalEffectState.PossibleDispatch) || PaymentsApiTransport.Routes.Count != 2) return 1;
var visible = PaymentsApiTransport.Project("operation-one", ExternalEffectState.ConfirmedOccurred, "pi_visible", true);
if (visible.ExternalReference != "pi_visible") return 1;
try { PaymentsApiTransport.RequireVersion("v0"); return 1; } catch (InvalidOperationException) { }
try { _ = new PaymentsApiConfiguration(PaymentsApiProfile.None); return 1; } catch (ArgumentException) { }
Console.WriteLine("PASS Host.Api transport: exact profile, version, routes, uncertainty, and redaction");
return 0;
