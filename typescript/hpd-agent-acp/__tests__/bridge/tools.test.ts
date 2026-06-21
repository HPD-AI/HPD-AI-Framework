/**
 * Unit tests for bridge/tools.ts — tool name → ACP ToolCallKind classifier.
 *
 * What these tests cover:
 *   toolNameToKind maps HPD tool names to ACP display kinds via case-insensitive
 *   regex matching. Each pattern group is verified plus boundary/fallback cases.
 *
 * Test type: unit — pure function, zero dependencies.
 */

import { describe, it, expect } from 'vitest';
import { toolNameToKind } from '../../src/bridge/tools.js';

describe('toolNameToKind', () => {
  // ── read ────────────────────────────────────────────────────────────────
  it('read_file → read', () => expect(toolNameToKind('read_file')).toBe('read'));
  it('get_file → read',  () => expect(toolNameToKind('get_file')).toBe('read'));
  it('view_file → read', () => expect(toolNameToKind('view_file')).toBe('read'));
  it('cat_file → read',  () => expect(toolNameToKind('cat_file')).toBe('read'));
  it('open_file → read', () => expect(toolNameToKind('open_file')).toBe('read'));

  // ── edit ────────────────────────────────────────────────────────────────
  it('write_file → edit',  () => expect(toolNameToKind('write_file')).toBe('edit'));
  it('create_file → edit', () => expect(toolNameToKind('create_file')).toBe('edit'));
  it('edit_file → edit',   () => expect(toolNameToKind('edit_file')).toBe('edit'));
  it('str_replace → edit', () => expect(toolNameToKind('str_replace')).toBe('edit'));
  it('patch_file → edit',  () => expect(toolNameToKind('patch_file')).toBe('edit'));
  it('update_file → edit', () => expect(toolNameToKind('update_file')).toBe('edit'));
  it('str_replace_based_edit_tool → edit', () =>
    expect(toolNameToKind('str_replace_based_edit_tool')).toBe('edit'));

  // ── delete ──────────────────────────────────────────────────────────────
  it('delete_file → delete', () => expect(toolNameToKind('delete_file')).toBe('delete'));
  it('remove_file → delete', () => expect(toolNameToKind('remove_file')).toBe('delete'));

  // ── move ────────────────────────────────────────────────────────────────
  it('move_file → move',   () => expect(toolNameToKind('move_file')).toBe('move'));
  it('rename_file → move', () => expect(toolNameToKind('rename_file')).toBe('move'));

  // ── search ──────────────────────────────────────────────────────────────
  it('search → search',    () => expect(toolNameToKind('search')).toBe('search'));
  it('grep → search',      () => expect(toolNameToKind('grep')).toBe('search'));
  it('glob → search',      () => expect(toolNameToKind('glob')).toBe('search'));
  it('find_file → search', () => expect(toolNameToKind('find_file')).toBe('search'));
  it('ripgrep → search',   () => expect(toolNameToKind('ripgrep')).toBe('search'));
  it('rg (exact) → search', () => expect(toolNameToKind('rg')).toBe('search'));

  // ── execute ─────────────────────────────────────────────────────────────
  it('bash → execute',        () => expect(toolNameToKind('bash')).toBe('execute'));
  it('shell → execute',       () => expect(toolNameToKind('shell')).toBe('execute'));
  it('run_command → execute', () => expect(toolNameToKind('run_command')).toBe('execute'));
  it('execute → execute',     () => expect(toolNameToKind('execute')).toBe('execute'));
  it('terminal → execute',    () => expect(toolNameToKind('terminal')).toBe('execute'));

  // ── fetch ───────────────────────────────────────────────────────────────
  it('web_fetch → fetch',    () => expect(toolNameToKind('web_fetch')).toBe('fetch'));
  it('http_request → fetch', () => expect(toolNameToKind('http_request')).toBe('fetch'));
  it('fetch_url → fetch',    () => expect(toolNameToKind('fetch_url')).toBe('fetch'));
  it('browse → fetch',       () => expect(toolNameToKind('browse')).toBe('fetch'));

  // ── think ───────────────────────────────────────────────────────────────
  it('think → think',   () => expect(toolNameToKind('think')).toBe('think'));
  it('plan → think',    () => expect(toolNameToKind('plan')).toBe('think'));
  it('reason → think',  () => expect(toolNameToKind('reason')).toBe('think'));
  it('reflect → think', () => expect(toolNameToKind('reflect')).toBe('think'));

  // ── switch_mode ─────────────────────────────────────────────────────────
  it('switch_mode → switch_mode', () => expect(toolNameToKind('switch_mode')).toBe('switch_mode'));
  it('set_mode → switch_mode',    () => expect(toolNameToKind('set_mode')).toBe('switch_mode'));

  // ── fallback ────────────────────────────────────────────────────────────
  it('unknown_tool → other',  () => expect(toolNameToKind('unknown_tool')).toBe('other'));
  it('empty string → other',  () => expect(toolNameToKind('')).toBe('other'));
  it('send_message → other',  () => expect(toolNameToKind('send_message')).toBe('other'));

  // ── case insensitivity ──────────────────────────────────────────────────
  it('READ_FILE (uppercase) → read', () => expect(toolNameToKind('READ_FILE')).toBe('read'));
  it('Write_File (mixed) → edit',    () => expect(toolNameToKind('Write_File')).toBe('edit'));
  it('BASH (uppercase) → execute',   () => expect(toolNameToKind('BASH')).toBe('execute'));

  // ── rg word boundary — should not match "program" ───────────────────────
  it('program does not match rg boundary → other', () =>
    expect(toolNameToKind('program')).toBe('other'));
});
