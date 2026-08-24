using HPD.Payments.Primitives.Identity;
using HPD.Payments.Runtime.Settlement;

var scope = ScopeId.Create("tenant", "aot", "settlement");
SemanticId Id(string kind, string local) => SemanticId.Create(scope, "settlement", kind, local);
var expected = SettlementAccountingState.Create(Id("movement", "one"), 100m, "usd", OwnerGeneration.Create(1), Id("evidence", "expected"));
var mismatch = expected.Observe(SettlementAccountingObservationKind.Included, Id("evidence", "included-90"), 90m);
if (!mismatch.Residual || mismatch.AccountingAcknowledged) return 1;
var included = expected.Observe(SettlementAccountingObservationKind.Included, Id("evidence", "included-100"), 100m);
var exported = included.Observe(SettlementAccountingObservationKind.AccountingAcknowledged, Id("evidence", "export"));
if (!exported.AccountingAcknowledged || exported.Residual) return 1;
var excluded = expected.Observe(SettlementAccountingObservationKind.Excluded, Id("evidence", "excluded"));
if (!excluded.Excluded || !excluded.Residual) return 1;
Console.WriteLine("PASS settlement/accounting Native AOT mismatch graph");
return 0;
