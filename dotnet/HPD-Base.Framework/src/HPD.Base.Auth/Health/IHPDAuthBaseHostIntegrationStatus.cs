namespace HPD.Base.Auth;

/// <summary>
/// Reports whether an HPD.Auth host integration is present for the BASE adapter.
/// </summary>
internal interface IHPDBaseAuthHostIntegrationStatus
{
    /// <summary>
    /// Gets a value indicating whether HPD.Auth host services were detected.
    /// </summary>
    bool HPDAuthServicesDetected { get; }

    /// <summary>
    /// Gets the integration source that reported the detected services.
    /// </summary>
    string? Source { get; }

    /// <summary>
    /// Gets required HPD.Auth service names that were not detected.
    /// </summary>
    string[] MissingRequiredServiceNames { get; }
}
