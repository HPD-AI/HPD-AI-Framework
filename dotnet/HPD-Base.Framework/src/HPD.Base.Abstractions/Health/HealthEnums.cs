namespace HPD.Base.Health;

public enum HealthStatus { Healthy, Degraded, Unhealthy, Unknown, Disabled }
public enum HealthScope { Runtime, Module, Store, Collection, Projection, Dependency }
public enum HealthMetricValueKind { Text, Number, Boolean }
public enum DiagnosticSeverity { Info, Warning, Error, Critical }
public enum DiagnosticCategory { Configuration, Compatibility, Capability, Health, Policy, Schema, Store, Projection }
