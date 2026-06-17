/**
 * Unit tests for AgentClient eval query methods.
 *
 * What these tests cover:
 *   getScores, getScoresByThread, writeScore, getEvaluatorSummary,
 *   getRiskAutonomyDistribution — one-line passthroughs to the underlying
 *   AgentHttpApi. The tests verify:
 *     1. The correct HTTP method and URL (including query params) are called.
 *     2. The request body (writeScore) carries the right payload.
 *     3. The return value is the parsed JSON the server sent back.
 *
 * Test type: unit — all network I/O is replaced by vi.spyOn(globalThis, 'fetch').
 * API under test: AgentHttpApi through AgentClient convenience methods.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { AgentClient } from '../src/client.js';
import type {
  ScoreRecord,
  EvaluatorSummary,
  RiskAutonomyDataPoint,
} from '../src/types/evals.js';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function mockFetchJson(body: unknown, status = 200) {
  vi.spyOn(globalThis, 'fetch').mockResolvedValue({
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
    text: async () => JSON.stringify(body),
  } as Response);
}

const BASE = 'http://localhost:5135';

// Minimal fixtures
const SCORE_RECORD: ScoreRecord = {
  id: 'score-1',
  evaluatorName: 'TurnRiskEvaluator',
  evaluatorVersion: '1.0.0',
  source: 'Live',
  sessionId: 'session-1',
  threadId: 'main',
  turnIndex: 0,
  agentName: 'coder',
  turnDuration: 'PT1S',
  samplingRate: 1.0,
  policy: 'TrackTrend',
  createdAt: '2026-02-28T10:00:00Z',
  result: {
    metrics: {
      risk: { value: 2.5, interpretation: 'Passing', reason: 'Low risk' },
    },
  },
};

const EVALUATOR_SUMMARY: EvaluatorSummary = {
  evaluatorName: 'TurnRiskEvaluator',
  totalCount: 100,
  averageScore: 2.5,
  passRate: 0.94,
  averageJudgeCostUsd: 0.001,
  averageJudgeDuration: 'PT4S',
  failureCount: 6,
};

const RISK_AUTONOMY_POINT: RiskAutonomyDataPoint = {
  sessionId: 'session-1',
  threadId: 'main',
  turnIndex: 0,
  agentName: 'coder',
  riskScore: 3.0,
  autonomyScore: 7.5,
  createdAt: '2026-02-28T10:00:00Z',
};

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('AgentClient — eval query methods', () => {
  let client: AgentClient;

  beforeEach(() => {
    vi.resetAllMocks();
    client = new AgentClient(BASE);
  });

  // ── getScores ──────────────────────────────────────────────────────────────

  it('getScores: GET /evals/scores?evaluatorName=…, returns ScoreRecord[]', async () => {
    mockFetchJson([SCORE_RECORD]);

    const result = await client.getScores('TurnRiskEvaluator');

    const [url, init] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0] as [string, RequestInit];
    expect(url).toBe(`${BASE}/evals/scores?evaluatorName=TurnRiskEvaluator`);
    expect(init.method).toBe('GET');
    expect(result).toEqual([SCORE_RECORD]);
  });

  it('getScores: appends from and to when provided', async () => {
    mockFetchJson([]);

    await client.getScores('TurnRiskEvaluator', '2026-02-01T00:00:00Z', '2026-02-28T00:00:00Z');

    const [url] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0] as [string, RequestInit];
    expect(url).toContain('from=2026-02-01T00%3A00%3A00Z');
    expect(url).toContain('to=2026-02-28T00%3A00%3A00Z');
  });

  it('getScores: omits from/to when not provided', async () => {
    mockFetchJson([]);

    await client.getScores('TurnRiskEvaluator');

    const [url] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0] as [string, RequestInit];
    expect(url).not.toContain('from=');
    expect(url).not.toContain('to=');
  });

  it('getScores: keeps relative API base URLs relative when adding query params', async () => {
    const relativeClient = new AgentClient('/api/hpd-agent');
    const spy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => [],
      text: async () => '[]',
    } as Response);

    await relativeClient.getScores('TurnRiskEvaluator', '2026-02-01T00:00:00Z', '2026-02-28T00:00:00Z');

    expect(spy).toHaveBeenCalledWith(
      '/api/hpd-agent/evals/scores?evaluatorName=TurnRiskEvaluator&from=2026-02-01T00%3A00%3A00Z&to=2026-02-28T00%3A00%3A00Z',
      expect.objectContaining({ method: 'GET' }),
    );
  });

  // ── getScoresByThread ──────────────────────────────────────────────────────

  it('getScoresByThread: GET /evals/scores/by-thread?sessionId=…, returns ScoreRecord[]', async () => {
    mockFetchJson([SCORE_RECORD]);

    const result = await client.getScoresByThread('session-1');

    const [url, init] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0] as [string, RequestInit];
    expect(url).toBe(`${BASE}/evals/scores/by-thread?sessionId=session-1`);
    expect(init.method).toBe('GET');
    expect(result).toEqual([SCORE_RECORD]);
  });

  it('getScoresByThread: appends threadId when provided', async () => {
    mockFetchJson([]);

    await client.getScoresByThread('session-1', 'main');

    const [url] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0] as [string, RequestInit];
    expect(url).toContain('threadId=main');
  });

  it('getScoresByThread: omits threadId when not provided', async () => {
    mockFetchJson([]);

    await client.getScoresByThread('session-1');

    const [url] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0] as [string, RequestInit];
    expect(url).not.toContain('threadId=');
  });

  // ── writeScore ─────────────────────────────────────────────────────────────

  it('writeScore: POST /evals/scores with body, returns ScoreRecord with assigned id', async () => {
    mockFetchJson(SCORE_RECORD, 201);

    const { id: _id, ...recordWithoutId } = SCORE_RECORD;
    const result = await client.writeScore(recordWithoutId);

    const [url, init] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0] as [string, RequestInit];
    expect(url).toBe(`${BASE}/evals/scores`);
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual(recordWithoutId);
    expect(result).toEqual(SCORE_RECORD);
  });

  // ── getEvaluatorSummary ────────────────────────────────────────────────────

  it('getEvaluatorSummary: GET /evals/evaluators, returns EvaluatorSummary[]', async () => {
    mockFetchJson([EVALUATOR_SUMMARY]);

    const result = await client.getEvaluatorSummary();

    const [url, init] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0] as [string, RequestInit];
    expect(url).toBe(`${BASE}/evals/evaluators`);
    expect(init.method).toBe('GET');
    expect(result).toEqual([EVALUATOR_SUMMARY]);
  });

  it('getEvaluatorSummary: appends from/to when provided', async () => {
    mockFetchJson([]);

    await client.getEvaluatorSummary('2026-02-01T00:00:00Z', '2026-02-28T00:00:00Z');

    const [url] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0] as [string, RequestInit];
    expect(url).toContain('from=');
    expect(url).toContain('to=');
  });

  // ── getRiskAutonomyDistribution ────────────────────────────────────────────

  it('getRiskAutonomyDistribution: GET /evals/risk-autonomy, returns RiskAutonomyDataPoint[]', async () => {
    mockFetchJson([RISK_AUTONOMY_POINT]);

    const result = await client.getRiskAutonomyDistribution();

    const [url, init] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0] as [string, RequestInit];
    expect(url).toBe(`${BASE}/evals/risk-autonomy`);
    expect(init.method).toBe('GET');
    expect(result).toEqual([RISK_AUTONOMY_POINT]);
  });

  it('getRiskAutonomyDistribution: appends from/to when provided', async () => {
    mockFetchJson([]);

    await client.getRiskAutonomyDistribution('2026-02-01T00:00:00Z', '2026-02-28T00:00:00Z');

    const [url] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0] as [string, RequestInit];
    expect(url).toContain('from=');
    expect(url).toContain('to=');
  });
});
