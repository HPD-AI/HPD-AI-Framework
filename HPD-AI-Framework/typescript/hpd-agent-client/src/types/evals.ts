/**
 * Types for the /evals HTTP API surface.
 * All types correspond to IScoreStore return types serialized from the server.
 */

export interface ScoreRecord {
  id: string;
  evaluatorName: string;
  evaluatorVersion: string;
  source: 'Live' | 'Test' | 'Retroactive' | 'Human';
  sessionId: string;
  branchId: string;
  turnIndex: number;
  agentName: string;
  modelId?: string;
  turnUsage?: UsageDetails;
  turnDuration: string;        // ISO 8601 duration
  judgeModelId?: string;
  judgeUsage?: UsageDetails;
  judgeDuration?: string;      // ISO 8601 duration
  samplingRate: number;
  policy: 'MustAlwaysPass' | 'TrackTrend';
  createdAt: string;           // ISO 8601
  result: EvaluationResult;
}

export interface UsageDetails {
  inputTokenCount?: number;
  outputTokenCount?: number;
  totalTokenCount?: number;
}

export interface EvaluationResult {
  metrics?: Record<string, EvaluationMetric>;
  diagnostics?: string[];
  metadata?: Record<string, unknown>;
}

export interface EvaluationMetric {
  value?: unknown;
  interpretation?: 'Unknown' | 'Inconclusive' | 'Passing' | 'Failing';
  reason?: string;
}

export interface EvaluatorSummary {
  evaluatorName: string;
  totalCount: number;
  averageScore: number;
  passRate: number;
  averageJudgeCostUsd: number;
  averageJudgeDuration: string; // ISO 8601 duration
  failureCount: number;
}

export interface ScoreTrend {
  evaluatorName: string;
  buckets: ScoreBucket[];
}

export interface ScoreBucket {
  start: string;   // ISO 8601
  average: number;
  min: number;
  max: number;
  count: number;
  passRate: number;
}

export interface ScoreAggregate {
  average: number;
  min: number;
  max: number;
  count: number;
  passRate: number;
}

export interface BranchComparisonResult {
  sessionId: string;
  branchId1: string;
  branchId2: string;
  branch1Scores: Record<string, ScoreAggregate>;
  branch2Scores: Record<string, ScoreAggregate>;
}

export interface ToolUsageSummary {
  totalCalls: number;
  permissionDeniedCount: number;
  permissionDeniedRate: number;
}

export interface RiskAutonomyDataPoint {
  sessionId: string;
  branchId: string;
  turnIndex: number;
  agentName: string;
  riskScore: number;
  autonomyScore: number;
  createdAt: string; // ISO 8601
}

export interface PassRateResult {
  evaluatorName: string;
  passRate: number;
}

export interface FailureRateResult {
  evaluatorName: string;
  failureRate: number;
}

/**
 * Agent comparison result: keyed by agent name, each entry is the aggregate scores
 * for that agent on the given evaluator.
 * Maps to IDictionary<string, ScoreAggregate> from GetAgentComparisonAsync.
 */
export type AgentComparisonResult = Record<string, ScoreAggregate>;

/**
 * Cost breakdown: keyed by cost category (e.g. "total", "judge"), value is USD cost.
 * Maps to IDictionary<string, double> from GetCostBreakdownAsync.
 */
export type CostBreakdown = Record<string, number>;
