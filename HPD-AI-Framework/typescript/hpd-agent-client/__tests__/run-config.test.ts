/**
 * RunConfig type serialization tests (plan items 1.1–1.4)
 *
 * Verifies that RunConfig and ChatRunConfig serialize to the exact camelCase
 * wire format expected by the server's StreamRunConfigDto / ChatRunConfigDto.
 *
 * Test type: unit (Node) — pure value / JSON serialization, no network I/O.
 */

import { describe, it, expect } from 'vitest';
import type { RunConfig, ChatRunConfig } from '../src/types/run-config.js';

// ---------------------------------------------------------------------------
// 1.1 — All fields set, camelCase keys match server DTOs exactly
// ---------------------------------------------------------------------------

describe('RunConfig — wire format (camelCase)', () => {
  it('serializes all top-level fields to camelCase keys matching StreamRunConfigDto', () => {
    const rc: RunConfig = {
      providerKey: 'anthropic',
      modelId: 'claude-sonnet-4-6',
      additionalSystemInstructions: 'Be concise.',
      chat: { temperature: 0.7, maxOutputTokens: 4096, topP: 0.9 },
      permissionOverrides: { read_file: true, write_file: false },
      coalesceDeltas: true,
      skipTools: false,
      runTimeout: 'PT5M',
    };

    const parsed = JSON.parse(JSON.stringify(rc));

    // Top-level keys must match StreamRunConfigDto field names exactly
    expect(parsed).toHaveProperty('providerKey', 'anthropic');
    expect(parsed).toHaveProperty('modelId', 'claude-sonnet-4-6');
    expect(parsed).toHaveProperty('additionalSystemInstructions', 'Be concise.');
    expect(parsed).toHaveProperty('coalesceDeltas', true);
    expect(parsed).toHaveProperty('skipTools', false);
    expect(parsed).toHaveProperty('runTimeout', 'PT5M');

    // Nested chat keys must match ChatRunConfigDto
    expect(parsed.chat).toEqual({ temperature: 0.7, maxOutputTokens: 4096, topP: 0.9 });

    // No snake_case leakage
    expect(parsed).not.toHaveProperty('provider_key');
    expect(parsed).not.toHaveProperty('model_id');
    expect(parsed).not.toHaveProperty('run_timeout');
  });

  // ---------------------------------------------------------------------------
  // 1.2 — Only modelId set — all other fields absent from JSON
  // ---------------------------------------------------------------------------

  it('JSON.stringify omits undefined fields when only modelId is set', () => {
    const rc: RunConfig = { modelId: 'claude-sonnet-4-6' };
    const json = JSON.stringify(rc);
    const parsed = JSON.parse(json);

    expect(Object.keys(parsed)).toEqual(['modelId']);
    expect(parsed.modelId).toBe('claude-sonnet-4-6');
  });

  // ---------------------------------------------------------------------------
  // 1.3 — ChatRunConfig nested inside RunConfig.chat
  // ---------------------------------------------------------------------------

  it('serializes ChatRunConfig correctly under the chat key', () => {
    const rc: RunConfig = { chat: { temperature: 0.7 } };
    const parsed = JSON.parse(JSON.stringify(rc));

    expect(parsed).toEqual({ chat: { temperature: 0.7 } });
    expect(parsed.chat).toHaveProperty('temperature', 0.7);
    expect(Object.keys(parsed.chat)).toEqual(['temperature']);
  });

  it('ChatRunConfig omits undefined fields within chat', () => {
    const rc: RunConfig = { chat: { maxOutputTokens: 4096 } };
    const parsed = JSON.parse(JSON.stringify(rc));

    expect(Object.keys(parsed.chat)).toEqual(['maxOutputTokens']);
    expect(parsed.chat).not.toHaveProperty('temperature');
    expect(parsed.chat).not.toHaveProperty('topP');
  });

  // ---------------------------------------------------------------------------
  // 1.4 — Empty object has all fields as undefined — no keys in serialized output
  // ---------------------------------------------------------------------------

  it('empty RunConfig {} serializes to {}', () => {
    const rc: RunConfig = {};
    const json = JSON.stringify(rc);
    expect(json).toBe('{}');
  });

  it('RunConfig with undefined values omits those keys in JSON', () => {
    // Explicitly setting to undefined is the same as not setting at all
    const rc: RunConfig = {
      providerKey: undefined,
      modelId: undefined,
      chat: undefined,
    };
    const json = JSON.stringify(rc);
    expect(json).toBe('{}');
  });

  // ---------------------------------------------------------------------------
  // permissionOverrides — nested object serializes correctly
  // ---------------------------------------------------------------------------

  it('permissionOverrides serializes tool-name keys with boolean values', () => {
    const rc: RunConfig = {
      permissionOverrides: {
        read_file: true,
        write_file: false,
        execute_command: true,
      },
    };
    const parsed = JSON.parse(JSON.stringify(rc));
    expect(parsed.permissionOverrides).toEqual({
      read_file: true,
      write_file: false,
      execute_command: true,
    });
  });

  // ---------------------------------------------------------------------------
  // ChatRunConfig — standalone type verification
  // ---------------------------------------------------------------------------

  it('ChatRunConfig with all fields serializes correctly', () => {
    const chat: ChatRunConfig = {
      temperature: 0.5,
      maxOutputTokens: 2048,
      topP: 0.95,
      frequencyPenalty: 0.1,
      presencePenalty: 0.2,
    };
    const parsed = JSON.parse(JSON.stringify(chat));
    expect(parsed).toEqual({
      temperature: 0.5,
      maxOutputTokens: 2048,
      topP: 0.95,
      frequencyPenalty: 0.1,
      presencePenalty: 0.2,
    });
  });

  it('runTimeout ISO 8601 duration passes through as a string', () => {
    const rc: RunConfig = { runTimeout: 'PT30S' };
    expect(JSON.parse(JSON.stringify(rc))).toEqual({ runTimeout: 'PT30S' });
  });
});
