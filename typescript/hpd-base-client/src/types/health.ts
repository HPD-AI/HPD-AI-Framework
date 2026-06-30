import type { VisibilityLevel } from "./descriptors.js";

export type HealthStatus = "healthy" | "degraded" | "unhealthy" | "unknown" | "disabled";
export type HealthScope = "runtime" | "module" | "store" | "collection" | "projection" | "dependency";
export type DiagnosticSeverity = "info" | "warning" | "error" | "critical";
export type DiagnosticCategory = "configuration" | "compatibility" | "capability" | "health" | "policy" | "schema" | "store" | "projection";

export interface HealthDescriptor {
  id: string;
  scope: HealthScope;
  targetRef?: string;
  status?: HealthStatus;
  checkedAt: string;
  summary?: string;
  dependencies?: HealthDependency[];
  metrics?: HealthMetric[];
  publicSafe?: boolean;
  visibility?: VisibilityLevel;
}

export interface HealthDependency {
  id: string;
  kind: string;
  status?: HealthStatus;
}

export interface HealthMetric {
  name: string;
  kind: "text" | "number" | "boolean";
  textValue?: string;
  numberValue?: number;
  booleanValue?: boolean;
  unit?: string;
}

export interface DiagnosticDescriptor {
  id: string;
  code: string;
  severity?: DiagnosticSeverity;
  targetRef?: string;
  message: string;
  publicMessage?: string;
  targetPath?: string;
  category?: DiagnosticCategory;
  remediation?: string;
  relatedFeatureIds?: string[];
  visibility?: VisibilityLevel;
  emittedAt: string;
}
