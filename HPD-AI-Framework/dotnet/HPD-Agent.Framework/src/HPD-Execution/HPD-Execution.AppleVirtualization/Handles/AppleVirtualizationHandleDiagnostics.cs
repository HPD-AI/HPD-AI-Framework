namespace HPD.Execution.AppleVirtualization.Handles;

using HPD.Execution.Contracts;

internal static class AppleVirtualizationHandleDiagnostics
{
    public static readonly DiagnosticCode MissingHandle = new("AppleVirtualization.HandleMissing");
    public static readonly DiagnosticCode StaleHandle = new("AppleVirtualization.StaleHandle");
    public static readonly DiagnosticCode WrongHandleKind = new("AppleVirtualization.WrongHandleKind");
    public static readonly DiagnosticCode ResourceGenerationMismatch = new("AppleVirtualization.ResourceGenerationMismatch");

    public static Diagnostic Missing(ProviderId providerId, string targetPath) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = MissingHandle,
            Message = "The Apple Virtualization provider handle was not found in the provider state ledger.",
            ProviderId = providerId,
            TargetPath = targetPath,
        };

    public static Diagnostic Stale(ProviderId providerId, string targetPath, ulong expectedGeneration, ulong observedGeneration) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = StaleHandle,
            Message = $"The Apple Virtualization provider handle is stale. Expected provider generation {expectedGeneration}, observed {observedGeneration}.",
            ProviderId = providerId,
            TargetPath = targetPath,
        };

    public static Diagnostic WrongKind(ProviderId providerId, string targetPath, string expectedKind, string observedKind) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = WrongHandleKind,
            Message = $"The Apple Virtualization provider handle targets '{observedKind}', but '{expectedKind}' was required.",
            ProviderId = providerId,
            TargetPath = targetPath,
        };

    public static Diagnostic GenerationMismatch(ProviderId providerId, string targetPath, long expectedGeneration, long observedGeneration) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = ResourceGenerationMismatch,
            Message = $"The Apple Virtualization resource generation does not match. Expected resource generation {expectedGeneration}, observed {observedGeneration}.",
            ProviderId = providerId,
            TargetPath = targetPath,
        };
}
