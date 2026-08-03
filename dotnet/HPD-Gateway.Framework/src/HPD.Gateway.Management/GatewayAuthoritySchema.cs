using HPD.Base;

namespace HPD.Gateway.Management;

public static class GatewayAuthoritySchema
{
    public const string AcceptedRevisions = "gateway.management.revisions";
    public const string ValidationRecords = "gateway.management.validations";
    public const string AdministrativeAudit = "gateway.management.audit";
    public const string TargetOwnership = "gateway.management.target-ownership";
    public const string DesiredStates = "gateway.management.desired";
    public const string NodeDeliveryAuthorities = "gateway.management.delivery-authorities";
    public const string ActivationIntents = "gateway.management.activation-intents";
    public const string DeliveryOutbox = "gateway.management.delivery-outbox";
    public const string NodeOutcomes = "gateway.management.node-outcomes";
    public const string CommandReceipts = "gateway.management.command-receipts";
    public const string AdministrativeOperationIntents = "gateway.management.admin-intents";
    public const string AdministrativeObservations = "gateway.management.admin-observations";
    public const string AdministrativeCompletions = "gateway.management.admin-completions";

    public static void AddTo(HPDBaseBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddCollection(GatewayAcceptedRevision.Collection);
        builder.AddCollection(GatewayValidationRecord.Collection);
        builder.AddCollection(GatewayAdministrativeAuditRecord.Collection);
        builder.AddCollection(GatewayTargetOwnership.Collection);
        builder.AddCollection(GatewayDesiredState.Collection);
        builder.AddCollection(GatewayNodeDeliveryAuthorityState.Collection);
        builder.AddCollection(GatewayActivationIntent.Collection);
        builder.AddCollection(GatewayDeliveryOutboxItem.Collection);
        builder.AddCollection(GatewayNodeActivationOutcome.Collection);
        builder.AddCollection(GatewayCommandReceipt.Collection);
        builder.AddCollection(GatewayAdministrativeOperationIntent.Collection);
        builder.AddCollection(GatewayAdministrativeOperationObservation.Collection);
        builder.AddCollection(GatewayAdministrativeOperationCompletion.Collection);
    }
}
