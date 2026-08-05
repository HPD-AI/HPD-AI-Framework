namespace HPD.Base;

/// <summary>Defines the health status contract.</summary>
public enum HealthStatus { /// <summary>Identifies healthy.</summary>
Healthy, /// <summary>Identifies degraded.</summary>
Degraded, /// <summary>Identifies unhealthy.</summary>
Unhealthy, /// <summary>Identifies unknown.</summary>
Unknown, /// <summary>Identifies disabled.</summary>
Disabled }
/// <summary>Defines the health scope contract.</summary>
public enum HealthScope { /// <summary>Identifies runtime.</summary>
Runtime, /// <summary>Identifies module.</summary>
Module, /// <summary>Identifies store.</summary>
Store, /// <summary>Identifies collection.</summary>
Collection, /// <summary>Identifies projection.</summary>
Projection, /// <summary>Identifies dependency.</summary>
Dependency }
/// <summary>Defines the health metric value kind contract.</summary>
public enum HealthMetricValueKind { /// <summary>Identifies text.</summary>
Text, /// <summary>Identifies number.</summary>
Number, /// <summary>Identifies boolean.</summary>
Boolean }
/// <summary>Defines the diagnostic severity contract.</summary>
public enum DiagnosticSeverity { /// <summary>Identifies info.</summary>
Info, /// <summary>Identifies warning.</summary>
Warning, /// <summary>Identifies error.</summary>
Error, /// <summary>Identifies critical.</summary>
Critical }
/// <summary>Defines the diagnostic category contract.</summary>
public enum DiagnosticCategory { /// <summary>Identifies configuration.</summary>
Configuration, /// <summary>Identifies compatibility.</summary>
Compatibility, /// <summary>Identifies capability.</summary>
Capability, /// <summary>Identifies health.</summary>
Health, /// <summary>Identifies policy.</summary>
Policy, /// <summary>Identifies schema.</summary>
Schema, /// <summary>Identifies store.</summary>
Store, /// <summary>Identifies projection.</summary>
Projection }
